// PS5Stream: finds a USB/HDMI capture card and shows it in a low-latency ffplay window.
// Build: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /out:PS5Stream.exe PS5Stream.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

class PS5Stream
{
    // Devices that are never a capture card
    static readonly Regex NotCaptureVideo = new Regex(@"webcam|IR camera|virtual|integrated|facetime|OBS", RegexOptions.IgnoreCase);
    // Typical names of capture card audio endpoints
    static readonly Regex CaptureAudioHint = new Regex(@"capture|digital audio interface|hdmi|cam ?link|elgato|avermedia|live gamer|hd60|ugreen|usb ?video|ms2109|ms2130|macrosilicon|razer ripsaw|genki", RegexOptions.IgnoreCase);
    static readonly Regex NotCaptureAudio = new Regex(@"voicemeeter|microphone|headset|headphones|realtek|nvidia|stereo mix|cable", RegexOptions.IgnoreCase);

    static Dictionary<string, string> cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    static int Main(string[] args)
    {
        Console.Title = "PS5 Stream";
        Console.OutputEncoding = Encoding.UTF8;
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        OpenLog(dir);
        LoadConfig(Path.Combine(dir, "ps5stream.ini"));

        string ffmpeg = FindTool("ffmpeg.exe", dir);
        string ffplay = FindTool("ffplay.exe", dir);
        if (ffmpeg == null || ffplay == null)
            return Fail("ffmpeg/ffplay not found. Install: winget install Gyan.FFmpeg");

        List<string> videos, audios;
        ListDevices(ffmpeg, out videos, out audios);

        Note("Video devices: " + (videos.Count == 0 ? "(none)" : string.Join(" | ", videos.ToArray())));
        Note("Audio devices: " + (audios.Count == 0 ? "(none)" : string.Join(" | ", audios.ToArray())));
        Note("");

        string video = Get("video", null);
        if (video == null)
            foreach (string v in videos)
                if (!NotCaptureVideo.IsMatch(v)) { video = v; break; }
        if (video == null)
            return Fail("Capture card not found.\n" +
                "  - Plug the card into a USB port and the PS5 into the card HDMI IN.\n" +
                "  - The laptop HDMI port is an output. A PS5 plugged into it does not appear.\n" +
                "  - If the card uses another name, set video= in ps5stream.ini.");

        string audio = Get("audio", null);
        if (audio == null)
            audio = PickAudio(video, audios);
        if (audio != null && audio.Equals("none", StringComparison.OrdinalIgnoreCase))
            audio = null;

        Mode mode = PickMode(ffmpeg, video);

        string vf = FillFilter();
        Note("Capture card : " + video);
        Note("Audio        : " + (audio ?? "(not found, set audio= in ps5stream.ini)"));
        Note("Mode         : " + (mode == null ? "default" : mode.ToString()));
        Note("Screen       : " + ScreenSize());
        Note("Frame        : " + (vf == null ? "native 16:9" : vf));
        Note("");
        Note("Window keys: F or double-click fullscreen, M mute,");
        Note("9/0 volume, Esc or Q quit.");
        Note("");

        var sb = new StringBuilder();
        sb.Append("-hide_banner -loglevel warning ");
        sb.Append("-fflags nobuffer -flags low_delay -probesize 200000 -analyzeduration 200000 ");
        sb.Append("-threads 1 -f dshow -rtbufsize ").Append(Get("rtbuf_kb", "256")).Append("K ");
        if (mode != null)
        {
            if (mode.Codec != null) sb.Append("-vcodec ").Append(mode.Codec).Append(' ');
            if (mode.PixFmt != null) sb.Append("-pixel_format ").Append(mode.PixFmt).Append(' ');
            sb.AppendFormat("-video_size {0}x{1} -framerate {2} ", mode.W, mode.H, mode.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (audio != null) sb.Append("-audio_buffer_size ").Append(Get("audio_buffer_ms", "5")).Append(' ');
        sb.Append("-i \"video=").Append(video);
        if (audio != null) sb.Append(":audio=").Append(audio);
        sb.Append('"');
        sb.Append(" -framedrop -sync video -vf ");
        sb.Append(vf == null ? "setpts=0" : vf);
        sb.Append(" -window_title \"PS5\"");
        if (Get("fullscreen", "1") == "1") sb.Append(" -fs");
        string extra = Get("extra", "");
        if (extra.Length > 0) sb.Append(' ').Append(extra);

        string ffArgs = sb.ToString();
        Note("ffplay " + ffArgs);
        Note("");

        int code = Run(ffplay, ffArgs);
        if (code != 0 && mode != null)
        {
            Note("Selected mode failed. Retrying the device default...");
            ffArgs = Regex.Replace(ffArgs, @"-vcodec \S+ |-pixel_format \S+ |-video_size \S+ |-framerate \S+ ", "");
            code = Run(ffplay, ffArgs);
        }
        if (code != 0)
            return Fail("ffplay exited with code " + code + ". The card may be open in another program (OBS, Discord, a browser).");
        return 0;
    }

    class Mode
    {
        public string Codec, PixFmt;
        public int W, H;
        public double Fps;
        public override string ToString()
        {
            return W + "x" + H + " @ " + Fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + " fps (" + (Codec ?? PixFmt) + ")";
        }
    }

    static Mode PickMode(string ffmpeg, string video)
    {
        int maxW = int.Parse(Get("max_width", "1920"));
        int maxH = int.Parse(Get("max_height", "1080"));
        double maxFps = double.Parse(Get("max_fps", "60"), System.Globalization.CultureInfo.InvariantCulture);

        string output = Capture(ffmpeg, "-hide_banner -f dshow -list_options true -i \"video=" + video + "\"");
        var rx = new Regex(@"(vcodec|pixel_format)=(\S+)\s+min s=\d+x\d+ fps=[\d.]+\s+max s=(\d+)x(\d+) fps=([\d.]+)");
        Mode best = null;
        foreach (Match m in rx.Matches(output))
        {
            var md = new Mode();
            if (m.Groups[1].Value == "vcodec") md.Codec = m.Groups[2].Value; else md.PixFmt = m.Groups[2].Value;
            md.W = int.Parse(m.Groups[3].Value);
            md.H = int.Parse(m.Groups[4].Value);
            md.Fps = Math.Min(double.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture), maxFps);
            if (md.W > maxW || md.H > maxH) continue;
            if (md.Codec != null && md.Codec != "mjpeg") continue; // only mjpeg among compressed formats
            if (best == null || Better(md, best)) best = md;
        }
        return best;
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    static string ScreenSize()
    {
        return GetSystemMetrics(0) + "x" + GetSystemMetrics(1);
    }

    static int Even(int n)
    {
        return n < 2 ? 2 : n - (n % 2);
    }

    // The sharp 16:9 frame stays intact. A soft copy of the same frame fills the 16:10 bars.
    static string FillFilter()
    {
        string mode = Get("fill_screen", "ambient");
        if (mode == "0" || mode.Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
        int sw = Even(GetSystemMetrics(0));
        int sh = Even(GetSystemMetrics(1));
        if (sw < 320 || sh < 240) return null;
        if (mode.Equals("stretch", StringComparison.OrdinalIgnoreCase))
            return "libplacebo=w=" + sw + ":h=" + sh + ":fit_mode=fill:upscaler=ewa_lanczos:sigmoid=1:range=pc:format=yuv444p,setpts=0";
        int dw = sw;
        int dh = Even(dw * 9 / 16);
        if (dh > sh)
        {
            dh = sh;
            dw = Even(dh * 16 / 9);
        }
        int ox = (sw - dw) / 2;
        int oy = (sh - dh) / 2;
        return "scale=iw:ih:in_range=jpeg:out_range=pc:flags=neighbor,format=yuv420p,split[fg][bg];"
            + "[bg]scale=8:8,scale=" + sw + ":" + sh + ":flags=bilinear[bgb];"
            + "[fg]scale=" + dw + ":" + dh + ":flags=bilinear[fgs];"
            + "[bgb][fgs]overlay=" + ox + ":" + oy + ",setsar=1,setpts=0";
    }

    static bool Better(Mode a, Mode b)
    {
        // Higher pixels*fps wins; on a tie prefer raw (no decode delay)
        double sa = (double)a.W * a.H * Math.Round(a.Fps), sb = (double)b.W * b.H * Math.Round(b.Fps);
        if (sa != sb) return sa > sb;
        return a.PixFmt != null && b.PixFmt == null;
    }

    static string PickAudio(string video, List<string> audios)
    {
        // 1. Audio device sharing a distinctive word with the video device name
        foreach (string a in audios)
        {
            if (NotCaptureAudio.IsMatch(a)) continue;
            foreach (string word in video.Split(new[] { ' ', '(', ')', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                if (word.Length >= 4 && a.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !Regex.IsMatch(word, "^(video|audio|device|usb)$", RegexOptions.IgnoreCase))
                    return a;
        }
        // 2. Audio device with a typical capture-card name
        foreach (string a in audios)
            if (!NotCaptureAudio.IsMatch(a) && CaptureAudioHint.IsMatch(a)) return a;
        return null;
    }

    static void ListDevices(string ffmpeg, out List<string> videos, out List<string> audios)
    {
        videos = new List<string>();
        audios = new List<string>();
        string output = Capture(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy");
        foreach (Match m in Regex.Matches(output, "\"([^\"]+)\" \\((video|audio|none)\\)"))
        {
            if (m.Groups[2].Value == "audio") audios.Add(m.Groups[1].Value);
            else videos.Add(m.Groups[1].Value);
        }
    }

    static string Capture(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false;
        psi.RedirectStandardError = true;
        psi.RedirectStandardOutput = true;
        psi.CreateNoWindow = true;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.StandardOutputEncoding = Encoding.UTF8;
        using (var p = Process.Start(psi))
        {
            var outTask = p.StandardOutput.ReadToEndAsync();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return outTask.Result + err;
        }
    }

    static StreamWriter logFile;
    static string lastLogLine;
    static int lastLogCount;

    static void OpenLog(string dir)
    {
        string path = Path.Combine(dir, "ps5stream.log");
        logFile = new StreamWriter(path, false, new UTF8Encoding(false));
        logFile.AutoFlush = true;
        Note("log " + path);
    }

    static void Note(string line)
    {
        Console.WriteLine(line);
        WriteLog(line);
    }

    static void WriteLog(string line)
    {
        if (logFile == null) return;
        if (line == lastLogLine)
        {
            lastLogCount++;
            return;
        }
        FlushLogRepeat();
        lastLogLine = line;
        lastLogCount = 1;
        logFile.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " " + line);
    }

    static void FlushLogRepeat()
    {
        if (logFile == null || lastLogCount <= 1) return;
        logFile.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
            + " repeated " + lastLogCount + " times");
        lastLogCount = 1;
    }

    static int Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = false;
        using (var p = Process.Start(psi))
        {
            var pump = new System.Threading.Thread(() =>
            {
                string line;
                while ((line = p.StandardError.ReadLine()) != null)
                    WriteLog(line);
            });
            pump.IsBackground = true;
            pump.Start();
            p.WaitForExit();
            pump.Join(1000);
            FlushLogRepeat();
            return p.ExitCode;
        }
    }

    static string FindTool(string name, string dir)
    {
        string local = Path.Combine(dir, name);
        if (File.Exists(local)) return local;
        foreach (string p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string f = Path.Combine(p.Trim(), name);
                if (p.Trim().Length > 0 && File.Exists(f)) return f;
            }
            catch (ArgumentException) { }
        }
        string winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages");
        if (Directory.Exists(winget))
            foreach (string f in Directory.GetFiles(winget, name, SearchOption.AllDirectories))
                return f;
        return null;
    }

    static void LoadConfig(string path)
    {
        if (!File.Exists(path)) return;
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string val = line.Substring(eq + 1).Trim();
            if (val.Length > 0) cfg[line.Substring(0, eq).Trim()] = val;
        }
    }

    static string Get(string key, string def)
    {
        string v;
        return cfg.TryGetValue(key, out v) ? v : def;
    }

    static int Fail(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Note(msg);
        Console.ResetColor();
        Note("");
        if (!Console.IsInputRedirected)
        {
            Note("Press any key...");
            Console.ReadKey(true);
        }
        return 1;
    }
}

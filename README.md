# PS5 Capture Stream

Low-latency fullscreen preview of a PS5 HDMI capture card on Windows.

The capture card is shown as it arrives: 1920x1080 at 60 fps. The sharp frame stays 16:9. On a 16:10 panel the bars are filled with an 8x8 average of that frame, not a full blur. Frame interpolation and heavy scalers are off because they make the delay jump.

## What you need

- Windows with the .NET Framework 4 csc compiler
- FFmpeg on PATH (`ffmpeg.exe` and `ffplay.exe`). Install with `winget install Gyan.FFmpeg`
- A UVC HDMI capture card. USB 2 cards top out at 1080p60 MJPEG

## Run

```
PS5.bat
PS5-blur.bat
```

`PS5.bat` shows the 16:9 frame with black bars. `PS5-blur.bat` fills those bars with an 8x8 color average of the same frame. The first launch builds `PS5Stream.exe` from `PS5Stream.cs`. Keys inside the picture: F or double-click for fullscreen, M mute, 9 and 0 volume, Esc or Q quit.

`Devices.bat` prints the DirectShow device list.

## Settings

`ps5stream.ini` sits next to the executable.

- `video` and `audio` override auto-detect. Leave them empty to pick the capture card.
- `rtbuf_kb` is the capture buffer cap. Keep it small. A multi-megabyte buffer holds old frames and the lag grows.
- `audio_buffer_ms` is the DirectShow audio buffer.
- `PS5.bat` forces black bars. `PS5-blur.bat` forces the 8x8 color fill. The `fill_screen` value in the ini is only the fallback when the exe is started with no argument.
- `fullscreen=1` starts fullscreen.

A log is written to `ps5stream.log` on every launch. Repeated lines are collapsed.

## Latency

One 60 fps frame is 16.7 ms. A USB 2 MJPEG capture card cannot reach microsecond latency. This player keeps the software queue to a few frames so the delay stays flat instead of climbing toward half a second.

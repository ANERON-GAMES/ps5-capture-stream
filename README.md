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
```

The first launch builds `PS5Stream.exe` from `PS5Stream.cs`. The window lists the video and audio devices it found. Keys inside the picture: F or double-click for fullscreen, M mute, 9 and 0 volume, Esc or Q quit.

`Devices.bat` prints the DirectShow device list.

## Settings

`ps5stream.ini` sits next to the executable.

- `video` and `audio` override auto-detect. Leave them empty to pick the capture card.
- `rtbuf_kb` is the capture buffer cap. Keep it small. A multi-megabyte buffer holds old frames and the lag grows.
- `audio_buffer_ms` is the DirectShow audio buffer.
- `fill_screen=ambient` fills the bars from an 8x8 color average. `0` leaves black bars. `stretch` fills the panel and costs more.
- `fullscreen=1` starts fullscreen.

A log is written to `ps5stream.log` on every launch. Repeated lines are collapsed.

## Latency

One 60 fps frame is 16.7 ms. A USB 2 MJPEG capture card cannot reach microsecond latency. This player keeps the software queue to a few frames so the delay stays flat instead of climbing toward half a second.

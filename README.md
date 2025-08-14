# FFmpeg.Unity

A Videoplayer for [YoutubeDLSharp](https://github.com/Bluegrams/YoutubeDLSharp) (required).

## What is it?

FFmpeg.Unity implements a working Videoplayer based on YoutubeDLSharp's yt-dlp and ffmpeg functionality.

Please refer to YoutubeDLSharp's [README.md](https://github.com/Bluegrams/YoutubeDLSharp/blob/master/README.md#how-do-i-install-it) for further information on how to set this up.

## How do I install it?

1. Once YoutubeDLSharp is present in your project you can install FFmpeg.Unity through the Unity Package Manager directly from git:

```
Window > Package Management > Package Manager.
Click the "+" button, select "Install package from git URL..."
Paste "https://github.com/VirtualBrightPlayz/FFmpeg.Unity.git?path=/Packages/FFmpeg.Unity#dynload"
```
2. By installing the package using the method above, you will need to copy over the Scenes/ and Scripts/ folders.

3. You will need the "latest" FFmpeg 6.1 Shared (LGPL):
[Windows](https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n6.1-latest-win64-lgpl-shared-6.1.zip)
[Linux](https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n6.1-latest-linux64-lgpl-shared-6.1.tar.xz)

4. Then copy the contents of `FFmpeg/bin` and `FFmpeg/lib` to the `Assets/StreamingAssets/ffmpeg` directory in your project.

5. If on Linux, make sure to set `Assets/StreamingAssets/yt-dlp` as executable: `chmod u+x yt-dlp`

6. You can then open the `Scenes/FFplay` example scene to test the Videoplayer with a working YouTube link in the "Content Url" field of the "FF Play Player" script component during Unity play mode.

# FFmpeg.Unity

A Videoplayer for [YoutubeDLSharp](https://github.com/Bluegrams/YoutubeDLSharp).

## What is it?

FFmpeg.Unity implements a working Videoplayer based on YoutubeDLSharp's yt-dlp and ffmpeg functionality.

Please refer to YoutubeDLSharp's [README.md](https://github.com/Bluegrams/YoutubeDLSharp/blob/master/README.md#how-do-i-install-it) for further information on how to set this up.

## How do I install it?

Once YoutubeDLSharp is present in your project you can install FFmpeg.Unity through the Unity Package Manager directly from git

```
Window > Package Management > Package Manager.
Click the "+" button, select "Install package from git URL..."
Paste "https://github.com/VirtualBrightPlayz/FFmpeg.Unity.git?path=/Packages/FFmpeg.Unity#dynload"
```

You will need the latest [FFmpeg/bin](https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n6.1-latest-win64-lgpl-shared-6.1.zip) copied to the "Assets/StreamingAssets/ffmpeg" directory in your project.

You can then open the "FFPlay" example scene in the Scenes/ folder to test the Videoplayer with a working YouTube link in the "Content Url" field of the FFTest2 script component during play mode.

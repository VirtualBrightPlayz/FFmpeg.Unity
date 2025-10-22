# FFmpeg.Unity

A Videoplayer for Unity, with an example using [YoutubeDLSharp](https://github.com/Bluegrams/YoutubeDLSharp).

## What is it?

FFmpeg.Unity implements a working Videoplayer based on ffmpeg functionality.

## How do I install it?

1. You can install FFmpeg.Unity through the Unity Package Manager directly from git:

```
Window > Package Management > Package Manager.
Click the "+" button, select "Install package from git URL..."
Paste "https://github.com/VirtualBrightPlayz/FFmpeg.Unity.git?path=/Packages/FFmpeg.Unity#master"
```
2. By installing the package using the method above, you will need to copy over the Scenes/ and Scripts/ folders.

3. You will need the "latest" FFmpeg 6.1 Shared (LGPL):<br>
[Windows](https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2025-08-31-13-00/ffmpeg-n6.1.3-win64-lgpl-shared-6.1.zip)
[Linux](https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2025-08-31-13-00/ffmpeg-n6.1.3-linux64-lgpl-shared-6.1.tar.xz)

4. Windows: Only copy the .dll files found within the `bin` directory in the downloaded archive to a `Assets/StreamingAssets/ffmpeg` directory in your project.

5. Linux: Only copy the .so files found within the `lib` directorry in the downloaded acrhive to a `Assets/StreamingAssets/ffmpeg`. If using yt-dlp, make sure to set executable bits on `Assets/StreamingAssets/yt-dlp` e.g. `chmod u+x yt-dlp`

6. You can then open the `Scenes/FFplay` example scene to test the Videoplayer with a working YouTube link in the "Content Url" field of the "FF Play Player" script component during Unity play mode.

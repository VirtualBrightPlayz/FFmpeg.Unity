public static unsafe partial class DynamicallyLinkedBindings
{
#if UNITY_STANDALONE_WIN
    public const string avformat = "avformat-60";
    public const string avutil = "avutil-58";
    public const string avcodec = "avcodec-60";
    public const string avdevice = "avdevice-60";
    public const string avfilter = "avfilter-9";
    public const string swscale = "swscale-7";
    public const string swresample = "swresample-4";
#else
    public const string avformat = "libavformat";
    public const string avutil = "libavutil";
    public const string avcodec = "libavcodec";
    public const string avdevice = "libavdevice";
    public const string avfilter = "libavfilter";
    public const string swscale = "libswscale";
    public const string swresample = "libswresample";
#endif
}
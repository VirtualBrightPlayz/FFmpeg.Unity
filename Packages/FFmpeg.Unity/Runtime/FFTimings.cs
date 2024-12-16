using System;
using System.Collections.Generic;
using System.IO;
using FFmpeg.Unity.Helpers;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFTimings : IDisposable
    {
        static FFTimings()
        {
            DynamicallyLinkedBindings.Initialize();
        }

        public FFmpegCtx context;
        public VideoStreamDecoder decoder;

        public bool IsInputValid;
        public double StartTime;

        private long pts;

        private AVRational timeBase;
        private double timeBaseSeconds;

        private AVPacket currentPacket;
        private AVFrame currentFrame;

        public FFTimings(string url, AVMediaType mediaType, AVHWDeviceType deviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            context = new FFmpegCtx(url);
            IsInputValid = context.HasStream(mediaType);
            Init(mediaType, deviceType);
        }

        public FFTimings(Stream stream, AVMediaType mediaType, AVHWDeviceType deviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            context = new FFmpegCtx(stream);
            IsInputValid = context.HasStream(mediaType);
            Init(mediaType, deviceType);
        }

        private void Init(AVMediaType type, AVHWDeviceType deviceType)
        {
            if (!IsInputValid)
                return;
            if (context.TryGetTimeBase(type, out timeBase))
            {
                timeBaseSeconds = ffmpeg.av_q2d(timeBase);
                decoder = new VideoStreamDecoder(context, type, deviceType);
                if (type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    pts = 0;
                    AVFrame frame = GetFrame();
                    StartTime = currentPacket.dts * timeBaseSeconds;
                }
                Debug.Log($"timeBase={timeBase.num}/{timeBase.den}");
                Debug.Log($"timeBaseSeconds={timeBaseSeconds}");
            }
        }

        public void Update(double timestamp)
        {
            if (!IsInputValid)
                return;
            pts = (long)(Math.Max(double.Epsilon, timestamp) / timeBaseSeconds);
        }

        public void Seek(double timestamp)
        {
            if (!IsInputValid)
                return;
            context.Seek(decoder, timestamp);
            if (context.NextFrame(out AVPacket packet))
            {
                AVFrame frame = DecodeFrame();
                if (frame.format != -1)
                {
                    currentPacket = packet;
                    currentFrame = frame;
                    pts = currentPacket.dts;
                    return;
                }
            }
            Update(timestamp);
            currentPacket = default;
        }

        public double GetLength()
        {
            if (!IsInputValid)
                return 0d;
            return context.GetLength(decoder);
        }

        public bool IsEndOfFile()
        {
            if (!IsInputValid)
                return false;
            return context.EndReached;
        }

        /// <summary>
        /// Returns the current frame for the active pts, decoding if needed
        /// </summary>
        public AVFrame GetCurrentFrame()
        {
            if (!IsInputValid)
                return new AVFrame()
                {
                    format = -1
                };
            return currentFrame;
        }

        public AVFrame GetFrame(int maxFrames = 250)
        {
            if (!IsInputValid)
                return new AVFrame()
                {
                    format = -1
                };
            int i = 0;
            while ((pts >= currentPacket.dts || currentPacket.dts == ffmpeg.AV_NOPTS_VALUE) && i <= maxFrames)
            {
                i++;
                if (context.NextFrame(out AVPacket packet))
                {
                    AVFrame frame = DecodeFrame();
                    if (frame.format != -1)
                    {
                        currentPacket = packet;
                        currentFrame = frame;
                    }
                }
                else
                    break;
            }
            return currentFrame;
        }

        public List<AVFrame> GetFrames(double maxDelta, int maxFrames = 250)
        {
            if (!IsInputValid)
                return new List<AVFrame>();
            long ptsDelta = (long)(Math.Max(double.Epsilon, maxDelta) / timeBaseSeconds);
            List<AVFrame> frames = new List<AVFrame>();
            int i = 0;
            long dts = currentPacket.dts;
            while ((pts >= dts || currentPacket.dts == ffmpeg.AV_NOPTS_VALUE) && i <= maxFrames)
            {
                i++;
                if (context.NextFrame(out AVPacket packet))
                {
                    AVFrame frame = DecodeFrame();
                    if (frame.format != -1)
                    {
                        currentPacket = packet;
                        currentFrame = frame;
                        if (Math.Abs(pts - packet.dts) <= ptsDelta)
                        {
                            dts = currentPacket.dts;
                            frames.Add(frame);
                        }
                    }
                }
                else
                    break;
            }
            return frames;
        }

        private AVFrame DecodeFrame()
        {
            decoder.Decode(out AVFrame frame);
            return frame;
        }

        private AVFrame DecodeMultiFrame()
        {
            int retCode;
            AVFrame frame;
            do
            {
                retCode = decoder.Decode(out frame);
            } while (retCode == 1);
            return frame;
        }

        public void Dispose()
        {
            decoder?.Dispose();
            context?.Dispose();
        }
    }
}
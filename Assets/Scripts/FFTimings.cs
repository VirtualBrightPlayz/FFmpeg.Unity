using System;
using System.Collections.Generic;
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

        private long pts;

        private AVRational timeBase;
        private double timeBaseSeconds;

        private AVPacket currentPacket;

        private AVFrame currentFrame;

        private double currentFrameTimer;

        public FFTimings(string url, AVMediaType mediaType, AVHWDeviceType deviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            context = new FFmpegCtx(url);
            Init(mediaType, deviceType);
        }

        private void Init(AVMediaType type, AVHWDeviceType deviceType)
        {
            if (context.TryGetTimeBase(type, out timeBase))
            {
                timeBaseSeconds = ffmpeg.av_q2d(timeBase);
                decoder = new VideoStreamDecoder(context, type, deviceType);
                // if (context.NextFrame(out currentPacket))
                {
                    // pts = currentPacket.pts;
                    // currentFrame = DecodeFrame();
                    currentFrameTimer = 0d;
                    // Debug.Log($"pts={pts}");
                    Debug.Log($"timeBase={timeBase.num}/{timeBase.den}");
                    Debug.Log($"timeBaseSeconds={timeBaseSeconds}");
                }
            }
        }

        public void Update(double timestamp)
        {
            pts = (long)(Math.Max(double.Epsilon, timestamp) / timeBaseSeconds);
            // currentFrameTimer = timestamp / timeBase.num;
            // pts = (long)(currentFrameTimer * timeBase.den);
        }

        /// <summary>
        /// Returns the current frame for the active pts, decoding if needed
        /// </summary>
        public AVFrame GetCurrentFrame()
        {
            while (pts >= currentPacket.pts || currentPacket.pts == ffmpeg.AV_NOPTS_VALUE)
            {
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

        public List<AVFrame> GetCurrentFrames()
        {
            List<AVFrame> frames = new List<AVFrame>();
            while (pts >= currentPacket.pts || currentPacket.pts == ffmpeg.AV_NOPTS_VALUE)
            {
                if (context.NextFrame(out AVPacket packet))
                {
                    AVFrame frame = DecodeMultiFrame();
                    if (frame.format != -1)
                    {
                        currentPacket = packet;
                        currentFrame = frame;
                        frames.Add(frame);
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
            decoder.Dispose();
            context.Dispose();
        }
    }
}
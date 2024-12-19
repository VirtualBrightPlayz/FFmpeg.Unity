using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFTexturePlayer : MonoBehaviour
    {
        public delegate void OnDisplayDelegate(Texture2D texture);

        public long pts;
        public event OnDisplayDelegate OnDisplay;
        private Texture2D image;
        private int frameWidth;
        private int frameHeight;
        private byte[] frameData = new byte[0];
        private byte[] backbuffer = new byte[0];
        private readonly Mutex mutex = new Mutex();

        public void PlayPacket(AVFrame frame)
        {
            pts = frame.pts;
            int len = frame.width * frame.height * 3;
            if (backbuffer.Length != len)
                backbuffer = new byte[len];
            if (SaveFrame(frame, backbuffer))
            {
                if (mutex.WaitOne())
                {
                    try
                    {
                        frameWidth = frame.width;
                        frameHeight = frame.height;
                        if (frameData.Length != len)
                            frameData = new byte[len];
                        Array.Copy(backbuffer, frameData, len);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        private void Update()
        {
            if (mutex.WaitOne(0))
            {
                try
                {
                    DisplayBytes(frameData, frameWidth, frameHeight);
                    OnDisplay?.Invoke(image);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private void DisplayBytes(byte[] data, int width, int height)
        {
            if (data == null || data.Length == 0)
                return;
            if (image == null)
                image = new Texture2D(16, 16, TextureFormat.RGB24, false);
            if (image.width != width || image.height != height)
                image.Reinitialize(width, height);
            var arr = image.GetRawTextureData<byte>();
            arr.CopyFrom(data);
            image.Apply(false);
        }

        #region Utils

        [ThreadStatic]
        private static byte[] line;

        public static unsafe bool SaveFrame(AVFrame frame, byte[] texture)
        {
            if (line == null)
            {
                line = new byte[4096 * 4096 * 6]; // TODO: is the buffer big enough?
            }
            if (frame.data[0] == null || frame.format == -1 || texture == null)
            {
                return false;
            }
            using var converter = new VideoFrameConverter(new System.Drawing.Size(frame.width, frame.height), (AVPixelFormat)frame.format, new System.Drawing.Size(frame.width, frame.height), AVPixelFormat.AV_PIX_FMT_RGB24);
            var convFrame = converter.Convert(frame);
            int len = frame.width * frame.height * 3;
            Marshal.Copy((IntPtr)convFrame.data[0], line, 0, len);
            Array.Copy(line, 0, texture, 0, len);
            return true;
        }

        #endregion
    }
}
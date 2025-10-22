using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFTexturePlayer : MonoBehaviour
    {
        public delegate void OnDisplayDelegate(Texture2D texture);

        public event OnDisplayDelegate OnDisplay;
        private Texture2D image;
        private int frameWidth;
        private int frameHeight;
        private long framePts;
        private byte[] frameData = new byte[0];
        private byte[] backBuffer = new byte[0];
        private readonly Mutex mutex = new Mutex();

        [Tooltip("Force output texture size to specified width. Set to 0 to use source width.")]
        public int imageWidth = 1280;

        [Tooltip("Force output texture size to specified height. Set to 0 to use source height.")]
        public int imageHeight = 720;

        [Tooltip("Flags whether the texture data should be flipped on the Y axis or not. Minor performance cost when enabled.")]
        public bool flipTexture = true;

        private NativeArray<byte> texData;

        [Header("Runtime Data")] public long pts;

        public void PlayPacket(AVFrame frame)
        {
            int width = imageWidth > 0 ? imageWidth : frame.width;
            int height = imageHeight > 0 ? imageHeight : frame.height;
            const byte pixelWidth = 3;
            int len = width * height * pixelWidth;
            if (backBuffer.Length != len)
                backBuffer = new byte[len];
            if (SaveFrame(frame, backBuffer, width, height))
            {
                if (mutex.WaitOne())
                {
                    try
                    {
                        framePts = frame.pts;
                        frameWidth = width;
                        frameHeight = height;
                        if (frameData.Length != len) frameData = new byte[len];
                        if (flipTexture) CopyAndFlip(backBuffer, frameData, frameWidth, frameHeight, pixelWidth);
                        else Array.Copy(backBuffer, frameData, len);
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
            if (framePts != pts && mutex.WaitOne(0))
            {
                try
                {
                    pts = framePts;
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
            {
                // image presumed to be changed. Cache new image information.
                image.Reinitialize(width, height);
                texData = image.GetRawTextureData<byte>();
            }

            texData.CopyFrom(data);
            image.Apply(false);
        }

        #region Utils

        [ThreadStatic] private static byte[] line;

        public static unsafe bool SaveFrame(AVFrame frame, byte[] texture, int width, int height)
        {
            if (line == null) line = new byte[4096 * 4096 * 6]; // TODO: is the buffer big enough?

            if (frame.data[0] == null || frame.format == -1 || texture == null)
                return false;

            using var converter = new VideoFrameConverter(new System.Drawing.Size(frame.width, frame.height), (AVPixelFormat)frame.format, new System.Drawing.Size(width, height), AVPixelFormat.AV_PIX_FMT_RGB24);
            var convFrame = converter.Convert(frame);
            int len = convFrame.width * convFrame.height * 3;
            Marshal.Copy((IntPtr)convFrame.data[0], line, 0, len);
            Array.Copy(line, 0, texture, 0, len);
            return true;
        }

        public static unsafe void CopyAndFlip(byte[] src, byte[] dst, int width, int height, int pixelWidth)
        {
            int rowWidth = width * pixelWidth;
            int heightLessOne = height - 1;
            fixed (byte* srcPtr = src)
            fixed (byte* dstPtr = dst)
            {
                // Copy rows in reverse order
                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = srcPtr + (y * rowWidth);
                    byte* dstRow = dstPtr + ((heightLessOne - y) * rowWidth);
                    UnsafeUtility.MemCpy(dstRow, srcRow, rowWidth);
                }
            }
        }

        #endregion
    }
}
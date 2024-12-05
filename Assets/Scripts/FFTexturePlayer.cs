using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFTexturePlayer : MonoBehaviour
    {
        public long pts;
        public MeshRenderer renderMesh;
        public int materialIndex = -1;
        private MaterialPropertyBlock propertyBlock;
        public Action<Texture2D> OnDisplay = null;
        private Texture2D image;
        private int framewidth;
        private int frameheight;
        private byte[] framedata = new byte[0];
        private Mutex mutex = new Mutex();

        public void PlayPacket(AVFrame frame)
        {
            pts = frame.pts;
            byte[] data = new byte[frame.width * frame.height * 3];
            if (SaveFrame(frame, data))
            {
                if (mutex.WaitOne())
                {
                    try
                    {
                        framewidth = frame.width;
                        frameheight = frame.height;
                        framedata = data;
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
                    DisplayBytes(framedata, framewidth, frameheight);
                    Display(image);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private void DisplayBytes(byte[] data, int framewidth, int frameheight)
        {
            if (data == null || data.Length == 0)
                return;
            if (image == null)
                image = new Texture2D(16, 16, TextureFormat.RGB24, false);
            if (image.width != framewidth || image.height != frameheight)
                image.Reinitialize(framewidth, frameheight);
            image.SetPixelData(data, 0);
            image.Apply(false);
        }

        private void Display(Texture2D texture)
        {
            if (OnDisplay == null)
            {
                if (propertyBlock == null)
                    propertyBlock = new MaterialPropertyBlock();
                if (texture != null)
                {
                    propertyBlock.SetTexture("_MainTex", texture);
                    propertyBlock.SetTexture("_EmissionMap", texture);
                }
                if (renderMesh != null)
                {
                    if (materialIndex == -1)
                        renderMesh.SetPropertyBlock(propertyBlock);
                    else
                        renderMesh.SetPropertyBlock(propertyBlock, materialIndex);
                }
            }
            else
            {
                OnDisplay.Invoke(texture);
            }
        }

        #region Utils

        [ThreadStatic]
        private static byte[] line;

        public unsafe static bool SaveFrame(AVFrame frame, byte[] texture)
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
            Marshal.Copy((IntPtr)convFrame.data[0], line, 0, frame.width * frame.height * 3);
            Array.Copy(line, 0, texture, 0, frame.width * frame.height * 3);
            return true;
        }

        #endregion
    }
}
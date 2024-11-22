using System;
using System.Runtime.InteropServices;
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

        public void PlayPacket(AVFrame frame)
        {
            pts = frame.pts;
            byte[] data = new byte[frame.width * frame.height * 3];
            if (SaveFrame(frame, data))
            {
                if (image == null)
                    image = new Texture2D(16, 16, TextureFormat.RGB24, false);
                if (image.width != frame.width || image.height != frame.height)
                    image.Reinitialize(frame.width, frame.height);
                image.SetPixelData(data, 0);
                image.Apply(false);
            }
            Display(image);
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
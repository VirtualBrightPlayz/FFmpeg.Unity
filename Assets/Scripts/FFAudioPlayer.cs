using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFAudioPlayer : MonoBehaviour
    {
        public long pts;
        // public long counter;
        public AudioSource source;
        private AudioClip clip;
        private float[] RingBuffer = new float[48_000];
        private int RingBufferPosition = 0;
        private int channels;
        private AVSampleFormat sampleFormat;
        private List<float> pcm = new List<float>();

        public void Init(int frequency, int channels, AVSampleFormat sampleFormat)
        {
            this.channels = channels;
            this.sampleFormat = sampleFormat;
            Debug.Log($"Freq={frequency}");
            clip = AudioClip.Create("BufferAudio", frequency * channels, channels, frequency, false);
            RingBuffer = new float[clip.samples];
            RingBufferPosition = 0;
            source.clip = clip;
            source.loop = true;
            source.Stop();
            source.Play();
        }

        public void PlayPackets(List<AVFrame> frames)
        {
            if (frames.Count == 0)
            {
                return;
            }
            pcm.Clear();
            foreach (var frame in frames)
            {
                QueuePacket(frame);
            }
            // Debug.Log($"frames={frames.Count} pcm={pcm.Count}");
            // counter += pcm.Count;
            FillBuffer();
        }

        private void FillBuffer()
        {
            int c = pcm.Count / channels;
            for (int i = 0; i < c; i++)
            {
                for (int j = 0; j < channels; j++)
                {
                    RingBuffer[RingBufferPosition] = pcm[i + c * j];
                    RingBufferPosition = (RingBufferPosition + 1) % RingBuffer.Length;
                }
            }
            if (clip != null)
            {
                clip.SetData(RingBuffer, 0);
            }
        }

        private unsafe void QueuePacket(AVFrame frame)
        {
            pts = frame.pts;
            for (uint ch = 0; ch < channels; ch++)
            {
                int size = ffmpeg.av_samples_get_buffer_size(null, 1, frame.nb_samples, sampleFormat, 1);
                if (size < 0)
                {
                    Debug.LogError("audio buffer size is less than zero");
                    continue;
                }
                byte[] backBuffer2 = new byte[size];
                float[] backBuffer3 = new float[size / sizeof(float)];
                Marshal.Copy((IntPtr)frame.data[ch], backBuffer2, 0, size);
                Buffer.BlockCopy(backBuffer2, 0, backBuffer3, 0, backBuffer2.Length);
                for (int i = 0; i < backBuffer3.Length; i++)
                {
                    pcm.Add(backBuffer3[i]);
                }
            }
        }
    }
}
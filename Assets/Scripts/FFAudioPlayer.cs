using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFAudioPlayer : MonoBehaviour
    {
        public long pts;
        public BufferAudioSource source;
        private AudioClip clip;
        private int channels;
        private AVSampleFormat sampleFormat;
        private List<float> pcm = new List<float>();

        public void Init(int frequency, int channels, AVSampleFormat sampleFormat)
        {
            this.channels = channels;
            this.sampleFormat = sampleFormat;
            Debug.Log($"Freq={frequency}");
            clip = AudioClip.Create("BufferAudio", frequency * channels, channels, frequency, false);
        }

        public void Pause()
        {
            source.audioSource.Pause();
        }

        public void Resume()
        {
            source.audioSource.UnPause();
        }

        public void PlayPackets(List<AVFrame> frames)
        {
            if (frames.Count == 0)
            {
                return;
            }
            foreach (var frame in frames)
            {
                QueuePacket(frame);
            }
        }

        private unsafe void QueuePacket(AVFrame frame)
        {
            pcm.Clear();
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
                break;
            }
            source.AddQueue(pcm.ToArray(), 1, clip.frequency);
        }
    }
}
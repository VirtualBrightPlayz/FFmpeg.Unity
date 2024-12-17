using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFAudioPlayer : MonoBehaviour
    {
        public delegate void OnResumeDelegate();

        public delegate void OnPauseDelegate();

        public delegate void OnSeekDelegate();

        public delegate void OnVolumeChangeDelegate(float volume);

        public delegate void AddQueueDelegate(float[] pcm, int channels, int frequency);

        public OnResumeDelegate OnResume;
        public OnPauseDelegate OnPause;
        public OnSeekDelegate OnSeek;
        public OnVolumeChangeDelegate OnVolumeChange;
        public AddQueueDelegate AddQueue;

        public long pts;
        public float bufferDelay = 1f;
        private AudioClip clip;
        private int channels;
        private int frequency;
        private AVSampleFormat sampleFormat;
        private List<float> pcm = new List<float>();

        public void Init(int frequency, int channels, AVSampleFormat sampleFormat)
        {
            this.channels = channels;
            this.sampleFormat = sampleFormat;
            this.frequency = frequency;
            Debug.Log($"Freq={frequency}");
            clip = AudioClip.Create("BufferAudio", frequency * channels, channels, frequency, false);
        }

        public void Pause() => OnPause?.Invoke();
        public void Resume() => OnResume?.Invoke();
        public void Seek() => OnSeek?.Invoke();
        public void SetVolume(float volume) => OnVolumeChange?.Invoke(volume);

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

            AddQueue?.Invoke(pcm.ToArray(), 1, frequency);
        }
    }
}
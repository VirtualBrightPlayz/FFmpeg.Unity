using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFPlayUnity : MonoBehaviour
    {
        public FFTimings videoTimings;
        public FFTimings audioTimings;

        private Thread thread;

        public delegate void OnEndReachedDelegate();

        public delegate void OnVideoEndReachedDelegate();

        public delegate void OnAudioEndReachedDelegate();

        public delegate void OnErrorDelegate();

        public delegate void OnMediaReadyDelegate();

        public OnEndReachedDelegate OnEndReached;
        public OnVideoEndReachedDelegate OnVideoEndReached;
        public OnAudioEndReachedDelegate OnAudioEndReached;
        public OnErrorDelegate OnError;
        public OnMediaReadyDelegate OnMediaReady;

        public double videoOffset = 0d;
        public double audioOffset = 0d;

        public FFTexturePlayer texturePlayer;
        public FFAudioPlayer audioPlayer;

        private double timeOffset = 0d;
        private double pauseTime = 0d;

        public bool IsPlaying { get; private set; } = false;
        public bool IsStream { get; private set; } = false;

        public bool IsPaused { get; private set; } = false;

        // public double timeAsDouble => Time.timeAsDouble;
        public double timeAsDouble { get; private set; }

        public double PlaybackTime => IsPaused ? pauseTime : timeAsDouble - timeOffset;

        public double VideoTime => timeAsDouble - timeOffset + videoOffset;
        public double AudioTime => timeAsDouble - timeOffset + audioOffset;

        public void Play(string url)
        {
            Play(url, url);
        }

        public void Play(Stream streamV, Stream streamA)
        {
            IsPlaying = false;
            StopThread();
            OnDestroy();
            videoTimings = new FFTimings(streamV, AVMediaType.AVMEDIA_TYPE_VIDEO);
            audioTimings = new FFTimings(streamA, AVMediaType.AVMEDIA_TYPE_AUDIO);
            Init();
        }

        public void Play(string urlV, string urlA)
        {
            IsPlaying = false;
            StopThread();
            OnDestroy();
            videoTimings = new FFTimings(urlV, AVMediaType.AVMEDIA_TYPE_VIDEO);
            audioTimings = new FFTimings(urlA, AVMediaType.AVMEDIA_TYPE_AUDIO);
            Init();
        }

        private void Init()
        {
            if (audioTimings.IsInputValid)
                audioPlayer.Init(audioTimings.decoder.SampleRate, audioTimings.decoder.Channels, audioTimings.decoder.SampleFormat);
            if (videoTimings.IsInputValid)
            {
                timeOffset = timeAsDouble - videoTimings.StartTime;
                IsStream = Math.Abs(videoTimings.StartTime) > 5d;
            }
            else
                timeOffset = timeAsDouble;
            if (!videoTimings.IsInputValid && !audioTimings.IsInputValid)
            {
                IsPaused = true;
                StopThread();
                Debug.LogError("AV not found");
                IsPlaying = false;
                OnError?.Invoke();
            }
            else
            {
                OnMediaReady?.Invoke();
                audioPlayer.Resume();
                RunThread();
                IsPlaying = true;
            }
        }

        public void Seek(double timestamp)
        {
            if (IsStream)
                return;
            StopThread();
            timeOffset = timeAsDouble - timestamp;
            pauseTime = timestamp;
            if (videoTimings != null)
            {
                videoTimings.Seek(VideoTime);
            }
            if (audioTimings != null)
            {
                audioTimings.Seek(AudioTime);
                audioTimings.GetFrames(10d);
                audioPlayer.Seek();
            }
            RunThread();
        }

        public double GetLength()
        {
            if (videoTimings != null && videoTimings.IsInputValid)
                return videoTimings.GetLength();
            if (audioTimings != null && audioTimings.IsInputValid)
                return audioTimings.GetLength();
            return 0d;
        }

        public void Pause()
        {
            if (IsPaused)
                return;
            pauseTime = PlaybackTime;
            audioPlayer.Pause();
            IsPaused = true;
            StopThread();
            IsPlaying = false;
        }

        public void Resume()
        {
            if (!IsPaused)
                return;
            StopThread();
            timeOffset = timeAsDouble - pauseTime;
            audioPlayer.Resume();
            IsPaused = false;
            RunThread();
            IsPlaying = true;
        }

        private void Update()
        {
            timeAsDouble = Time.timeAsDouble;
            if (!IsPaused)
            {
                if (!thread.IsAlive && IsPlaying)
                {
                    // StopThread();
                    // RunThread();
                }
                if (videoTimings != null)
                {
                    if (videoTimings.IsEndOfFile())
                    {
                        Pause();
                        OnVideoEndReached?.Invoke();
                        OnEndReached?.Invoke();
                    }
                }
                if (audioTimings != null)
                {
                    if (audioTimings.IsEndOfFile())
                    {
                        Pause();
                        OnAudioEndReached?.Invoke();
                        OnEndReached?.Invoke();
                    }
                }
            }
        }

        private void ThreadUpdate()
        {
            Debug.Log("ThreadUpdate Start");
            while (!IsPaused)
            {
                Thread.Sleep(3);
                try
                {
                    if (videoTimings != null)
                    {
                        videoTimings.Update(VideoTime);
                        texturePlayer.PlayPacket(videoTimings.GetFrame(250));
                    }

                    if (audioTimings != null)
                    {
                        audioTimings.Update(AudioTime);
                        audioPlayer.PlayPackets(audioTimings.GetFrames(audioPlayer.bufferDelay, 500));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    break;
                }
            }

            Debug.Log("ThreadUpdate Done");
        }

        private void OnDestroy()
        {
            videoTimings?.Dispose();
            videoTimings = null;
            audioTimings?.Dispose();
            audioTimings = null;
        }

        private void RunThread()
        {
            if (thread.IsAlive)
                throw new Exception();
            // if (thread.IsAlive() && thread.IsStarted())
                // StopThread();
            IsPaused = false;
            thread = new Thread(ThreadUpdate);
            thread.Start();
        }

        private void StopThread()
        {
            bool paused = IsPaused;
            IsPaused = true;
            if (thread.IsAlive)
                thread.Join();
            IsPaused = paused;
        }

        public void OnEnable()
        {
            thread = new Thread(ThreadUpdate);
        }

        public void OnDisable()
        {
            IsPaused = true;
            StopThread();
            OnDestroy();
        }
    }
}
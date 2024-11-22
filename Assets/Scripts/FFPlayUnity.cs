using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFPlayUnity : MonoBehaviour
    {
        public FFTimings videoTimings;
        public FFTimings audioTimings;

        public double videoOffset = 0d;
        public double audioOffset = 0d;

        public FFTexturePlayer texturePlayer;
        public FFAudioPlayer audioPlayer;

        private double timeOffset = 0d;
        private double pauseTime = 0d;

        public bool IsPaused { get; private set; } = false;

        public double PlaybackTime => IsPaused ? pauseTime : Time.timeAsDouble - timeOffset;

        public double VideoTime => Time.timeAsDouble - timeOffset + videoOffset;
        public double AudioTime => Time.timeAsDouble - timeOffset + audioOffset;

        public void Play(string url)
        {
            Play(url, url);
        }

        public void Play(string urlV, string urlA)
        {
            Resume();
            videoTimings = new FFTimings(urlV, AVMediaType.AVMEDIA_TYPE_VIDEO);
            audioTimings = new FFTimings(urlA, AVMediaType.AVMEDIA_TYPE_AUDIO);
            if (audioTimings.IsInputValid)
                audioPlayer.Init(audioTimings.decoder.SampleRate, audioTimings.decoder.Channels, audioTimings.decoder.SampleFormat);
            if (videoTimings.IsInputValid)
                timeOffset = Time.timeAsDouble - videoTimings.StartTime;
            else
                timeOffset = Time.timeAsDouble;
        }

        public void Seek(double timestamp)
        {
            timeOffset = Time.timeAsDouble - timestamp;
            if (videoTimings != null)
            {
                videoTimings.Seek(VideoTime);
            }
            if (audioTimings != null)
            {
                audioTimings.Seek(AudioTime);
            }
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
            pauseTime = PlaybackTime;
            audioPlayer.Pause();
            IsPaused = true;
        }

        public void Resume()
        {
            timeOffset = Time.timeAsDouble - pauseTime;
            audioPlayer.Resume();
            IsPaused = false;
        }

        private void Update()
        {
            if (!IsPaused)
            {
                if (videoTimings != null)
                {
                    videoTimings.Update(VideoTime);
                    texturePlayer.PlayPacket(videoTimings.GetCurrentFrame());
                }
                if (audioTimings != null)
                {
                    audioTimings.Update(AudioTime);
                    audioPlayer.PlayPackets(audioTimings.GetCurrentFrames());
                }
            }
        }

        private void OnDestroy()
        {
            videoTimings?.Dispose();
            videoTimings = null;
            audioTimings?.Dispose();
            audioTimings = null;
        }
    }
}
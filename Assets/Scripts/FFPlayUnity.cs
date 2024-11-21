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

        public void Play(string url)
        {
            Play(url, url);
        }

        public void Play(string urlV, string urlA)
        {
            DynamicallyLinkedBindings.Initialize();
            videoTimings = new FFTimings(urlV, AVMediaType.AVMEDIA_TYPE_VIDEO, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
            audioTimings = new FFTimings(urlA, AVMediaType.AVMEDIA_TYPE_AUDIO);
            audioPlayer.Init(audioTimings.decoder.SampleRate, audioTimings.decoder.Channels, audioTimings.decoder.SampleFormat);
            timeOffset = Time.timeAsDouble;
        }

        private void Update()
        {
            if (videoTimings != null)
            {
                videoTimings.Update(Time.timeAsDouble - timeOffset + videoOffset);
                texturePlayer.PlayPacket(videoTimings.GetCurrentFrame());
            }
            if (audioTimings != null)
            {
                audioTimings.Update(Time.timeAsDouble - timeOffset + audioOffset);
                audioPlayer.PlayPackets(audioTimings.GetCurrentFrames());
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
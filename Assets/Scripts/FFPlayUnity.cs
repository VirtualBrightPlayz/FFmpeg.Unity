using UnityEngine;

namespace FFmpeg.Unity
{
    public class FFPlayUnity : MonoBehaviour
    {
        public FFTimings videoTimings;
        public FFTimings audioTimings;

        public double audioOffset = 0d;

        public FFTexturePlayer texturePlayer;
        public FFAudioPlayer audioPlayer;

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
        }

        private void Start()
        {
            Play("https://virtualwebsite.net/files/hidden/memes/crack.mp4");
        }

        private void Update()
        {
            videoTimings.Update(Time.timeAsDouble);
            audioTimings.Update(Time.timeAsDouble + audioOffset);
            texturePlayer.PlayPacket(videoTimings.GetCurrentFrame());
            audioPlayer.PlayPackets(audioTimings.GetCurrentFrames());
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
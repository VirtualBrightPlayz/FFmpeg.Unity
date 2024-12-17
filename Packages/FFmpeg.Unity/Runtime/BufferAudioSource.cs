using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BufferAudioSource : MonoBehaviour
{
    public struct BufferValue
    {
        public float[] pcm;
        public int channels;
        public int frequency;
    }

    private float[] RingBuffer = null;
    private int RingBufferPosition = 0;
    private int PlaybackPosition = 0;

    private bool shouldStop = false;
    private int stopTime = 0;
    private float stopTimer = 0;
    private int lastTimeSamples = 0;
    private int maxEmptyReads = 0;
    private AudioClip clip = null;
    public FFmpeg.Unity.FFAudioPlayer audioPlayer;
    [HideInInspector] public AudioSource audioSource;

    [SerializeField] private bool lockVolume;
    [SerializeField] private bool volumeControlsMute;

    private float[] spectrum = new float[1024];

    private ConcurrentQueue<BufferValue> bufferQueue = new ConcurrentQueue<BufferValue>();
    private int clipchannels;
    private int clipfrequency;

    private void Start()
    {
        if (!audioPlayer) audioPlayer = GetComponentInParent<FFmpeg.Unity.FFAudioPlayer>(true);
        if (!audioPlayer)
        {
            Debug.LogWarning("No FFAudioPlayer found.");
        }
        audioSource = GetComponent<AudioSource>();
        audioPlayer.OnPause += Pause;
        audioPlayer.OnResume += Resume;
        audioPlayer.OnSeek += Seek;
        audioPlayer.OnVolumeChange += SetVolume;
        audioPlayer.AddQueue += AddQueue;
    }

    private void OnDestroy()
    {
        audioPlayer.OnPause -= Pause;
        audioPlayer.OnResume -= Resume;
        audioPlayer.OnSeek -= Seek;
        audioPlayer.OnVolumeChange -= SetVolume;
        audioPlayer.AddQueue -= AddQueue;
    }

    public void Pause()
    {
        audioSource.Pause();
    }

    public void Resume()
    {
        audioSource.UnPause();
    }

    public void Seek()
    {
        Stop();
    }

    private void SetVolume(float volume)
    {
        if (!lockVolume) audioSource.volume = volume;
        if (volumeControlsMute) audioSource.mute = volume == 0;
    }

    private void Update()
    {
        while (bufferQueue.TryDequeue(out var result))
        {
            TryCreateNewClip(result.pcm, result.channels, result.frequency, false);
        }

        if (clip == null)
            return;
        /*
        int currentDeltaSamples = audioSource.timeSamples - lastTimeSamples;
        if (audioSource.timeSamples < lastTimeSamples)
            currentDeltaSamples = clip.samples - audioSource.timeSamples;
        */
        stopTimer -= Time.deltaTime;
        if (stopTimer <= 0)
        {
            shouldStop = true;
        }
        lastTimeSamples = audioSource.timeSamples;

        if (shouldStop && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    private void PcmCallback(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = RingBuffer[PlaybackPosition];
            // RingBuffer[PlaybackPosition] = 0f;
            PlaybackPosition = (PlaybackPosition + 1) % RingBuffer.Length;
        }
    }

    private void AddRingBuffer(float[] pcm)
    {
        if (RingBuffer == null || clipchannels == 0 || clipfrequency == 0)
            return;
        shouldStop = false;
        stopTime += pcm.Length;
        stopTimer += (float)pcm.Length / clipfrequency / clipchannels;
        for (int i = 0; i < pcm.Length; i++)
        {
            RingBuffer[RingBufferPosition] = pcm[i];
            RingBufferPosition = (RingBufferPosition + 1) % RingBuffer.Length;
        }
    }

    public void Stop()
    {
        shouldStop = true;
        audioSource.Stop();
        RingBufferPosition = 0;
        PlaybackPosition = 0;
    }

    private void RunOnMain(float[] pcm, int channels, int frequency)
    {
        bufferQueue.Enqueue(new BufferValue()
        {
            pcm = pcm,
            channels = channels,
            frequency = frequency,
        });
    }

    private void TryCreateNewClip(float[] pcm, int channels, int frequency, bool newClip2)
    {
        bool newClip = false;
        if (clip == null || clipchannels != channels || clipfrequency != frequency)
        {
            maxEmptyReads = (int)(frequency * channels * audioPlayer.bufferDelay);
            newClip = true;
        }
        AddRingBuffer(pcm);
        if (/*shouldStop &&*/ !audioSource.isPlaying)
        {
            newClip = true;
        }
        if (!newClip)
            return;
        Debug.Log("New clip");
        shouldStop = false;
        clipchannels = channels;
        clipfrequency = frequency;
        RingBuffer = new float[maxEmptyReads];
        clip = AudioClip.Create("BufferAudio", RingBuffer.Length, channels, frequency, true, PcmCallback);
        audioSource.clip = clip;
        audioSource.loop = true;
        audioSource.Stop();
        RingBufferPosition = 0;
        PlaybackPosition = 0;
        stopTime = RingBufferPosition + pcm.Length;
        stopTimer = audioPlayer.bufferDelay;
        AddRingBuffer(pcm);
        audioSource.Play();
    }

    public void AddQueue(float[] pcm, int channels, int frequency)
    {
        bool newClip = false;
        /*if (clip == null || clipchannels != channels || clipfrequency != frequency)
        {
            maxEmptyReads = (int)(frequency * channels * bufferDelay);
            newClip = true;
        }
        AddRingBuffer(pcm);*/
        // RunOnMain(new Action<float[], int, int, bool>(TryCreateNewClip), pcm, channels, frequency, newClip);
        RunOnMain(pcm, channels, frequency);
    }
}
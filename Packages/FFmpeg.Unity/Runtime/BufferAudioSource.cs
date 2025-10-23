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
        public double dspTime;
        public float[] pcm;
        public int channels;
        public int frequency;
    }

    private float[] RingBuffer = null;
    private long RingBufferPosition = 0;
    private long PlaybackPosition = 0;
    private readonly Mutex mutex = new Mutex();

    private bool shouldStop = false;
    private float stopTimer = 0;
    private int lastTimeSamples = 0;
    private int maxEmptyReads = 0;
    private AudioClip clip = null;
    public FFmpeg.Unity.FFAudioPlayer audioPlayer;
    [HideInInspector] public AudioSource audioSource;

    public bool lockVolume;
    public bool volumeControlsMute;

    public float bufferDelay = 0f;
    private float dspBufferDelay = 0f;
    private int dspBufferSize = 0;

    private ConcurrentQueue<BufferValue> bufferQueue = new ConcurrentQueue<BufferValue>();
    private int clipchannels;
    private int clipfrequency;

    private void Start()
    {
        AudioSettings.GetDSPBufferSize(out int len, out _);
        var conf = AudioSettings.GetConfiguration();
        // TODO: 2 = dual channel?
        dspBufferSize = len;
        float delay = (float)len / AudioSettings.outputSampleRate / 2;
        dspBufferDelay = delay;
        // bufferDelay += delay;

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
        if (!shouldStop)
            stopTimer -= Time.deltaTime;
        if (stopTimer <= 0)
        {
            shouldStop = true;
            stopTimer = 0;
            // stopTimer += audioPlayer.bufferSize;
        }
        lastTimeSamples = audioSource.timeSamples;

        if (shouldStop && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    private void PcmCallback(float[] data)
    {
        if (mutex.WaitOne())
        {
            try
            {
                if (PlaybackPosition > RingBufferPosition)
                {
                    // we are ahead, fill data with silence
                    // Debug.LogWarning($"PlaybackPosition={PlaybackPosition} RingBufferPosition={RingBufferPosition}");
                    Array.Fill(data, 0f);
                }
                else
                {
                    long pos = PlaybackPosition;
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = RingBuffer[pos % RingBuffer.Length];
                        RingBuffer[pos % RingBuffer.Length] = 0f;
                        pos++;
                    }
                    PlaybackPosition = pos;
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private float[] pcmBuf = new float[0];

    private void AddRingBuffer(ICollection<float> pcm, int frequency, int channels)
    {
        if (RingBuffer == null)
            return;
        // if (RingBufferPosition - (long)(frequency * channels * dspBufferDelay) > PlaybackPosition)
        // {
        //     Debug.LogWarning($"PlaybackPosition={PlaybackPosition} RingBufferPosition={RingBufferPosition}");
        //     return;
        // }
        if (pcmBuf.Length != RingBuffer.Length)
            pcmBuf = new float[RingBuffer.Length];
        pcm.CopyTo(pcmBuf, 0);
        if (mutex.WaitOne())
        {
            try
            {
                shouldStop = false;
                stopTimer += (float)pcm.Count / frequency / channels;
                long pos = RingBufferPosition + (long)(frequency * channels * bufferDelay);
                for (int i = 0; i < pcm.Count; i++)
                {
                    RingBuffer[pos % RingBuffer.Length] = pcmBuf[i];
                    pos++;
                }
                RingBufferPosition += pcm.Count;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public void Stop()
    {
        // Debug.Log($"PlaybackPosition={PlaybackPosition} RingBufferPosition={RingBufferPosition}");
        shouldStop = true;
        audioSource.Stop();
        audioSource.clip = null;
        if (RingBuffer != null)
            Array.Fill(RingBuffer, 0f);
    }

    private void ResetRingBuffer(int frequency, int channels)
    {
        // Debug.Log($"PlaybackPosition={PlaybackPosition} RingBufferPosition={RingBufferPosition}");
        maxEmptyReads = (int)(frequency * channels * audioPlayer.bufferSize);
        if (RingBuffer == null)
        {
            // RingBufferPosition = (long)(frequency * channels * bufferDelay);// % RingBuffer.Length;
            // PlaybackPosition = (int)(frequency * channels * AudioSettings.dspTime) % RingBuffer.Length;
            RingBufferPosition = 0;
            PlaybackPosition = 0;
        }
        RingBuffer = new float[maxEmptyReads];
    }

    private void RunOnMain(float[] pcm, int channels, int frequency)
    {
        bufferQueue.Enqueue(new BufferValue()
        {
            dspTime = AudioSettings.dspTime,
            pcm = pcm,
            channels = channels,
            frequency = frequency,
        });
    }

    private void TryCreateNewClip(float[] pcm, int channels, int frequency, bool thread)
    {
        bool newClip = false;
        if (clip == null || clipchannels != channels || clipfrequency != frequency)
        {
            maxEmptyReads = (int)(frequency * channels * audioPlayer.bufferSize);
            newClip = true;
        }
        if (thread)
        {
            if (newClip)
            {
                RunOnMain(pcm, channels, frequency);
            }
            else
            {
                // AddRingBuffer(pcm);
            }
            return;
        }
        // AddRingBuffer(pcm);
        if (!audioSource.isPlaying)
        {
            newClip = true;
        }
        if (!newClip)
            return;
        Debug.Log("New clip");
        shouldStop = false;
        clipchannels = channels;
        clipfrequency = frequency;
        if (mutex.WaitOne())
        {
            try
            {
                ResetRingBuffer(frequency, channels);
                clip = AudioClip.Create("BufferAudio", RingBuffer.Length, channels, frequency, true, PcmCallback);
                audioSource.clip = clip;
                audioSource.loop = true;
                audioSource.Stop();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        stopTimer = audioPlayer.bufferSize;
        // AddRingBuffer(pcm);
        audioSource.Play();
    }

    public void AddQueue(ICollection<float> pcm, int channels, int frequency)
    {
        maxEmptyReads = (int)(frequency * channels * audioPlayer.bufferSize);
        if (RingBuffer == null || RingBuffer.Length != maxEmptyReads)
        {
            if (mutex.WaitOne())
            {
                try
                {
                    ResetRingBuffer(frequency, channels);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
        AddRingBuffer(pcm, channels, frequency);
        RunOnMain(Array.Empty<float>(), channels, frequency);
    }
}
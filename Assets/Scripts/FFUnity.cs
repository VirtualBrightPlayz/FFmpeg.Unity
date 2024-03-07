using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.Unity.Helpers;
using UnityEngine;
using UnityEngine.Profiling;

namespace FFmpeg.Unity
{
    public class FFUnity : MonoBehaviour
    {
        public bool IsPaused => _paused;

        public struct TexData
        {
            public double time;
            public byte[] data;
            public int w;
            public int h;
        }

        public Renderer renderMesh;
        public AudioSource source;
        public int materialIndex = -1;

        [SerializeField]
        public bool _paused;
        private bool _wasPaused = false;

        // time controls
        [SerializeField]
        public double _offset = 0.0d;
        private double _prevTime = 0.0d;
        private double _timeOffset = 0.0d;
        private Stopwatch _videoWatch;
        private double _startTime;
        private double? _lastPts;
        private int? _lastPts2;
        public double timer;
        public double PlaybackTime => _lastVideoTex?.pts ?? _elapsedOffset;
        // public double PlaybackTime => _streamCtx != null && _streamCtx.TryGetTimeBase(out double timebase) ? _lastVideoTex.pts : 0d;
        public double _elapsedTotalSeconds => _videoWatch?.Elapsed.TotalSeconds ?? 0d;
        public double _elapsedOffset => _elapsedTotalSeconds - _timeOffset;
        private double _elapsedPTSOffset => _streamCtx.TryGetPts(out var pts) && _streamCtx.TryGetStart(out var strt) ? _elapsedOffset * pts + strt : default;

        // buffer controls
        private int _videoBufferCount = 1024;
        private int _audioBufferCount = 1;
        [SerializeField]
        // [Range(0, 1)]
        public double _videoTimeBuffer = 0d;
        [SerializeField]
        public double _videoSkipBuffer = 0d;
        [SerializeField]
        // [Range(0, 1)]
        public double _audioTimeBuffer = 0d;
        [SerializeField]
        public double _audioSkipBuffer = 0d;
        private int _audioBufferSize = 256;

        // unity assets
        private Queue<TexturePool.TexturePoolState> _videoTextures;
        private TexturePool.TexturePoolState _lastVideoTex;
        private TexturePool _texturePool;
        private TexData? _lastTexData;
        private AudioClip _audioClip;

        // decoders
        [SerializeField]
        public AVHWDeviceType _hwType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        private FFmpegCtx _streamCtx;
        private FFmpegCtx _streamVideoCtx;
        private FFmpegCtx _streamAudioCtx;
        private VideoStreamDecoder _videoDecoder;
        private VideoStreamDecoder _audioDecoder;
        private VideoFrameConverter _videoConverter;
        private Queue<TexData> _videoFrameClones;
        private Mutex _videoMutex = new Mutex();
        private Thread _decodeThread;
        private Mutex _audioLocker = new Mutex();
        private Queue<float> _audioStream;
        private MemoryStream _audioMemStream;

        // buffers
        private AVFrame[] _videoFrames;
        private AVFrame[] _audioFrames;
        private int _videoDisplayIndex = 0;
        private int _audioDisplayIndex = 0;
        private int _videoWriteIndex = 0;
        private int _audioWriteIndex = 0;

        // logging
        public Action<object> Log = UnityEngine.Debug.Log;
        public Action<object> LogWarning = UnityEngine.Debug.LogWarning;
        public Action<object> LogError = UnityEngine.Debug.LogError;

        private void OnEnable()
        {
            _paused = true;
        }

        private void OnDisable()
        {
            _paused = true;
        }

        public void Seek(double seek)
        {
            Log(nameof(Seek));
            _decodeThread?.Abort();
            source.Stop();
            // if (_audioLocker.WaitOne())
            {
                _audioMemStream.Position = 0;
                _audioStream.Clear();
                // _audioLocker.ReleaseMutex();
            }
            // if (_videoMutex.WaitOne())
            {
                _videoFrameClones.Clear();
                foreach (var tex in _videoTextures)
                {
                    _texturePool.Release(tex);
                }
                // _texturePool.Dispose();
                // _texturePool = new TexturePool(_videoBufferCount);
                _videoTextures.Clear();
                _lastVideoTex = null;
                _lastTexData = null;
                // _videoMutex.ReleaseMutex();
            }
            _videoWatch.Restart();
            ResetTimers();
            _timeOffset = -seek;
            // FillVideoBuffers(true);
            _prevTime = _offset;
            _lastPts = null;
            _lastPts2 = null;
            if (false) // isStream
            {
                _timeOffset = -_videoDecoder.GetTime(_videoFrames[0]);
            }
            _streamCtx.Seek(seek);
            FillVideoBuffers(false);
            source.clip = _audioClip;
            source.Play();
            // source.PlayScheduled(_startTime);
            StartDecodeThread();
        }

        public void Play(Stream video)
        {
            DynamicallyLinkedBindings.Initialize();
            _decodeThread?.Abort();
            _videoDecoder?.Dispose();
            _audioDecoder?.Dispose();
            _streamCtx?.Dispose();
            _streamCtx = new FFmpegCtx(video);
            Init();
        }

        public void Play(string url)
        {
            DynamicallyLinkedBindings.Initialize();
            _decodeThread?.Abort();
            _videoDecoder?.Dispose();
            _audioDecoder?.Dispose();
            _streamCtx?.Dispose();
            _streamCtx = new FFmpegCtx(url);
            Init();
        }

        public void Resume()
        {
            _paused = false;
        }

        public void Pause()
        {
            _paused = true;
        }

        private void ResetTimers()
        {
            // reset index counters and timers
            _videoDisplayIndex = 0;
            _audioDisplayIndex = 0;
            _videoWriteIndex = 0;
            _audioWriteIndex = 0;
            _lastPts = null;
            _lastPts2 = null;
            _offset = 0d;
            _prevTime = 0d;
            _timeOffset = 0d;
            timer = 0d;
        }

        private void Init()
        {
            _paused = true;

            // Stopwatches are more accurate than Time.timeAsDouble(?)
            _videoWatch = new Stopwatch();

            // pre-allocate buffers, prevent the C# GC from using CPU
            _texturePool = new TexturePool(_videoBufferCount);
            _videoTextures = new Queue<TexturePool.TexturePoolState>(_videoBufferCount);
            _audioClip = null; // don't create audio clip yet, we have nothing to play.
            _videoFrames = new AVFrame[_videoBufferCount];
            _videoFrameClones = new Queue<TexData>(_videoBufferCount);
            _audioFrames = new AVFrame[_audioBufferCount];
            _audioMemStream = new MemoryStream();
            _audioStream = new Queue<float>(_audioBufferSize * 4);

            ResetTimers();
            _lastVideoTex = null;
            _lastTexData = null;

            // init decoders
            _videoMutex = new Mutex(false, "Video Mutex");
            _videoDecoder = new VideoStreamDecoder(_streamCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, _hwType);
            _audioLocker = new Mutex(false, "Audio Mutex");
            _audioDecoder = new VideoStreamDecoder(_streamCtx, AVMediaType.AVMEDIA_TYPE_AUDIO);
            _paused = false;
            Seek(0.001d);
            // _videoWatch.Restart();
            _startTime = AudioSettings.dspTime;
            _audioClip = AudioClip.Create($"{name}-AudioClip", _audioBufferSize * _audioDecoder.Channels, _audioDecoder.Channels, _audioDecoder.SampleRate, true, AudioCallback, AudioPosCallback);
            source.clip = _audioClip;
            source.Play();
            // source.PlayScheduled(_startTime);
            Log(nameof(Play));
        }

        private void Update()
        {
            if (_videoWatch == null)
                return;
            
            if (_streamCtx.EndReached && !_paused)
            {
                Pause();
            }
            
            // if (_streamCtx.TryGetTime(out var time1) && PlaybackTime - time1 > 1)
            {
                // Log(_streamCtx.GetTime());
                // Seek(PlaybackTime);
            }

            if (_offset != _prevTime)
            {
                // Seek(_offset);
            }

            if (!_paused)
            {
                // _offset += Time.deltaTime;
                _offset = _elapsedOffset;
                if (!_videoWatch.IsRunning)
                {
                    _videoWatch.Start();
                    _startTime = AudioSettings.dspTime;
                    source.UnPause();
                }
            }
            else
            {
                if (_videoWatch.IsRunning)
                {
                    _videoWatch.Stop();
                    source.Pause();
                }
                _startTime = AudioSettings.dspTime;
            }

            if (!_paused)
            {
                if (_decodeThread == null || !_decodeThread.IsAlive)
                    StartDecodeThread();
                // FillVideoBuffers(true);

                int idx = _videoDisplayIndex;
                if (_videoMutex.WaitOne(1))
                {
                    UpdateVideoFromClones(idx);
                    _videoMutex.ReleaseMutex();
                }
                // timer += Time.deltaTime;
                if (_streamCtx.TryGetFps(_videoDecoder, out double fps))
                {
                    // while (timer >= 0)
                    while (_elapsedOffset - timer >= 1d / fps)
                    {
                        // timer -= 1d / fps;
                        timer += 1d / fps;
                        // if (_lastVideoTex == null || /*(_elapsedOffset - PlaybackTime) <= 0 ||*/ (_elapsedOffset - PlaybackTime) > _videoTimeBuffer)
                            Present(idx);
                    }
                }

                if (_streamCtx.TryGetTime(_videoDecoder, _videoFrames[idx], out var time2))
                {
                    // _lastPts = time2;
                }
            }

            _prevTime = _offset;
            _wasPaused = _paused;
        }

        private void Update_Thread()
        {
            _streamCtx.NextFrame(out _);
            while (!_paused)
            {
                try
                {
                    FillVideoBuffers(false);
                    Thread.Sleep(1);
                    Thread.Yield();
                }
                catch (Exception e)
                {
                    // _videoWatch.Stop();
                    LogError(e);
                    Pause();
                    // Thread.Sleep(1000);
                }
            }
            Log("AV Thread stopped.");
            _videoWatch.Stop();
            _paused = true;
        }

        private void StartDecodeThread()
        {
            _decodeThread = new Thread(() => Update_Thread());
            _decodeThread.Name = $"AV Decode Thread {name}";
            _decodeThread.Start();
        }

        #region Callbacks
        private void Present(int idx)
        {
            if (_videoTextures.Count == 0)
            {
                // LogWarning("Buffer is dry!");
                // _lastPts2 = null;
                return;
            }
            if (_streamCtx.TryGetPts(_videoDecoder, out double pts))
                _lastPts2 = (int)_elapsedOffset;
            // Destroy(_lastVideoTex);
            var tex = _videoTextures.Dequeue();
            if (tex != _lastVideoTex)
                _texturePool.Release(_lastVideoTex);
            _lastVideoTex = tex;
            if (materialIndex == -1)
                renderMesh.material.mainTexture = _lastVideoTex.texture;
            else
                renderMesh.materials[materialIndex].mainTexture = _lastVideoTex.texture;
        }

        private void AudioPosCallback(int pos)
        {
        }

        private bool _lastAudioRead = false;
        private int _audioMissCount = 0;

        private unsafe void AudioCallback(float[] data)
        {
            byte[] bytes = new byte[data.Length * sizeof(float)];
            if (_audioLocker.WaitOne(0))
            {
                // /*
                if (_audioStream.Count < data.Length)
                {
                    _lastAudioRead = false;
                    // _audioLocker.ReleaseMutex();
                    // return;
                }
                for (int i = 0; i < data.Length; i++)
                {
                    if (_audioStream.Count > 0)
                        data[i] = _audioStream.Dequeue();
                    else
                        data[i] = 0;
                }
                // */
                // _audioMemStream.Read(bytes);
                _audioLocker.ReleaseMutex();
            }
            // Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
        }
        #endregion

        #region Buffer Handling
        private void FillVideoBuffers(bool mainThread)
        {
            if (_streamCtx == null)
                return;
            bool found = false;
            int k = 0;
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            // for (int i = 0; i < 1; i++)
            // for (int i = 0; i < _videoBufferCount; i++)
            if (!_streamCtx.TryGetFps(_videoDecoder, out var fps))
                return;
            fps *= 20;
            // fps = 120;
            while (k < fps && sw.ElapsedMilliseconds <= 1000)
            {
                k++;
                if (_paused)
                    break;
                double timeBuffer = 0.5d;
                double timeBufferSkip = 0.15d;
                double pts = default;
                double time = default;
                if (_videoDecoder.CanDecode() && _streamCtx.TryGetTime(_videoDecoder, out time))
                {
                    if (time - _videoTimeBuffer > _elapsedOffset)
                        break;
                    else if (time + _videoSkipBuffer < _elapsedOffset)
                    {
                        _streamCtx.NextFrame(out _);
                        continue;
                    }
                }
                if (_audioDecoder.CanDecode() && _streamCtx.TryGetTime(_audioDecoder, out time))
                {
                    if (time - _audioTimeBuffer > _elapsedOffset)
                        break;
                    else if (time + _audioSkipBuffer < _elapsedOffset)
                    {
                        _streamCtx.NextFrame(out _);
                        continue;
                    }
                }
                if (_streamCtx.NextFrame(out _))
                {
                    int vid = _videoDecoder.Decode(out AVFrame vFrame);
                    int aud = _audioDecoder.Decode(out AVFrame aFrame);
                    found = false;
                    switch (vid)
                    {
                        case 0:
                            _videoFrames[_videoWriteIndex % _videoFrames.Length] = vFrame;
                            if (mainThread)
                            {
                                UpdateVideo(_videoWriteIndex % _videoFrames.Length);
                            }
                            else
                            {
                                // if (_videoTextures.Count <= _videoBufferCount)
                                {
                                    if (_videoMutex.WaitOne(1))
                                    {
                                        byte[] frameClone = new byte[vFrame.width * vFrame.height * 3];
                                        if (!SaveFrame(vFrame, vFrame.width, vFrame.height, frameClone, _videoDecoder.HWPixelFormat))
                                        {
                                            LogError("Could not save frame");
                                            _videoWriteIndex--;
                                        }
                                        else
                                        {
                                            _streamCtx.TryGetTime(_videoDecoder, vFrame, out time);
                                            _lastPts = time;
                                            _videoFrameClones.Enqueue(new TexData()
                                            {
                                                time = time,
                                                data = frameClone,
                                                w = vFrame.width,
                                                h = vFrame.height,
                                            });
                                        }
                                        _videoMutex.ReleaseMutex();
                                    }
                                }
                            }
                            _videoWriteIndex++;
                            found = false;
                            break;
                        case 1:
                            found = true;
                            break;
                    }
                    switch (aud)
                    {
                        case 0:
                            _audioFrames[_audioWriteIndex % _audioFrames.Length] = aFrame;
                            UpdateAudio(_audioWriteIndex % _audioFrames.Length);
                            _audioWriteIndex++;
                            found = false;
                            break;
                        case 1:
                            found = true;
                            break;
                    }
                    if (found)
                        k--;
                }
                else
                    break;
            }
            if (k >= fps)
                LogWarning("Max while true reached!");
        }

#if false
        private void UpdateVideoBuffer(bool mainThread)
        {
            if (_videoDecoder == null)
                return;
            Profiler.BeginSample(nameof(UpdateVideoBuffer), this);
            double offset = _elapsedTotalSeconds - _timeOffset;

            int j = 0;
            while (offset + _videoTimeBuffer > _videoDecoder.GetTime(_videoFrames[_videoDisplayIndex]) && j < 60)
            {
                j++;
                AVFrame frame;
                if (_videoSkipBuffer > 0 && offset + _videoTimeBuffer - _videoDecoder.GetTime(_videoFrames[_videoDisplayIndex]) > _videoSkipBuffer)
                {
                    Profiler.BeginSample("Video Frame Skip", this);
                    LogWarning("Video Frame Skip");
                    _videoDecoder.TrySkipNextFrame(out long pts);
                    // if (_videoMutex.WaitOne(10))
                    {
                        _videoFrames[_videoDisplayIndex].pts = pts;
                        // _videoMutex.ReleaseMutex();
                    }
                    Profiler.EndSample();
                    continue;
                }
                Profiler.BeginSample("Video Decode", this);
                if (_paused || !_videoDecoder.TryDecodeNextFrame(out frame))
                {
                    // LogError($"Failed to decode frame {offset + _videoTimeBuffer} {_videoDecoder.GetTime(_videoFrames[_videoDisplayIndex])}!");
                    Profiler.EndSample();
                    _paused = true;
                    break;
                }
                Profiler.EndSample();
                // if (_videoMutex.WaitOne())
                {
                    int idx = _videoDisplayIndex;
                    _videoDisplayIndex = (_videoDisplayIndex + 1) % _videoBufferCount;
                    _videoFrames[_videoDisplayIndex] = frame;
                    if (mainThread)
                    {
                        UpdateVideo(_videoDisplayIndex);
                    }
                    else if (!SaveFrame(frame, frame.width, frame.height, _videoFrameClones[_videoDisplayIndex], _videoDecoder.HWPixelFormat))
                    {
                        LogError("Could not save frame");
                        _videoDisplayIndex = idx;
                    }
                    // _videoMutex.ReleaseMutex();
                }
            }
            // Debug.Assert(j < 60);
            Profiler.EndSample();
        }

        private void UpdateAudioBuffer()
        {
            if (_audioDecoder == null)
                return;
            Profiler.BeginSample(nameof(UpdateAudioBuffer), this);
            double offset = _elapsedTotalSeconds - _timeOffset;

            int j = 0;
            bool found = false;
            while (offset + _audioTimeBuffer > _audioDecoder.GetTime(_audioFrames[_audioDisplayIndex]) && j < 60)
            {
                j++;
                AVFrame frame;
                if (_audioSkipBuffer > 0 && offset + _audioTimeBuffer - _audioDecoder.GetTime(_audioFrames[_audioDisplayIndex]) > _audioSkipBuffer)
                {
                    Profiler.BeginSample("Audio Frame Skip", this);
                    LogWarning("Audio Frame Skip");
                    _audioDecoder.TrySkipNextFrame(out long pts);
                    _audioFrames[_audioDisplayIndex].pts = pts;
                    Profiler.EndSample();
                    continue;
                }
                Profiler.BeginSample("Audio Decode", this);
                if (_paused || !_audioDecoder.TryDecodeNextFrame(out frame))
                {
                    Profiler.EndSample();
                    _paused = true;
                    break;
                }
                Profiler.EndSample();
                found = true;
                _audioDisplayIndex = (_audioDisplayIndex + 1) % _audioBufferCount;
                _audioFrames[_audioDisplayIndex] = frame;
            }
            if (found && j < 60)
                UpdateAudio(_audioDisplayIndex);
            Profiler.EndSample();
        }
#endif
        #endregion

        #region Frame Handling
        private unsafe Texture2D UpdateVideo(int idx)
        {
            Profiler.BeginSample(nameof(UpdateVideo), this);
            AVFrame videoFrame;
            videoFrame = _videoFrames[idx];
            if (videoFrame.data[0] == null)
            {
                Profiler.EndSample();
                return null;
            }
            // if (_videoTextures[idx] == null)
                // _videoTextures[idx] = SaveFrame(videoFrame, videoFrame.width, videoFrame.height, _videoDecoder.HWPixelFormat);
            // else
                // SaveFrame(videoFrame, videoFrame.width, videoFrame.height, _videoTextures[idx], _videoDecoder.HWPixelFormat);
            var tex = _texturePool.Get();
            if (tex.texture == null)
            {
                tex.texture = SaveFrame(videoFrame, videoFrame.width, videoFrame.height, _videoDecoder.HWPixelFormat);
            }
            else
                SaveFrame(videoFrame, videoFrame.width, videoFrame.height, tex.texture, _videoDecoder.HWPixelFormat);
            tex.texture.name = $"{name}-Texture2D-{idx}";
            // if (_videoTextures.Count > _videoBufferCount)
            //     LogWarning("Warn");
            _videoTextures.Enqueue(tex);
            Profiler.EndSample();
            return tex.texture;
        }

        private unsafe void UpdateVideoFromClones(int idx)
        {
            Profiler.BeginSample(nameof(UpdateVideoFromClones), this);
            if (_videoFrameClones.Count == 0)
            {
                // LogWarning("Buffer is dry!");
                Profiler.EndSample();
                return;
            }
            TexData videoFrame = _videoFrameClones.Peek();
            /*
            if (Math.Abs(videoFrame.time - _elapsedOffset) > _videoTimeBuffer)
            {
                _videoFrameClones.Dequeue();
                // _videoFrameClones.Clear();
                Profiler.EndSample();
                return;
            }
            if (videoFrame.time < _elapsedOffset)
            {
                Profiler.EndSample();
                return;
            }
            */
            _videoFrameClones.Dequeue();
            /*
            _streamCtx.TryGetFps(_videoDecoder, out double pts);
            if (_lastTexData.HasValue && Math.Abs(_elapsedOffset - videoFrame.time) < 1d / pts)
            {
                Log($"{videoFrame.time:0.00} {_lastTexData.GetValueOrDefault().time:0.00} {pts:0.00}");
                // _lastTexData = videoFrame;
                Profiler.EndSample();
                return;
            }
            */
            var tex = _texturePool.Get();
            if (tex.texture != null)
            {
                if (tex.texture.width != videoFrame.w || tex.texture.height != videoFrame.h)
                    tex.texture.Reinitialize(videoFrame.w, videoFrame.h);
                tex.texture.LoadRawTextureData(videoFrame.data);
                // tex.SetPixelData(videoFrame, 0);
                tex.texture.Apply(false);
            }
            tex.pts = videoFrame.time;
            _lastTexData = videoFrame;
            _videoTextures.Enqueue(tex);
            /*
            if (_videoTextures[idx] == null)
            {
                UpdateVideo(idx);
            }
            else
            {
                _videoTextures[idx].SetPixelData(videoFrame, 0);
                _videoTextures[idx].Apply(false);
            }
            */
            Profiler.EndSample();
        }

        private unsafe void UpdateAudio(int idx)
        {
            Profiler.BeginSample(nameof(UpdateAudio), this);
            var audioFrame = _audioFrames[idx];
            if (audioFrame.data[0] == null)
            {
                Profiler.EndSample();
                return;
            }
            /*
            _streamCtx.TryGetFps(_audioDecoder, out double pts);
            _streamCtx.TryGetTime(_audioDecoder, audioFrame, out double time);
            if (Math.Abs(_elapsedOffset + (_audioBufferSize / 48000) - time) < 1d / pts)
            {
                Log($"{time:0.00} {pts:0.00}");
                Profiler.EndSample();
                return;
            }
            */
            List<float> vals = new List<float>();
            for (uint ch = 0; ch < _audioDecoder.Channels; ch++)
            {
                int size = ffmpeg.av_samples_get_buffer_size(null, 1, audioFrame.nb_samples, _audioDecoder.SampleFormat, 1);
                if (size < 0)
                {
                    LogError("audio buffer size is less than zero");
                    Profiler.EndSample();
                    return;
                }
                byte[] backBuffer2 = new byte[size];
                float[] backBuffer3 = new float[size / sizeof(float)];
                Marshal.Copy((IntPtr)audioFrame.data[ch], backBuffer2, 0, size);
                Buffer.BlockCopy(backBuffer2, 0, backBuffer3, 0, backBuffer2.Length);
                // if (_audioLocker.WaitOne(0))
                {
                    // /*
                    for (int i = 0; i < backBuffer3.Length; i++)
                    {
                        vals.Add(backBuffer3[i]);
                        // _audioStream.Enqueue(backBuffer3[i]);
                    }
                    // */
                    /*
                    long pos = _audioMemStream.Position;
                    _audioMemStream.Seek(0, SeekOrigin.End);
                    _audioMemStream.Write(backBuffer2);
                    _audioMemStream.Flush();
                    _audioMemStream.Position = pos;
                    */

                    // _audioLocker.ReleaseMutex();
                }
            }
            if (_audioLocker.WaitOne(0))
            {
                int c = vals.Count / _audioDecoder.Channels;
                for (int i = 0; i < c; i++)
                {
                    for (int j = 0; j < _audioDecoder.Channels; j++)
                    {
                        _audioStream.Enqueue(vals[i + c * j]);
                    }
                }
                _audioLocker.ReleaseMutex();
            }
            Profiler.EndSample();
        }
        #endregion

        public static Texture2D SaveFrame(AVFrame frame, int width, int height, AVPixelFormat format)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            SaveFrame(frame, width, height, texture, format);
            return texture;
        }

        private static byte[] line = new byte[4096 * 4096 * 3];

        public unsafe static bool SaveFrame(AVFrame frame, int width, int height, byte[] texture, AVPixelFormat format)
        {
            if (frame.data[0] == null || frame.format == -1 || texture == null)
            {
                return false;
            }
            using var converter = new VideoFrameConverter(new System.Drawing.Size(width, height), (AVPixelFormat)frame.format, new System.Drawing.Size(width, height), AVPixelFormat.AV_PIX_FMT_RGB24);
            var convFrame = converter.Convert(frame);
            Marshal.Copy((IntPtr)convFrame.data[0], line, 0, width * height * 3);
            Array.Copy(line, 0, texture, 0, width * height * 3);
            // converter.Dispose();
            return true;
        }

        public unsafe static void SaveFrame(AVFrame frame, int width, int height, Texture2D texture, AVPixelFormat format)
        {
            Profiler.BeginSample(nameof(SaveFrame), texture);
            if (frame.data[0] == null || frame.format == -1)
            {
                Profiler.EndSample();
                return;
            }
            // Array.Clear(line, 0, line.Length);
            using var converter = new VideoFrameConverter(new System.Drawing.Size(width, height), (AVPixelFormat)frame.format, new System.Drawing.Size(width, height), AVPixelFormat.AV_PIX_FMT_RGB24);
            Profiler.BeginSample(nameof(SaveFrame) + "Convert", texture);
            var convFrame = converter.Convert(frame);
            Profiler.EndSample();
            // Marshal.Copy((IntPtr)convFrame.data[0], line, 0, width * height * 3);
            Profiler.BeginSample(nameof(SaveFrame) + "LoadTexture", texture);
            if (texture.width != width || texture.height != height)
                texture.Reinitialize(width, height);
            texture.LoadRawTextureData((IntPtr)convFrame.data[0], width * height * 3);
            // texture.SetPixelData(line, 0);
            texture.Apply(false);
            Profiler.EndSample();
            // converter.Dispose();
            Profiler.EndSample();
        }
    }
}
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
        [SerializeField]
        public bool CanSeek = true;

        // time controls
        [SerializeField]
        public double _offset = 0.0d;
        private double _prevTime = 0.0d;
        private double _timeOffset = 0.0d;
        [SerializeField]
        public double _videoOffset = -0.0d;
        private Stopwatch _videoWatch;
        private double? _lastPts;
        private int? _lastPts2;
        public double timer;
        public double PlaybackTime => _lastVideoTex?.pts ?? _elapsedOffset;
        public double _elapsedTotalSeconds => _videoWatch?.Elapsed.TotalSeconds ?? 0d;
        public double _elapsedOffsetVideo => _elapsedTotalSeconds + _videoOffset - _timeOffset;
        public double _elapsedOffset => _elapsedTotalSeconds - _timeOffset;
        private double? seekTarget = null;

        // buffer controls
        private int _videoBufferCount = 4;
        private int _audioBufferCount = 1;
        [SerializeField]
        public double _videoTimeBuffer = 1d;
        [SerializeField]
        public double _videoSkipBuffer = 0.1d;
        [SerializeField]
        public double _audioTimeBuffer = 1d;
        [SerializeField]
        public double _audioSkipBuffer = 0.1d;
        private int _audioBufferSize = 128;

        // unity assets
        private Queue<TexturePool.TexturePoolState> _videoTextures;
        private TexturePool.TexturePoolState _lastVideoTex;
        private TexturePool _texturePool;
        private TexData? _lastTexData;
        private Texture2D image;
        private AudioClip _audioClip;
        private MaterialPropertyBlock propertyBlock;
        public Action<Texture2D> OnDisplay = null;

        // decoders
        [SerializeField]
        public AVHWDeviceType _hwType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
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

        private void OnDestroy()
        {
            _paused = true;
            _decodeThread?.Abort();
            _decodeThread?.Join();
            _texturePool?.Dispose();
            _videoDecoder?.Dispose();
            _audioDecoder?.Dispose();
            _streamVideoCtx?.Dispose();
            _streamAudioCtx?.Dispose();
        }

        public void Seek(double seek)
        {
            Log(nameof(Seek));
            _paused = true;
            seekTarget = seek;
        }

        public void SeekInternal(double seek)
        {
            if (source)
                source.Stop();
            if (_audioLocker.WaitOne())
            {
                _audioMemStream.Position = 0;
                _audioStream.Clear();
                _audioLocker.ReleaseMutex();
            }
            // if (_videoMutex.WaitOne())
            {
                _videoFrameClones.Clear();
                foreach (var tex in _videoTextures)
                {
                    _texturePool.Release(tex);
                }
                _videoTextures.Clear();
                _lastVideoTex = null;
                _lastTexData = null;
                // _videoMutex.ReleaseMutex();
            }
            _videoWatch.Restart();
            ResetTimers();
            _timeOffset = -seek;
            _prevTime = _offset;
            _lastPts = null;
            _lastPts2 = null;
            if (CanSeek)
            {
                _streamVideoCtx.Seek(_videoDecoder, seek);
                _streamAudioCtx.Seek(_audioDecoder, seek);
            }
            _videoDecoder.Seek();
            _audioDecoder?.Seek();
            if (source)
            {
                source.clip = _audioClip;
                source.Play();
            }
            seekTarget = null;
            _paused = false;
            StartDecodeThread();
        }

        public void Play(Stream video, Stream audio)
        {
            DynamicallyLinkedBindings.Initialize();
            _paused = true;
            _decodeThread?.Abort();
            _decodeThread?.Join();
            _texturePool?.Dispose();
            _videoDecoder?.Dispose();
            _audioDecoder?.Dispose();
            _streamVideoCtx?.Dispose();
            _streamAudioCtx?.Dispose();
            _streamVideoCtx = new FFmpegCtx(video);
            _streamAudioCtx = new FFmpegCtx(audio);
            Init();
        }

        public void Play(string urlV, string urlA)
        {
            DynamicallyLinkedBindings.Initialize();
            _paused = true;
            _decodeThread?.Abort();
            _decodeThread?.Join();
            _texturePool?.Dispose();
            _videoDecoder?.Dispose();
            _audioDecoder?.Dispose();
            _streamVideoCtx?.Dispose();
            _streamAudioCtx?.Dispose();
            _streamVideoCtx = new FFmpegCtx(urlV);
            _streamAudioCtx = new FFmpegCtx(urlA);
            Init();
        }

        public void Resume()
        {
            if (!CanSeek)
                Init();
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
            _videoDecoder = new VideoStreamDecoder(_streamVideoCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, _hwType);
            _audioLocker = new Mutex(false, "Audio Mutex");
            if (source)
            {
                _audioDecoder = new VideoStreamDecoder(_streamAudioCtx, AVMediaType.AVMEDIA_TYPE_AUDIO);
                // _audioClip = AudioClip.Create($"{name}-AudioClip", _audioBufferSize * _audioDecoder.Channels, _audioDecoder.Channels, _audioDecoder.SampleRate, false);
                // var arr = new float[_audioClip.samples];
                // Array.Fill(arr, 1f);
                // _audioClip.SetData(arr, 0);
                _audioClip = AudioClip.Create($"{name}-AudioClip", _audioBufferSize * _audioDecoder.Channels, _audioDecoder.Channels, _audioDecoder.SampleRate, true, AudioCallback, AudioPosCallback);
                source.clip = _audioClip;
                source.Play();
            }
            // _paused = false;
            Log(nameof(Play));
            Seek(0d);
        }

        private void Update()
        {
            if (_videoWatch == null || _streamVideoCtx == null)
                return;
            
            if (CanSeek && (_offset >= _streamVideoCtx.GetLength() || (_streamVideoCtx.EndReached && (_audioDecoder == null || _streamAudioCtx.EndReached) && _videoTextures.Count == 0 && (_audioDecoder == null || _audioStream.Count == 0))) && !_paused)
            {
                Pause();
                // Finished
            }

            if (seekTarget.HasValue && (_decodeThread == null || !_decodeThread.IsAlive))
            {
                SeekInternal(seekTarget.Value);
            }

            if (!_paused)
            {
                _offset = _elapsedOffset;
                if (!_videoWatch.IsRunning)
                {
                    _videoWatch.Start();
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
            }

            if (!_paused)
            {
                if (_decodeThread == null || !_decodeThread.IsAlive)
                    StartDecodeThread();

                int idx = _videoDisplayIndex;
                int j = 0;
                while (Math.Abs(_elapsedOffsetVideo - (PlaybackTime + _videoOffset)) >= 0.25d || _lastVideoTex == null)
                {
                    j++;
                    if (j >= 128)
                        break;
                    if (_videoMutex.WaitOne())
                    {
                        bool failed = !UpdateVideoFromClones(idx);
                        _videoMutex.ReleaseMutex();
                        // if (failed)
                        //     break;
                    }
                    Present(idx, true);
                }
            }

            _prevTime = _offset;
            _wasPaused = _paused;
        }

        private void Update_Thread()
        {
            Log("AV Thread started.");
            double fps;
            if (!_streamVideoCtx.TryGetFps(_videoDecoder, out fps))
                fps = 30d;
            double fpsMs = 1d / fps * 1000;
            fps = 1d / fps;
            while (!_paused)
            {
                try
                {
                    long ms = FillVideoBuffers(false, fps, fpsMs);
                    Thread.Sleep((int)Math.Max(5, fpsMs - ms));
                    // Thread.Sleep(1);
                    // Thread.Yield();
                }
                catch (Exception e)
                {
                    LogError(e);
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
        private bool Present(int idx, bool display)
        {
            if (_lastTexData.HasValue)
            {
                _lastVideoTex = new TexturePool.TexturePoolState()
                {
                    pts = _lastTexData.Value.time,
                };
                if (display)
                {
                    if (!image)
                        image = new Texture2D(16, 16, TextureFormat.RGB24, false);
                    if (image.width != _lastTexData.Value.w || image.height != _lastTexData.Value.h)
                        image.Reinitialize(_lastTexData.Value.w, _lastTexData.Value.h);
                    image.SetPixelData(_lastTexData.Value.data, 0);
                    image.Apply(false);
                    Display(image);
                }
                _lastTexData = null;
                return true;
            }
            return false;
        }

        private void Display(Texture2D texture)
        {
            // if (!texture)
                // return;
            if (OnDisplay == null)
            {
                if (propertyBlock == null)
                    propertyBlock = new MaterialPropertyBlock();
                propertyBlock.SetTexture("_MainTex", texture);
                if (renderMesh != null)
                {
                    if (materialIndex == -1)
                        renderMesh.SetPropertyBlock(propertyBlock);
                    else
                        renderMesh.SetPropertyBlock(propertyBlock, materialIndex);
                }
            }
            else
            {
                OnDisplay.Invoke(texture);
            }
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
        private double _lastVideoDecodeTime;
        private double _lastAudioDecodeTime;
        [NonSerialized]
        public int skippedFrames = 0;

        private long FillVideoBuffers(bool mainThread, double invFps, double fpsMs)
        {
            if (_streamVideoCtx == null || _streamAudioCtx == null)
                return 0;
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            while (sw.ElapsedMilliseconds <= fpsMs)
            {
                double time = default;
                bool decodeV = true;
                bool decodeA = _audioDecoder != null;
                if (_lastVideoTex != null)
                {
                    if (Math.Abs(_elapsedOffsetVideo - PlaybackTime) > _videoTimeBuffer * 5 && !CanSeek)
                    {
                        _timeOffset = -PlaybackTime;
                    }
                }
                if (_lastVideoTex != null && _videoDecoder.CanDecode() && _streamVideoCtx.TryGetTime(_videoDecoder, out time))
                {
                    if (_elapsedOffsetVideo + _videoTimeBuffer < time)
                        decodeV = false;
                    if (_elapsedOffsetVideo > time + _videoSkipBuffer && CanSeek)
                    {
                        _streamVideoCtx.NextFrame(out _);
                        skippedFrames++;
                        decodeV = false;
                    }
                }
                if (_lastVideoTex != null && _audioDecoder != null && _audioDecoder.CanDecode() && _streamAudioCtx.TryGetTime(_audioDecoder, out time))
                {
                    if (_elapsedOffset + _audioTimeBuffer < time)
                        decodeA = false;
                    if (_elapsedOffset > time + _audioSkipBuffer && CanSeek)
                    {
                        _streamAudioCtx.NextFrame(out _);
                        skippedFrames++;
                        decodeA = false;
                    }
                }
                {
                    int vid = -1;
                    int aud = -1;
                    AVFrame vFrame = default;
                    AVFrame aFrame = default;
                    if (decodeV)
                    {
                        _streamVideoCtx.NextFrame(out _);
                        vid = _videoDecoder.Decode(out vFrame);
                    }
                    if (decodeA)
                    {
                        _streamAudioCtx.NextFrame(out _);
                        aud = _audioDecoder.Decode(out aFrame);
                    }
                    switch (vid)
                    {
                        case 0:
                            if (_streamVideoCtx.TryGetTime(_videoDecoder, vFrame, out time) && _elapsedOffsetVideo > time + _videoSkipBuffer && CanSeek)
                                break;
                            if (_streamVideoCtx.TryGetTime(_videoDecoder, vFrame, out time) && time != 0)
                                _lastVideoDecodeTime = time;
                            _videoFrames[_videoWriteIndex % _videoFrames.Length] = vFrame;
                            if (mainThread)
                            {
                                UpdateVideo(_videoWriteIndex % _videoFrames.Length);
                            }
                            else
                            {
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
                                            _streamVideoCtx.TryGetTime(_videoDecoder, vFrame, out time);
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
                            break;
                        case 1:
                            break;
                    }
                    switch (aud)
                    {
                        case 0:
                            if (_streamAudioCtx.TryGetTime(_audioDecoder, aFrame, out time) && _elapsedOffset > time + _audioSkipBuffer && CanSeek)
                                break;
                            if (_streamAudioCtx.TryGetTime(_audioDecoder, aFrame, out time) && time != 0)
                                _lastAudioDecodeTime = time;
                            _audioFrames[_audioWriteIndex % _audioFrames.Length] = aFrame;
                            UpdateAudio(_audioWriteIndex % _audioFrames.Length);
                            _audioWriteIndex++;
                            break;
                        case 1:
                            break;
                    }
                }
            }
            return sw.ElapsedMilliseconds;
        }
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
            var tex = _texturePool.Get();
            if (tex.texture == null)
            {
                tex.texture = SaveFrame(videoFrame, videoFrame.width, videoFrame.height, _videoDecoder.HWPixelFormat);
            }
            else
                SaveFrame(videoFrame, videoFrame.width, videoFrame.height, tex.texture, _videoDecoder.HWPixelFormat);
            tex.texture.name = $"{name}-Texture2D-{idx}";
            _videoTextures.Enqueue(tex);
            Profiler.EndSample();
            return tex.texture;
        }

        private unsafe bool UpdateVideoFromClones(int idx)
        {
            Profiler.BeginSample(nameof(UpdateVideoFromClones), this);
            if (_videoFrameClones.Count == 0)
            {
                Profiler.EndSample();
                return false;
            }
            TexData videoFrame = _videoFrameClones.Dequeue();
            _lastTexData = videoFrame;
            Profiler.EndSample();
            return true;
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
                {
                    for (int i = 0; i < backBuffer3.Length; i++)
                    {
                        vals.Add(backBuffer3[i]);
                    }
                }
            }
            if (_audioLocker.WaitOne(0))
            {
                int c = vals.Count / _audioDecoder.Channels;
                for (int i = 0; i < c; i++)
                {
                    // float val = 0f;
                    for (int j = 0; j < _audioDecoder.Channels; j++)
                    {
                        // val += vals[i + c * j];
                        _audioStream.Enqueue(vals[i + c * j]);
                    }
                    // _audioStream.Enqueue(val / _audioDecoder.Channels);
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

        [ThreadStatic]
        private static byte[] line;

        public unsafe static bool SaveFrame(AVFrame frame, int width, int height, byte[] texture, AVPixelFormat format)
        {
            if (line == null)
            {
                line = new byte[4096 * 4096 * 6];
            }
            if (frame.data[0] == null || frame.format == -1 || texture == null)
            {
                return false;
            }
            using var converter = new VideoFrameConverter(new System.Drawing.Size(frame.width, frame.height), (AVPixelFormat)frame.format, new System.Drawing.Size(width, height), AVPixelFormat.AV_PIX_FMT_RGB24);
            var convFrame = converter.Convert(frame);
            // var convFrame = converter.Convert(frame, 32);
            Marshal.Copy((IntPtr)convFrame.data[0], line, 0, width * height * 3);
            Array.Copy(line, 0, texture, 0, width * height * 3);
            return true;
        }

        public unsafe static void SaveFrame(AVFrame frame, int width, int height, Texture2D texture, AVPixelFormat format)
        {
            if (line == null)
            {
                line = new byte[4096 * 4096 * 6];
            }
            Profiler.BeginSample(nameof(SaveFrame), texture);
            if (frame.data[0] == null || frame.format == -1)
            {
                Profiler.EndSample();
                return;
            }
            using var converter = new VideoFrameConverter(new System.Drawing.Size(width, height), (AVPixelFormat)frame.format, new System.Drawing.Size(width, height), AVPixelFormat.AV_PIX_FMT_RGB24);
            Profiler.BeginSample(nameof(SaveFrame) + "Convert", texture);
            var convFrame = converter.Convert(frame);
            Profiler.EndSample();
            Profiler.BeginSample(nameof(SaveFrame) + "LoadTexture", texture);
            if (texture.width != width || texture.height != height)
                texture.Reinitialize(width, height);
            Marshal.Copy((IntPtr)convFrame.data[0], line, 0, width * height * 3);
            texture.LoadRawTextureData((IntPtr)convFrame.data[0], width * height * 3);
            texture.Apply(false);
            Profiler.EndSample();
            Profiler.EndSample();
        }
    }
}
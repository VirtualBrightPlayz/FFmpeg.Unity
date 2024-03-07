using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CobaltSharp;
using FFmpeg.Unity;
using UnityEngine;
using YoutubeExplode;
using YoutubeExplode.Common;

public class FFTest : MonoBehaviour
{
    public FFUnity ffmpeg;
    public string contentUrl;
    public bool stream = false;
    private int id;
    private Rect windowRect;

    private void Start()
    {
        id = GetInstanceID();
        Play();
    }

    private void OnGUI()
    {
        windowRect = GUILayout.Window(id, windowRect, OnWindow, name);
    }

    private void OnWindow(int wid)
    {
        GUILayout.BeginHorizontal();
        {
            GUILayout.Label("URL:");
            contentUrl = GUILayout.TextField(contentUrl);
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Play"))
            {
                Play();
            }
            if (GUILayout.Button("Resume"))
            {
                ffmpeg.Resume();
            }
            if (GUILayout.Button("Pause"))
            {
                ffmpeg.Pause();
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Volume:");
        ffmpeg.source.volume = GUILayout.HorizontalSlider(ffmpeg.source.volume, 0f, 1f);
        GUILayout.Label($"DisplayTime: {ffmpeg?.PlaybackTime:0.0}");
        GUILayout.Label($"Time: {ffmpeg?._elapsedOffset:0.0}");
        GUILayout.Label($"Diff: {(ffmpeg?._elapsedOffset - ffmpeg?.PlaybackTime):0.0}");
        if (GUILayout.Button("Seek Back 5s"))
        {
            ffmpeg.Seek(ffmpeg.PlaybackTime - 5d);
            // ffmpeg.Seek(10);
        }
        if (GUILayout.Button("Seek Forward 5s"))
        {
            ffmpeg.Seek(ffmpeg.PlaybackTime + 5d);
            // ffmpeg.Seek(5);
        }
        GUI.DragWindow(new Rect(0, 0, windowRect.width, 20));
    }

    public void Play(string url)
    {
        HttpClient client = new HttpClient();
        HttpRequestMessage requestVideo = new HttpRequestMessage(HttpMethod.Get, url);
        HttpContent contentVideo = client.SendAsync(requestVideo, HttpCompletionOption.ResponseHeadersRead).Result.Content;
        try
        {
            Stream video = contentVideo.ReadAsStreamAsync().Result;
            ffmpeg.Play(video);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public void PlayStream(string url)
    {
        ffmpeg.Play(url);
    }

    [ContextMenu(nameof(Play))]
    public async void Play()
    {
        if (string.IsNullOrEmpty(contentUrl))
            return;
        if (stream)
        {
            PlayStream(contentUrl);
            return;
        }
        
        var yt = new YoutubeClient();
        Debug.Log("Start");
        // var video = await yt.Videos.GetAsync(contentUrl);
        // Debug.Log(video.Url);
        var video = await yt.Videos.Streams.GetManifestAsync(contentUrl);
        // var ytStream = video.GetMuxedStreams().FirstOrDefault(x => x.VideoResolution.Height == 360) ?? video.GetMuxedStreams().FirstOrDefault();
        var ytStream = video.GetMuxedStreams().FirstOrDefault();
        PlayStream(ytStream.Url);
        return;

        Cobalt cobalt = new Cobalt();
        GetMedia getMedia = new GetMedia()
        {
            url = contentUrl,
            vQuality = VideoQuality.q360,
        };
        MediaResponse mediaResponse = cobalt.GetMedia(getMedia);
        if (mediaResponse.status == Status.Error)
        {
            PlayStream(contentUrl);
            return;
        }
        if (mediaResponse.status == Status.Picker)
        {
            foreach (PickerItem item in mediaResponse.picker)
            {
                Debug.Log(item.url);
            }
        }
        else
        {
            Play(mediaResponse.url);
        }
    }
}

#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using UnityEngine.TestTools;
using UnityEngineInternal.Video;

public class VideoClipProvider : IPrebuildSetup
{
    private const string kAssetName = "basic_clip_with_audio_1280_720_1_1_50_100";
    private const string kGameObjectName = "BasicVideoClipWithAudio";
    private GameObject m_GameObject;
    private VideoPlaybackMgr m_PlaybackManager;
    private List<VideoPlayback> m_CreatedVideoPlaybacks;
    private string m_RealAssetPath;

    public void Setup()
    {
    }

    [OneTimeSetUp]
    public void TestFixtureSetUp()
    {
        var guids = AssetDatabase.FindAssets(kAssetName);
        m_RealAssetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        AssetDatabase.ImportAsset(m_RealAssetPath, ImportAssetOptions.ForceUpdate);
        m_GameObject = new GameObject(kGameObjectName);
        m_GameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        m_GameObject.AddComponent<Camera>();
        m_PlaybackManager = new VideoPlaybackMgr();
        var playback = m_PlaybackManager.CreateVideoPlayback(m_RealAssetPath, null, null, null);
        playback.SetAudioTarget(0, true, false, null);
        m_CreatedVideoPlaybacks = new List<VideoPlayback>
        {
            playback
        };
        while (!m_CreatedVideoPlaybacks.TrueForAll(p => p.IsReady()))
        {
            VideoPlaybackMgr.ProcessOSMainLoopMessagesForTesting();
            m_PlaybackManager.Update();
        }
    }

    [OneTimeTearDown]
    public void TestFixtureTearDown()
    {
        Object.DestroyImmediate(m_GameObject);
        foreach (var playback in m_CreatedVideoPlaybacks)
            m_PlaybackManager.ReleaseVideoPlayback(playback);
        m_CreatedVideoPlaybacks.Clear();
        while (m_PlaybackManager.videoPlaybackCount > 0)
        {
            VideoPlaybackMgr.ProcessOSMainLoopMessagesForTesting();
            m_PlaybackManager.Update();
        }
        m_PlaybackManager.Dispose();
    }

    [Test]
    [Ignore("Fails intermittently under CI, https://github.com/Unity-Technologies/media/issues/38")]
    public void ReadWholeClip_ReturnsAllSamples()
    {
        var video = m_CreatedVideoPlaybacks[0];
        using (AudioSampleProvider provider = video.GetAudioSampleProvider(0))
        {
            Assert.IsNotNull(provider);
            Assert.That(provider.valid);

            var sampleFrameCount = video.GetDuration() * video.GetAudioSampleRate(0);
            long readSampleFrameCount = 0;
            var channelCount = video.GetAudioChannelCount(0);
            const int sampleFrameCountPerChunk = 4096;

            using (var buffer = new NativeArray<float>(sampleFrameCountPerChunk * channelCount, Allocator.Persistent))
            {
                long sampleFrameCountThisTime;
                do
                {
                    sampleFrameCountThisTime = provider.ConsumeSampleFrames(buffer);
                    readSampleFrameCount += sampleFrameCountThisTime;
                }
                while (sampleFrameCountThisTime == sampleFrameCountPerChunk && readSampleFrameCount < sampleFrameCount);
            }

            // Have to be flexible over the number of samples: the decoding may pad with a bit more samples than the file
            // itself announces.
            Assert.That(readSampleFrameCount, Is.GreaterThanOrEqualTo(sampleFrameCount));
        }
    }
}
#endif

using System;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine.Experimental.Audio;
using Object = UnityEngine.Object;

public abstract class AudioClipProvider : IPrebuildSetup
{
    protected AudioClip m_AudioClip;
    protected GameObject m_GameObject;

    protected abstract AudioClipLoadType LoadType { get; }
    protected abstract string AssetName { get; }
    protected abstract string GameObjectName { get; }

    public void Setup()
    {
#if UNITY_EDITOR
        var guids = AssetDatabase.FindAssets(AssetName);
        var realAssetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        var importer = AssetImporter.GetAtPath(realAssetPath) as AudioImporter;
        var settings = importer.defaultSampleSettings;
        settings.loadType = LoadType;
        importer.defaultSampleSettings = settings;
        AssetDatabase.ImportAsset(realAssetPath, ImportAssetOptions.ForceUpdate);
        m_GameObject = new GameObject(GameObjectName);
        var holder = m_GameObject.AddComponent<AudioClipHolder>();
        holder.Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(realAssetPath);
#endif
    }

    [OneTimeSetUp]
    public void TestFixtureSetUp()
    {
        m_GameObject = GameObject.Find(GameObjectName);
        var holder = m_GameObject.GetComponent<AudioClipHolder>();
        m_AudioClip = holder.Clip;
    }

    [OneTimeTearDown]
    public void TestFixtureTearDown()
    {
        Object.DestroyImmediate(m_GameObject);
    }

    protected void ReadWholeClip_ReturnsAllSamples()
    {
        using (var provider = CreateAudioSampleProvider(m_AudioClip))
        {
            Assert.That(provider, !Is.Null);
            Assert.That(provider.valid);

            var sampleFrameCount = m_AudioClip.samples;
            long readSampleFrameCount = 0;
            var channelCount = m_AudioClip.channels;
            const int sampleFrameCountPerChunk = 4096;
            var sumOfAllSamples = 0.0F;

            using (var buffer = new NativeArray<float>(sampleFrameCountPerChunk * channelCount, Allocator.Persistent))
            {
                long sampleFrameCountThisTime;
                do
                {
                    sampleFrameCountThisTime = provider.ConsumeSampleFrames(buffer);
                    readSampleFrameCount += sampleFrameCountThisTime;
                    for (var i = 0; i < channelCount * sampleFrameCountThisTime; ++i)
                        sumOfAllSamples += Math.Abs(buffer[i]);
                }
                while (sampleFrameCountThisTime == sampleFrameCountPerChunk);
            }

            // Have to be flexible over the number of samples: the decoding may pad with a bit more samples than the file
            // itself announces.
            Assert.That(readSampleFrameCount, Is.GreaterThanOrEqualTo(sampleFrameCount));
            Assert.That(sumOfAllSamples, Is.GreaterThan(0.0F));
        }
    }

    protected void Looping_ReturnsSamplesIndefinitely()
    {
        using (var provider = CreateAudioSampleProvider(m_AudioClip, 0, 0, true))
        {
            Assert.That(provider, !Is.Null);
            Assert.That(provider.valid);

            var sampleFrameCount = m_AudioClip.samples;
            long readSampleFrameCount = 0;
            var channelCount = m_AudioClip.channels;
            const int sampleFrameCountPerChunk = 4096;
            var sumOfAllSamples = 0.0F;

            using (var buffer = new NativeArray<float>(sampleFrameCountPerChunk * channelCount, Allocator.Persistent))
            {
                do
                {
                    long sampleFrameCountThisTime = provider.ConsumeSampleFrames(buffer);
                    readSampleFrameCount += sampleFrameCountThisTime;
                    for (var i = 0; i < channelCount * sampleFrameCountThisTime; ++i)
                        sumOfAllSamples += Math.Abs(buffer[i]);
                }
                while (readSampleFrameCount < 3 * sampleFrameCount);
            }

            Assert.That(sumOfAllSamples, Is.GreaterThan(0.0F));
        }
    }

    protected void AllowedDropouts_ReturnsSamplesIndefinitely()
    {
        using (var provider = CreateAudioSampleProvider(m_AudioClip, 0, 0, false, true))
        {
            Assert.That(provider, !Is.Null);
            Assert.That(provider.valid);

            var sampleFrameCount = m_AudioClip.samples;
            long readSampleFrameCount = 0;
            var channelCount = m_AudioClip.channels;
            const int sampleFrameCountPerChunk = 4096;
            var sumOfAllSamples = 0.0F;

            using (var buffer = new NativeArray<float>(sampleFrameCountPerChunk * channelCount, Allocator.Persistent))
            {
                // First bytes contain the file.
                long sampleFrameCountThisTime;
                do
                {
                    sampleFrameCountThisTime = provider.ConsumeSampleFrames(buffer);
                    readSampleFrameCount += sampleFrameCountThisTime;
                    for (var i = 0; i < channelCount * sampleFrameCountThisTime; ++i)
                        sumOfAllSamples += Math.Abs(buffer[i]);
                }
                while (readSampleFrameCount < sampleFrameCount);

                Assert.That(sumOfAllSamples, Is.GreaterThan(0.0F));

                // Read one buffer worth of samples since the provider adds delay for deglitching. We want to be past the
                // delay tail.
                provider.ConsumeSampleFrames(buffer);

                // The rest is silence.
                sumOfAllSamples = 0.0F;
                do
                {
                    sampleFrameCountThisTime = provider.ConsumeSampleFrames(buffer);
                    Assert.That(sampleFrameCountThisTime, Is.GreaterThan(0));
                    if (sampleFrameCountThisTime == 0)
                        break;
                    readSampleFrameCount += sampleFrameCountThisTime;
                    for (var i = 0; i < channelCount * sampleFrameCountThisTime; ++i)
                        sumOfAllSamples += Math.Abs(buffer[i]);
                }
                while (readSampleFrameCount < 3 * sampleFrameCount);

                Assert.That(sumOfAllSamples, Is.EqualTo(0.0F));
            }
        }
    }

    static AudioSampleProvider CreateAudioSampleProvider(AudioClip audioClip, long startSampleFrameIndex = 0,
        long endSampleFrameIndex = 0, bool loop = false, bool allowDrop = false)
    {
        return AudioSampleProvider.Lookup(
            audioClip.Internal_CreateAudioClipSampleProvider((ulong)startSampleFrameIndex, endSampleFrameIndex, loop, allowDrop),
            null, 0);
    }
}

[Ignore("Fails intermittently under CI, https://github.com/Unity-Technologies/media/issues/38")]
public class DecompressOnLoadAudioClipProvider : AudioClipProvider
{
    [Test]
    public void ReadWholeClip_Returns_AllSamples() { ReadWholeClip_ReturnsAllSamples(); }
    [Test]
    public void Looping_ReturnsSamples_Indefinitely() { Looping_ReturnsSamplesIndefinitely(); }
    [Test]
    public void AllowedDropouts_ReturnsSamples_Indefinitely() { AllowedDropouts_ReturnsSamplesIndefinitely(); }

    protected override AudioClipLoadType LoadType => AudioClipLoadType.DecompressOnLoad;
    protected override string AssetName => "decoderSoundDecompressOnLoad";
    protected override string GameObjectName => "AudioClipGODecompressOnLoad";
}

[Ignore("Fails intermittently under CI, https://github.com/Unity-Technologies/media/issues/38")]
public class StreamingAudioClipProvider : AudioClipProvider
{
    [Test]
    public void ReadWholeClip_Returns_AllSamples() { ReadWholeClip_ReturnsAllSamples(); }
    [Test]
    public void Looping_ReturnsSamples_Indefinitely() { Looping_ReturnsSamplesIndefinitely(); }
    [Test]
    public void AllowedDropouts_ReturnsSamples_Indefinitely() { AllowedDropouts_ReturnsSamplesIndefinitely(); }

    protected override AudioClipLoadType LoadType => AudioClipLoadType.Streaming;
    protected override string AssetName => "decoderSoundStreamingAudio";
    protected override string GameObjectName => "AudioClipGOStreamingAudio";
}

[Ignore("Fails intermittently under CI, https://github.com/Unity-Technologies/media/issues/38")]
public class CompressedInMemoryAudioClipProvider : AudioClipProvider
{
    [Test]
    public void ReadWholeClip_Returns_AllSamples() { ReadWholeClip_ReturnsAllSamples(); }
    [Test]
    public void Looping_ReturnsSamples_Indefinitely() { Looping_ReturnsSamplesIndefinitely(); }
    [Test]
    public void AllowedDropouts_ReturnsSamples_Indefinitely() { AllowedDropouts_ReturnsSamplesIndefinitely(); }

    protected override AudioClipLoadType LoadType => AudioClipLoadType.CompressedInMemory;
    protected override string AssetName => "decoderSoundCompressedInMemory";
    protected override string GameObjectName => "AudioClipGOCompressedInMemory";
}

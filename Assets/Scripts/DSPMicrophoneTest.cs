using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Collections;

public class DSPMicrophoneTest : MonoBehaviour
{
    public GameObject ScopeRendererPrefab;
    public GameObject SpectrumRendererPrefab;


    DSPGraph _Graph;
    MyAudioDriver _Driver;
    AudioOutputHandle _OutputHandle;

    DSPNode _Microphone;
    DSPNode _MonoToStereo;
    DSPNode _Scope;
    DSPNode _Spectrum;

    AudioClip _MicrophoneClip;
    float[] _MicrophoneDataArray;
    NativeArray<float> _MicrophoneBuffer;

    ScopeRenderer _ScopeRenderer;
    SpectrumRenderer _SpectrumRenderer;

    void Start()
    {
        ConfigureDSP();
    }

    private void Update()
    {
        _MicrophoneClip.GetData(_MicrophoneDataArray, 0);
        _MicrophoneBuffer.CopyFrom(_MicrophoneDataArray);

        using (var block = _Graph.CreateCommandBlock())
        {
            block.UpdateAudioKernel<MicrophoneNodeKernel, MicrophoneNode.Parameters, MicrophoneNode.Providers, MicrophoneNode>(new MicrophoneNodeKernel(_MicrophoneBuffer), _Microphone);
        }

        _Graph.Update();
    }

    void ConfigureDSP()
    {
        var format = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(AudioSettings.speakerMode);
        var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
        AudioSettings.GetDSPBufferSize(out var bufferLength, out var numBuffers);
        var sampleRate = AudioSettings.outputSampleRate;
        Debug.LogFormat("Format={2} Channels={3} BufferLength={0} SampleRate={1}", bufferLength, sampleRate, format, channels);
        _MicrophoneClip = Microphone.Start(Microphone.devices[0], true, 1, sampleRate);
        Debug.LogFormat("Microphone Channels={0} Frequency={1} Samples={2} Ambisonic={3}", _MicrophoneClip.channels, _MicrophoneClip.frequency, _MicrophoneClip.samples, _MicrophoneClip.ambisonic);
        _MicrophoneBuffer = new NativeArray<float>(_MicrophoneClip.samples, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _MicrophoneDataArray = new float[_MicrophoneClip.samples];

        _Graph = DSPGraph.Create(format, channels, bufferLength, sampleRate);
        _Driver = new MyAudioDriver { Graph = _Graph };
        _OutputHandle = _Driver.AttachToDefaultOutput();

        // create graph structure
        using (var block = _Graph.CreateCommandBlock())
        {
            //
            // create nodes
            //
            _Microphone = block.CreateDSPNode<MicrophoneNode.Parameters, MicrophoneNode.Providers, MicrophoneNode>();
            block.AddOutletPort(_Microphone, 1);

            _Scope = block.CreateDSPNode<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>();
            block.AddInletPort(_Scope, 1);

            _Spectrum = block.CreateDSPNode<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>();
            block.AddInletPort(_Spectrum, 1);

            _MonoToStereo = block.CreateDSPNode<MonoToStereoNode.Parameters, MonoToStereoNode.Providers, MonoToStereoNode>();
            block.AddInletPort(_MonoToStereo, 1); // left
            block.AddInletPort(_MonoToStereo, 1); // right
            block.AddOutletPort(_MonoToStereo, 2);

            //
            // connect nodes
            //
            block.Connect(_Microphone, 0, _MonoToStereo, 0);
            block.Connect(_Microphone, 0, _MonoToStereo, 1);

            block.Connect(_MonoToStereo, 0, _Graph.RootDSP, 0);

            block.Connect(_Microphone, 0, _Scope, 0);
            block.Connect(_Microphone, 0, _Spectrum, 0);

            //
            // set parameters
            //
            block.SetFloat<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(_Scope, ScopeNode.Parameters.Time, 1f);
            block.SetFloat<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(_Scope, ScopeNode.Parameters.TriggerTreshold, 0f);

            block.SetFloat<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>(_Spectrum, SpectrumNode.Parameters.Window, (float)SpectrumNode.WindowType.Hamming);
        }

        _ScopeRenderer = SpawnScopeRenderer(_Scope);
        _ScopeRenderer.Height = 5.0f;
        _ScopeRenderer.Offset = 0.0f;

        _SpectrumRenderer = SpawnSpectrumRenderer(_Spectrum);

        StartCoroutine(InitMicCoroutine());
    }

    ScopeRenderer SpawnScopeRenderer(DSPNode scopeNode)
    {
        GameObject go = Instantiate(ScopeRendererPrefab);
        ScopeRenderer scope = go.GetComponent<ScopeRenderer>();
        scope.Init(_Graph, scopeNode);
        return scope;
    }

    SpectrumRenderer SpawnSpectrumRenderer(DSPNode spectrumNode)
    {
        GameObject go = Instantiate(SpectrumRendererPrefab);
        SpectrumRenderer spectrum = go.GetComponent<SpectrumRenderer>();
        spectrum.Init(_Graph, spectrumNode);
        return spectrum;
    }

    IEnumerator InitMicCoroutine()
    {
        var audioConfig = AudioSettings.GetConfiguration();

        string microphoneDevice = Microphone.devices[0];
        Debug.Log("Microphones:");
        foreach (var device in Microphone.devices)
        {
            Debug.Log("- " + device);
        }
        Debug.Log("Selected device: " + microphoneDevice);

        _MicrophoneClip = Microphone.Start(microphoneDevice, true, 1, audioConfig.sampleRate);
        while (!(Microphone.GetPosition(microphoneDevice) > 0)) { }
        int micPos = Microphone.GetPosition(null);
        Debug.Log("Start Mic(pos): " + micPos);

        using (var block = _Graph.CreateCommandBlock())
        {
            block.UpdateAudioKernel<MicrophoneNodePlayKernel, MicrophoneNode.Parameters, MicrophoneNode.Providers, MicrophoneNode>(new MicrophoneNodePlayKernel(micPos), _Microphone);
        }

        yield return null;
    }

    private void OnDestroy()
    {
        if (_MicrophoneBuffer.IsCreated) _MicrophoneBuffer.Dispose();
    }
}

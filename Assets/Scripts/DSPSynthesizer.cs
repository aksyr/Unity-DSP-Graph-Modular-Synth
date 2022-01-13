using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;

public class DSPSynthesizer : MonoBehaviour
{
    public GameObject ScopeRendererPrefab;
    public GameObject SpectrumRendererPrefab;

    DSPGraph _Graph;
    MyAudioDriver _Driver;
    AudioOutputHandle _OutputHandle;

    DSPNode _Oscilator1;
    DSPNode _Oscilator2;
    DSPNode _Oscilator3;
    DSPNode _Oscilator4;

    DSPNode _ADSR1;
    DSPNode _ADSR2;
    DSPNode _ADSR3;
    DSPNode _ADSR4;

    DSPNode _VCA1;
    DSPNode _VCA2;

    DSPNode _Mixer3;
    DSPNode _Mixer4;

    DSPNode _Midi;

    DSPNode _Attenuator;

    DSPNode _MonoToStereo;

    DSPNode _Scope;
    DSPNode _Spectrum;

    ScopeRenderer _ScopeRenderer;
    SpectrumRenderer _SpectrumRenderer;

    void Start()
    {
        ConfigureDSP();
    }

    private void Update()
    {
        _Graph.Update();
    }

    void ConfigureDSP()
    {
        var format = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(AudioSettings.speakerMode);
        var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
        AudioSettings.GetDSPBufferSize(out var bufferLength, out var numBuffers);
        var sampleRate = AudioSettings.outputSampleRate;
        Debug.LogFormat("Format={2} Channels={3} BufferLength={0} SampleRate={1}", bufferLength, sampleRate, format, channels);

        _Graph = DSPGraph.Create(format, channels, bufferLength, sampleRate);
        _Driver = new MyAudioDriver { Graph = _Graph };
        _OutputHandle = _Driver.AttachToDefaultOutput();

        CreateSynth0();
    }

    void CreateSynth0()
    {
        // create graph structure
        using (var block = _Graph.CreateCommandBlock())
        {
            //
            // create nodes
            //
            _Oscilator1 = CreateOscilator(block);
            _Oscilator2 = CreateOscilator(block);
            _Oscilator3 = CreateOscilator(block);
            _Oscilator4 = CreateOscilator(block);

            _ADSR1 = CreateADSR(block);
            _ADSR2 = CreateADSR(block);
            _ADSR3 = CreateADSR(block);
            _ADSR4 = CreateADSR(block);

            _VCA1 = CreateVCA(block);
            _VCA2 = CreateVCA(block);

            _Mixer3 = CreateMixer(block);
            _Mixer4 = CreateMixer(block);

            _Midi = CreateMidi(block);

            _Attenuator = CreateAttenuator(block);

            _MonoToStereo = CreateMonoToStereo(block);

            _Scope = CreateMonoScope(block);
            _Spectrum = CreateSpectrum(block);

            //
            // connect nodes
            //
            block.Connect(_Midi, 0, _ADSR1, 0); // midi gate to adsr
            block.Connect(_Midi, 0, _ADSR2, 0);
            block.Connect(_Midi, 0, _ADSR3, 0);
            block.Connect(_Midi, 0, _ADSR4, 0);

            block.Connect(_Midi, 1, _Oscilator1, 1); // midi note to oscilator pitch
            block.Connect(_Midi, 1, _Oscilator2, 1);
            block.Connect(_Midi, 1, _Oscilator3, 1);
            block.Connect(_Midi, 1, _Oscilator4, 1);

            block.Connect(_Midi, 2, _Oscilator1, 2); // midi retrigger to oscilator reset phase
            block.Connect(_Midi, 2, _Oscilator2, 2);
            block.Connect(_Midi, 2, _Oscilator3, 2);
            block.Connect(_Midi, 2, _Oscilator4, 2);

            block.Connect(_ADSR1, 0, _VCA1, 0); // adsr gate to vca voltage
            block.Connect(_ADSR2, 0, _VCA2, 0);

            block.Connect(_Oscilator1, 0, _VCA1, 1); // oscilator out to vca in
            block.Connect(_Oscilator2, 0, _VCA2, 1);

            block.Connect(_VCA1, 0, _Oscilator3, 0); // vca out to oscilator fm
            block.Connect(_VCA2, 0, _Oscilator4, 0);

            block.Connect(_ADSR3, 0, _Mixer3, 1); // adsr gate to mixer cv
            block.Connect(_ADSR4, 0, _Mixer4, 1);

            block.Connect(_Oscilator3, 0, _Mixer3, 0); // oscilator out to mixer in
            block.Connect(_Oscilator4, 0, _Mixer4, 0);

            block.Connect(_Mixer3, 0, _Attenuator, 0); // mixer out to attenuator in
            block.Connect(_Mixer4, 0, _Attenuator, 0);

            block.Connect(_Attenuator, 0, _MonoToStereo, 0); // attenuator out to monotostereo left
            block.Connect(_Attenuator, 0, _MonoToStereo, 1); // attenuator out to monotostereo right

            block.Connect(_MonoToStereo, 0, _Graph.RootDSP, 0); // monotostereo out to output

            block.Connect(_Attenuator, 0, _Scope, 0);
            block.Connect(_Attenuator, 0, _Spectrum, 0);

            //
            // parameters
            //
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator1, OscilatorNode.Parameters.Frequency, 130.813f);
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator1, OscilatorNode.Parameters.Mode, (float)OscilatorNode.Mode.Sine);

            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator2, OscilatorNode.Parameters.Frequency, 130.813f);
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator2, OscilatorNode.Parameters.Mode, (float)OscilatorNode.Mode.Sine);

            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator3, OscilatorNode.Parameters.Frequency, 261.626f);
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator3, OscilatorNode.Parameters.Mode, (float)OscilatorNode.Mode.Sine);
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator3, OscilatorNode.Parameters.FMMultiplier, 0.5f);

            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator4, OscilatorNode.Parameters.Frequency, 130.813f);
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator4, OscilatorNode.Parameters.Mode, (float)OscilatorNode.Mode.Sine);
            block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_Oscilator4, OscilatorNode.Parameters.FMMultiplier, 0.4f);

            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR1, ADSRNode.Parameters.Attack, 0.1f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR1, ADSRNode.Parameters.Decay, 0.05f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR1, ADSRNode.Parameters.Sustain, 0.5f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR1, ADSRNode.Parameters.Release, 0.2f);

            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR2, ADSRNode.Parameters.Attack, 0.1f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR2, ADSRNode.Parameters.Decay, 0.05f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR2, ADSRNode.Parameters.Sustain, 0.5f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR2, ADSRNode.Parameters.Release, 0.2f);

            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR3, ADSRNode.Parameters.Attack, 0.05f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR3, ADSRNode.Parameters.Decay, 0.05f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR3, ADSRNode.Parameters.Sustain, 0.5f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR3, ADSRNode.Parameters.Release, 0.1f);

            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR4, ADSRNode.Parameters.Attack, 0.05f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR4, ADSRNode.Parameters.Decay, 0.05f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR4, ADSRNode.Parameters.Sustain, 0.5f);
            block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_ADSR4, ADSRNode.Parameters.Release, 0.1f);

            block.SetFloat<VCANode.Parameters, VCANode.Providers, VCANode>(_VCA1, VCANode.Parameters.Multiplier, 1.0f);
            block.SetFloat<VCANode.Parameters, VCANode.Providers, VCANode>(_VCA2, VCANode.Parameters.Multiplier, 1.0f);

            block.SetFloat<AttenuatorNode.Parameters, AttenuatorNode.Providers, AttenuatorNode>(_Attenuator, AttenuatorNode.Parameters.Multiplier, 1.0f);

            block.SetFloat<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(_Scope, ScopeNode.Parameters.Time, 0.05f);
            block.SetFloat<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(_Scope, ScopeNode.Parameters.TriggerTreshold, 0f);

            block.SetFloat<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>(_Spectrum, SpectrumNode.Parameters.Window, (float)SpectrumNode.WindowType.BlackmanHarris);
        }

        _ScopeRenderer = SpawnScopeRenderer(_Scope);
        _ScopeRenderer.Height = 5.01f;
        _ScopeRenderer.Offset = 0f;

        _SpectrumRenderer = SpawnSpectrumRenderer(_Spectrum);
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

    static DSPNode CreateOscilator(DSPCommandBlock block)
    {
        var oscilator = block.CreateDSPNode<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>();
        block.AddInletPort(oscilator, 16); // fm
        block.AddInletPort(oscilator, 16); // pitch
        block.AddInletPort(oscilator, 16); // phase reset
        block.AddOutletPort(oscilator, 16);
        return oscilator;
    }

    static DSPNode CreateADSR(DSPCommandBlock block)
    {
        var adsr = block.CreateDSPNode<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>();
        block.AddInletPort(adsr, 16); // gate
        block.AddOutletPort(adsr, 16);
        return adsr;
    }

    static DSPNode CreateVCA(DSPCommandBlock block)
    {
        var vca = block.CreateDSPNode<VCANode.Parameters, VCANode.Providers, VCANode>();
        block.AddInletPort(vca, 16); // voltage
        block.AddInletPort(vca, 16); // input
        block.AddOutletPort(vca, 16);
        return vca;
    }

    static DSPNode CreateMixer(DSPCommandBlock block)
    {
        var mixer = block.CreateDSPNode<MixerNode.Parameters, MixerNode.Providers, MixerNode>();
        block.AddInletPort(mixer, 16); // input
        block.AddInletPort(mixer, 16); // cv
        block.AddOutletPort(mixer, 1);
        return mixer;
    }

    static DSPNode CreateMidi(DSPCommandBlock block)
    {
        var midi = block.CreateDSPNode<MidiNode.Parameters, MidiNode.Providers, MidiNode>();
        block.AddOutletPort(midi, 16); // gate
        block.AddOutletPort(midi, 16); // note
        block.AddOutletPort(midi, 16); // retrigger
        return midi;
    }

    static DSPNode CreateAttenuator(DSPCommandBlock block)
    {
        var attenuator = block.CreateDSPNode<AttenuatorNode.Parameters, AttenuatorNode.Providers, AttenuatorNode>();
        block.AddInletPort(attenuator, 1);
        block.AddOutletPort(attenuator, 1);
        return attenuator;
    }

    static DSPNode CreateMonoToStereo(DSPCommandBlock block)
    {
        var mts = block.CreateDSPNode<MonoToStereoNode.Parameters, MonoToStereoNode.Providers, MonoToStereoNode>();
        block.AddInletPort(mts, 1); // left
        block.AddInletPort(mts, 1); // right
        block.AddOutletPort(mts, 2);
        return mts;
    }

    static DSPNode CreateMonoScope(DSPCommandBlock block)
    {
        var scope = block.CreateDSPNode<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>();
        block.AddInletPort(scope, 1);
        return scope;
    }

    static DSPNode CreateSpectrum(DSPCommandBlock block)
    {
        var scope = block.CreateDSPNode<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>();
        block.AddInletPort(scope, 1);
        return scope;
    }
}

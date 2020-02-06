# Unity DSP Graph Modular Synth
**Unity DSP Graph Modular Synth** is an experiment in creating modular synth using Unity's **DSP Graph** preview package.
It uses modified DSP Graph 0.1.0-preview.11 which allows inputs and outputs to remain not connected so that it's easier to leave nodes without all connections.
A lot of things in this project are directly inspired by **[VCV Rack](https://vcvrack.com)**.

![sample](https://github.com/aksyr/Unity-DSP-Graph-Modular-Synth/raw/master/sampleGraph.png)

Scenes
------
- **DSP Microphone Input**<br>
  Naive implementation of microphone input in DSPGraph. Uses *IAudioKernelUpdate* to copy data from microphone AudioClip.
- **DSP Synthesizer Code**<br>
  Organs-like synth assembled in code with Scope and Spectrum preview on screen.
- **DSP Synthesizer Graph**<br>
  The same organs-like synth assembled in xNode graph, which allows easy realtime manipulation during playback.<br>
  Hear this synth sound sample:<br>
      <a href="http://www.youtube.com/watch?feature=player_embedded&v=PNYKHUhx-k0
" target="_blank"><img src="https://i.imgur.com/Fh1psuW.png" 
alt="WATCH SAMPLE VIDEO" width="250" height="250" border="0" /></a>

Nodes
-----
- **ADSRNode**<br>
  Simple linear ADSR envelope.
- **AttenuatorNode**<br>
  Multiplies amplitude of input signal by factor.
- **MergeNode**<br>
  Merges multiple mono inputs into single polyphonic output.
- **MidiNode**<br>
  Used MidiJack's native library to process incoming midi events and translate them into signal outputs *(Gate, Note, Retrigger)*.
- **MixerNode**<br>
  Mixes polyphonic input into single mono output.
- **MonoToStereo**<br>
  Takes two mono inputs and outputs stereo output.
- **OscilatorNode**<br>
  Oscilates at given frequency using sine, triangle, saw or square wave. Can be modulated by fm input or pitch input from MidiNode. Supports multiple oscilators in polyphony mode.
- **ScopeNode**<br>
  Used for drawing scope data.<br>
  ![sample](https://github.com/aksyr/Unity-DSP-Graph-Modular-Synth/raw/master/sampleScope.gif)
- **SpectrumNode**<br>
  Used for drawing spectrum data.<br>
  ![sample](https://github.com/aksyr/Unity-DSP-Graph-Modular-Synth/raw/master/sampleSpectrum.gif)
- **SplitNode**<br>
  Splits single poly input into multiple mono outputs.
- **VCANode**<br>
  Multiplies two inputs together and outputs result.

Dependencies
------------
* keijiro's [MidiJack](https://github.com/keijiro/MidiJack)
* Siccity's [xNode](https://github.com/Siccity/xNode)

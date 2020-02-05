using UnityEngine;
using Unity.Audio;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;


// The 'audio job'. This is the kernel that defines a running DSP node inside the
// DSPGraph. It is a struct that implements the IAudioKernel interface. It can contain
// internal state, and will have the Execute function called as part of the graph
// traversal during an audio frame.
[BurstCompile(CompileSynchronously = true)]
struct PlayClipNode : IAudioKernel<PlayClipNode.Parameters, PlayClipNode.SampleProviders>
{
    // Parameters are currently defined with enumerations. Each enum value corresponds to
    // a parameter within the node. Setting a value for a parameter uses these enum values.
    public enum Parameters { Rate }

    // Sample providers are defined with enumerations. Each enum value defines a slot where
    // a sample provider can live on a IAudioKernel. Sample providers are used to get samples from
    // AudioClips and VideoPlayers. They will eventually be able to pull samples from microphones and other concepts.
    public enum SampleProviders { DefaultSlot }

    // The clip sample rate might be different to the output rate used by the system. Therefore we use a resampler
    // here.
    public Resampler Resampler;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<float> ResampleBuffer;

    public bool Playing;

    public void Initialize()
    {
        // During an initialization phase, we have access to a resource context which we can
        // do buffer allocations with safely in the job.
        ResampleBuffer = new NativeArray<float>(1025 * 2, Allocator.AudioKernel);

        // set position to "end of buffer", to force pulling data on first iteration
        Resampler.Position = (double)ResampleBuffer.Length / 2;
    }

    public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
    {
        if (Playing)
        {
            // During the creation phase of this node we added an output port to feed samples to.
            // This API gives access to that output buffer.
            var buffer = context.Outputs.GetSampleBuffer(0);

            // Get the sample provider for the AudioClip currently being played. This allows
            // streaming of samples from the clip into a buffer.
            var provider = context.Providers.GetSampleProvider(SampleProviders.DefaultSlot);

            // We pass the provider to the resampler. If the resampler finishes streaming all the samples, it returns
            // true.
            var finished = Resampler.ResampleLerpRead(provider, ResampleBuffer, buffer.Buffer, context.Parameters, Parameters.Rate);

            if (finished)
            {
                // Post an async event back to the main thread, telling the handler that the clip has stopped playing.
                context.PostEvent(new ClipStopped());
                Playing = false;
            }
        }
    }

    public void Dispose()
    {
        if (ResampleBuffer.IsCreated)
            ResampleBuffer.Dispose();
    }
}

[BurstCompile(CompileSynchronously = true)]
struct PlayClipKernel : IAudioKernelUpdate<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>
{
    // This update job is used to kick off playback of the node.
    public void Update(ref PlayClipNode audioKernel)
    {
        audioKernel.Playing = true;
    }
}

// Token Event that indicates that playback has finished
struct ClipStopped {}

// Bootstrap MonoBehaviour to get the example running.
public class PlayClip : MonoBehaviour
{
    public AudioClip ClipToPlay;

    AudioOutputHandle m_Output;
    DSPGraph m_Graph;
    DSPNode m_Node;
    DSPConnection m_Connection;

    int m_HandlerID;

    void Start()
    {
        var format = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(AudioSettings.speakerMode);
        var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
        AudioSettings.GetDSPBufferSize(out var bufferLength, out var numBuffers);

        var sampleRate = AudioSettings.outputSampleRate;

        m_Graph = DSPGraph.Create(format, channels, bufferLength, sampleRate);

        var driver = new DefaultDSPGraphDriver { Graph = m_Graph };
        m_Output = driver.AttachToDefaultOutput();

        // Add an event handler delegate to the graph for ClipStopped. So we are notified
        // of when a clip is stopped in the node and can handle the resources on the main thread.
        m_HandlerID = m_Graph.AddNodeEventHandler<ClipStopped>((node, evt) =>
        {
            Debug.Log("Received ClipStopped event on main thread, cleaning resources");
        });

        // All async interaction with the graph must be done through a DSPCommandBlock.
        // Create it here and complete it once all commands are added.
        var block = m_Graph.CreateCommandBlock();

        m_Node = block.CreateDSPNode<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>();

        // Currently input and output ports are dynamic and added via this API to a node.
        // This will change to a static definition of nodes in the future.
        block.AddOutletPort(m_Node, 2, SoundFormat.Stereo);

        // Connect the node to the root of the graph.
        m_Connection = block.Connect(m_Node, 0, m_Graph.RootDSP, 0);

        // We are done, fire off the command block atomically to the mixer thread.
        block.Complete();
    }

    void Update()
    {
        m_Graph.Update();
    }

    void OnDisable()
    {
        // Command blocks can also be completed via the C# 'using' construct for convenience
        using (var block = m_Graph.CreateCommandBlock())
        {
            block.Disconnect(m_Connection);
            block.ReleaseDSPNode(m_Node);
        }

        m_Graph.RemoveNodeEventHandler(m_HandlerID);

        m_Output.Dispose();
    }

    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 150, 100), "Play Clip!"))
        {
            if (ClipToPlay == null)
            {
                Debug.Log("No clip assigned, not playing (" + gameObject.name + ")");
                return;
            }

            using (var block = m_Graph.CreateCommandBlock())
            {
                // Decide on playback rate here by taking the provider input rate and the output settings of the system
                var resampleRate = (float)ClipToPlay.frequency / AudioSettings.outputSampleRate;
                block.SetFloat<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(m_Node, PlayClipNode.Parameters.Rate, resampleRate);

                // Assign the sample provider to the slot of the node.
                block.SetSampleProvider<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(ClipToPlay, m_Node, PlayClipNode.SampleProviders.DefaultSlot);

                // Kick off playback. This will be done in a better way in the future.
                block.UpdateAudioKernel<PlayClipKernel, PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(new PlayClipKernel(), m_Node);
            }
        }
    }
}

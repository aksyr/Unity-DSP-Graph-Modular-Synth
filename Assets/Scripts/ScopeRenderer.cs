using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Collections;

//using ScopeRequest = Unity.Audio.DSPNodeUpdateRequest<ScopeUpdateKernel, ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>;

public class ScopeRenderer : MonoBehaviour
{
    public RenderTexture ScopeRT;
    public ComputeShader Compute;

    private bool _Initialized = false;
    private bool _Waiting = false;

    private DSPGraph _Graph;
    private DSPNode _ScopeNode;
    private NativeArray<float> _BufferX;

    private ComputeBuffer _ScopeDataBuffer;
    private int _GridKernelId;
    private int _ScopeKernelId;

    public float Height = 5f;
    public float Offset = 0f;

    void Awake()
    {
        ScopeRT = new RenderTexture(ScopeNode.BUFFER_SIZE, 340, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
        ScopeRT.enableRandomWrite = true;
        ScopeRT.Create();

        _BufferX = new NativeArray<float>(ScopeNode.BUFFER_SIZE * ScopeNode.MAX_CHANNELS, Allocator.Persistent);

        _ScopeDataBuffer = new ComputeBuffer(ScopeNode.BUFFER_SIZE * ScopeNode.MAX_CHANNELS, sizeof(float));

        _GridKernelId = Compute.FindKernel("Grid");
        _ScopeKernelId = Compute.FindKernel("Scope");
    }

    public void Init(DSPGraph graph, DSPNode scopeNode)
    {
        _Graph = graph;
        _ScopeNode = scopeNode;
        _Initialized = true;

        var sm = FindObjectOfType<ScopeManager>();
        if(sm != null)sm.Register(this);
    }

    void OnDestroy()
    {
        ScopeRT?.Release();
        _BufferX.Dispose();
        _ScopeDataBuffer?.Dispose();
        _Initialized = false;
    }

    void Update()
    {
        if (_Initialized == false) return;

        if (_Waiting)
        {
            //    if(_Request.Done)
            //    {
            //        UpdateRequestFinished(_Request);
            //    }
        }
        else
        {
            using (DSPCommandBlock block = _Graph.CreateCommandBlock())
            {
                _Waiting = true;
                block.CreateUpdateRequest<ScopeUpdateKernel, ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(new ScopeUpdateKernel(_BufferX), _ScopeNode, req =>
                {
                    UpdateRequestFinished(req);
                    req.Dispose();
                });
            }
        }
    }

    void UpdateRequestFinished(DSPNodeUpdateRequest<ScopeUpdateKernel, ScopeNode.Parameters, ScopeNode.Providers, ScopeNode> request)
    {
        _Waiting = false;
        if (_Initialized == false) return;
        _ScopeDataBuffer?.SetData(_BufferX);

        Compute.SetInt("InputChannelsX", request.UpdateJob.InputChannelsX);
        Compute.SetInt("MaxChannels", ScopeNode.MAX_CHANNELS);
        Compute.SetInt("BufferSize", ScopeNode.BUFFER_SIZE);
        Compute.SetInt("BufferIdx", request.UpdateJob.BufferIdx);
        Compute.SetFloat("TriggerThreshold", request.UpdateJob.TriggerThreshold);
        Compute.SetFloat("ScopeXHeight", Height);
        Compute.SetFloat("ScopeXOffset", Offset);

        Compute.SetTexture(_GridKernelId, "Result", ScopeRT);
        Compute.Dispatch(_GridKernelId, ScopeRT.width, ScopeRT.height, 1);

        Compute.SetBuffer(_ScopeKernelId, "ScopeData", _ScopeDataBuffer);
        Compute.SetTexture(_ScopeKernelId, "Result", ScopeRT);
        Compute.Dispatch(_ScopeKernelId, ScopeNode.BUFFER_SIZE, 1, 1);
    }
}

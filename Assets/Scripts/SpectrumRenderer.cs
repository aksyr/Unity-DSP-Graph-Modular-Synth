using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Collections;
using Unity.Mathematics;

public class SpectrumRenderer : MonoBehaviour
{
    public RenderTexture SpectrumRT;
    public ComputeShader Compute;

    private bool _Initialized = false;
    private bool _Waiting = false;

    private DSPGraph _Graph;
    private DSPNode _ScopeNode;
    private NativeArray<float2> _Buffer;

    private ComputeBuffer _ScopeDataBuffer;
    private int _GridKernelId;
    private int _SpectrumKernelId;

    void Awake()
    {
        SpectrumRT = new RenderTexture(SpectrumNode.BUFFER_SIZE, 340, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
        SpectrumRT.enableRandomWrite = true;
        SpectrumRT.Create();

        _Buffer = new NativeArray<float2>(SpectrumNode.BUFFER_SIZE, Allocator.Persistent);
        _ScopeDataBuffer = new ComputeBuffer(_Buffer.Length, sizeof(float)*2);

        _GridKernelId = Compute.FindKernel("Grid");
        _SpectrumKernelId = Compute.FindKernel("Spectrum");
    }

    public void Init(DSPGraph graph, DSPNode scopeNode)
    {
        _Graph = graph;
        _ScopeNode = scopeNode;
        _Initialized = true;

        var sm = FindObjectOfType<ScopeManager>();
        if (sm != null) sm.Register(this);
    }

    void OnDestroy()
    {
        SpectrumRT?.Release();
        _Buffer.Dispose();
        _ScopeDataBuffer?.Dispose();
        _Initialized = false;
    }

    void Update()
    {
        if (_Initialized == false) return;

        if (!_Waiting)
        {
            using (DSPCommandBlock block = _Graph.CreateCommandBlock())
            {
                _Waiting = true;
                block.CreateUpdateRequest<SpectrumUpdateKernel, SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>(new SpectrumUpdateKernel(_Buffer), _ScopeNode, req =>
                {
                    UpdateRequestFinished(req);
                    req.Dispose();
                });
            }
        }
    }

    void UpdateRequestFinished(DSPNodeUpdateRequest<SpectrumUpdateKernel, SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode> request)
    {
        _Waiting = false;
        if (_Initialized == false) return;
        _ScopeDataBuffer?.SetData(_Buffer);

        Compute.SetInt("BufferSize", _Buffer.Length);
        Compute.SetInt("SampleRate", AudioSettings.GetConfiguration().sampleRate);

        Compute.SetTexture(_GridKernelId, "Result", SpectrumRT);
        Compute.Dispatch(_GridKernelId, SpectrumRT.width, SpectrumRT.height, 1);

        Compute.SetBuffer(_SpectrumKernelId, "SpectrumData", _ScopeDataBuffer);
        Compute.SetTexture(_SpectrumKernelId, "Result", SpectrumRT);
        Compute.Dispatch(_SpectrumKernelId, SpectrumRT.width, 1, 1);
    }
}

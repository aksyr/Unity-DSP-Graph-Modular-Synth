using UnityEngine;
using System.Collections;
using Unity.Mathematics;
using Unity.Audio;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;


[BurstCompile(CompileSynchronously = true)]
public struct SpectrumNode : IAudioKernel<SpectrumNode.Parameters, SpectrumNode.Providers>
{
    public enum WindowType
    {
        Rectangular = 0,
        Hamming,
        BlackmanHarris
    }

    public enum Parameters
    {
        Window
    }

    public enum Providers
    {
    }

    public const int BUFFER_SIZE = 1024;

    private NativeArray<float2> _Buffer;
    public NativeArray<float2> Buffer { get { return _Buffer; } }

    public void Initialize()
    {
        _Buffer = new NativeArray<float2>(BUFFER_SIZE, Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Inputs.Count != 1) return;

        SampleBuffer input = context.Inputs.GetSampleBuffer(0);
        Debug.Assert(input.Channels == 1);
        Debug.Assert(input.Samples == BUFFER_SIZE);
        NativeArray<float> inputBuffer = input.GetBuffer(0);

        WindowType windowType = (WindowType)math.round(context.Parameters.GetFloat(Parameters.Window, 0));

        //unsafe
        //{
        //    UnsafeUtility.MemClear(_Buffer.GetUnsafePtr(), UnsafeUtility.SizeOf<float2>() * _Buffer.Length);
        //    UnsafeUtility.MemCpyStride(_Buffer.GetUnsafePtr(), UnsafeUtility.SizeOf<float2>(), inputBuffer.GetUnsafeReadOnlyPtr(), UnsafeUtility.SizeOf<float>(), UnsafeUtility.SizeOf<float>(), math.min(inputBuffer.Length, _Buffer.Length));
        //}

        // prepare
        int q = (int)math.round(math.log(_Buffer.Length) / math.log(2));
        Debug.Assert((int)math.round(math.pow(2, q)) == _Buffer.Length);
        int offset = 32 - q;
        int c = ~(~0 << q);

        for (int i = 0; i < inputBuffer.Length; ++i)
        {
            float window = windowVal(windowType, i, inputBuffer.Length);
            _Buffer[i] = new float2(inputBuffer[i] * window, 0f);
        }
        for(int i = inputBuffer.Length; i < _Buffer.Length; ++i)
        {
            _Buffer[i] = float2.zero;
        }
        for(int i=1; i<_Buffer.Length; ++i)
        {
            int revI = (math.reversebits(i) >> offset) & c;
            if (revI <= i) continue;
            var temp = _Buffer[i];
            _Buffer[i] = _Buffer[revI];
            _Buffer[revI] = temp;
        }

        // fft
        fft(_Buffer);
    }

    float windowVal(WindowType windowType, int n, int N)
    {
        switch (windowType)
        {
            case WindowType.Rectangular: return 1.0f;
            case WindowType.Hamming: return hammingWindow(n, N);
            case WindowType.BlackmanHarris: return blackmanHarrisWindow(n, N);
        }
        return 1.0f;
    }

    float hammingWindow(int n, int N)
    {
        const float a0 = 0.53836f;
        const float a1 = 0.46164f;
        return a0 - a1 * math.cos((float)n / (float)N);
    }

    float blackmanHarrisWindow(int n, int N)
    {
        const float a0 = 0.35875f;
        const float a1 = 0.48829f;
        const float a2 = 0.14128f;
        const float a3 = 0.01168f;

        return a0 - a1 * math.cos(2 * math.PI * n / N) + a2 * math.cos(4 * math.PI * n / N) - a3 * math.cos(6 * math.PI * n / N);
    }

    void fft(NativeArray<float2> buffer)
    {
        for (int N = 2; N <= buffer.Length; N <<= 1)
        {
            for (int i = 0; i < buffer.Length; i += N)
            {
                for (int k = 0; k < N / 2; k++)
                {
                    int evenIndex = i + k;
                    int oddIndex = i + k + (N / 2);
                    var even = buffer[evenIndex];
                    var odd = buffer[oddIndex];

                    float term = -2 * math.PI * k / (float)N;
                    float2 exp = mulComplex(new float2(math.cos(term), math.sin(term)), odd);

                    buffer[evenIndex] = even + exp;
                    buffer[oddIndex] = even - exp;
                }
            }
        }
    }

    float2 mulComplex(float2 l, float2 r)
    {
        return new float2(
            l.x * r.x - l.y * r.y,
            l.x * r.y + l.y * r.x);
    }

    public void Dispose()
    {
        if (_Buffer.IsCreated) _Buffer.Dispose();
    }
}

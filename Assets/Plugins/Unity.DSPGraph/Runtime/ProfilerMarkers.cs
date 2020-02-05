using Unity.Profiling;

namespace Unity.Audio
{
    internal struct ProfilerMarkers
    {
        public ProfilerMarker BeginMixMarker;
        public ProfilerMarker ReadMixMarker;
        public ProfilerMarker MixNodeInputsMarker;
        public ProfilerMarker AudioKernelInitializeMarker;
        public ProfilerMarker AudioKernelExecuteMarker;
        public ProfilerMarker AudioKernelExecutionWrapperMarker;
        public ProfilerMarker AudioKernelDisposeMarker;

        public static ProfilerMarkers Create()
        {
            return new ProfilerMarkers
            {
                BeginMixMarker = new ProfilerMarker("DSPGraph.BeginMix"),
                ReadMixMarker = new ProfilerMarker("DSPGraph.ReadMix"),
                MixNodeInputsMarker = new ProfilerMarker("DSPGraph.MixJobInputs"),
                AudioKernelInitializeMarker = new ProfilerMarker("AudioKernel.Initialize"),
                AudioKernelExecuteMarker = new ProfilerMarker("AudioKernel.Execute"),
                AudioKernelExecutionWrapperMarker = new ProfilerMarker("AudioKernel.ExecutionWrapper"),
                AudioKernelDisposeMarker = new ProfilerMarker("AudioKernel.Dispose"),
            };
        }
    }
}
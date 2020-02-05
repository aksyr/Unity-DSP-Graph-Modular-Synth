using System;

namespace Unity.Audio
{
    internal interface IDSPCommand : IDisposable
    {
        void Schedule();
        void Cancel();
    }

    // This is just here to give us a pointer type
    internal struct DSPCommand : IDSPCommand
    {
        public void Schedule()
        {
            throw new NotImplementedException();
        }

        public void Cancel()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public static unsafe void Schedule(void* commandPointer)
        {
            switch (*(DSPCommandType*)commandPointer)
            {
                case DSPCommandType.Complete:
                    ((CompleteCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.CreateDSPNode:
                    ((CreateDSPNodeCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.SetFloat:
                    ((SetFloatCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.AddFloatKey:
                    ((AddFloatKeyCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.SustainFloat:
                    ((SustainFloatCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.UpdateAudioKernel:
                    ((UpdateAudioKernelCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.UpdateAudioKernelRequest:
                    ((UpdateAudioKernelRequestCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.ReleaseDSPNode:
                    ((ReleaseDSPNodeCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.ClearDSPNode:
                    ((ClearDSPNodeCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.Connect:
                    ((ConnectCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.Disconnect:
                    ((DisconnectCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.DisconnectByHandle:
                    ((DisconnectByHandleCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.SetAttenuation:
                    ((SetAttenuationCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.SetAttenuationBuffer:
                    ((SetAttenuationBufferCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.AddAttenuationKey:
                    ((AddAttenuationKeyCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.AddAttenuationKeyBuffer:
                    ((AddAttenuationKeyBufferCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.SustainAttenuation:
                    ((SustainAttenuationCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.AddInletPort:
                    ((AddInletPortCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.AddOutletPort:
                    ((AddOutletPortCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.SetSampleProvider:
                    ((SetSampleProviderCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.InsertSampleProvider:
                    ((InsertSampleProviderCommand*)commandPointer)->Schedule();
                    break;
                case DSPCommandType.RemoveSampleProvider:
                    ((RemoveSampleProviderCommand*)commandPointer)->Schedule();
                    break;
                default:
                    throw new InvalidOperationException("Invalid command type");
            }
        }

        internal static unsafe void Dispose(void* commandPointer)
        {
            switch (*(DSPCommandType*)commandPointer)
            {
                case DSPCommandType.Complete:
                    ((CompleteCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.CreateDSPNode:
                    ((CreateDSPNodeCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.SetFloat:
                    ((SetFloatCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.AddFloatKey:
                    ((AddFloatKeyCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.SustainFloat:
                    ((SustainFloatCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.UpdateAudioKernel:
                    ((UpdateAudioKernelCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.UpdateAudioKernelRequest:
                    ((UpdateAudioKernelRequestCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.ReleaseDSPNode:
                    ((ReleaseDSPNodeCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.ClearDSPNode:
                    ((ReleaseDSPNodeCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.Connect:
                    ((ConnectCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.Disconnect:
                    ((DisconnectCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.DisconnectByHandle:
                    ((DisconnectByHandleCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.SetAttenuation:
                    ((SetAttenuationCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.SetAttenuationBuffer:
                    ((SetAttenuationBufferCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.AddAttenuationKey:
                    ((AddAttenuationKeyCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.AddAttenuationKeyBuffer:
                    ((AddAttenuationKeyBufferCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.SustainAttenuation:
                    ((SustainAttenuationCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.AddInletPort:
                    ((AddInletPortCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.AddOutletPort:
                    ((AddOutletPortCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.SetSampleProvider:
                    ((SetSampleProviderCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.InsertSampleProvider:
                    ((InsertSampleProviderCommand*)commandPointer)->Dispose();
                    break;
                case DSPCommandType.RemoveSampleProvider:
                    ((RemoveSampleProviderCommand*)commandPointer)->Dispose();
                    break;
                default:
                    throw new InvalidOperationException("Invalid command type");
            }
        }

        internal static unsafe void Cancel(void* commandPointer)
        {
            switch (*(DSPCommandType*)commandPointer)
            {
                case DSPCommandType.Complete:
                    ((CompleteCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.CreateDSPNode:
                    ((CreateDSPNodeCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.SetFloat:
                    ((SetFloatCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.AddFloatKey:
                    ((AddFloatKeyCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.SustainFloat:
                    ((SustainFloatCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.UpdateAudioKernel:
                    ((UpdateAudioKernelCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.UpdateAudioKernelRequest:
                    ((UpdateAudioKernelRequestCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.ClearDSPNode:
                    ((ClearDSPNodeCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.Connect:
                    ((ConnectCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.Disconnect:
                    ((DisconnectCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.DisconnectByHandle:
                    ((DisconnectByHandleCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.SetAttenuation:
                    ((SetAttenuationCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.SetAttenuationBuffer:
                    ((SetAttenuationBufferCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.AddAttenuationKey:
                    ((AddAttenuationKeyCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.AddAttenuationKeyBuffer:
                    ((AddAttenuationKeyBufferCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.SustainAttenuation:
                    ((SustainAttenuationCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.AddInletPort:
                    ((AddInletPortCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.AddOutletPort:
                    ((AddOutletPortCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.SetSampleProvider:
                    ((SetSampleProviderCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.InsertSampleProvider:
                    ((InsertSampleProviderCommand*)commandPointer)->Cancel();
                    break;
                case DSPCommandType.RemoveSampleProvider:
                    ((RemoveSampleProviderCommand*)commandPointer)->Cancel();
                    break;
                default:
                    throw new InvalidOperationException("Invalid command type");
            }
        }
    }
}

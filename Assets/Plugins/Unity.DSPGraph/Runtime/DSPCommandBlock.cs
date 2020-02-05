using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using UnityEngine.Video;
using UnityEngine.Experimental.Video;

namespace Unity.Audio
{
    /// <summary>
    /// DSPCommandBlock provides us with a set of Asynchronous APIs which allows us to schedule batches of DSPGraph commands
    /// </summary>
    public unsafe struct DSPCommandBlock : IHandle<DSPCommandBlock>, IDisposable
    {
        private readonly Handle m_Handle;
        private readonly DSPGraph m_Graph;
        private GrowableBuffer<IntPtr> m_Commands;

        public bool Valid => m_Handle.Valid && m_Graph.Valid;

        internal DSPCommandBlock(DSPGraph graph)
        {
            m_Graph = graph;
            m_Handle = graph.AllocateHandle();
            m_Commands = new GrowableBuffer<IntPtr>(Allocator.Persistent);
        }

        /// <summary>
        /// Method to create a new <typeparamref name="DSPNode"/> in the graph
        /// </summary>
        /// <remarks>
        /// A kernel of type <typeparamref name="TAudioKernel"/> is created inside the graph.
        /// A handle in the form of <typeparamref name="DSPNode"/> is returned
        /// for controlling the kernel.
        /// The created kernel will be bursted if its implementation is decorated with BurstCompileAttribute
        /// </remarks>
        /// <typeparam name="TParameters">Enum defining the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum defining the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">IAudioKernel which is the DSP kernel of the node</typeparam>
        /// <returns>A DSPNode handle </returns>
        public DSPNode CreateDSPNode<TParameters, TProviders, TAudioKernel>()
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out void* jobReflectionData, out AudioKernelExtensions.ParameterDescriptionData parameterDescriptionData, out AudioKernelExtensions.SampleProviderDescriptionData sampleProviderDescriptionData);
            var kernel = new TAudioKernel();
            var node = new DSPNode
            {
                Graph = m_Graph.Handle,
                Handle = m_Graph.AllocateHandle(),
            };

            QueueCommand(new CreateDSPNodeCommand
            {
                m_Type = DSPCommandType.CreateDSPNode,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_JobReflectionData = jobReflectionData,
                m_ParameterDescriptionData = parameterDescriptionData,
                m_SampleProviderDescriptionData = sampleProviderDescriptionData,
                m_JobStructMemory = Utility.CopyToPersistentAllocation(ref kernel),
                m_KernelAlignment = UnsafeUtility.AlignOf<TAudioKernel>(),
                m_KernelSize = UnsafeUtility.SizeOf<TAudioKernel>(),
                m_NodeHandle = node.Handle,
            });

            return node;
        }

        /// <summary>
        /// Sets the value of a parameter on the specified node
        /// </summary>
        /// <param name="node">DSPNode in which the parameter value is set</param>
        /// <param name="parameter">Enum which specifies the parameter</param>
        /// <param name="value">Target value to be set</param>
        /// <param name="interpolationLength">The number of samples to reach the desired value</param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="ArgumentException">Exception thrown when parameter is unknown</exception>
        public void SetFloat<TParameters, TProviders, TAudioKernel>(DSPNode node, TParameters parameter, float value, int interpolationLength = 0)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out void* jobReflectionData, out AudioKernelExtensions.ParameterDescriptionData parameterDescriptionData);

            QueueCommand(new SetFloatCommand
            {
                m_Type = DSPCommandType.SetFloat,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_InterpolationLength = (uint)interpolationLength,
                m_JobReflectionData = jobReflectionData,
                m_Node = node.Handle,
                m_Parameter = (uint)UnsafeUtility.EnumToInt(parameter),
                m_ParameterDescriptionData = parameterDescriptionData,
                m_Value = value,
            });
        }

        /// <summary>
        /// Add a parameter value at a particular DSP clock time.
        /// </summary>
        /// <remarks>
        /// This schedules a value of the specified parameter in the future.
        /// Multiple keys can be scheduled as long as they are scheduled monotonically.
        /// Use this to schedule parameter automation.
        /// </remarks>
        /// <param name="node">The DSPNode to add the parameter key to</param>
        /// <param name="parameter">The parameter to set</param>
        /// <param name="dspClock">The clock value in the future that this parameter should reach the specified value</param>
        /// <param name="value">The desired value of the parameter at the specified time</param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="ArgumentException">Throws exception when parameter is unknown</exception>
        public void AddFloatKey<TParameters, TProviders, TAudioKernel>(DSPNode node, TParameters parameter, long dspClock, float value)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out void* jobReflectionData, out AudioKernelExtensions.ParameterDescriptionData parameterDescriptionData);
            QueueCommand(new AddFloatKeyCommand
            {
                m_Type = DSPCommandType.AddFloatKey,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_DSPClock = (ulong)dspClock,
                m_JobReflectionData = jobReflectionData,
                m_ParameterDescriptionData = parameterDescriptionData,
                m_Parameter = (uint)UnsafeUtility.EnumToInt(parameter),
                m_Value = value,
            });
        }

        /// <summary>
        /// This API will sustain the previous parameter value until the dspclock time is reached
        /// </summary>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <param name="node">The node that should have its parameter sustained</param>
        /// <param name="parameter">The parameter to sustain</param>
        /// <param name="dspClock">The time in which the parameter should be sustained until</param>
        /// <exception cref="ArgumentException">Throws exception when parameter is unknown</exception>
        public void SustainFloat<TParameters, TProviders, TAudioKernel>(DSPNode node, TParameters parameter, long dspClock)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out void* jobReflectionData, out AudioKernelExtensions.ParameterDescriptionData parameterDescriptionData);
            QueueCommand(new SustainFloatCommand
            {
                m_Type = DSPCommandType.SustainFloat,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_JobReflectionData = jobReflectionData,
                m_ParameterDescriptionData = parameterDescriptionData,
                m_Parameter = (uint)UnsafeUtility.EnumToInt(parameter),
                m_DSPClock = (ulong)dspClock,
            });
        }

        /// <summary>
        /// This API will run the <see cref="TAudioKernelUpdate"/> on the target DSPNode asynchronously.
        /// </summary>
        /// <remarks>
        /// This version simply applies the update kernel to the specified DSP kernel.
        /// The DSP kernel is passed as a ref to the update structure so that it can be modified.
        /// </remarks>
        /// <typeparam name="TAudioKernelUpdate">The update kernel type</typeparam>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <param name="updateJob">The structure used to update the running DSP kernel</param>
        /// <param name="node">The node that this update should operate on</param>
        public void UpdateAudioKernel<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(TAudioKernelUpdate updateJob, DSPNode node)
            where TAudioKernelUpdate : struct, IAudioKernelUpdate<TParameters, TProviders, TAudioKernel>
            where TParameters        : unmanaged, Enum
            where TProviders         : unmanaged, Enum
            where TAudioKernel       : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out void* nodeReflectionData, out AudioKernelExtensions.ParameterDescriptionData dummy);
            AudioKernelUpdateExtensions.GetReflectionData<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(out void* updateJobReflectionData);

            QueueCommand(new UpdateAudioKernelCommand
            {
                m_Type = DSPCommandType.UpdateAudioKernel,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_JobReflectionData = nodeReflectionData,
                m_JobStructMemory = Utility.CopyToPersistentAllocation(ref updateJob),
                m_UpdateReflectionData = updateJobReflectionData,
            });
        }

        /// <summary>
        /// This API will run the TAudioKernelUpdate on the target DSPNode asynchronously. Also returns
        /// a <typeparamref name="DSPNodeUpdateRequest"/> that can be used to track the progress of
        /// the update.
        /// </summary>
        /// <remarks>
        /// The <typeparamref name="DSPNodeUpdateRequest"/> returned allow access to the update structure
        /// after it has updated the DSP kernel. This can be used to retrieve information from the DSP kernel
        /// and process it on the main thread.
        /// </remarks>
        /// <typeparam name="TAudioKernelUpdate">The update kernel type</typeparam>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <param name="updateJob">The structure used to update the running DSP kernel</param>
        /// <param name="node">The node that this update should operate on</param>
        /// <param name="callback">This will be executed on the main thread after the update job has run asynchronously</param>
        /// <returns>A struct that can query status and also retrieve the update structure after it has updated the node</returns>
        public DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel> CreateUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(
            TAudioKernelUpdate updateJob, DSPNode node, Action<DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>> callback)
            where TAudioKernelUpdate : struct, IAudioKernelUpdate<TParameters, TProviders, TAudioKernel>
            where TParameters        : unmanaged, Enum
            where TProviders         : unmanaged, Enum
            where TAudioKernel       : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out void* nodeReflectionData, out AudioKernelExtensions.ParameterDescriptionData dummy);
            AudioKernelUpdateExtensions.GetReflectionData<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(out void* updateJobReflectionData);

            var request = new DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(node);
            m_Graph.RegisterUpdateRequest(request, DSPGraphExtensions.WrapAction(callback, request));

            QueueCommand(new UpdateAudioKernelRequestCommand
            {
                m_Type = DSPCommandType.UpdateAudioKernelRequest,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_UpdateRequestHandle = request.Handle,
                m_JobStructMemory = Utility.CopyToPersistentAllocation(ref updateJob),
                m_JobReflectionData = nodeReflectionData,
                m_UpdateReflectionData = updateJobReflectionData,
            });

            return request;
        }

        /// <summary>
        /// Asynchronously releases the DSPNode and automatically disconnects all inputs and outputs.
        /// </summary>
        /// <remarks>
        /// It also releases all the resources allocation through the resource context.
        /// </remarks>
        /// <param name="node">The node to release</param>
        public void ReleaseDSPNode(DSPNode node)
        {
            AssertSameGraphAsNode(node);
            QueueCommand(new ReleaseDSPNodeCommand
            {
                m_Type = DSPCommandType.ReleaseDSPNode,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_NodeHandle = node.Handle,
            });
        }

        internal void ClearDSPNode(DSPNode node)
        {
            AssertSameGraphAsNode(node);
            QueueCommand(new ClearDSPNodeCommand
            {
                m_Type = DSPCommandType.ClearDSPNode,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node,
            });
        }

        /// <summary>
        /// Connects 2 nodes
        /// </summary>
        /// <remarks>
        /// It is also necessary to ensure that the formats of the ports of both source and target are the same.
        /// </remarks>
        /// <param name="source">The source node for the connection</param>
        /// <param name="outputPort">The index of the source's output port </param>
        /// <param name="destination">The destination node for the connection</param>
        /// <param name="inputPort">The index of the destination's input port</param>
        /// <returns>Returns a DSPConnection object</returns>
        public DSPConnection Connect(DSPNode source, int outputPort, DSPNode destination, int inputPort)
        {
            AssertSameGraph(source.Graph, "The block and output node passed must be from the same parent DSPGraph");
            AssertSameGraph(destination.Graph, "The block and input node passed must be from the same parent DSPGraph");

            var connection = new DSPConnection
            {
                Graph = m_Graph.Handle,
                Handle = m_Graph.AllocateHandle(),
            };
            QueueCommand(new ConnectCommand
            {
                m_Type = DSPCommandType.Connect,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Source = source.Handle,
                m_OutputPort = outputPort,
                m_Destination = destination.Handle,
                m_InputPort = inputPort,
            });
            return connection;
        }

        /// <summary>
        /// Disconnects 2 nodes based on the <typeparamref name="DSPConnection"/> handle.
        /// </summary>
        /// <param name="connection">The handle specifying the connection between 2 nodes</param>
        public void Disconnect(DSPConnection connection)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new DisconnectByHandleCommand
            {
                m_Type = DSPCommandType.DisconnectByHandle,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
            });
        }

        /// <summary>
        /// Removes connection between two nodes
        /// </summary>
        /// <remarks>
        /// The <typeparamref name="DSPConnection"/> returned during the connection phase
        /// of the given relationship will also become invalid.
        /// </remarks>
        /// <param name="source">The source node for the connection</param>
        /// <param name="outputPort">The index of the source's output port </param>
        /// <param name="destination">The destination node for the connection</param>
        /// <param name="inputPort">The index of the destination's input port</param>
        public void Disconnect(DSPNode source, int outputPort, DSPNode destination, int inputPort)
        {
            AssertSameGraph(source.Graph, "The block and output node passed must be from the same parent DSPGraph");
            AssertSameGraph(destination.Graph, "The block and input node passed must be from the same parent DSPGraph");
            QueueCommand(new DisconnectCommand
            {
                m_Type = DSPCommandType.Disconnect,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Source = source.Handle,
                m_OutputPort = outputPort,
                m_Destination = destination.Handle,
                m_InputPort = inputPort,
            });
        }

        /// <summary>
        /// Sets the attenuation of a connection. Attenuation will be applied when the samples are routed to the next node through the associated connection
        /// </summary>
        /// <param name="connection">DSPConnection on which the attenuation is set</param>
        /// <param name="value">Float value of the attenuation</param>
        /// <param name="interpolationLength">UInt specifying the interpolation length</param>
        public void SetAttenuation(DSPConnection connection, float value, int interpolationLength = 0)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new SetAttenuationCommand
            {
                m_Type = DSPCommandType.SetAttenuation,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 1,
                m_Value0 = value,
                m_InterpolationLength = (uint)interpolationLength,
            });
        }

        public void SetAttenuation(DSPConnection connection, float value1, float value2, int interpolationLength = 0)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new SetAttenuationCommand
            {
                m_Type = DSPCommandType.SetAttenuation,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 2,
                m_Value0 = value1,
                m_Value1 = value2,
                m_InterpolationLength = (uint)interpolationLength,
            });
        }

        public void SetAttenuation(DSPConnection connection, float value1, float value2, float value3, int interpolationLength = 0)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new SetAttenuationCommand
            {
                m_Type = DSPCommandType.SetAttenuation,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 3,
                m_Value0 = value1,
                m_Value1 = value2,
                m_Value2 = value3,
                m_InterpolationLength = (uint)interpolationLength,
            });
        }

        public void SetAttenuation(DSPConnection connection, float value1, float value2, float value3, float value4, int interpolationLength = 0)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new SetAttenuationCommand
            {
                m_Type = DSPCommandType.SetAttenuation,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 4,
                m_Value0 = value1,
                m_Value1 = value2,
                m_Value2 = value3,
                m_Value3 = value4,
                m_InterpolationLength = (uint)interpolationLength,
            });
        }

        public void SetAttenuation(DSPConnection connection, float* value, byte dimension, int interpolationLength = 0)
        {
            AssertSameGraphAsConnection(connection);
            ValidateDimension(dimension);
            QueueCommand(new SetAttenuationBufferCommand
            {
                m_Type = DSPCommandType.SetAttenuationBuffer,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = dimension,
                m_Values = value,
                m_InterpolationLength = (uint)interpolationLength,
            });
        }

        private static void ValidateDimension(byte dimension)
        {
            if (dimension < 0 || dimension > 4 /* FIXME */)
                throw new ArgumentOutOfRangeException("dimension");
        }

        /// <summary>
        /// Used to add attenuation value to a DSPConnection
        /// </summary>
        /// <param name="connection">DSPConnection to which attenuation is applied</param>
        /// <param name="dspClock">Specifies the DSPClock time at which the attenuation value takes effect</param>
        /// <param name="value">Attenuation value to be applied</param>
        public void AddAttenuationKey(DSPConnection connection, long dspClock, float value)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new AddAttenuationKeyCommand
            {
                m_Type = DSPCommandType.AddAttenuationKey,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 1,
                m_Value0 = value,
                m_DSPClock = (ulong)dspClock,
            });
        }

        public void AddAttenuationKey(DSPConnection connection, long dspClock, float value1, float value2)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new AddAttenuationKeyCommand
            {
                m_Type = DSPCommandType.AddAttenuationKey,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 2,
                m_Value0 = value1,
                m_Value1 = value2,
                m_DSPClock = (ulong)dspClock,
            });
        }

        public void AddAttenuationKey(DSPConnection connection, long dspClock, float value1, float value2, float value3)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new AddAttenuationKeyCommand
            {
                m_Type = DSPCommandType.AddAttenuationKey,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 3,
                m_Value0 = value1,
                m_Value1 = value2,
                m_Value2 = value3,
                m_DSPClock = (ulong)dspClock,
            });
        }

        public void AddAttenuationKey(DSPConnection connection, long dspClock, float value1, float value2, float value3, float value4)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new AddAttenuationKeyCommand
            {
                m_Type = DSPCommandType.AddAttenuationKey,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = 4,
                m_Value0 = value1,
                m_Value1 = value2,
                m_Value2 = value3,
                m_Value3 = value4,
                m_DSPClock = (ulong)dspClock,
            });
        }

        public void AddAttenuationKey(DSPConnection connection, long dspClock, float* value, byte dimension)
        {
            AssertSameGraphAsConnection(connection);
            ValidateDimension(dimension);
            QueueCommand(new AddAttenuationKeyBufferCommand
            {
                m_Type = DSPCommandType.AddAttenuationKeyBuffer,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_Dimension = dimension,
                m_Values = value,
                m_DSPClock = (ulong)dspClock,
            });
        }

        /// <summary>
        /// Retains the same attenuation value till the specified DSPClock time is reached
        /// </summary>
        /// <param name="connection">The DSPConnection on which the attenuation is retained</param>
        /// <param name="dspClock">The DSPClock time upto which attenuation value is kept unchanged</param>
        public void SustainAttenuation(DSPConnection connection, long dspClock)
        {
            AssertSameGraphAsConnection(connection);
            QueueCommand(new SustainAttenuationCommand
            {
                m_Type = DSPCommandType.SustainAttenuation,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Connection = connection.Handle,
                m_DSPClock = (ulong)dspClock,
            });
        }

        /// <summary>
        /// Adds an inlet port to the node
        /// </summary>
        /// <remarks>
        /// Ports are where signal flow comes into and out of the DSP kernel
        /// </remarks>
        /// <param name="node">DSPNode specifying the Node on which the inlet is added</param>
        /// <param name="channelCount">Int specifying the number of channels</param>
        /// <param name="format">SoundFormat which specified the speaker support</param>
        public void AddInletPort(DSPNode node, int channelCount, SoundFormat format)
        {
            AssertSameGraphAsNode(node);
            QueueCommand(new AddInletPortCommand
            {
                m_Type = DSPCommandType.AddInletPort,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_ChannelCount = channelCount,
                m_Format = (int)format,
            });
        }

        /// <summary>
        /// Adds an outlet port to the node
        /// </summary>
        /// <remarks>
        /// Ports are where signal flow comes into and out of the DSP kernel
        /// </remarks>
        /// <param name="node">DSPNode specifying the node on which the outlet is added</param>
        /// <param name="channelCount">Int specifying the number of channels in the port</param>
        /// <param name="format">SoundFormat specifying the speaker support</param>
        public void AddOutletPort(DSPNode node, int channelCount, SoundFormat format)
        {
            AssertSameGraphAsNode(node);
            QueueCommand(new AddOutletPortCommand
            {
                m_Type = DSPCommandType.AddOutletPort,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_ChannelCount = channelCount,
                m_Format = (int)format,
            });
        }

        /// <summary>
        /// Adds an inlet port to the node
        /// </summary>
        /// <remarks>
        /// Ports are where signal flow comes into and out of the DSP kernel
        /// </remarks>
        /// <param name="node">DSPNode specifying the Node on which the inlet is added</param>
        /// <param name="portIndex">Int specifying port index</param>
        public void RemoveInletPort(DSPNode node, int portIndex)
        {
            AssertSameGraphAsNode(node);
            QueueCommand(new RemoveInletPortCommand
            {
                m_Type = DSPCommandType.RemoveInletPort,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_PortIndex = portIndex,
            });
        }

        /// <summary>
        /// Adds an outlet port to the node
        /// </summary>
        /// <remarks>
        /// Ports are where signal flow comes into and out of the DSP kernel
        /// </remarks>
        /// <param name="node">DSPNode specifying the node on which the outlet is added</param>
        /// <param name="portIndex">Int specifying port index</param>
        public void RemoveOutletPort(DSPNode node, int portIndex)
        {
            AssertSameGraphAsNode(node);
            QueueCommand(new RemoveOutletPortCommand
            {
                m_Type = DSPCommandType.RemoveOutletPort,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_PortIndex = portIndex,
            });
        }

        /// <summary>
        /// Sets the audio sample provider for a node. To clear an existing entry, set the provider to null. If the provider is not an array, then index is ignored.
        /// </summary>
        /// <remarks>
        /// The provider can be null to clear an existing entry.
        /// </remarks>
        /// <param name="clip">The provider to be set on the specified <typeparamref name="DSPNode"/></param>
        /// <param name="node">The node to set the audio sample provider on</param>
        /// <param name="item">The sample provider 'slot' that the given provider should be assigned to</param>
        /// <param name="index">The index into the array that the provider should be set. This is if the sample provider slot is an array.</param>
        /// <param name="startSampleFrameIndex"></param>
        /// <param name="endSampleFrameIndex"></param>
        /// <param name="loop"></param>
        /// <param name="enableSilencePadding"></param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="IndexOutOfRangeException">If the passed index into the sample provider slot is invalid</exception>
        public void SetSampleProvider<TParameters, TProviders, TAudioKernel>(
            AudioClip clip, DSPNode node, TProviders item, int index = 0,
            long startSampleFrameIndex = 0, long endSampleFrameIndex = 0, bool loop = false, bool enableSilencePadding = false)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            uint sampleProviderId = (clip == null) ? 0 : clip.Internal_CreateAudioClipSampleProvider((ulong)startSampleFrameIndex, endSampleFrameIndex, loop, enableSilencePadding);
            SetSampleProvider<TParameters, TProviders, TAudioKernel>(sampleProviderId, node, item, index, true);
        }

        /// <summary>
        /// Sets the audio sample provider for a node. To clear an existing entry, set the provider to null. If the provider is not an array, then index is ignored.
        /// </summary>
        /// <remarks>
        /// The provider can be null to clear an existing entry.
        /// </remarks>
        /// <param name="video">The provider to be set on the specified <typeparamref name="DSPNode"/></param>
        /// <param name="node">The node to set the audio sample provider on</param>
        /// <param name="item">The sample provider 'slot' that the given provider should be assigned to</param>
        /// <param name="index">The index into the array that the provider should be set. This is if the sample provider slot is an array.</param>
        /// <param name="trackIndex"></param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="IndexOutOfRangeException">If the passed index into the sample provider slot is invalid</exception>
        public void SetSampleProvider<TParameters, TProviders, TAudioKernel>(
            VideoPlayer video, DSPNode node, TProviders item, int index = 0, int trackIndex = 0)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            uint sampleProviderId = (video == null) ? 0 : video.InternalGetAudioSampleProviderId((ushort)trackIndex);
            SetSampleProvider<TParameters, TProviders, TAudioKernel>(sampleProviderId, node, item, index);
        }

        internal void SetSampleProvider<TParameters, TProviders, TAudioKernel>(
            uint providerId, DSPNode node, TProviders item, int index = 0, bool destroyOnRemove = false)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out AudioKernelExtensions.SampleProviderDescriptionData sampleProviderDescriptionData);
            var providerIndex = GetProviderIndex(item, sampleProviderDescriptionData);

            // Index validation for fixed-size array items can be performed here. For variable-array,
            // it can only be performed in the job threads, where the array size is known and stable.
            if (sampleProviderDescriptionData.Descriptions[providerIndex].m_IsArray &&
                sampleProviderDescriptionData.Descriptions[providerIndex].m_Size >= 0 &&
                (sampleProviderDescriptionData.Descriptions[providerIndex].m_Size < index || index < 0))
                throw new ArgumentOutOfRangeException(nameof(index));

            QueueCommand(new SetSampleProviderCommand
            {
                m_Type = DSPCommandType.SetSampleProvider,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_SampleProviderDescriptionData = sampleProviderDescriptionData,
                m_ProviderId = providerId,
                m_Item = UnsafeUtility.EnumToInt(item),
                m_Index = index,
                m_DestroyOnRemove = destroyOnRemove,
            });
        }

        /// <summary>
        /// Inserts a sample provider to the list
        /// </summary>
        /// <param name="clip">The provider to be inserted on the specified <typeparamref name="DSPNode"/></param>
        /// <param name="node">The node to set the audio sample provider on</param>
        /// <param name="item">The sample provider 'slot' that the given provider should be inserted into</param>
        /// <param name="index">The index into the array that the provider should be set. This is if the sample provider slot is an array.</param>
        /// <param name="startSampleFrameIndex">The first frame of the audio clip to be used in the sample provider</param>
        /// <param name="endSampleFrameIndex">The last frame of the audio clip to be used in the sample provider</param>
        /// <param name="loop">Whether the clip should be looped</param>
        /// <param name="enableSilencePadding">Whether the provider should emit silence after clip playback ends</param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="ArgumentNullException">If the passed <typeparamref name="AudioClip"/>is null</exception>
        /// <exception cref="InvalidOperationException">If the passed index into the sample provider slot is invalid</exception>
        public void InsertSampleProvider<TParameters, TProviders, TAudioKernel>(
            AudioClip clip, DSPNode node, TProviders item, int index = -1,
            long startSampleFrameIndex = 0, long endSampleFrameIndex = 0, bool loop = false, bool enableSilencePadding = false)
            where TParameters   : unmanaged, Enum
            where TProviders    : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            InsertSampleProvider<TParameters, TProviders, TAudioKernel>(clip.Internal_CreateAudioClipSampleProvider((ulong)startSampleFrameIndex, endSampleFrameIndex, loop, enableSilencePadding),
                node, item, index, true);
        }

        /// <summary>
        /// Inserts a sample provider to the list
        /// </summary>
        /// <param name="video">The provider to be inserted on the specified <typeparamref name="DSPNode"/></param>
        /// <param name="node">The node to set the audio sample provider on</param>
        /// <param name="item">The sample provider 'slot' that the given provider should be inserted into</param>
        /// <param name="index">The index into the array that the provider should be set. This is if the sample provider slot is an array.</param>
        /// <param name="trackIndex">The index of the audio track from which to create the sample provider</param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="ArgumentNullException">If the passed <typeparamref name="VideoPlayer"/>is null</exception>
        /// <exception cref="InvalidOperationException">If the passed index into the sample provider slot is invalid</exception>
        public void InsertSampleProvider<TParameters, TProviders, TAudioKernel>(
            VideoPlayer video, DSPNode node, TProviders item, int index = -1, int trackIndex = 0)
            where TParameters   : unmanaged, Enum
            where TProviders    : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            if (video == null)
                throw new ArgumentNullException(nameof(video));
            InsertSampleProvider<TParameters, TProviders, TAudioKernel>(video.InternalGetAudioSampleProviderId((ushort)trackIndex),
                node, item, index);
        }

        internal void InsertSampleProvider<TParameters, TProviders, TAudioKernel>(
            uint providerId, DSPNode node, TProviders item, int index = -1, bool destroyOnRemove = false)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out AudioKernelExtensions.SampleProviderDescriptionData sampleProviderDescriptionData);
            var providerIndex = GetProviderIndex(item, sampleProviderDescriptionData);

            // Can only insert into variable-size arrays.
            if (!sampleProviderDescriptionData.Descriptions[providerIndex].m_IsArray ||
                sampleProviderDescriptionData.Descriptions[providerIndex].m_Size >= 0)
                throw new InvalidOperationException("Can only insert into variable-size array.");

            QueueCommand(new InsertSampleProviderCommand
            {
                m_Type = DSPCommandType.InsertSampleProvider,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_SampleProviderDescriptionData = sampleProviderDescriptionData,
                m_ProviderId = providerId,
                m_Item = UnsafeUtility.EnumToInt(item),
                m_Index = index,
                m_DestroyOnRemove = destroyOnRemove,
            });
        }

        /// <summary>
        /// Removes <typeparamref name="AudioSampleProvider"/> from the specified <typeparamref name="DSPNode"/>. If index is not passed or is -1 then it is removed from the last.
        /// </summary>
        /// <param name="node">The node to remove the <typeparamref name="AudioSampleProvider"/> from</param>
        /// <param name="item">The sample provider 'slot' that should have the sample provider removed from</param>
        /// <param name="index">The index into the array that the provider should be removed from. This is if the sample provider slot is an array.</param>
        /// <typeparam name="TParameters">Enum type of the parameters of the node</typeparam>
        /// <typeparam name="TProviders">Enum type of the sample providers of the node</typeparam>
        /// <typeparam name="TAudioKernel">The kernel type of the node</typeparam>
        /// <exception cref="ArgumentException">Unknown SampleProvider</exception>
        /// <exception cref="InvalidOperationException">Can only remove from variable-size array</exception>
        public void RemoveSampleProvider<TParameters, TProviders, TAudioKernel>(DSPNode node, TProviders item, int index = -1)
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            AssertSameGraphAsNode(node);

            AudioKernelExtensions.GetReflectionData<TAudioKernel, TParameters, TProviders>(out AudioKernelExtensions.SampleProviderDescriptionData sampleProviderDescriptionData);
            var itemIndex = UnsafeUtility.EnumToInt(item);
            // Can only remove from variable-size arrays.
            if (!sampleProviderDescriptionData.Descriptions[itemIndex].m_IsArray ||
                sampleProviderDescriptionData.Descriptions[itemIndex].m_Size >= 0)
                throw new InvalidOperationException("Can only remove sample providers from variable-size array");

            QueueCommand(new RemoveSampleProviderCommand
            {
                m_Type = DSPCommandType.RemoveSampleProvider,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
                m_Node = node.Handle,
                m_SampleProviderDescriptionData = sampleProviderDescriptionData,
                m_Item = UnsafeUtility.EnumToInt(item),
                m_Index = index,
            });
        }

        /// <summary>
        /// Completes the DSPCommandBlock and sends it for asynchronous atomic handling by the DSPGraph. Once this is called, the DSPCommandBlock is disposed and cannot be used again.
        /// </summary>
        public void Complete()
        {
            if (!m_Commands.Valid)
                return;

            QueueCommand(new CompleteCommand
            {
                m_Type = DSPCommandType.Complete,
                m_Graph = m_Graph,
                m_Handle = m_Handle,
            });
            m_Graph.ScheduleCommandBuffer(m_Commands);
            m_Commands.Dispose();
            m_Commands = default;
        }

        public void Cancel()
        {
            if (!m_Commands.Valid)
                return;

            foreach (IntPtr command in m_Commands)
            {
                DSPCommand.Cancel((void*)command);
                m_Graph.ReleaseCommand((void*)command);
            }

            m_Commands.Dispose();
            m_Commands = default;
            m_Graph.DisposeHandle(m_Handle);
        }

        private void AssertSameGraph(Handle graph, string message)
        {
            if (!m_Graph.Handle.Equals(graph))
                throw new ArgumentException(message);
        }

        private void AssertSameGraphAsNode(DSPNode node)
        {
            AssertSameGraph(node.Graph, "The block and node passed must be from the same parent DSPGraph");
        }

        private void AssertSameGraphAsConnection(DSPConnection connection)
        {
            AssertSameGraph(connection.Graph, "The block and connection passed must be from the same parent DSPGraph");
        }

        void QueueCommand<T>(T command)
            where T : unmanaged, IDSPCommand
        {
            if (!m_Commands.Valid)
                throw new InvalidOperationException("Command buffer has not been initialized");
            var queueCommand = m_Graph.AllocateCommand<T>();
            *queueCommand = command;
            m_Commands.Add((IntPtr)queueCommand);
        }

        private static int GetProviderIndex<T>(T provider, AudioKernelExtensions.SampleProviderDescriptionData sampleProviderDescriptionData)
            where T : unmanaged, Enum
        {
            var index = Convert.ToInt32(provider);
            if (index < 0 || index >= sampleProviderDescriptionData.SampleProviderCount)
                throw new ArgumentException($"Unknown sample provider {provider}");
            return index;
        }

        internal static int GetProviderIndex(int index, AudioKernelExtensions.SampleProviderDescriptionData sampleProviderDescriptionData)
        {
            if (index < 0 || index >= sampleProviderDescriptionData.SampleProviderCount)
                throw new ArgumentException($"Unknown sample provider {index}");
            return index;
        }

        public void Dispose()
        {
            Complete();
        }

        public bool Equals(DSPCommandBlock other)
        {
            return m_Handle.Equals(other.m_Handle) && m_Graph.Equals(other.m_Graph);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPCommandBlock other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = m_Handle.GetHashCode();
                hashCode = (hashCode * 397) ^ m_Graph.GetHashCode();
                return hashCode;
            }
        }
    }
}

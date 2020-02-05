using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Media.Utilities;

namespace Unity.Audio
{
    internal static class DSPGraphExtensions
    {
        private static GrowableBuffer<DSPGraph> s_GraphRegistry;
        private static bool s_Initialized;

        public static unsafe DSPGraph** UnsafeGraphBuffer => (DSPGraph**)s_GraphRegistry.Description.Data;
        internal static readonly DSPGraph.Trampoline DisposeMethod = DSPGraph.DoDispose;

        public static void Initialize()
        {
            if (s_Initialized)
                return;
            s_Initialized = true;
            Utility.EnableLeakTracking = true;
            s_GraphRegistry = new GrowableBuffer<DSPGraph>(Allocator.Persistent);
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
        }

        /// <summary>
        /// This is our last chance to clean up native allocations.
        /// It will be called after reflection data is already gone,
        /// so we can't run dispose jobs etc.
        /// </summary>
        private static void OnDomainUnload(object sender, EventArgs e)
        {
            if (!s_Initialized)
                return;

            s_Initialized = false;
            foreach (var graph in s_GraphRegistry)
                if (graph.Valid)
                    graph.Dispose(DSPGraph.DisposeBehavior.DeallocateOnly);
            s_GraphRegistry.Dispose();
            Utility.VerifyLeaks();
        }

        public static int FindFreeGraphIndex()
        {
            Initialize();
            for (int i = 0; i < s_GraphRegistry.Count; ++i)
                if (!s_GraphRegistry[i].Valid)
                    return i;
            s_GraphRegistry.Add(default);
            return s_GraphRegistry.Count - 1;
        }

        public static DSPGraph Lookup(Handle handle)
        {
            return Lookup(handle.Id);
        }

        private static DSPGraph Lookup(int index)
        {
            Initialize();
            return s_GraphRegistry[index];
        }

        public static void Register(this DSPGraph self)
        {
            s_GraphRegistry[self.Handle.Id] = self;
        }

        public static void Unregister(this DSPGraph self)
        {
            s_GraphRegistry[self.Handle.Id] = default;
        }

        public static void PostEvent<TNodeEvent>(this DSPGraph self, int nodeIndex, TNodeEvent data)
            where TNodeEvent : struct
        {
            var hash = BurstRuntime.GetHashCode64<TNodeEvent>();
            var eventHandlers = self.EventHandlers;

            for (int i = 0; i < eventHandlers.Count; ++i)
                if (eventHandlers[i].Hash == hash)
                    QueueEventCallback(self, nodeIndex, eventHandlers[i].Handler, data);
        }

        private static unsafe void QueueEventCallback<TNodeEvent>(this DSPGraph self, int nodeIndex, GCHandle handler, TNodeEvent data)
            where TNodeEvent : struct
        {
            var handle = self.EventHandlerAllocator.Acquire();
            *handle = new DSPGraph.EventHandlerDescription
            {
                Handler = handler,
                Data = Utility.CopyToPersistentAllocation(ref data),
                NodeIndex = nodeIndex,
            };

            self.MainThreadCallbacks.Enqueue(handle);
        }

        public static unsafe void InvokePendingCallbacks(this DSPGraph self)
        {
            var invocations = self.MainThreadCallbacks;
            while (!invocations.IsEmpty)
            {
                DSPGraph.EventHandlerDescription* description = invocations.Dequeue();

                if (description->Handler.Target != null)
                {
                    if (description->Data == null)
                    {
                        // Update request callback
                        ((Action)description->Handler.Target)();
                        description->Handler.Free();
                    }
                    else
                    {
                        // Node event callback
                        ((DSPGraph.NodeEventCallback)description->Handler.Target)(self.LookupNode(description->NodeIndex), description->Data);
                        Utility.FreeUnsafe(description->Data);
                        // Don't free callback handle here - it can be reused until the event is unregistered
                    }
                }

                self.EventHandlerAllocator.Release(description);
            }
        }

        public static GCHandle WrapAction<T>(Action<T> callback, T argument)
        {
            if (callback == null)
                return default;
            void wrapper() => callback(argument);
            return GCHandle.Alloc((Action)wrapper);
        }
    }
}

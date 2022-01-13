# About Media Utilities

com.unity.media.utilities is a set of high-performance, burst-friendly C# utility classes created for use by Unity's media packages.

It includes:
- AtomicFreeList: A multiple-reader, multiple-writer blittable generic concurrent free list
- AtomicQueue: A multiple-reader, multiple-writer blittable generic concurrent queue
- BatchAllocator: A batching allocator backed by native allocations
- FIFO: TODO
- GrowableBuffer: A List<T>-like blittable data structure
- PagedFreeList: A growable, sparse, blittable generic list whose storage is never relocated once allocated

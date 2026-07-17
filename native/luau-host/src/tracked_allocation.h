#ifndef LUAU_HOST_TRACKED_ALLOCATION_H
#define LUAU_HOST_TRACKED_ALLOCATION_H

#include <cstddef>
#include <cstdlib>
#include <limits>

namespace luau_host_internal
{
struct alignas(std::max_align_t) TrackedAllocationHeader
{
    size_t retainedSize;
};

static_assert(
    sizeof(TrackedAllocationHeader) % alignof(std::max_align_t) == 0,
    "Tracked allocator payloads must preserve maximum fundamental alignment");

using ReallocateFunction = void* (*)(void* block, size_t size);
using FreeFunction = void (*)(void* block);

struct TrackedAllocationResizeResult
{
    void* block;
    size_t retainedSize;
    bool failed;
};

inline TrackedAllocationHeader* trackedallocationheader(void* block)
{
    return block ? static_cast<TrackedAllocationHeader*>(block) - 1 : nullptr;
}

inline size_t trackedallocationsize(void* block)
{
    TrackedAllocationHeader* header = trackedallocationheader(block);
    return header ? header->retainedSize : 0;
}

inline TrackedAllocationResizeResult resizetrackedallocation(
    void* block,
    size_t newSize,
    ReallocateFunction reallocate = std::realloc)
{
    const size_t retainedSize = trackedallocationsize(block);
    if (!reallocate || newSize > std::numeric_limits<size_t>::max() - sizeof(TrackedAllocationHeader))
        return {nullptr, retainedSize, true};

    TrackedAllocationHeader* previous = trackedallocationheader(block);
    void* storage = reallocate(previous, sizeof(TrackedAllocationHeader) + newSize);
    if (!storage)
    {
        // A prior failed shrink can leave more storage than Luau's current
        // logical size. Reusing that retained capacity is a successful resize;
        // its full size remains charged until a later realloc or free.
        if (block && newSize <= retainedSize)
            return {block, retainedSize, false};

        return {nullptr, retainedSize, true};
    }

    TrackedAllocationHeader* resized = static_cast<TrackedAllocationHeader*>(storage);
    resized->retainedSize = newSize;
    return {resized + 1, newSize, false};
}

inline void freetrackedallocation(void* block, FreeFunction release = std::free)
{
    if (block && release)
        release(trackedallocationheader(block));
}
} // namespace luau_host_internal

#endif /* LUAU_HOST_TRACKED_ALLOCATION_H */

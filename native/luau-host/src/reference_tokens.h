#pragma once

#include <atomic>
#include <cstdint>
#include <limits>

namespace luau_host_internal
{
// Process-lifetime allocator for opaque managed reference tokens. Values are
// deliberately never recycled: after INT32_MAX has been issued, allocation
// remains exhausted for the rest of the process. This makes every stale token
// unambiguously invalid without encoding VM registry implementation details.
class MonotonicReferenceTokenAllocator
{
public:
    explicit MonotonicReferenceTokenAllocator(uint64_t first = 1) noexcept
        : next_(first)
    {
    }

    int32_t allocate() noexcept
    {
        uint64_t candidate = next_.load(std::memory_order_relaxed);
        constexpr uint64_t maximum = uint64_t(std::numeric_limits<int32_t>::max());

        while (candidate <= maximum)
        {
            if (candidate != 0 && next_.compare_exchange_weak(
                    candidate,
                    candidate + 1,
                    std::memory_order_relaxed,
                    std::memory_order_relaxed))
                return int32_t(candidate);

            if (candidate == 0 && next_.compare_exchange_weak(
                    candidate,
                    UINT64_C(1),
                    std::memory_order_relaxed,
                    std::memory_order_relaxed))
                candidate = 1;
        }

        return 0;
    }

private:
    std::atomic<uint64_t> next_;
};
} // namespace luau_host_internal

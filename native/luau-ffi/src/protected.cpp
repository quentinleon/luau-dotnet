// This bridge contains every Luau call inside a native protected frame. Luau
// is built with LUA_USE_LONGJMP=1 when its public ABI uses extern "C"; without
// this frame an allocator failure can otherwise jump across Rust and CLR
// frames. Keep all operation contexts trivially destructible.

#include "lua.h"
#include "lualib.h"
#include "luacode.h"

#include "ldo.h"

#include <atomic>
#include <stddef.h>
#include <stdint.h>
#include <new>
#include <string>

namespace
{
using InterruptPoll = int (*)(lua_State* L, int gc);

// Every managed state installs the same static UnmanagedCallersOnly poll
// function. Keep that process-wide function pointer native so the Luau
// callback itself never points directly at independently managed state.
std::atomic<void*> interruptPoll = {nullptr};
std::atomic<unsigned> interruptPollOperations = {0};
std::atomic_flag interruptLifecycleLock = ATOMIC_FLAG_INIT;
unsigned interruptInstalledStates = 0;

void lockinterruptlifecycle()
{
    while (interruptLifecycleLock.test_and_set(std::memory_order_acquire))
    {
    }
}

void unlockinterruptlifecycle()
{
    interruptLifecycleLock.clear(std::memory_order_release);
}

void interrupttrampoline(lua_State* L, int gc)
{
    void* pointer = nullptr;
    for (;;)
    {
        pointer = interruptPoll.load(std::memory_order_acquire);
        if (!pointer)
            return;

        interruptPollOperations.fetch_add(1, std::memory_order_acq_rel);
        if (pointer == interruptPoll.load(std::memory_order_acquire))
            break;

        interruptPollOperations.fetch_sub(1, std::memory_order_release);
    }

    InterruptPoll poll = reinterpret_cast<InterruptPoll>(pointer);
    int action = poll(L, gc);

    // Decrement before yield/throw: either control-flow operation can bypass
    // normal C++ scope exit, while final uninstall waits for this counter.
    interruptPollOperations.fetch_sub(1, std::memory_order_release);

    if (action == 0)
        return;

    // The managed poll has returned before either path changes VM control
    // flow. Yield where Luau permits it; otherwise use an allocation-free
    // native sentinel unwind that the surrounding resume/pcall catches.
    if (lua_isyieldable(L))
    {
        lua_yield(L, 0);
        return;
    }

    luaD_throw(L, LUA_ERRMEM);
}

struct ProtectedCallContext
{
    Pfunc operation;
    void* operationContext;
};

void runprotectedoperation(lua_State* L, void* userdata)
{
    ProtectedCallContext* context = static_cast<ProtectedCallContext*>(userdata);

    // Public push/get/new APIs assume that their result slot is already within
    // the active C frame's stack limit. Grow it while the native error frame is
    // installed so both stack OOM and ordinary allocator OOM are contained.
    if (!lua_checkstack(L, 1))
        luaD_throw(L, LUA_ERRMEM);

    context->operation(L, context->operationContext);
}

int protect(lua_State* L, Pfunc operation, void* context, int consumed)
{
    LUAU_ASSERT(L);
    LUAU_ASSERT(consumed >= 0);
    LUAU_ASSERT(L->top - L->base >= consumed);

    // On failure luaD_pcall restores this stack position and pushes exactly
    // one native error object. Inputs consumed by a setter/concat/closure are
    // deliberately discarded, matching protected-call stack semantics.
    ProtectedCallContext call = {operation, context};
    return luaD_pcall(L, runprotectedoperation, &call, savestack(L, L->top - consumed), 0);
}

struct CheckStackContext
{
    int size;
    int result;
};

void checkstack(lua_State* L, void* userdata)
{
    CheckStackContext* context = static_cast<CheckStackContext*>(userdata);
    context->result = lua_checkstack(L, context->size);
}

struct StateResultContext
{
    lua_State* result;
};

void newthread(lua_State* L, void* userdata)
{
    StateResultContext* context = static_cast<StateResultContext*>(userdata);
    context->result = lua_newthread(L);
}

void resetthread(lua_State* L, void*)
{
    lua_resetthread(L);
}

struct IndexContext
{
    int index;
    int result;
};

void pushvalue(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_pushvalue(L, context->index);
}

void gettable(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_gettable(L, context->index);
}

void rawget(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_rawget(L, context->index);
}

void next(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_next(L, context->index);
}

void getmetatable(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_getmetatable(L, context->index);
}

void getfenv(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_getfenv(L, context->index);
}

void settable(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_settable(L, context->index);
}

void rawset(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_rawset(L, context->index);
}

void setmetatable(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_setmetatable(L, context->index);
}

void setfenv(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_setfenv(L, context->index);
}

void clonefunction(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_clonefunction(L, context->index);
}

void cleartable(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_cleartable(L, context->index);
}

void clonetable(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    lua_clonetable(L, context->index);
}

void makeref(lua_State* L, void* userdata)
{
    IndexContext* context = static_cast<IndexContext*>(userdata);
    context->result = lua_ref(L, context->index);
}

struct IntContext
{
    int value;
    int result;
};

void pushboolean(lua_State* L, void* userdata)
{
    lua_pushboolean(L, static_cast<IntContext*>(userdata)->value);
}

void pushinteger(lua_State* L, void* userdata)
{
    lua_pushinteger(L, static_cast<IntContext*>(userdata)->value);
}

void pushthread(lua_State* L, void* userdata)
{
    static_cast<IntContext*>(userdata)->result = lua_pushthread(L);
}

struct UnsignedContext
{
    unsigned value;
};

void pushunsigned(lua_State* L, void* userdata)
{
    lua_pushunsigned(L, static_cast<UnsignedContext*>(userdata)->value);
}

struct NumberContext
{
    double value;
};

void pushnumber(lua_State* L, void* userdata)
{
    lua_pushnumber(L, static_cast<NumberContext*>(userdata)->value);
}

struct VectorContext
{
    float x;
    float y;
    float z;
};

void pushvector(lua_State* L, void* userdata)
{
    VectorContext* context = static_cast<VectorContext*>(userdata);
    lua_pushvector(L, context->x, context->y, context->z);
}

void pushnil(lua_State* L, void*)
{
    lua_pushnil(L);
}

struct StringContext
{
    const char* value;
    size_t length;
    int index;
    int result;
    const char* pointerResult;
};

void pushlstring(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    lua_pushlstring(L, context->value, context->length);
}

void getfield(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    context->result = lua_getfield(L, context->index, context->value);
}

void rawgetfield(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    context->result = lua_rawgetfield(L, context->index, context->value);
}

void setfield(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    lua_setfield(L, context->index, context->value);
}

void rawsetfield(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    lua_rawsetfield(L, context->index, context->value);
}

void tolstring(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    context->pointerResult = lua_tolstring(L, context->index, &context->length);
}

void auxtolstring(lua_State* L, void* userdata)
{
    StringContext* context = static_cast<StringContext*>(userdata);
    context->pointerResult = luaL_tolstring(L, context->index, &context->length);
}

struct RawIndexContext
{
    int index;
    int item;
    int result;
};

void rawgeti(lua_State* L, void* userdata)
{
    RawIndexContext* context = static_cast<RawIndexContext*>(userdata);
    context->result = lua_rawgeti(L, context->index, context->item);
}

void rawseti(lua_State* L, void* userdata)
{
    RawIndexContext* context = static_cast<RawIndexContext*>(userdata);
    lua_rawseti(L, context->index, context->item);
}

struct TableContext
{
    int arraySize;
    int recordSize;
};

void createtable(lua_State* L, void* userdata)
{
    TableContext* context = static_cast<TableContext*>(userdata);
    lua_createtable(L, context->arraySize, context->recordSize);
}

struct ClosureContext
{
    lua_CFunction function;
    const char* debugName;
    int upvalues;
    lua_Continuation continuation;
};

void pushcclosure(lua_State* L, void* userdata)
{
    ClosureContext* context = static_cast<ClosureContext*>(userdata);
    lua_pushcclosurek(L, context->function, context->debugName, context->upvalues, context->continuation);
}

struct LightUserDataContext
{
    void* pointer;
    int tag;
};

void pushlightuserdata(lua_State* L, void* userdata)
{
    LightUserDataContext* context = static_cast<LightUserDataContext*>(userdata);
    lua_pushlightuserdatatagged(L, context->pointer, context->tag);
}

struct AllocationContext
{
    size_t size;
    int tag;
    void (*destructor)(void*);
    void* result;
};

void newuserdata(lua_State* L, void* userdata)
{
    AllocationContext* context = static_cast<AllocationContext*>(userdata);
    context->result = lua_newuserdatatagged(L, context->size, context->tag);
}

void newuserdatadtor(lua_State* L, void* userdata)
{
    AllocationContext* context = static_cast<AllocationContext*>(userdata);
    context->result = lua_newuserdatadtor(L, context->size, context->destructor);
}

void newbuffer(lua_State* L, void* userdata)
{
    AllocationContext* context = static_cast<AllocationContext*>(userdata);
    context->result = lua_newbuffer(L, context->size);
}

struct LoadContext
{
    const char* chunkName;
    const char* bytecode;
    size_t size;
    int environment;
    int result;
};

void load(lua_State* L, void* userdata)
{
    LoadContext* context = static_cast<LoadContext*>(userdata);
    context->result = luau_load(L, context->chunkName, context->bytecode, context->size, context->environment);
}

struct GcContext
{
    int operation;
    int data;
    int result;
};

void collect(lua_State* L, void* userdata)
{
    GcContext* context = static_cast<GcContext*>(userdata);
    context->result = lua_gc(L, context->operation, context->data);
}

struct ConcatContext
{
    int count;
};

void concat(lua_State* L, void* userdata)
{
    lua_concat(L, static_cast<ConcatContext*>(userdata)->count);
}

struct LibraryContext
{
    int library;
    int result;
};

void openlibrary(lua_State* L, void* userdata)
{
    LibraryContext* context = static_cast<LibraryContext*>(userdata);
    switch (context->library)
    {
    case 0: context->result = luaopen_base(L); break;
    case 1: context->result = luaopen_coroutine(L); break;
    case 2: context->result = luaopen_table(L); break;
    case 3: context->result = luaopen_os(L); break;
    case 4: context->result = luaopen_string(L); break;
    case 5: context->result = luaopen_bit32(L); break;
    case 6: context->result = luaopen_buffer(L); break;
    case 7: context->result = luaopen_utf8(L); break;
    case 8: context->result = luaopen_math(L); break;
    case 9: context->result = luaopen_debug(L); break;
    case 10: context->result = luaopen_vector(L); break;
    default: context->result = -1; break;
    }
}

void openlibs(lua_State* L, void*)
{
    luaL_openlibs(L);
}

void sandbox(lua_State* L, void*)
{
    luaL_sandbox(L);
}

void sandboxthread(lua_State* L, void*)
{
    luaL_sandboxthread(L);
}
} // namespace

extern "C"
{
int luau_ffi_protected_abi_version()
{
    return 2;
}

int luau_ffi_protected_compile(
    const char* source,
    size_t size,
    lua_CompileOptions* options,
    char** output,
    size_t* outputSize)
{
    if (!output || !outputSize)
        return 2;

    *output = nullptr;
    *outputSize = 0;

    if ((!source && size != 0) || size > std::string().max_size())
        return 2;

    try
    {
        size_t compiledSize = 0;
        char* compiled = luau_compile(source, size, options, &compiledSize);
        if (!compiled)
            return 1;

        *output = compiled;
        *outputSize = compiledSize;
        return 0;
    }
    catch (const std::bad_alloc&)
    {
        return 1;
    }
    catch (...)
    {
        return 2;
    }
}

int luau_ffi_protected_checkstack(lua_State* L, int size, int* result)
{
    CheckStackContext context = {size, 0};
    int status = protect(L, checkstack, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_newthread(lua_State* L, lua_State** result)
{
    if (result) *result = nullptr;
    StateResultContext context = {};
    int status = protect(L, newthread, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_resetthread(lua_State* L)
{
    // lua_resetthread first mutates call-frame state, then shrinks the call-info
    // and value stacks. Either shrink can allocate and fail. luaD_pcall cannot
    // safely restore a pre-reset CallInfo index after that partial mutation, so
    // contain the longjmp with the raw native frame and make failure terminal
    // for this state. No error-object/stack-shape guarantee is made on failure.
    return luaD_rawrunprotected(L, resetthread, nullptr);
}

int luau_ffi_protected_install_interrupt(lua_State* L, void* poll)
{
    if (!L || !poll)
        return 0;

    lua_Callbacks* callbacks = lua_callbacks(L);
    lockinterruptlifecycle();

    void* current = interruptPoll.load(std::memory_order_relaxed);
    if (callbacks->interrupt == interrupttrampoline)
    {
        int result = current == poll;
        unlockinterruptlifecycle();
        return result;
    }

    if (interruptInstalledStates != 0 && current != poll)
    {
        unlockinterruptlifecycle();
        return 0;
    }

    if (interruptInstalledStates == 0)
        interruptPoll.store(poll, std::memory_order_release);

    callbacks->interrupt = interrupttrampoline;
    interruptInstalledStates++;
    unlockinterruptlifecycle();
    return 1;
}

void luau_ffi_protected_uninstall_interrupt(lua_State* L)
{
    if (!L)
        return;

    lua_Callbacks* callbacks = lua_callbacks(L);
    lockinterruptlifecycle();

    if (callbacks->interrupt == interrupttrampoline)
    {
        callbacks->interrupt = nullptr;
        LUAU_ASSERT(interruptInstalledStates > 0);
        interruptInstalledStates--;

        if (interruptInstalledStates == 0)
        {
            // Stop new trampoline entries, then wait out any managed poll that
            // already acquired the old pointer. Holding the lifecycle lock
            // prevents a new domain/pointer install until that drain finishes.
            interruptPoll.store(nullptr, std::memory_order_release);
            while (interruptPollOperations.load(std::memory_order_acquire) != 0)
            {
            }
        }
    }

    unlockinterruptlifecycle();
}

int luau_ffi_protected_pushvalue(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, pushvalue, &context, 0); }
int luau_ffi_protected_pushnil(lua_State* L) { return protect(L, pushnil, nullptr, 0); }
int luau_ffi_protected_pushboolean(lua_State* L, int value) { IntContext context = {value, 0}; return protect(L, pushboolean, &context, 0); }
int luau_ffi_protected_pushinteger(lua_State* L, int value) { IntContext context = {value, 0}; return protect(L, pushinteger, &context, 0); }
int luau_ffi_protected_pushunsigned(lua_State* L, unsigned value) { UnsignedContext context = {value}; return protect(L, pushunsigned, &context, 0); }
int luau_ffi_protected_pushnumber(lua_State* L, double value) { NumberContext context = {value}; return protect(L, pushnumber, &context, 0); }
int luau_ffi_protected_pushvector(lua_State* L, float x, float y, float z) { VectorContext context = {x, y, z}; return protect(L, pushvector, &context, 0); }
int luau_ffi_protected_pushlstring(lua_State* L, const char* value, size_t length) { StringContext context = {value, length, 0, 0, nullptr}; return protect(L, pushlstring, &context, 0); }

int luau_ffi_protected_pushcclosurek(lua_State* L, lua_CFunction function, const char* debugName, int upvalues, lua_Continuation continuation)
{
    ClosureContext context = {function, debugName, upvalues, continuation};
    return protect(L, pushcclosure, &context, upvalues);
}

int luau_ffi_protected_pushlightuserdatatagged(lua_State* L, void* pointer, int tag) { LightUserDataContext context = {pointer, tag}; return protect(L, pushlightuserdata, &context, 0); }

int luau_ffi_protected_pushthread(lua_State* L, int* result)
{
    IntContext context = {};
    int status = protect(L, pushthread, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_newuserdatatagged(lua_State* L, size_t size, int tag, void** result)
{
    if (result) *result = nullptr;
    AllocationContext context = {size, tag, nullptr, nullptr};
    int status = protect(L, newuserdata, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_newuserdatadtor(lua_State* L, size_t size, void (*destructor)(void*), void** result)
{
    if (result) *result = nullptr;
    AllocationContext context = {size, 0, destructor, nullptr};
    int status = protect(L, newuserdatadtor, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_newbuffer(lua_State* L, size_t size, void** result)
{
    if (result) *result = nullptr;
    AllocationContext context = {size, 0, nullptr, nullptr};
    int status = protect(L, newbuffer, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_gettable(lua_State* L, int index, int* result) { IndexContext context = {index, 0}; int status = protect(L, gettable, &context, 1); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_getfield(lua_State* L, int index, const char* key, int* result) { StringContext context = {key, 0, index, 0, nullptr}; int status = protect(L, getfield, &context, 0); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_rawgetfield(lua_State* L, int index, const char* key, int* result) { StringContext context = {key, 0, index, 0, nullptr}; int status = protect(L, rawgetfield, &context, 0); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_rawget(lua_State* L, int index, int* result) { IndexContext context = {index, 0}; int status = protect(L, rawget, &context, 1); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_rawgeti(lua_State* L, int index, int item, int* result) { RawIndexContext context = {index, item, 0}; int status = protect(L, rawgeti, &context, 0); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_next(lua_State* L, int index, int* result) { IndexContext context = {index, 0}; int status = protect(L, next, &context, 1); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_createtable(lua_State* L, int arraySize, int recordSize) { TableContext context = {arraySize, recordSize}; return protect(L, createtable, &context, 0); }
int luau_ffi_protected_getmetatable(lua_State* L, int index, int* result) { IndexContext context = {index, 0}; int status = protect(L, getmetatable, &context, 0); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_getfenv(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, getfenv, &context, 0); }

int luau_ffi_protected_settable(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, settable, &context, 2); }
int luau_ffi_protected_setfield(lua_State* L, int index, const char* key) { StringContext context = {key, 0, index, 0, nullptr}; return protect(L, setfield, &context, 1); }
int luau_ffi_protected_rawsetfield(lua_State* L, int index, const char* key) { StringContext context = {key, 0, index, 0, nullptr}; return protect(L, rawsetfield, &context, 1); }
int luau_ffi_protected_rawset(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, rawset, &context, 2); }
int luau_ffi_protected_rawseti(lua_State* L, int index, int item) { RawIndexContext context = {index, item, 0}; return protect(L, rawseti, &context, 1); }
int luau_ffi_protected_setmetatable(lua_State* L, int index, int* result) { IndexContext context = {index, 0}; int status = protect(L, setmetatable, &context, 1); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_setfenv(lua_State* L, int index, int* result) { IndexContext context = {index, 0}; int status = protect(L, setfenv, &context, 1); if (status == 0 && result) *result = context.result; return status; }

int luau_ffi_protected_load(lua_State* L, const char* chunkName, const char* bytecode, size_t size, int environment, int* result)
{
    LoadContext context = {chunkName, bytecode, size, environment, 0};
    int status = protect(L, load, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_gc(lua_State* L, int operation, int data, int* result) { GcContext context = {operation, data, 0}; int status = protect(L, collect, &context, 0); if (status == 0 && result) *result = context.result; return status; }
int luau_ffi_protected_concat(lua_State* L, int count) { ConcatContext context = {count}; return protect(L, concat, &context, count); }
int luau_ffi_protected_clonefunction(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, clonefunction, &context, 0); }
int luau_ffi_protected_cleartable(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, cleartable, &context, 0); }
int luau_ffi_protected_clonetable(lua_State* L, int index) { IndexContext context = {index, 0}; return protect(L, clonetable, &context, 0); }

int luau_ffi_protected_ref(lua_State* L, int index, int* result)
{
    IndexContext context = {index, 0};
    int status = protect(L, makeref, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_tolstring(lua_State* L, int index, const char** result, size_t* length)
{
    StringContext context = {nullptr, 0, index, 0, nullptr};
    int status = protect(L, tolstring, &context, 0);
    if (status == 0)
    {
        if (result) *result = context.pointerResult;
        if (length) *length = context.length;
    }
    return status;
}

int luau_ffi_protected_luaL_tolstring(lua_State* L, int index, const char** result, size_t* length)
{
    StringContext context = {nullptr, 0, index, 0, nullptr};
    int status = protect(L, auxtolstring, &context, 0);
    if (status == 0)
    {
        if (result) *result = context.pointerResult;
        if (length) *length = context.length;
    }
    return status;
}

int luau_ffi_protected_openlibrary(lua_State* L, int library, int* result)
{
    LibraryContext context = {library, -1};
    int status = protect(L, openlibrary, &context, 0);
    if (status == 0 && result) *result = context.result;
    return status;
}

int luau_ffi_protected_openlibs(lua_State* L) { return protect(L, openlibs, nullptr, 0); }
int luau_ffi_protected_sandbox(lua_State* L) { return protect(L, sandbox, nullptr, 0); }
int luau_ffi_protected_sandboxthread(lua_State* L) { return protect(L, sandboxthread, nullptr, 0); }
}

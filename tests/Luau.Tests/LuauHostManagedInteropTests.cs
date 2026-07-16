
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

[CollectionDefinition(LuauHostNativeAbiCollection.Name, DisableParallelization = true)]
public sealed class LuauHostNativeAbiCollection
{
    public const string Name = "Luau host native ABI";
}

[Collection(LuauHostNativeAbiCollection.Name)]
public sealed unsafe class LuauHostManagedInteropTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int InterruptPoll(lua_State* state, int gc);

    static readonly InterruptPoll StopPoll = PollAndStop;
    static readonly InterruptPoll ContinuePoll = PollAndContinue;
    static readonly InterruptPoll NonYieldableStopPoll = PollAndStopWhenNonYieldable;
    static int pollCount;

    [Fact]
    public void HostMemoryByteCountsSaturateForManagedDiagnostics()
    {
        Assert.Equal(long.MaxValue, LuauVmContext.ToDiagnosticByteCount(ulong.MaxValue));
        Assert.Equal(123, LuauVmContext.ToDiagnosticByteCount(123));
    }

    [Fact]
    public void QuotaLimitedRootCreationFailureIsTypedAndDoesNotPoisonLaterCreation()
    {
        var exception = Assert.Throws<LuauMemoryLimitException>(() => LuauState.Create(
            new LuauStateOptions
            {
                MemoryLimitBytes = 1,
                BytecodePolicy = LuauBytecodePolicy.Reject,
            }));

        Assert.Equal(1, exception.LimitBytes);
        Assert.Equal(1, exception.Usage.LimitBytes);
        Assert.True(exception.AttemptedBytes > exception.LimitBytes);

        using var recovered = LuauState.Create();
        Assert.Equal(42, Assert.Single(recovered.DoString("return 42")).Read<int>());
    }

    [Fact]
    public void ProtectedNextPreservesNativeIterationStackSemantics()
    {
        var state = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)state);

        try
        {
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_createtable(state, 1, 0));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushinteger64(state, 1));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushinteger64(state, 42));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_rawset(state, -3));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushnil(state));

            var hasNext = -1;
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_next(state, -2, &hasNext));
            Assert.Equal(1, hasNext);
            Assert.Equal(3, lua_gettop(state));
            var isInteger = 0;
            Assert.Equal(1, lua_tointeger64(state, -2, &isInteger));
            Assert.NotEqual(0, isInteger);
            Assert.Equal(42, lua_tointeger64(state, -1, &isInteger));
            Assert.NotEqual(0, isInteger);

            lua_pop(state, 1);
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_next(state, -2, &hasNext));
            Assert.Equal(0, hasNext);
            Assert.Equal(1, lua_gettop(state));
        }
        finally
        {
            lua_close(state);
        }
    }

    [Fact]
    public void InterruptTrampolineYieldsAndFinalUninstallAllowsDifferentPoll()
    {
        var bytecode = LuauCompiler.Compile("while true do end"u8);
        var root = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)root);

        try
        {
            lua_State* thread = null;
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_newthread(root, &thread));
            Assert.NotEqual(IntPtr.Zero, (IntPtr)thread);
            LoadCompiled(thread, bytecode, "@luau-host-interrupt\0"u8);

            Volatile.Write(ref pollCount, 0);
            var stopPointer = Marshal.GetFunctionPointerForDelegate(StopPoll);
            var continuePointer = Marshal.GetFunctionPointerForDelegate(ContinuePoll);

            Assert.Equal(1, luau_ffi_protected_install_interrupt(root, stopPointer.ToPointer()));
            try
            {
                Assert.Equal((int)lua_Status.LUA_YIELD, lua_resume(thread, null, 0));
            }
            finally
            {
                luau_ffi_protected_uninstall_interrupt(root);
            }

            Assert.True(Volatile.Read(ref pollCount) > 0);
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_resetthread(thread));

            Assert.Equal(1, luau_ffi_protected_install_interrupt(root, continuePointer.ToPointer()));
            Assert.Equal(1, luau_ffi_protected_install_interrupt(root, continuePointer.ToPointer()));
            luau_ffi_protected_uninstall_interrupt(root);
            Assert.Equal(1, luau_ffi_protected_install_interrupt(root, stopPointer.ToPointer()));
            luau_ffi_protected_uninstall_interrupt(root);
        }
        finally
        {
            lua_close(root);
        }
    }

    [Fact]
    public void InterruptTrampolineHardUnwindsAfterNonYieldableManagedPollReturns()
    {
        var source = """
            local haystack = string.rep("x", 100)
            local pattern = string.rep("x?", 100) .. string.rep("x", 100)
            return string.find(haystack, pattern)
            """u8;
        var bytecode = LuauCompiler.Compile(source);
        var root = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)root);

        try
        {
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_openlibs(root));
            lua_settop(root, 0);
            lua_State* thread = null;
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_newthread(root, &thread));
            LoadCompiled(thread, bytecode, "@luau-host-nonyieldable\0"u8);

            Volatile.Write(ref pollCount, 0);
            var pollPointer = Marshal.GetFunctionPointerForDelegate(NonYieldableStopPoll);
            Assert.Equal(1, luau_ffi_protected_install_interrupt(root, pollPointer.ToPointer()));
            try
            {
                Assert.Equal((int)lua_Status.LUA_ERRMEM, lua_resume(thread, null, 0));
            }
            finally
            {
                luau_ffi_protected_uninstall_interrupt(root);
            }

            Assert.True(Volatile.Read(ref pollCount) > 0);
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_resetthread(thread));
        }
        finally
        {
            lua_close(root);
        }
    }

    [Fact]
    public void ProtectedAllocationReturnsMemoryStatusAndLeavesStateUsable()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 1_048_576,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
#pragma warning disable CS0618
        var pointer = state.AsPointer();
#pragma warning restore CS0618
        var originalTop = lua_gettop(pointer);
        state.Context.ArmQuotaFailureOnNextGrowth();
        void* buffer = (void*)1;

        var status = luau_ffi_protected_newbuffer(pointer, 2_097_152, &buffer);

        Assert.True(TryDecodeProtectedResult(status, out var hostStatus, out var hasErrorObject));
        Assert.Equal(LuauHostStatus.MemoryQuota, hostStatus);
        Assert.True(hasErrorObject);
        Assert.Equal(IntPtr.Zero, (IntPtr)buffer);
        Assert.Equal(LuauAllocatorFailure.QuotaExceeded, state.Context.AllocatorFailure);
        Assert.Equal(originalTop + 1, lua_gettop(pointer));
        Assert.Contains("memory", ReadTopString(pointer), StringComparison.OrdinalIgnoreCase);

        lua_settop(pointer, originalTop);
        state.Context.ResetAllocatorFailure();
        status = luau_ffi_protected_newbuffer(pointer, 16, &buffer);
        Assert.Equal((int)lua_Status.LUA_OK, status);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)buffer);
        Assert.Equal(originalTop + 1, lua_gettop(pointer));
    }

    [Fact]
    public void ProtectedLoadContainsQuotaFailureAndRestoresItsStackBoundary()
    {
        var source = Encoding.UTF8.GetBytes($"return [[{new string('x', 1_025)}]]");
        var bytecode = LuauCompiler.Compile(source);
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 1_048_576,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
#pragma warning disable CS0618
        var pointer = state.AsPointer();
#pragma warning restore CS0618
        var originalTop = lua_gettop(pointer);
        state.Context.ArmQuotaFailureOnNextGrowth();
        var loadResult = -1;

        fixed (byte* chunkName = "@luau-host-load-oom\0"u8)
        fixed (byte* bytecodePointer = bytecode)
        {
            var status = luau_ffi_protected_load(
                pointer,
                chunkName,
                bytecodePointer,
                (nuint)bytecode.Length,
                0,
                &loadResult);
            Assert.Equal((int)lua_Status.LUA_OK, status);
        }

        Assert.NotEqual((int)lua_Status.LUA_OK, loadResult);
        Assert.Equal(LuauAllocatorFailure.QuotaExceeded, state.Context.AllocatorFailure);
        Assert.Equal(originalTop + 1, lua_gettop(pointer));
        Assert.Contains("memory", ReadTopString(pointer), StringComparison.OrdinalIgnoreCase);

        lua_settop(pointer, originalTop);
        state.Context.ResetAllocatorFailure();
        void* smallBuffer = null;
        Assert.Equal(
            (int)lua_Status.LUA_OK,
            luau_ffi_protected_newbuffer(pointer, 32, &smallBuffer));
        Assert.NotEqual(IntPtr.Zero, (IntPtr)smallBuffer);
    }

    [Fact]
    public void StandaloneCompilerOwnedBuffersRemainValidAcrossRepeatedCompileAndLoad()
    {
        byte[] bytecode = [];
        for (var iteration = 0; iteration < 64; iteration++)
        {
            bytecode = LuauCompiler.Compile(Encoding.UTF8.GetBytes($"return {iteration}"));
            Assert.NotEmpty(bytecode);
        }

        using var state = LuauState.Create();
        var results = state.ExecuteTrustedBytecode(
            bytecode,
            "@luau-host/compiler-buffer.luau");

        Assert.Equal(63, Assert.Single(results).Read<int>());
    }

    [Fact]
    public void AdvancedCompileOptionsAreRejectedInsteadOfSilentlyDiscarded()
    {
        AssertUnsupported(new lua_CompileOptions { vectorLib = (byte*)1 });
        AssertUnsupported(new lua_CompileOptions { vectorCtor = (byte*)1 });
        AssertUnsupported(new lua_CompileOptions { vectorType = (byte*)1 });
        AssertUnsupported(new lua_CompileOptions { mutableGlobals = (byte**)1 });
        AssertUnsupported(new lua_CompileOptions { userdataTypes = (byte**)1 });
        AssertUnsupported(new lua_CompileOptions { librariesWithKnownMembers = (byte**)1 });
        AssertUnsupported(new lua_CompileOptions { libraryMemberTypeCb = (void*)1 });
        AssertUnsupported(new lua_CompileOptions { libraryMemberConstantCb = (void*)1 });
        AssertUnsupported(new lua_CompileOptions { disabledBuiltins = (byte**)1 });
    }

    [Fact]
    public void ArbitraryUserdataDestructorDelegateIsRootedUntilNativeDestruction()
    {
        var state = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)state);
        var counter = new DestructionCounter();
        WeakReference? probeReference = null;

        try
        {
            probeReference = CreateUserdataWithEphemeralDestructor(state, counter);
            ForceManagedCollection();
            Assert.True(probeReference.IsAlive);

            lua_settop(state, 0);
            for (var index = 0; index < 2; index++)
            {
                var result = 0;
                Assert.Equal(
                    (int)lua_Status.LUA_OK,
                    luau_ffi_protected_gc(state, operation: 2, data: 0, &result));
            }

            Assert.Equal(1, Volatile.Read(ref counter.Count));
        }
        finally
        {
            lua_close(state);
        }

        for (var index = 0; index < 5 && probeReference!.IsAlive; index++)
        {
            ForceManagedCollection();
            Thread.Sleep(10);
        }

        Assert.False(probeReference!.IsAlive);
        Assert.Equal(1, Volatile.Read(ref counter.Count));
    }

    static void AssertUnsupported(lua_CompileOptions nativeOptions)
    {
#pragma warning disable CS0618
        var options = new LuauCompileOptions(nativeOptions);
#pragma warning restore CS0618

        var exception = Assert.Throws<PlatformNotSupportedException>(
            () => LuauCompiler.Compile("return 42"u8, options));
        Assert.Contains("compile ABI does not support", exception.Message, StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateUserdataWithEphemeralDestructor(lua_State* state, DestructionCounter counter)
    {
        var probe = new DestructionProbe(counter);
        lua_UserdataDestructor destructor = probe.Destroy;
        void* userdata = null;

        Assert.Equal(
            (int)lua_Status.LUA_OK,
            luau_ffi_protected_newuserdatadtor(state, sizeof(int), destructor, &userdata));
        Assert.NotEqual(IntPtr.Zero, (IntPtr)userdata);
        return new WeakReference(probe);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ForceManagedCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    sealed class DestructionCounter
    {
        internal int Count;
    }

    sealed class DestructionProbe
    {
        readonly DestructionCounter counter;

        internal DestructionProbe(DestructionCounter counter)
        {
            this.counter = counter;
        }

        internal void Destroy(void* userdata)
        {
            Assert.NotEqual(IntPtr.Zero, (IntPtr)userdata);
            Interlocked.Increment(ref counter.Count);
        }
    }

    static void LoadCompiled(lua_State* state, byte[] bytecode, ReadOnlySpan<byte> chunkName)
    {
        var loadResult = -1;
        fixed (byte* bytecodePointer = bytecode)
        fixed (byte* chunkNamePointer = chunkName)
        {
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_load(
                    state,
                    chunkNamePointer,
                    bytecodePointer,
                    (nuint)bytecode.Length,
                    0,
                    &loadResult));
        }
        Assert.Equal((int)lua_Status.LUA_OK, loadResult);
    }

    static string ReadTopString(lua_State* state)
    {
        nuint length = 0;
        var pointer = lua_tolstring(state, -1, &length);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)pointer);
        Assert.True(length <= int.MaxValue);
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(pointer, (int)length));
    }

    static int PollAndStop(lua_State* state, int gc)
    {
        if (gc >= 0)
        {
            return 0;
        }

        Interlocked.Increment(ref pollCount);
        return 1;
    }

    static int PollAndContinue(lua_State* state, int gc)
    {
        return 0;
    }

    static int PollAndStopWhenNonYieldable(lua_State* state, int gc)
    {
        if (gc >= 0 || lua_isyieldable(state) != 0)
        {
            return 0;
        }

        Interlocked.Increment(ref pollCount);
        return 1;
    }
}

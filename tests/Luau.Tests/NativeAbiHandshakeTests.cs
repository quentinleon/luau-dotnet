using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

public sealed unsafe class NativeAbiHandshakeTests
{
    [Fact]
    public void CurrentPluginReportsMatchingProtectedAbiLayoutAndTypeTags()
    {
        var info = default(luau_ffi_abi_info_v2);

        var status = luau_ffi_protected_abi_info_v2(
            &info,
            checked((uint)sizeof(luau_ffi_abi_info_v2)));

        Assert.Equal((int)LUAU_ABI_INFO_OK, status);
        Assert.Equal(64, sizeof(luau_ffi_abi_info_v2));
        Assert.Equal((uint)sizeof(luau_ffi_abi_info_v2), info.struct_size);
        Assert.Equal(LuauNativeProtection.ExpectedAbiVersion, info.protected_abi_version);
        Assert.Equal((byte)sizeof(void*), info.pointer_size);
        Assert.Equal((byte)sizeof(nuint), info.size_t_size);
        Assert.Equal((byte)1, info.little_endian);
        Assert.Equal((uint)sizeof(lua_CompileOptions), info.compile_options_size);
        Assert.Equal((uint)sizeof(lua_Callbacks), info.callbacks_size);
        Assert.Equal((int)lua_Type.LUA_TNIL, info.type_nil);
        Assert.Equal((int)lua_Type.LUA_TBOOLEAN, info.type_boolean);
        Assert.Equal((int)lua_Type.LUA_TLIGHTUSERDATA, info.type_lightuserdata);
        Assert.Equal((int)lua_Type.LUA_TNUMBER, info.type_number);
        Assert.Equal((int)lua_Type.LUA_TVECTOR, info.type_vector);
        Assert.Equal((int)lua_Type.LUA_TSTRING, info.type_string);
        Assert.Equal((int)lua_Type.LUA_TTABLE, info.type_table);
        Assert.Equal((int)lua_Type.LUA_TFUNCTION, info.type_function);
        Assert.Equal((int)lua_Type.LUA_TUSERDATA, info.type_userdata);
        Assert.Equal((int)lua_Type.LUA_TTHREAD, info.type_thread);
        Assert.Equal((int)lua_Type.LUA_TBUFFER, info.type_buffer);

        LuauNativeAbiVerifier.Validate(info);
    }

    [Fact]
    public void NativeQueryPartiallyInitializesOnlyTheCallerBuffer()
    {
        const int callerSize = 8;
        var buffer = stackalloc byte[callerSize + 1];
        new Span<byte>(buffer, callerSize + 1).Fill(0xcc);

        var status = luau_ffi_protected_abi_info_v2(
            (luau_ffi_abi_info_v2*)buffer,
            callerSize);

        Assert.Equal((int)LUAU_ABI_INFO_BUFFER_TOO_SMALL, status);
        Assert.Equal((uint)sizeof(luau_ffi_abi_info_v2), *(uint*)buffer);
        Assert.Equal(LuauNativeProtection.ExpectedAbiVersion, *(uint*)(buffer + sizeof(uint)));
        Assert.Equal(0xcc, buffer[callerSize]);

        new Span<byte>(buffer, callerSize + 1).Fill(0xcc);
        status = luau_ffi_protected_abi_info_v2((luau_ffi_abi_info_v2*)buffer, 3);

        Assert.Equal((int)LUAU_ABI_INFO_BUFFER_TOO_SMALL, status);
        Assert.Equal(new byte[] { 0xcc, 0xcc, 0xcc }, new ReadOnlySpan<byte>(buffer, 3).ToArray());
        Assert.Equal(0xcc, buffer[3]);
    }

    [Fact]
    public void RootCreationRejectsWrongProtectedAbiVersionBeforeNativeStateCreation()
    {
        var info = CreateMatchingInfo();
        info.protected_abi_version++;
        var verifier = CreateVerifier(info);

        var exception = Assert.Throws<PlatformNotSupportedException>(
            () => LuauState.Create(LuauStateOptions.Default, verifier));

        Assert.Contains("protected-call ABI", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifierRejectsWrongPointerSize()
    {
        var info = CreateMatchingInfo();
        info.pointer_size = info.pointer_size == 8 ? (byte)4 : (byte)8;
        var verifier = CreateVerifier(info);

        var exception = Assert.Throws<PlatformNotSupportedException>(verifier.EnsureAvailable);

        Assert.Contains("pointer size", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneCompilerRejectsWrongTypeTagBeforeNativeCompilation()
    {
        var info = CreateMatchingInfo();
        info.type_function++;
        var verifier = CreateVerifier(info);

        var exception = Assert.Throws<PlatformNotSupportedException>(
            () => LuauCompiler.Compile("return 42"u8, options: null, verifier));

        Assert.Contains("function type tag", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifierRejectsUndersizedSelfDescriptionRecord()
    {
        var info = CreateMatchingInfo();
        info.struct_size--;
        var verifier = CreateVerifier(info);

        var exception = Assert.Throws<PlatformNotSupportedException>(verifier.EnsureAvailable);

        Assert.Contains("ABI information record", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingHandshakePrecedesStandaloneCompilationAndRootCreation()
    {
        LuauNativeProtection.EnsureAvailable();

        Assert.NotEmpty(LuauCompiler.Compile("return 42"u8));
        using var state = LuauState.Create();
        Assert.False(state.IsDisposed);
    }

    [Fact]
    public void ConcurrentIndependentRootCreationRunsInjectedHandshakeOnce()
    {
        var info = CreateMatchingInfo();
        var queryCount = 0;
        var verifier = CreateVerifier(info, () =>
        {
            Interlocked.Increment(ref queryCount);
            Thread.Sleep(25);
        });

        Parallel.For(0, 16, _ =>
        {
            using var state = LuauState.Create(LuauStateOptions.Default, verifier);
            Assert.True(state.IsMainThread);
        });

        Assert.Equal(1, queryCount);
    }

    static LuauNativeAbiVerifier CreateVerifier(
        luau_ffi_abi_info_v2 info,
        Action? beforeQuery = null)
    {
        return new LuauNativeAbiVerifier((output, outputSize) =>
        {
            beforeQuery?.Invoke();
            if (output == null)
            {
                return (int)LUAU_ABI_INFO_INVALID_ARGUMENT;
            }
            if (outputSize < sizeof(luau_ffi_abi_info_v2))
            {
                return (int)LUAU_ABI_INFO_BUFFER_TOO_SMALL;
            }

            *output = info;
            return (int)LUAU_ABI_INFO_OK;
        });
    }

    static luau_ffi_abi_info_v2 CreateMatchingInfo()
    {
        return new luau_ffi_abi_info_v2
        {
            struct_size = checked((uint)sizeof(luau_ffi_abi_info_v2)),
            protected_abi_version = LuauNativeProtection.ExpectedAbiVersion,
            pointer_size = checked((byte)sizeof(void*)),
            size_t_size = checked((byte)sizeof(nuint)),
            little_endian = BitConverter.IsLittleEndian ? (byte)1 : (byte)0,
            compile_options_size = checked((uint)sizeof(lua_CompileOptions)),
            callbacks_size = checked((uint)sizeof(lua_Callbacks)),
            type_nil = (int)lua_Type.LUA_TNIL,
            type_boolean = (int)lua_Type.LUA_TBOOLEAN,
            type_lightuserdata = (int)lua_Type.LUA_TLIGHTUSERDATA,
            type_number = (int)lua_Type.LUA_TNUMBER,
            type_vector = (int)lua_Type.LUA_TVECTOR,
            type_string = (int)lua_Type.LUA_TSTRING,
            type_table = (int)lua_Type.LUA_TTABLE,
            type_function = (int)lua_Type.LUA_TFUNCTION,
            type_userdata = (int)lua_Type.LUA_TUSERDATA,
            type_thread = (int)lua_Type.LUA_TTHREAD,
            type_buffer = (int)lua_Type.LUA_TBUFFER,
        };
    }
}

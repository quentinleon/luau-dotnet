using System.Buffers;
using System.Text;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    public LuauValue this[ReadOnlySpan<byte> key]
    {
        get
        {
            ThrowIfDisposed();
            if (key.IndexOf((byte)0) >= 0)
            {
                throw new ArgumentException("Global names cannot contain a NUL byte.", nameof(key));
            }

            using var access = EnterNativeAccess();
            using var hostOperation = new LuauDirectHostOperationScope(this);
            var buffer = ArrayPool<byte>.Shared.Rent(checked(key.Length + 1));
            try
            {
                key.CopyTo(buffer);
                buffer[key.Length] = 0;
                fixed (byte* s = buffer)
                {
                    var ignoredType = 0;
                    LuauNativeProtection.Prepare(context);
                    var status = luau_host_global_get(l, s, &ignoredType);
                    LuauNativeProtection.ThrowIfFailed(this, l, status, "read a Luau global");
                }

                var result = Pop();
                hostOperation.Complete(
                    "A direct host global read cannot yield or suspend the Luau thread.");
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        set
        {
            ThrowIfDisposed();
            if (key.IndexOf((byte)0) >= 0)
            {
                throw new ArgumentException("Global names cannot contain a NUL byte.", nameof(key));
            }

            if (IsMainThread && context.IsRootSandboxed)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "Root globals are read-only after SandboxRoot has been applied.");
            }

            var buffer = ArrayPool<byte>.Shared.Rent(checked(key.Length + 1));
            using var access = EnterNativeAccess();
            using var hostOperation = new LuauDirectHostOperationScope(this);
            try
            {
                key.CopyTo(buffer);
                buffer[key.Length] = 0;
                Push(value);
                fixed (byte* s = buffer)
                {
                    LuauNativeProtection.Prepare(context);
                    var status = luau_host_global_set(l, s);
                    LuauNativeProtection.ThrowIfFailed(this, l, status, "write a Luau global");
                }

                hostOperation.Complete(
                    "A direct host global write cannot yield or suspend the Luau thread.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public LuauValue this[string key]
    {
        get
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var buffer = ArrayPool<byte>.Shared.Rent(checked(Encoding.UTF8.GetMaxByteCount(key.Length) + 1));
            try
            {
                var count = Encoding.UTF8.GetBytes(key, buffer);
                buffer[count] = 0;
                return this[buffer.AsSpan(0, count)];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        set
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var buffer = ArrayPool<byte>.Shared.Rent(checked(Encoding.UTF8.GetMaxByteCount(key.Length) + 1));
            try
            {
                var count = Encoding.UTF8.GetBytes(key, buffer);
                buffer[count] = 0;
                this[buffer.AsSpan(0, count)] = value;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}

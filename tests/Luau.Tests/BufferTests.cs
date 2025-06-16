using System.Text;

namespace Luau.Tests;

public class BufferTests
{
    [Fact]
    public void CreateAndDispose()
    {
        using var state = LuauState.Create();
        var buffer = state.CreateBuffer(10);
    }

    [Fact]
    public void AsSpan()
    {
        using var state = LuauState.Create();
        state.OpenBufferLibrary();

        var buffer = state.CreateBuffer(10);

        var span = buffer.AsSpan();
        span[0] = (byte)'1';
        span[1] = (byte)'2';
        span[2] = (byte)'3';
        span[3] = (byte)'4';
        span[4] = (byte)'5';
        "hello"u8.CopyTo(span[5..]);

        Assert.Equal("12345hello", Encoding.UTF8.GetString(buffer.AsSpan()));

        state["b"] = buffer;
        var results = state.DoString("return buffer.tostring(b)");

        Assert.Equal("12345hello", results[0].Read<string>());
    }
}
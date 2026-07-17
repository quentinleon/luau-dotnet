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
    public void BoundedReadsAndWrites()
    {
        using var state = LuauState.Create();
        state.OpenBufferLibrary();

        var buffer = state.CreateBuffer(10);

        buffer.Write(0, "12345"u8);
        buffer.Write(5, "hello"u8);

        Span<byte> middle = stackalloc byte[5];
        buffer.Read(2, middle);
        Assert.Equal("345he", Encoding.UTF8.GetString(middle));
        Assert.Equal("12345hello", Encoding.UTF8.GetString(buffer.ToArray()));
        Assert.Throws<ArgumentException>(() => buffer.Write(6, "world"u8));
        Assert.Throws<ArgumentException>(() => buffer.Read(6, new byte[5]));

        state["b"] = buffer;
        var results = state.DoString("return buffer.tostring(b)");

        Assert.Equal("12345hello", results[0].Read<string>());
    }
}

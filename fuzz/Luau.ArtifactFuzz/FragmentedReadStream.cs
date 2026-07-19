namespace Luau.ArtifactFuzz;

sealed class FragmentedReadStream(byte[] input) : Stream
{
    int position;
    int readCount;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (position == input.Length)
        {
            return 0;
        }

        var fragment = 1 + ((readCount++ * 17 + input.Length) % 97);
        var actual = Math.Min(Math.Min(count, fragment), input.Length - position);
        input.AsSpan(position, actual).CopyTo(buffer.AsSpan(offset, actual));
        position += actual;
        return actual;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

namespace EngineNet.Core.DiscImages;

/// <summary>
/// Virtual, non-allocating read-only stream window over a contiguous byte range of a parent <see cref="System.IO.Stream"/>.
/// Disposing a <see cref="SubStream"/> does not close or dispose the underlying parent stream.
/// </summary>
public sealed class SubStream : System.IO.Stream {
    /* :: :: Fields :: START :: */

    private readonly System.IO.Stream _baseStream;
    private readonly long _baseOffset;
    private readonly long _length;
    private readonly object _syncLock;
    private long _position;

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Properties :: START :: */

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _length;

    /// <inheritdoc />
    public override long Position {
        get => _position;
        set {
            if (value < 0 || value > _length) {
                throw new System.ArgumentOutOfRangeException(paramName: nameof(value), message: "Position must be within the SubStream bounds.");
            }
            _position = value;
        }
    }

    /* :: :: Properties :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Creates a new read-only stream slice over <paramref name="baseStream"/>.
    /// </summary>
    /// <param name="baseStream">The parent stream to read from.</param>
    /// <param name="baseOffset">Absolute starting byte offset in <paramref name="baseStream"/>.</param>
    /// <param name="length">Length of the virtual slice in bytes.</param>
    /// <param name="syncLock">Optional synchronization object for thread-safe seeks/reads on the shared stream.</param>
    public SubStream(System.IO.Stream baseStream, long baseOffset, long length, object? syncLock = null) {
        if (baseStream is null) {
            throw new System.ArgumentNullException(paramName: nameof(baseStream));
        }
        if (baseOffset < 0) {
            throw new System.ArgumentOutOfRangeException(paramName: nameof(baseOffset));
        }
        if (length < 0) {
            throw new System.ArgumentOutOfRangeException(paramName: nameof(length));
        }

        _baseStream = baseStream;
        _baseOffset = baseOffset;
        _length = length;
        _syncLock = syncLock ?? baseStream;
        _position = 0;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) {
        if (buffer is null) {
            throw new System.ArgumentNullException(paramName: nameof(buffer));
        }
        if (offset < 0 || count < 0 || offset + count > buffer.Length) {
            throw new System.ArgumentOutOfRangeException(paramName: nameof(count));
        }

        return Read(buffer: System.MemoryExtensions.AsSpan(array: buffer, start: offset, length: count));
    }

    /// <inheritdoc />
    public override int Read(System.Span<byte> buffer) {
        long remaining = _length - _position;
        if (remaining <= 0 || buffer.IsEmpty) {
            return 0;
        }

        int toRead = (int)System.Math.Min(val1: buffer.Length, val2: remaining);
        System.Span<byte> slice = buffer.Slice(start: 0, length: toRead);

        lock (_syncLock) {
            _baseStream.Seek(offset: _baseOffset + _position, origin: System.IO.SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < toRead) {
                int read = _baseStream.Read(buffer: slice.Slice(start: totalRead));
                if (read <= 0) {
                    break;
                }
                totalRead += read;
            }
            _position += totalRead;
            return totalRead;
        }
    }

    /// <inheritdoc />
    public override long Seek(long offset, System.IO.SeekOrigin origin) {
        long target = origin switch {
            System.IO.SeekOrigin.Begin => offset,
            System.IO.SeekOrigin.Current => _position + offset,
            System.IO.SeekOrigin.End => _length + offset,
            _ => throw new System.ArgumentOutOfRangeException(paramName: nameof(origin))
        };

        if (target < 0 || target > _length) {
            throw new System.IO.IOException("Attempted to seek outside the bounds of the SubStream.");
        }

        _position = target;
        return _position;
    }

    /// <inheritdoc />
    public override void Flush() {
        // Read-only stream; no-op.
    }

    /// <inheritdoc />
    public override void SetLength(long value) {
        throw new System.NotSupportedException("SubStream is read-only.");
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) {
        throw new System.NotSupportedException("SubStream is read-only.");
    }

    /* :: :: Methods :: END :: */
}

namespace NovaVMS.Protocols.Common;

/// <summary>
/// Read-only stream fed by a producer (e.g. a native callback). Reads block until data arrives,
/// the producer completes, or no data has arrived for the idle timeout - then the stream ends,
/// which the player reports as an interrupted stream and the caller reconnects.
/// </summary>
internal class BlockingPipeStream : Stream
{
    private readonly object _syncRoot = new();
    private readonly Queue<byte[]> _chunks = new();
    private readonly long _maxBufferedBytes;
    private readonly bool _dropWhenFull;
    private readonly TimeSpan _idleTimeout;
    private byte[]? _current;
    private int _currentOffset;
    private long _bufferedBytes;
    private bool _isCompleted;

    /// <param name="maxBufferedBytes">Buffer limit.</param>
    /// <param name="dropWhenFull">
    /// True for live video: when the reader falls behind, buffered data is dropped to keep the delay low.
    /// False for recordings: the producer waits until the reader catches up.
    /// </param>
    /// <param name="idleTimeout">Time without data after which the stream ends.</param>
    public BlockingPipeStream(long maxBufferedBytes, bool dropWhenFull, TimeSpan idleTimeout)
    {
        _maxBufferedBytes = maxBufferedBytes;
        _dropWhenFull = dropWhenFull;
        _idleTimeout = idleTimeout;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => -1;

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Adds data; called by the producer, which must not modify the array afterwards.
    /// </summary>
    public void Append(byte[] chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_isCompleted)
            {
                return;
            }

            if (_bufferedBytes + chunk.Length > _maxBufferedBytes)
            {
                if (_dropWhenFull)
                {
                    _chunks.Clear();
                    _bufferedBytes = 0;
                }
                else
                {
                    while (!_isCompleted && _bufferedBytes + chunk.Length > _maxBufferedBytes && _chunks.Count > 0)
                    {
                        Monitor.Wait(_syncRoot, _idleTimeout);
                    }

                    if (_isCompleted)
                    {
                        return;
                    }
                }
            }

            _chunks.Enqueue(chunk);
            _bufferedBytes += chunk.Length;
            Monitor.PulseAll(_syncRoot);
        }
    }

    /// <summary>
    /// Ends the stream; pending reads return the remaining data and then 0.
    /// </summary>
    public void Complete()
    {
        lock (_syncRoot)
        {
            _isCompleted = true;
            Monitor.PulseAll(_syncRoot);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        lock (_syncRoot)
        {
            while (_current is null)
            {
                if (_chunks.Count > 0)
                {
                    _current = _chunks.Dequeue();
                    _currentOffset = 0;
                    _bufferedBytes -= _current.Length;
                    Monitor.PulseAll(_syncRoot);
                    break;
                }

                if (_isCompleted)
                {
                    return 0;
                }

                if (!Monitor.Wait(_syncRoot, _idleTimeout))
                {
                    _isCompleted = true;
                    return 0;
                }
            }

            var length = Math.Min(buffer.Length, _current.Length - _currentOffset);
            _current.AsSpan(_currentOffset, length).CopyTo(buffer);
            _currentOffset += length;
            if (_currentOffset >= _current.Length)
            {
                _current = null;
            }

            return length;
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Complete();
        }

        base.Dispose(disposing);
    }
}

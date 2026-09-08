using System;
using System.Collections.Concurrent;
using System.IO;

namespace TiaMcpServer
{
    /// <summary>
    /// Thread-safe blocking stream backed by a concurrent queue.
    /// One thread writes (HTTP side), another thread reads (MCP SDK side).
    /// Read blocks until data is available; Write never blocks.
    /// </summary>
    internal sealed class McpBlockingStream : Stream
    {
        private readonly BlockingCollection<byte[]> _chunks = new BlockingCollection<byte[]>();
        private byte[] _current = Array.Empty<byte>();
        private int _currentOffset = 0;
        private bool _completed = false;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return 0;

            // Return bytes from the current chunk if any remain
            if (_currentOffset < _current.Length)
            {
                int available = _current.Length - _currentOffset;
                int toRead = Math.Min(count, available);
                Array.Copy(_current, _currentOffset, buffer, offset, toRead);
                _currentOffset += toRead;
                return toRead;
            }

            // Wait for the next chunk; returns 0 on stream completion
            if (_completed) return 0;

            try
            {
                _current = _chunks.Take();
            }
            catch (InvalidOperationException)
            {
                // Collection was completed
                _completed = true;
                return 0;
            }

            _currentOffset = 0;
            int toRead2 = Math.Min(count, _current.Length);
            Array.Copy(_current, 0, buffer, offset, toRead2);
            _currentOffset = toRead2;
            return toRead2;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return;
            var chunk = new byte[count];
            Array.Copy(buffer, offset, chunk, 0, count);
            _chunks.Add(chunk);
        }

        /// <summary>Signals that no more data will be written.</summary>
        public void CompleteWriting() => _chunks.CompleteAdding();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

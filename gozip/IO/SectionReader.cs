using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Golang.IO
{
    internal class SectionReader : Stream
    {
        private readonly SemaphoreSlim readLock;
        private readonly Stream stream;
        private readonly long offset;
        private readonly long size;

        public SectionReader(SemaphoreSlim readLock, Stream stream, long offset, long size)
        {
            this.readLock = readLock;
            this.stream = stream;
            this.offset = offset;
            this.size = size;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => this.size;
        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.Position < 0)
            {
                throw new InvalidOperationException("Position is negative");
            }

            count = Math.Min(count, (int)(this.size - this.Position));
            if (count <= 0)
            {
                return 0;
            }

            var start = this.Position + this.offset;

            this.readLock.Wait();
            try
            {
                this.stream.Position = start;
                int read = this.stream.Read(buffer, offset, count);
                this.Position += read;
                return read;
            }
            finally
            {
                this.readLock.Release();
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (this.Position < 0)
            {
                throw new InvalidOperationException("Position is negative");
            }

            count = Math.Min(count, (int)(this.size - this.Position));
            if (count <= 0)
            {
                return 0;
            }

            var start = this.Position + this.offset;

            await this.readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this.stream.Position = start;
                int read = await this.stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                this.Position += read;
                return read;
            }
            finally
            {
                this.readLock.Release();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    break;
                case SeekOrigin.Current:
                    offset += this.Position;
                    break;
                case SeekOrigin.End:
                    offset += this.Length;
                    break;
            }
            return this.Position = offset;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
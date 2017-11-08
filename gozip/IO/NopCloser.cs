using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Golang.IO
{
    internal sealed class NopCloser : Stream
    {
        public Stream Inner { get; }

        public NopCloser(Stream inner)
        {
            this.Inner = inner;
        }

        public override bool CanRead => this.Inner.CanRead;
        public override bool CanWrite => this.Inner.CanWrite;
        public override bool CanSeek => this.Inner.CanSeek;
        public override long Position { get => this.Inner.Position; set => this.Inner.Position = value; }
        public override long Length => this.Inner.Length;
        public override bool CanTimeout => this.Inner.CanTimeout;
        public override int ReadTimeout { get => this.Inner.ReadTimeout; set => this.Inner.ReadTimeout = value; }
        public override int WriteTimeout { get => this.Inner.WriteTimeout; set => this.Inner.WriteTimeout = value; }

        public override void SetLength(long value)
        {
            this.Inner.SetLength(value);
        }

        public override void Flush()
        {
            this.Inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return this.Inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.Inner.Read(buffer, offset, count);
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return this.Inner.BeginRead(buffer, offset, count, callback, state);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return this.Inner.EndRead(asyncResult);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.Inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override int ReadByte()
        {
            return this.Inner.ReadByte();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.Inner.Write(buffer, offset, count);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return this.Inner.BeginWrite(buffer, offset, count, callback, state);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            this.Inner.EndWrite(asyncResult);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.Inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            this.Inner.WriteByte(value);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.Inner.Seek(offset, origin);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return this.Inner.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        public override bool Equals(object obj)
        {
            while (obj is NopCloser)
            {
                obj = ((NopCloser)obj).Inner;
            }
            return this.Inner.Equals(obj);
        }

        public override int GetHashCode()
        {
            return this.Inner.GetHashCode();
        }

        public override string ToString()
        {
            return this.Inner.ToString();
        }
    }
}

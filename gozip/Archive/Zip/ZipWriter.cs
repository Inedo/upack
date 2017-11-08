// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using Golang.Hash;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Golang.Archive.Zip
{
    internal sealed class Header
    {
        public ZipFileHeader fileHeader;
        public ulong offset;
    }

    // ZipWriter implements a zip file writer.
    public sealed class ZipWriter : IDisposable
    {
        private readonly Dictionary<ushort, Compressor> compressors = new Dictionary<ushort, Compressor>();

        private readonly CountWriter cw;
        private readonly List<Header> dir = new List<Header>();
        private FileWriter last;
        private bool closed;

        // Comment is the central directory comment and must be set before Close is called.
        public string Comment { get; set; }

        // NewWriter returns a new Writer writing a zip file to w.
        public ZipWriter(Stream w)
        {
            this.cw = new CountWriter(w);
        }

        // SetOffset sets the offset of the beginning of the zip data within the
        // underlying writer. It should be used when the zip data is appended to an
        // existing file, such as a binary executable.
        // It must be called before any data is written.
        public void SetOffset(long n)
        {
            if (this.cw.count != 0)
            {
                throw new InvalidOperationException("zip: SetOffset called after data was written");
            }
            this.cw.count = n;
        }

        // Flush flushes any buffered data to the underlying writer.
        // Calling Flush is not normally necessary; calling Close is sufficient.
        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.cw.w.FlushAsync(cancellationToken);
        }

        // Flush flushes any buffered data to the underlying writer.
        // Calling Flush is not normally necessary; calling Close is sufficient.
        public void Flush()
        {
            this.cw.w.Flush();
        }

        // Close finishes writing the zip file by writing the central directory.
        // It does not (and cannot) close the underlying writer.
        public void Close()
        {
            if (Encoding.UTF8.GetByteCount(this.Comment) > ushort.MaxValue)
            {
                throw new InvalidOperationException("zip: Writer.Comment too long");
            }

            if (this.last != null && !this.last.closed)
            {
                this.last.close();
                this.last = null;
            }
            if (this.closed) {
                throw new InvalidOperationException("zip: writer closed twice");
            }
            this.closed = true;

            // write central directory
            var start = (ulong)this.cw.count;
            foreach (var h in this.dir)
            {
                using (var b = new BinaryWriter(this.cw, Encoding.UTF8, true))
                {
                    b.Write((uint)Constants.directoryHeaderSignature);
                    b.Write((ushort)h.fileHeader.CreatorVersion);
                    b.Write((ushort)h.fileHeader.ReaderVersion);
                    b.Write((ushort)h.fileHeader.Flags);
                    b.Write((ushort)h.fileHeader.Method);
                    b.Write((ushort)h.fileHeader.ModifiedTime);
                    b.Write((ushort)h.fileHeader.ModifiedDate);
                    b.Write((uint)h.fileHeader.CRC32);
                    if (h.fileHeader.isZip64 || h.offset >= uint.MaxValue)
                    {
                        // the file needs a zip64 header. store maxint in both
                        // 32 bit size fields (and offset later) to signal that the
                        // zip64 extra header should be used.
                        b.Write(uint.MaxValue); // compressed size
                        b.Write(uint.MaxValue); // uncompressed size

                        // append a zip64 extra block to Extra
                        using (var buf = new MemoryStream((h.fileHeader.Extra?.Length ?? 0) + 28)) // 2x uint16 + 3x uint64
                        using (var eb = new BinaryWriter(buf, Encoding.UTF8, true))
                        {
                            eb.Write(h.fileHeader.Extra);
                            eb.Write((ushort)Constants.zip64ExtraId);
                            eb.Write((ushort)24); // size = 3x uint64
                            eb.Write((ulong)h.fileHeader.UncompressedSize64);
                            eb.Write((ulong)h.fileHeader.CompressedSize64);
                            eb.Write((ulong)h.offset);
                            eb.Flush();
                            h.fileHeader.Extra = buf.ToArray();
                        }
                    }
                    else
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        b.Write((uint)h.fileHeader.CompressedSize);
                        b.Write((uint)h.fileHeader.UncompressedSize);
#pragma warning restore CS0618 // Type or member is obsolete
                    }

                    b.Write((ushort)Encoding.UTF8.GetByteCount(h.fileHeader.RawName));
                    b.Write((ushort)h.fileHeader.Extra.Length);
                    b.Write((ushort)Encoding.UTF8.GetByteCount(h.fileHeader.Comment));
                    b.Write(new byte[4]); // skip disk number start and internal file attr (2x uint16)
                    b.Write((uint)h.fileHeader.ExternalAttrs);
                    if (h.offset > uint.MaxValue)
                    {
                        b.Write(uint.MaxValue);
                    }
                    else
                    {
                        b.Write((uint)h.offset);
                    }

                    b.Write(h.fileHeader.RawName.ToCharArray());
                    b.Write(h.fileHeader.Extra);
                    b.Write(h.fileHeader.Comment.ToCharArray());
                }
                var end = (ulong)this.cw.count;

                var records = (ulong)this.dir.Count;
                var size = end - start;
                var offset = start;

                if (records >= ushort.MaxValue || size >= uint.MaxValue || offset >= uint.MaxValue)
                {
                    using (var b = new BinaryWriter(this.cw, Encoding.UTF8, true))
                    {
                        // zip64 end of central directory record
                        b.Write((uint)Constants.directory64EndSignature);
                        b.Write((ulong)Constants.directory64EndLen - 12); // length minus signature (uint32) and length fields (uint64)
                        b.Write((ushort)Constants.zipVersion45); // version made by
                        b.Write((ushort)Constants.zipVersion45); // version needed to extract
                        b.Write((uint)0); // number of this disk
                        b.Write((uint)0); // number of the disk with the start of the central directory
                        b.Write((ulong)records); // total number of entries in the central directory on this disk
                        b.Write((ulong)records); // total number of entries in the central directory
                        b.Write((ulong)size); // size of the central directory
                        b.Write((ulong)offset); // offset of start of central directory with respect to the starting disk number

                        // zip64 end of central directory locator
                        b.Write((uint)Constants.directory64LocSignature);
                        b.Write((uint)0); // number of the disk with the start of the zip64 end of central directory
                        b.Write((ulong)end); // relative offset of the zip64 end of central directory record
                        b.Write((uint)1); // total number of disks
                    }

                    // store max values in the regular end record to signal that
                    // that the zip64 values should be used instead
                    records = ushort.MaxValue;
                    size = uint.MaxValue;
                    offset = uint.MaxValue;
                }

                // write end record
                using (var b = new BinaryWriter(this.cw, Encoding.UTF8, true))
                {
                    b.Write((uint)Constants.directoryEndSignature);
                    b.Write(new byte[4]); // skip over disk number and first disk number (2x uint16)
                    b.Write((ushort)records); // number of entries this disk
                    b.Write((ushort)records); // number of entries total
                    b.Write((uint)size); // size of directory
                    b.Write((uint)offset); // start of directory
                    b.Write((ushort)Encoding.UTF8.GetByteCount(this.Comment)); // byte size of EOCD comment

                    b.Write(this.Comment.ToCharArray());
                }

                this.cw.w.Flush();
            }
        }

        // Create adds a file to the zip file using the provided name.
        // It returns a Writer to which the file contents should be written.
        // The name must be a relative path: it must not start with a drive
        // letter (e.g. C:) or leading slash, and only forward slashes are
        // allowed.
        // The file's contents must be written to the io.Writer before the next
        // call to Create, CreateHeader, or Close.
        public Stream Create(string name)
        {
            var header = new ZipFileHeader
            {
                RawName = name,
                Method = Constants.Deflate
            };
            return this.CreateHeader(header);
        }

        // CreateHeader adds a file to the zip file using the provided FileHeader
        // for the file metadata.
        // It returns a Writer to which the file contents should be written.
        //
        // The file's contents must be written to the io.Writer before the next
        // call to Create, CreateHeader, or Close. The provided FileHeader fh
        // must not be modified after a call to CreateHeader.
        public Stream CreateHeader(ZipFileHeader fh)
        {
            if (this.last != null && !this.last.closed)
            {
                this.last.close();
            }
            if (this.dir.Count > 0 && this.dir[this.dir.Count - 1].fileHeader == fh)
            {
                // See https://golang.org/issue/11144 confusion.
                throw new InvalidOperationException("archive/zip: invalid duplicate FileHeader");
            }

            fh.Flags |= 0x8; // we will write a data descriptor

            // The ZIP format has a sad state of affairs regarding character encoding.
            // Officially, the name and comment fields are supposed to be encoded
            // in CP-437 (which is mostly compatible with ASCII), unless the UTF-8
            // flag bit is set. However, there are several problems:
            //
            // * Many ZIP readers still do not support UTF-8.
            // * If the UTF-8 flag is cleared, several readers simply interpret the
            //   name and comment fields as whatever the local system encoding is.
            //
            // In order to avoid breaking readers without UTF-8 support,
            // we avoid setting the UTF-8 flag if the strings are CP-437 compatible.
            // However, if the strings require multibyte UTF-8 encoding and is a
            // valid UTF-8 string, then we set the UTF-8 bit.
            //
            // For the case, where the user explicitly wants to specify the encoding
            // as UTF-8, they will need to set the flag bit themselves.
            // TODO: For the case, where the user explicitly wants to specify that the
            // encoding is *not* UTF-8, that is currently not possible.
            // See golang.org/issue/10741.
            if (fh.RawName.All(c => c <= 127) || fh.Comment.All(c => c <= 127))
            {
                fh.Flags |= 0x800;
            }

            fh.CreatorVersion = (ushort)((fh.CreatorVersion & 0xff00) | Constants.zipVersion20); // preserve compatibility byte
            fh.ReaderVersion = Constants.zipVersion20;

            var fw = new FileWriter
            {
                zipw = this.cw,
                compCount = new CountWriter(this.cw),
                crc32 = new CRC32()
            };
            var comp = this.compressor(fh.Method);
            if (comp == null)
            {
                throw new InvalidOperationException("zip: unsupported compression algorithm");
            }
            fw.comp = comp(fw.compCount);
            fw.rawCount = new CountWriter(fw.comp);

            var h = new Header
            {
                fileHeader = fh,
                offset = (ulong)this.cw.count
            };
            this.dir.Add(h);
            fw.header = h;

            writeHeader(this.cw, fh);

            this.last = fw;
            return fw;
        }

        private static void writeHeader(Stream w, ZipFileHeader h)
        {
            if (Encoding.UTF8.GetByteCount(h.RawName) > ushort.MaxValue)
            {
                throw new InvalidOperationException("zip: FileHeader.Name too long");
            }
            if (h.Extra.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException("zip: FileHeader.Extra too long");
            }

            using (var b = new BinaryWriter(w, Encoding.UTF8, true))
            {
                b.Write((uint)Constants.fileHeaderSignature);
                b.Write((ushort)h.ReaderVersion);
                b.Write((ushort)h.Flags);
                b.Write((ushort)h.Method);
                b.Write((ushort)h.ModifiedTime);
                b.Write((ushort)h.ModifiedDate);
                b.Write((uint)0); // since we are writing a data descriptor crc32,
                b.Write((uint)0); // compressed size,
                b.Write((uint)0); // and uncompressed size should be zero
                b.Write((ushort)Encoding.UTF8.GetByteCount(h.RawName));
                b.Write((ushort)h.Extra.Length);
                b.Write(h.RawName.ToCharArray());
                b.Write(h.Extra);
            }
        }

        // RegisterCompressor registers or overrides a custom compressor for a specific
        // method ID. If a compressor for a given method is not found, Writer will
        // default to looking up the compressor at the package level.
        public void RegisterCompressor(ushort method, Compressor comp)
        {
            this.compressors[method] = comp;
        }

        private Compressor compressor(ushort method)
        {
            if (this.compressors.TryGetValue(method, out var comp))
            {
                return comp;
            }
            return Compressors.compressor(method);
        }

        public void Dispose()
        {
            if (!this.closed)
            {
                this.Close();
            }
        }
    }

    internal sealed class FileWriter : Stream
    {
        internal Header header;
        internal Stream zipw;
        internal CountWriter rawCount;
        internal Stream comp;
        internal CountWriter compCount;
        internal CRC32 crc32;
        internal bool closed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => this.rawCount.count;
        public override long Position { get => this.rawCount.count; set => throw new NotSupportedException(); }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (this.closed)
            {
                throw new InvalidOperationException("zip: write to closed file");
            }
            return Task.WhenAll(
                Task.Run(() => this.crc32.Write(buffer, offset, count), cancellationToken),
                this.rawCount.WriteAsync(buffer, offset, count, cancellationToken)
            );
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (this.closed)
            {
                throw new InvalidOperationException("zip: write to closed file");
            }
            this.crc32.Write(buffer, offset, count);
            this.rawCount.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!this.closed)
            {
                this.close();
            }
        }

        internal void close()
        {
            if (this.closed)
            {
                throw new ObjectDisposedException(nameof(FileWriter));
            }
            this.closed = true;
            this.comp.Dispose();

            // update FileHeader
            var fh = this.header.fileHeader;
            fh.CRC32 = this.crc32.CRC;
            fh.CompressedSize64 = (ulong)this.compCount.count;
            fh.UncompressedSize64 = (ulong)this.rawCount.count;

#pragma warning disable CS0618 // Type or member is obsolete
            if (fh.isZip64)
            {
                fh.CompressedSize = uint.MaxValue;
                fh.UncompressedSize = uint.MaxValue;
                fh.ReaderVersion = Constants.zipVersion45; // requires 4.5 - File uses ZIP64 format extensions
            }
            else
            {
                fh.CompressedSize = (uint)fh.CompressedSize64;
                fh.UncompressedSize = (uint)fh.UncompressedSize64;
            }
#pragma warning restore CS0618 // Type or member is obsolete

            // Write data descriptor. This is more complicated than one would
            // think, see e.g. comments in zipfile.c:putextended() and
            // http://bugs.sun.com/bugdatabase/view_bug.do?bug_id=7073588.
            // The approach here is to write 8 byte sizes if needed without
            // adding a zip64 extra in the local header (too late anyway).
            using (var b = new BinaryWriter(this.zipw, Encoding.UTF8, true))
            {
                b.Write((uint)Constants.dataDescriptorSignature); // de-facto standard, required by OS X
                b.Write((uint)fh.CRC32);
                if (fh.isZip64)
                {
                    b.Write((ulong)fh.CompressedSize64);
                    b.Write((ulong)fh.UncompressedSize64);
                }
            }
        }

        public override void Flush()
        {
            this.rawCount.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return this.rawCount.FlushAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class CountWriter : Stream
    {
        internal readonly Stream w;
        internal long count;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => this.count;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        internal CountWriter(Stream w)
        {
            this.w = w;
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            this.count += count;
            return this.w.BeginWrite(buffer, offset, count, callback, state);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            this.w.EndWrite(asyncResult);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.count += count;
            return this.w.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.count += count;
            this.w.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            this.count++;
            this.w.WriteByte(value);
        }

        public override void Flush()
        {
            this.w.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return this.w.FlushAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}

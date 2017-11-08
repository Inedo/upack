// Copyright 2010 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using Golang.Hash;
using Golang.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Golang.Archive.Zip
{
    public sealed class ZipReader : IDisposable
    {
        internal const string FormatErrorMessage = "zip: not a valid zip file";

        private readonly Dictionary<ushort, Decompressor> decompressors = new Dictionary<ushort, Decompressor>();
        internal readonly SemaphoreSlim readLock = new SemaphoreSlim(1);
        internal readonly Stream r;
        public IReadOnlyList<ZipFile> File { get; }
        public string Comment { get; }

        // NewReader returns a new Reader reading from r, which is assumed to
        // have the given size in bytes.
        public ZipReader(Stream r, bool keepOpen = false)
        {
            if (keepOpen)
            {
                r = new NopCloser(r);
            }
            var end = readDirectoryEnd(r);
            if (end.directoryRecords > (ulong)r.Length / Constants.fileHeaderLen)
            {
                throw new InvalidDataException($"archive/zip: TOC declares impossible {end.directoryRecords} files in {r.Length} byte zip");
            }
            this.r = r;
            var files = new List<ZipFile>((int)end.directoryRecords);
            this.Comment = end.comment;
            r.Position = (long)end.directoryOffset;

            // The count of files inside a zip is truncated to fit in a uint16.
            // Gloss over this by reading headers until we encounter
            // a bad one, and then only report an ErrFormat or UnexpectedEOF if
            // the file count modulo 65536 is incorrect.
            ExceptionDispatchInfo exceptionInfo;
            while (true)
            {
                var f = new ZipFile(this);
                try
                {
                    readDirectoryHeader(f, r);
                }
                catch (EndOfStreamException ex)
                {
                    exceptionInfo = ExceptionDispatchInfo.Capture(ex);
                    break;
                }
                catch (InvalidDataException ex) when (ex.Message == FormatErrorMessage)
                {
                    exceptionInfo = ExceptionDispatchInfo.Capture(ex);
                    break;
                }
                files.Add(f);
            }
            this.File = files;
            if (unchecked((ushort)files.Count) != (ushort)end.directoryRecords) // only compare 16 bits here
            {
                // Return the readDirectoryHeader error if we read
                // the wrong number of directory entries.
                exceptionInfo.Throw();
            }
        }

        // readDirectoryHeader attempts to read a directory header from r.
        // It returns io.ErrUnexpectedEOF if it cannot read a complete header,
        // and ErrFormat if it doesn't find a valid header signature.
        private static void readDirectoryHeader(ZipFile f, Stream r)
        {
            var buf = r.ReadFull(Constants.directoryHeaderLen);
            int filenameLen, extraLen, commentLen;
            using (var b = new BinaryReader(new MemoryStream(buf, false)))
            {
                var sig = b.ReadUInt32();
                if (sig != Constants.directoryHeaderSignature)
                {
                    throw new InvalidDataException(FormatErrorMessage);
                }
                f.Header.CreatorVersion = b.ReadUInt16();
                f.Header.ReaderVersion = b.ReadUInt16();
                f.Header.Flags = b.ReadUInt16();
                f.Header.Method = b.ReadUInt16();
                f.Header.ModifiedTime = b.ReadUInt16();
                f.Header.ModifiedDate = b.ReadUInt16();
                f.Header.CRC32 = b.ReadUInt32();
#pragma warning disable CS0618 // Type or member is obsolete
                f.Header.CompressedSize = b.ReadUInt32();
                f.Header.UncompressedSize = b.ReadUInt32();
                f.Header.CompressedSize64 = (ulong)f.Header.CompressedSize;
                f.Header.UncompressedSize64 = (ulong)f.Header.UncompressedSize;
#pragma warning restore CS0618 // Type or member is obsolete
                filenameLen = (int)b.ReadUInt16();
                extraLen = (int)b.ReadUInt16();
                commentLen = (int)b.ReadUInt16();
                b.ReadBytes(4); // skipped start disk number and internal attributes (2x uint16)
                f.Header.ExternalAttrs = b.ReadUInt32();
                f.headerOffset = (long)b.ReadUInt32();
            }

            var d = r.ReadFull(filenameLen + extraLen + commentLen);
            f.Header.RawName = Encoding.UTF8.GetString(d, 0, filenameLen);
            f.Header.Extra = new ArraySegment<byte>(d, filenameLen, extraLen).ToArray();
            f.Header.Comment = Encoding.UTF8.GetString(d, filenameLen + extraLen, commentLen);

#pragma warning disable CS0618 // Type or member is obsolete
            bool needUSize = f.Header.UncompressedSize == uint.MaxValue;
            bool needCSize = f.Header.CompressedSize == uint.MaxValue;
#pragma warning restore CS0618 // Type or member is obsolete
            bool needHeaderOffset = f.headerOffset == uint.MaxValue;

            if ((f.Header.Extra?.Length ?? 0) > 0)
            {
                // Best effort to find what we need.
                // Other zip authors might not even follow the basic format,
                // and we'll just ignore the Extra content in that case.
                using (var ebuf = new MemoryStream(f.Header.Extra, false))
                using (var b = new BinaryReader(ebuf, Encoding.UTF8, true))
                {
                    while (ebuf.Length - ebuf.Position >= 4) // need at least tag and size
                    {
                        var tag = b.ReadUInt16();
                        var size = b.ReadUInt16();
                        if (size > ebuf.Length - ebuf.Position)
                        {
                            break;
                        }
                        if (tag == Constants.zip64ExtraId)
                        {
                            // update directory values from the zip64 extra block.
                            // They should only be consulted if the sizes read earlier
                            // are maxed out.
                            // See golang.org/issue/13367.
                            using (var eb = new BinaryReader(new MemoryStream(f.Header.Extra, (int)ebuf.Position, size, false)))
                            {
                                if (needUSize)
                                {
                                    needUSize = false;
                                    try
                                    {
                                        f.Header.UncompressedSize64 = eb.ReadUInt64();
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new InvalidDataException(FormatErrorMessage, ex);
                                    }
                                }
                                if (needCSize)
                                {
                                    needCSize = false;
                                    try
                                    {
                                        f.Header.CompressedSize64 = eb.ReadUInt64();
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new InvalidDataException(FormatErrorMessage, ex);
                                    }
                                }
                                if (needHeaderOffset)
                                {
                                    needHeaderOffset = false;
                                    try
                                    {
                                        f.headerOffset = (long)eb.ReadUInt64();
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new InvalidDataException(FormatErrorMessage, ex);
                                    }
                                }
                                break;
                            }
                        }
                        b.ReadBytes(size);
                    }
                }
            }

            // Assume that uncompressed size 2³²-1 could plausibly happen in
            // an old zip32 file that was sharding inputs into the largest chunks
            // possible (or is just malicious; search the web for 42.zip).
            // If needUSize is true still, it means we didn't see a zip64 extension.
            // As long as the compressed size is not also 2³²-1 (implausible)
            // and the header is not also 2³²-1 (equally implausible),
            // accept the uncompressed size 2³²-1 as valid.
            // If nothing else, this keeps archive/zip working with 42.zip.
            _ = needUSize;

            if (needCSize || needHeaderOffset)
            {
                throw new InvalidDataException(FormatErrorMessage);
            }
        }

        public void Dispose()
        {
            readLock.Dispose();
            r.Dispose();
        }

        // RegisterDecompressor registers or overrides a custom decompressor for a
        // specific method ID. If a decompressor for a given method is not found,
        // Reader will default to looking up the decompressor at the package level.
        public void RegisterDecompressor(ushort method, Decompressor dcomp)
        {
            this.decompressors[method] = dcomp;
        }

        internal Decompressor decompressor(ushort method)
        {
            if (this.decompressors.TryGetValue(method, out var dcomp))
            {
                return dcomp;
            }
            return Compressors.decompressor(method);
        }

        private static DirectoryEnd readDirectoryEnd(Stream r)
        {
            // look for directoryEndSignature in the last 1k, then in the last 65k
            byte[] buf = null;
            long directoryEndOffset = 0;
            for (int i = 0, bLen = 1024; i < 2; i++, bLen += 64 * 1024)
            {
                if (bLen > r.Length)
                {
                    bLen = (int)r.Length;
                }
                r.Position = r.Length - bLen;
                buf = r.ReadFull(bLen);
                var p = findSignatureInBlock(buf);
                if (p >= 0)
                {
                    buf = buf.Skip(p).ToArray();
                    directoryEndOffset = r.Length - bLen + (long)p;
                    break;
                }
                if (i == 1 || bLen == r.Length) {
                    throw new InvalidDataException(FormatErrorMessage);
                }
            }

            // read header into struct
            var d = new DirectoryEnd
            {
                diskNbr = (uint)BitConverter.ToUInt16(buf, 4),
                dirDiskNbr = (uint)BitConverter.ToUInt16(buf, 6),
                dirRecordsThisDisk = (ulong)BitConverter.ToUInt16(buf, 8),
                directoryRecords = (ulong)BitConverter.ToUInt16(buf, 10),
                directorySize = (ulong)BitConverter.ToUInt32(buf, 12),
                directoryOffset = (ulong)BitConverter.ToUInt32(buf, 16),
                commentLen = BitConverter.ToUInt16(buf, 20)
            };
            var l = (int)d.commentLen;
            if (l + 22 > buf.Length)
            {
                throw new InvalidDataException("zip: invalid comment length");
            }
            d.comment = Encoding.UTF8.GetString(buf, 22, l);

            // These values mean that the file can be a zip64 file
            if (d.directoryRecords == 0xffff || d.directorySize == 0xffff || d.directoryOffset == 0xffffffff)
            {
                var p = findDirectory64End(r, directoryEndOffset);
                if (p >= 0)
                {
                    readDirectory64End(r, p, d);
                }
            }
            // Make sure directoryOffset points to somewhere in our file.
            if (d.directoryOffset > long.MaxValue || (long)d.directoryOffset >= r.Length)
            {
                throw new InvalidDataException(FormatErrorMessage);
            }
            return d;
        }

        // findDirectory64End tries to read the zip64 locator just before the
        // directory end and returns the offset of the zip64 directory end if
        // found.
        private static long findDirectory64End(Stream r, long directoryEndOffset)
        {
            var locOffset = directoryEndOffset - Constants.directory64LocLen;
            if (locOffset < 0)
            {
                return -1; // no need to look for a header outside the file
            }
            byte[] buf;
            try
            {
                r.Position = locOffset;
                buf = r.ReadFull(Constants.directory64LocLen);
            }
            catch (EndOfStreamException)
            {
                return -1;
            }
            uint sig = BitConverter.ToUInt32(buf, 0);
            if (sig != Constants.directory64LocSignature)
            {
                return -1;
            }
            if (BitConverter.ToUInt32(buf, 4) != 0) // number of the disk with the start of the zip64 end of central directory
            {
                return -1; // the file is not a valid zip64-file
            }
            var p = BitConverter.ToUInt64(buf, 8); // relative offset of the zip64 end of central directory record
            if (BitConverter.ToUInt32(buf, 16) != 1) // total number of disks
            {
                return -1; // the file is not a valid zip64-file
            }
            return (long)p;
        }

        // readDirectory64End reads the zip64 directory end and updates the
        // directory end with the zip64 directory end values.
        private static void readDirectory64End(Stream r, long offset, DirectoryEnd d)
        {
            r.Position = offset;
            var buf = r.ReadFull(Constants.directory64EndLen);

            using (var b = new BinaryReader(new MemoryStream(buf, false)))
            {
                var sig = b.ReadUInt32();
                if (sig != Constants.directory64EndSignature)
                {
                    throw new InvalidDataException(FormatErrorMessage);
                }

                b.ReadBytes(12); // skip dir size, version and version needed (uint64 + 2x uint16)
                d.diskNbr = b.ReadUInt32(); // number of this disk
                d.dirDiskNbr = b.ReadUInt32(); // number of the disk with the start of the central directory
                d.dirRecordsThisDisk = b.ReadUInt64(); // total number of entries in the central directory on this disk
                d.directoryRecords = b.ReadUInt64(); // total number of entries in the central directory
                d.directorySize = b.ReadUInt64(); // size of the central directory
                d.directoryOffset = b.ReadUInt64(); // offset of start of central directory with respect to the starting disk number
            }
        }

        private static int findSignatureInBlock(byte[] b)
        {
            for (int i = b.Length - Constants.directoryEndLen; i >= 0; i--)
            {
                // defined from directoryEndSignature in Struct.cs
                if (b[i] == 'P' && b[i+1] == 'K' && b[i+2] == 0x05 && b[i+3] == 0x06)
                {
                    // n is length of comment
                    int n = b[i + Constants.directoryEndLen - 2] | (b[i + Constants.directoryEndLen - 1] << 8);
                    if (n + Constants.directoryEndLen + i <= b.Length)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }
    }

    public sealed class ZipFile
    {
        public ZipFileHeader Header { get; } = new ZipFileHeader();
        internal readonly ZipReader zip;
        internal long headerOffset;

        internal ZipFile(ZipReader zip)
        {
            this.zip = zip;
        }

        // DataOffset returns the offset of the file's possibly-compressed
        // data, relative to the beginning of the zip file.
        //
        // Most callers should instead use Open, which transparently
        // decompresses data and verifies checksums.
        public long DataOffset
        {
            get
            {
                var bodyOffset = this.findBodyOffset();
                return this.headerOffset + bodyOffset;
            }
        }

        private bool hasDataDescriptor => (this.Header.Flags & 0x8) != 0;

        // Open returns a ReadCloser that provides access to the File's contents.
        // Multiple files may be read concurrently.
        public Stream Open()
        {
            var bodyOffset = this.findBodyOffset();
            var size = (long)this.Header.CompressedSize64;
            var r = new SectionReader(this.zip.readLock, this.zip.r, this.headerOffset + bodyOffset, size);
            var dcomp = this.zip.decompressor(this.Header.Method);
            if (dcomp == null)
            {
                throw new InvalidDataException("zip: unsupported compression algorithm");
            }
            var rc = dcomp(r);
            Stream desr = null;
            if (this.hasDataDescriptor)
            {
                desr = new SectionReader(this.zip.readLock, this.zip.r, this.headerOffset + bodyOffset + size, Constants.dataDescriptorLen);
            }
            return new ChecksumReader
            {
                rc = rc,
                hash = new CRC32(),
                f = this,
                desr = desr
            };
        }

        // findBodyOffset does the minimum work to verify the file has a header
        // and returns the file body offset.
        private long findBodyOffset()
        {
            byte[] buf;
            this.zip.readLock.Wait();
            try
            {
                this.zip.r.Position = this.headerOffset;
                buf = this.zip.r.ReadFull(Constants.fileHeaderLen);
            }
            finally
            {
                this.zip.readLock.Release();
            }
            if (BitConverter.ToUInt32(buf, 0) != Constants.fileHeaderSignature)
            {
                throw new InvalidDataException(ZipReader.FormatErrorMessage);
            }
            var filenameLen = (int)BitConverter.ToUInt16(buf, 26);
            var extraLen = (int)BitConverter.ToUInt16(buf, 28);
            return Constants.fileHeaderLen + filenameLen + extraLen;
        }
    }

    internal sealed class ChecksumReader : Stream
    {
        internal Stream rc;
        internal CRC32 hash;
        internal ulong nread; // number of bytes read so far
        internal ZipFile f;
        internal Stream desr; // if non-null, where to read the data descriptor
        internal ExceptionDispatchInfo err; // sticky error

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => (long)this.f.Header.UncompressedSize64;

        public override long Position
        {
            get => (long)this.nread;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.err != null)
            {
                this.err.Throw();
            }
            int n = 0;
            try
            {
                n = this.rc.Read(buffer, offset, count);
                this.hash.Write(buffer, offset, n);
                this.nread += (ulong)n;
            }
            catch (Exception ex)
            {
                this.err = ExceptionDispatchInfo.Capture(ex);
                throw;
            }
            if (n == 0)
            {
                this.checkEOF();
            }
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (this.err != null)
            {
                this.err.Throw();
            }
            int n = 0;
            try
            {
                n = await this.rc.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                this.hash.Write(buffer, offset, n);
                this.nread += (ulong)n;
            }
            catch (Exception ex)
            {
                this.err = ExceptionDispatchInfo.Capture(ex);
                throw;
            }
            if (n == 0)
            {
                this.checkEOF();
            }
            return n;
        }

        private void checkEOF()
        {
            if (this.nread != this.f.Header.UncompressedSize64)
            {
                throw new EndOfStreamException($"unexpected end of file at {this.nread}/{this.f.Header.UncompressedSize64} bytes");
            }
            if (this.desr != null)
            {
                readDataDescriptor(this.desr, this.f);
                if (this.hash.CRC != this.f.Header.CRC32)
                {
                    throw new InvalidDataException("zip: checksum error");
                }
            }
            else
            {
                // If there's not a data descriptor, we still compare
                // the CRC32 of what we've read against the file header
                // or TOC's CRC32, if it seems like it was set.
                if (this.f.Header.CRC32 != 0 && this.hash.CRC != this.f.Header.CRC32)
                {
                    throw new InvalidDataException("zip: checksum error");
                }
            }
        }

        private static void readDataDescriptor(Stream r, ZipFile f)
        {
            // The spec says: "Although not originally assigned a
            // signature, the value 0x08074b50 has commonly been adopted
            // as a signature value for the data descriptor record.
            // Implementers should be aware that ZIP files may be
            // encountered with or without this signature marking data
            // descriptors and should account for either case when reading
            // ZIP files to ensure compatibility."
            //
            // dataDescriptorLen includes the size of the signature but
            // first read just those 4 bytes to see if it exists.
            var buf = r.ReadFull(4);
            if (BitConverter.ToUInt32(buf, 0) != Constants.dataDescriptorSignature)
            {
                // No data descriptor signature. Keep these four bytes.
                buf = buf.Concat(r.ReadFull(8)).ToArray();
            }
            else
            {
                buf = r.ReadFull(12);
            }
            if (BitConverter.ToUInt32(buf, 0) != f.Header.CRC32)
            {
                throw new InvalidDataException("zip: checksum error");
            }

            // The two sizes that follow here can be either 32 bits or 64 bits
            // but the spec is not very clear on this and different
            // interpretations has been made causing incompatibilities. We
            // already have the sizes from the central directory so we can
            // just ignore these.
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
            this.rc.Dispose();

            base.Dispose(disposing);
        }
    }

    internal static class StreamExtensions
    {
        public static byte[] ReadFull(this Stream stream, int length)
        {
            var buf = new byte[length];
            int offset = 0;
            while (offset != buf.Length)
            {
                int count = stream.Read(buf, offset, length - offset);
                if (count == 0)
                {
                    throw new EndOfStreamException($"unexpected end of stream at {offset}/{length} bytes");
                }
                offset += count;
            }
            return buf;
        }
    }
}

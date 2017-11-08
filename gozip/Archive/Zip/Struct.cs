// Copyright 2010 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.IO;

/*
Package zip provides support for reading and writing ZIP archives.

See: https://www.pkware.com/appnote

This package does not support disk spanning.

A note about ZIP64:

To be backwards compatible the FileHeader has both 32 and 64 bit Size
fields. The 64 bit fields will always contain the correct value and
for normal archives both fields will be the same. For files requiring
the ZIP64 format the 32 bit fields will be 0xffffffff and the 64 bit
fields must be used instead.
*/
namespace Golang.Archive.Zip
{
    public static class Constants
    {
        // Compression methods.
        public const ushort Store = 0;
        public const ushort Deflate = 8;

        internal const uint fileHeaderSignature = 0x04034b50;
        internal const uint directoryHeaderSignature = 0x02014b50;
        internal const uint directoryEndSignature = 0x06054b50;
        internal const uint directory64LocSignature = 0x07064b50;
        internal const uint directory64EndSignature = 0x06064b50;
        internal const uint dataDescriptorSignature = 0x08074b50; // de-facto standard; required by OS X Finder
        internal const int fileHeaderLen = 30; // + filename + extra
        internal const int directoryHeaderLen = 46; // + filename + extra + comment
        internal const int directoryEndLen = 22; // + comment
        internal const int dataDescriptorLen = 16; // four uint32: descriptor signature, crc32, compressed size, size
        internal const int dataDescriptor64Len = 24; // descriptor with 8 byte sizes
        internal const int directory64LocLen = 20; //
        internal const int directory64EndLen = 56; // + extra

        // Constants for the first byte in CreatorVersion
        internal const byte creatorFAT = 0;
        internal const byte creatorUnix = 3;
        internal const byte creatorNTFS = 11;
        internal const byte creatorVFAT = 14;
        internal const byte creatorMacOSX = 19;

        // version numbers
        internal const byte zipVersion20 = 20; // 2.0
        internal const byte zipVersion45 = 45; // 4.5 (reads and writes zip64 archives)

        // extra header id's
        internal const ushort zip64ExtraId = 0x0001; // zip64 Extended Information Extra Field
    }

    // FileHeader describes a file within a zip file.
    // See the zip spec for details.
    public sealed class ZipFileHeader
    {
        public ZipFileHeader()
        {
        }

        public ZipFileHeader(ZipFileHeader copy)
        {
            this.RawName = copy.RawName;
            this.CreatorVersion = copy.CreatorVersion;
            this.ReaderVersion = copy.ReaderVersion;
            this.Flags = copy.Flags;
            this.Method = copy.Method;
            this.ModifiedDate = copy.ModifiedDate;
            this.ModifiedTime = copy.ModifiedTime;
            this.CRC32 = copy.CRC32;
#pragma warning disable CS0618 // Type or member is obsolete
            this.CompressedSize = copy.CompressedSize;
            this.UncompressedSize = copy.UncompressedSize;
#pragma warning restore CS0618 // Type or member is obsolete
            this.CompressedSize64 = copy.CompressedSize64;
            this.UncompressedSize64 = copy.UncompressedSize64;
            this.Extra = (byte[])copy.Extra.Clone();
            this.ExternalAttrs = copy.ExternalAttrs;
            this.Comment = copy.Comment;
        }

        // Name is the name of the file.
        // It must be a relative path: it must not start with a drive
        // letter (e.g. C:) or leading slash, and only forward slashes
        // are allowed.
        public string Name
        {
            get => this.RawName.Replace('\\', '/').Trim('/') + (this.Mode.HasFlag(FileAttributes.Directory) ? "/" : "");
            set => this.RawName = value;
        }
        public string RawName { get; set; }

        public ushort CreatorVersion { get; set; }
        public ushort ReaderVersion { get; set; }
        public ushort Flags { get; set; }
        public ushort Method { get; set; }
        public ushort ModifiedTime; // MS-DOS time
        public ushort ModifiedDate; // MS-DOS date
        public uint CRC32 { get; set; }
        [Obsolete("Use CompressedSize64 instead.")]
        internal uint CompressedSize { get; set; }
        [Obsolete("Use UncompressedSize64 instead.")]
        internal uint UncompressedSize { get; set; }
        public ulong CompressedSize64 { get; set; }
        public ulong UncompressedSize64 { get; set; }
        public byte[] Extra { get; set; }
        public uint ExternalAttrs { get; set; } // Meaning depends on CreatorVersion
        public string Comment { get; set; }

        // msDosTimeToTime converts an MS-DOS date and time into a time.Time.
        // The resolution is 2s.
        // See: http://msdn.microsoft.com/en-us/library/ms724247(v=VS.85).aspx
        private static DateTimeOffset msDosTimeToTime(ushort dosDate, ushort dosTime)
        {
            return new DateTimeOffset(
                // date bits 0-4: day of month; 5-8: month; 9-15: years since 1980
                (dosDate>>9)+1980,
                ((dosDate>>5)&0xf) + 1,
                dosDate&0x1f,

                // time bits 0-4: second/2; 5-10: minute; 11-15: hour
                dosTime>>11,
                (dosTime>>5)&0x3f,
                (dosTime&0x1f)*2,
                0, // milliseconds

                TimeSpan.Zero
            );
        }

        // timeToMsDosTime converts a time.Time to an MS-DOS date and time.
        // The resolution is 2s.
        // See: http://msdn.microsoft.com/en-us/library/ms724274(v=VS.85).aspx
        private static void timeToMsDosTime(DateTimeOffset t, out ushort fDate, out ushort fTime)
        {
            var dt = t.UtcDateTime;
            fDate = (ushort)(t.Day + ((t.Month - 1) << 5) + ((t.Year - 1980) << 9));
            fTime = (ushort)((t.Second / 2) + (t.Minute << 5) + (t.Hour << 11));
        }

        // ModTime is the modification time in UTC.
        // The resolution is 2s.
        public DateTimeOffset ModTime
        {
            get => msDosTimeToTime(this.ModifiedDate, this.ModifiedTime);
            set => timeToMsDosTime(value, out this.ModifiedDate, out this.ModifiedTime);
        }

        // Unix constants. The specification doesn't mention them,
        // but these seem to be the values agreed on by tools.
        private const ushort s_IFMT = 0xf000;
        private const ushort s_IFSOCK = 0xc000;
        private const ushort s_IFLNK = 0xa000;
        private const ushort s_IFREG = 0x8000;
        private const ushort s_IFBLK = 0x6000;
        private const ushort s_IFDIR = 0x4000;
        private const ushort s_IFCHR = 0x2000;
        private const ushort s_IFIFO = 0x1000;
        private const ushort s_ISUID = 0x800;
        private const ushort s_ISGID = 0x400;
        private const ushort s_ISVTX = 0x200;

        private const byte msdosDir = 0x10;
        private const byte msdosReadOnly = 0x01;

        // Mode is the permission and mode bits for the FileHeader.
        public FileAttributes Mode
        {
            get
            {
                FileAttributes mode = 0;
                switch (this.CreatorVersion >> 8)
                {
                    case Constants.creatorUnix:
                    case Constants.creatorMacOSX:
                        mode = unixModeToFileMode(this.ExternalAttrs >> 16);
                        break;
                    case Constants.creatorNTFS:
                    case Constants.creatorVFAT:
                    case Constants.creatorFAT:
                        mode = msdosModeToFileMode(this.ExternalAttrs);
                        break;
                }
                if (!string.IsNullOrEmpty(this.RawName) && this.RawName.EndsWith("/"))
                {
                    mode |= FileAttributes.Directory;
                }
                return mode;
            }

            set
            {
                this.CreatorVersion = (ushort)((this.CreatorVersion & 0xff) | (Constants.creatorUnix << 8));
                this.ExternalAttrs = fileModeToUnixMode(value) << 16;

                // set MSDOS attributes too, as the original zip does.
                if (value.HasFlag(FileAttributes.Directory))
                {
                    this.ExternalAttrs |= msdosDir;
                }
                if (value.HasFlag(FileAttributes.ReadOnly))
                {
                    this.ExternalAttrs |= msdosReadOnly;
                }
            }
        }

        // isZip64 reports whether the file size exceeds the 32 bit limit
        internal bool isZip64 => this.CompressedSize64 >= uint.MaxValue || this.UncompressedSize64 >= uint.MaxValue;

        private static FileAttributes msdosModeToFileMode(uint m)
        {
            FileAttributes mode = 0;
            if ((m & msdosDir) != 0)
            {
                mode = FileAttributes.Directory;
            }
            if ((m & msdosReadOnly) != 0)
            {
                mode |= FileAttributes.ReadOnly;
            }
            return mode;
        }

        private static uint fileModeToUnixMode(FileAttributes mode)
        {
            uint m;
            if (mode.HasFlag(FileAttributes.Directory))
            {
                m = s_IFDIR | (7 << 6) | (7 << 3) | 7;
            }
            else
            {
                m = (6 << 6) | (6 << 3) | 6;
            }
            if (mode.HasFlag(FileAttributes.ReadOnly))
            {
                m &= ~(uint)((2 << 6) | (2 << 3) | 2);
            }
            return m;
        }

        private static FileAttributes unixModeToFileMode(uint m)
        {
            FileAttributes mode = 0;
            if ((m & s_IFDIR) != 0)
            {
                mode |= FileAttributes.Directory;
            }
            if ((m & (2 << 6)) == 0)
            {
                mode |= FileAttributes.ReadOnly;
            }
            return mode;
        }

        // FileInfoHeader creates a partially-populated FileHeader from an
        // os.FileInfo.
        // Because os.FileInfo's Name method returns only the base name of
        // the file it describes, it may be necessary to modify the Name field
        // of the returned header to provide the full path name of the file.
        public static ZipFileHeader FileInfoHeader(FileSystemInfo fi)
        {
            var size = (fi as FileInfo)?.Length ?? 0;
            var fh = new ZipFileHeader
            {
                RawName = fi.Name,
                UncompressedSize64 = (ulong)size
            };
            fh.ModTime = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero);
            fh.Mode = fi.Attributes;
#pragma warning disable CS0618 // Type or member is obsolete
            if (fh.UncompressedSize64 > uint.MaxValue)
            {
                fh.UncompressedSize = uint.MaxValue;
            }
            else
            {
                fh.UncompressedSize = (uint)fh.UncompressedSize64;
            }
#pragma warning restore CS0618 // Type or member is obsolete
            return fh;
        }
    }

    internal sealed class DirectoryEnd
    {
        internal uint diskNbr; // unused
        internal uint dirDiskNbr; // unused
        internal ulong dirRecordsThisDisk; // unused
        internal ulong directoryRecords;
        internal ulong directorySize;
        internal ulong directoryOffset; // relative to file
        internal ushort commentLen;
        internal string comment;
    }
}

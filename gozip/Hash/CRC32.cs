// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Security.Cryptography;

namespace Golang.Hash
{
    // Package crc32 implements the 32-bit cyclic redundancy check, or CRC-32,
    // checksum. See http://en.wikipedia.org/wiki/Cyclic_redundancy_check for
    // information.
    //
    // Polynomials are represented in LSB-first form also known as reversed representation.
    //
    // See http://en.wikipedia.org/wiki/Mathematics_of_cyclic_redundancy_checks#Reversed_representations_and_reciprocal_polynomials
    // for information.
    public sealed class CRC32 : HashAlgorithm
    {
        // Predefined polynomials.

        // IEEE is by far and away the most common CRC-32 polynomial.
        // Used by ethernet (IEEE 802.3), v.42, fddi, gzip, zip, png, ...
        public const uint IEEE = 0xedb88320;
        private static readonly Lazy<uint[]> IEEETable = new Lazy<uint[]>(() => MakeTable(IEEE));
        private static readonly Lazy<uint[][]> IEEESlicingTable = new Lazy<uint[][]>(() => MakeSlicingTable(IEEE, IEEETable.Value));

        // Castagnoli's polynomial, used in iSCSI.
        // Has better error detection characteristics than IEEE.
        // http://dx.doi.org/10.1109/26.231911
        public const uint Castagnoli = 0x82f63b78;
        private static readonly Lazy<uint[]> CastagnoliTable = new Lazy<uint[]>(() => MakeTable(Castagnoli));
        private static readonly Lazy<uint[][]> CastagnoliSlicingTable = new Lazy<uint[][]>(() => MakeSlicingTable(Castagnoli, CastagnoliTable.Value));

        // Koopman's polynomial.
        // Also has better error detection characteristics than IEEE.
        // http://dx.doi.org/10.1109/DSN.2002.1028931
        public const uint Koopman = 0xeb31d82e;
        private static readonly Lazy<uint[]> KoopmanTable = new Lazy<uint[]>(() => MakeTable(Koopman));
        private static readonly Lazy<uint[][]> KoopmanSlicingTable = new Lazy<uint[][]>(() => MakeSlicingTable(Koopman, KoopmanTable.Value));

        // Use slicing-by-8 when payload >= this value.
        private const int slicing8Cutoff = 16;

        public uint Polynomial { get; }

        public uint CRC { get; private set; }

        // Table is a 256-word table representing the polynomial for efficient processing.
        private readonly Lazy<uint[]> Table;

        // slicing8Table is array of 8 Tables, used by the slicing-by-8 algorithm.
        private readonly Lazy<uint[][]> SlicingTable;

        // simpleMakeTable allocates and constructs a Table for the specified
        // polynomial. The table is suitable for use with the simple algorithm
        // (simpleUpdate).
        private static uint[] MakeTable(uint poly)
        {
            var table = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                var crc = (uint)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                    {
                        crc = (crc >> 1) ^ poly;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
                table[i] = crc;
            }
            return table;
        }

        // slicingMakeTable constructs a slicing8Table for the specified polynomial. The
        // table is suitable for use with the slicing-by-8 algorithm (slicingUpdate).
        private static uint[][] MakeSlicingTable(uint poly, uint[] first)
        {
            var t = new uint[8][];
            t[0] = first;
            for (int i = 1; i < 8; i++)
            {
                t[i] = new uint[256];
            }
            for (int i = 0; i < 256; i++)
            {
                var crc = t[0][i];
                for (int j = 1; j < 8; j++)
                {
                    crc = t[0][crc & 0xFF] ^ (crc >> 8);
                    t[j][i] = crc;
                }
            }
            return t;
        }

        public CRC32(uint polynomial = IEEE)
        {
            this.Polynomial = polynomial;
            switch (polynomial)
            {
                case IEEE:
                    this.Table = IEEETable;
                    this.SlicingTable = IEEESlicingTable;
                    break;
                case Castagnoli:
                    this.Table = CastagnoliTable;
                    this.SlicingTable = CastagnoliSlicingTable;
                    break;
                case Koopman:
                    this.Table = KoopmanTable;
                    this.SlicingTable = KoopmanSlicingTable;
                    break;
                default:
                    this.Table = new Lazy<uint[]>(() => MakeTable(this.Polynomial));
                    this.SlicingTable = new Lazy<uint[][]>(() => MakeSlicingTable(this.Polynomial, this.Table.Value));
                    break;
            }
        }

        public override void Initialize()
        {
            this.CRC = 0;
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            if (cbSize >= slicing8Cutoff)
            {
                var tab = this.SlicingTable.Value;
                this.CRC = ~this.CRC;
                while (cbSize > 8)
                {
                    this.CRC ^= (uint)(array[ibStart]) | (uint)((array[ibStart + 1]) << 8) | (uint)((array[ibStart + 2]) << 16) | (uint)((array[ibStart + 3]) << 24);
                    this.CRC = tab[0][array[ibStart + 7]] ^ tab[1][array[ibStart + 6]] ^ tab[2][array[ibStart + 5]] ^ tab[3][array[ibStart + 4]] ^
                        tab[4][this.CRC >> 24] ^ tab[5][(this.CRC >> 16) & 0xFF] ^
                        tab[6][(this.CRC >> 8) & 0xFF] ^ tab[7][this.CRC & 0xFF];
                    ibStart += 8;
                    cbSize -= 8;
                }
                this.CRC = ~this.CRC;
            }
            if (cbSize != 0)
            {
                var tab = this.Table.Value;
                this.CRC = ~this.CRC;
                for (int i = 0; i < cbSize; i++)
                {
                    this.CRC = tab[(byte)this.CRC ^ array[ibStart + i]] ^ (this.CRC >> 8);
                }
                this.CRC = ~this.CRC;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            this.HashCore(buffer, offset, count);
        }

        protected override byte[] HashFinal()
        {
            return BitConverter.GetBytes(this.CRC);
        }
    }
}

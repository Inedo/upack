// Copyright 2010 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using Golang.IO;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;

namespace Golang.Archive.Zip
{
    // A Compressor returns a new compressing writer, writing to w.
    // The WriteCloser's Close method must be used to flush pending data to w.
    // The Compressor itself must be safe to invoke from multiple goroutines
    // simultaneously, but each returned writer will be used only by
    // one goroutine at a time.
    public delegate Stream Compressor(Stream w);

    // A Decompressor returns a new decompressing reader, reading from r.
    // The ReadCloser's Close method must be used to release associated resources.
    // The Decompressor itself must be safe to invoke from multiple goroutines
    // simultaneously, but each returned reader will be used only by
    // one goroutine at a time.
    public delegate Stream Decompressor(Stream r);

    public static class Compressors
    {
        private static readonly ConcurrentDictionary<ushort, Compressor> compressors = new ConcurrentDictionary<ushort, Compressor>();
        private static readonly ConcurrentDictionary<ushort, Decompressor> decompressors = new ConcurrentDictionary<ushort, Decompressor>();

        static Compressors()
        {
            compressors[Constants.Store] = w => new NopCloser(w);
            compressors[Constants.Deflate] = w => new DeflateStream(w, CompressionMode.Compress, true);

            decompressors[Constants.Store] = r => new NopCloser(r);
            decompressors[Constants.Deflate] = r => new DeflateStream(r, CompressionMode.Decompress, true);
        }

        // RegisterDecompressor allows custom decompressors for a specified method ID.
        // The common methods Store and Deflate are built in.
        public static void Register(ushort method, Decompressor dcomp)
        {
            if (!decompressors.TryAdd(method, dcomp))
            {
                throw new InvalidOperationException("decompressor already registered");
            }
        }

        // RegisterCompressor registers custom compressors for a specified method ID.
        // The common methods Store and Deflate are built in.
        public static void Register(ushort method, Compressor comp)
        {
            if (!compressors.TryAdd(method, comp))
            {
                throw new InvalidOperationException("compressor already registered");
            }
        }

        internal static Compressor compressor(ushort method)
        {
            if (compressors.TryGetValue(method, out var compressor))
            {
                return compressor;
            }
            return null;
        }

        internal static Decompressor decompressor(ushort method)
        {
            if (decompressors.TryGetValue(method, out var decompressor))
            {
                return decompressor;
            }
            return null;
        }
    }
}

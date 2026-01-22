#region C ZLib License
/* deflate.c -- compress data using the deflation algorithm
 * Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
 * For conditions of distribution and use, see copyright notice in zlib.h
 */

/*
 *  ALGORITHM
 *
 *      The "deflation" process depends on being able to identify portions
 *      of the input text which are identical to earlier input (within a
 *      sliding window trailing behind the input currently being processed).
 *
 *      The most straightforward technique turns out to be the fastest for
 *      most input files: try all possible matches and select the longest.
 *      The key feature of this algorithm is that insertions into the string
 *      dictionary are very simple and thus fast, and deletions are avoided
 *      completely. Insertions are performed at each input character, whereas
 *      string matches are performed only when the previous match ends. So it
 *      is preferable to spend more time in matches to allow very fast string
 *      insertions and avoid deletions. The matching algorithm for small
 *      strings is inspired from that of Rabin & Karp. A brute force approach
 *      is used to find longer strings when a small match has been found.
 *      A similar algorithm is used in comic (by Jan-Mark Wams) and freeze
 *      (by Leonid Broukhis).
 *         A previous version of this file used a more sophisticated algorithm
 *      (by Fiala and Greene) which is guaranteed to run in linear amortized
 *      time, but has a larger average cost, uses more memory and is patented.
 *      However the F&G algorithm may be faster for some highly redundant
 *      files if the parameter max_chain_length (described below) is too large.
 *
 *  ACKNOWLEDGEMENTS
 *
 *      The idea of lazy evaluation of matches is due to Jan-Mark Wams, and
 *      I found it in 'freeze' written by Leonid Broukhis.
 *      Thanks to many people for bug reports and testing.
 *
 *  REFERENCES
 *
 *      Deutsch, L.P.,"DEFLATE Compressed Data Format Specification".
 *      Available in http://tools.ietf.org/html/rfc1951
 *
 *      A description of the Rabin and Karp algorithm is given in the book
 *         "Algorithms" by R. Sedgewick, Addison-Wesley, p252.
 *
 *      Fiala,E.R., and Greene,D.H.
 *         Data Compression with Finite Windows, Comm.ACM, 32,4 (1989) 490-595
 *
 */
#endregion

#region C# Port License
/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2025 James F. Bellinger <http://software.seekye.com/remoteviewing>
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
#endregion

using System;
using System.Runtime.InteropServices;

namespace RemoteViewing.Utility;

unsafe static partial class ZLib
{
    const int DIST_CODE_LEN = 512;

    static readonly byte[] _dist_code = [
 0,  1,  2,  3,  4,  4,  5,  5,  6,  6,  6,  6,  7,  7,  7,  7,  8,  8,  8,  8,
 8,  8,  8,  8,  9,  9,  9,  9,  9,  9,  9,  9, 10, 10, 10, 10, 10, 10, 10, 10,
10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11,
11, 11, 11, 11, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 13, 13, 13, 13,
13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13,
13, 13, 13, 13, 13, 13, 13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 15, 15, 15, 15, 15, 15, 15, 15,
15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,  0,  0, 16, 17,
18, 18, 19, 19, 20, 20, 20, 20, 21, 21, 21, 21, 22, 22, 22, 22, 22, 22, 22, 22,
23, 23, 23, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
24, 24, 24, 24, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 27, 27, 27, 27, 27, 27, 27, 27,
27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
27, 27, 27, 27, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
28, 28, 28, 28, 28, 28, 28, 28, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29
];

    static readonly byte[] _length_code = [
 0,  1,  2,  3,  4,  5,  6,  7,  8,  8,  9,  9, 10, 10, 11, 11, 12, 12, 12, 12,
13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15, 16, 16, 16, 16, 16, 16, 16, 16,
17, 17, 17, 17, 17, 17, 17, 17, 18, 18, 18, 18, 18, 18, 18, 18, 19, 19, 19, 19,
19, 19, 19, 19, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20,
21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 22, 22, 22, 22,
22, 22, 22, 22, 22, 22, 22, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 23, 23, 23,
23, 23, 23, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 26, 26, 26, 26, 26, 26, 26, 26,
26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
26, 26, 26, 26, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 28
];

    static readonly int[] base_length = [
0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56,
64, 80, 96, 112, 128, 160, 192, 224, 0
];

    static readonly int[] base_dist = [
    0,     1,     2,     3,     4,     6,     8,    12,    16,    24,
   32,    48,    64,    96,   128,   192,   256,   384,   512,   768,
 1024,  1536,  2048,  3072,  4096,  6144,  8192, 12288, 16384, 24576
];

    static readonly ct_data* static_ltree;

    static readonly ct_data* static_dtree;

    static readonly static_tree_desc* static_l_desc;

    static readonly static_tree_desc* static_d_desc;

    static readonly static_tree_desc* static_bl_desc;

    static readonly int[] extra_lbits_value =
        /* extra bits for each length code */
        [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];

    static readonly int* extra_lbits;

    static readonly int[] extra_dbits_value =
       /* extra bits for each distance code */
       [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    static readonly int* extra_dbits;

    static readonly int[] extra_blbits_value =
       /* extra bits for each bit length code */
       [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 7];

    static readonly int* extra_blbits;

    static ZLib()
    {
        ushort* bl_count = stackalloc ushort[MAX_BITS + 1];
        /* number of codes at each bit length for an optimal tree */

        // Not a memory leak: This is allocated once at startup and the OS will deallocate it on exit.
        // TODO: We don't handle OutOfMemoryException here, and probably should. Granted, if we're
        //       running out at startup, we're in big trouble later. But still.
        static_ltree = (ct_data*)allocate(L_CODES + 2, sizeof(ct_data));
        static_dtree = (ct_data*)allocate(D_CODES, sizeof(ct_data));

        /* Construct the codes of the static literal tree */
        {
            for (uint bits = 0; bits <= MAX_BITS; bits++) bl_count[bits] = 0;
            uint n = 0;
            while (n <= 143) { static_ltree[n++].Len = 8; bl_count[8]++; }
            while (n <= 255) { static_ltree[n++].Len = 9; bl_count[9]++; }
            while (n <= 279) { static_ltree[n++].Len = 7; bl_count[7]++; }
            while (n <= 287) { static_ltree[n++].Len = 8; bl_count[8]++; }
            /* Codes 286 and 287 do not exist, but we must include them in the
             * tree construction to get a canonical Huffman tree (longest code
             * all ones)
             */
            gen_codes(static_ltree, L_CODES + 1, bl_count);
        }

        /* The static distance tree is trivial: */
        for (uint n = 0; n < D_CODES; n++)
        {
            static_dtree[n].Len = 5;
            static_dtree[n].Code = (ushort)bi_reverse(n, 5);
        }

        //
        extra_lbits = (int*)allocate(LENGTH_CODES, sizeof(int));
        for (int i = 0; i < LENGTH_CODES; i++) { extra_lbits[i] = extra_lbits_value[i]; }

        extra_dbits = (int*)allocate(D_CODES, sizeof(int));
        for (int i = 0; i < D_CODES; i++) { extra_dbits[i] = extra_dbits_value[i]; }

        extra_blbits = (int*)allocate(BL_CODES, sizeof(int));
        for (int i = 0; i < BL_CODES; i++) { extra_blbits[i] = extra_blbits_value[i]; }

        //
        static_l_desc = (static_tree_desc*)allocate(1, sizeof(static_tree_desc));
        static_l_desc->static_tree = static_ltree;
        static_l_desc->extra_bits = extra_lbits;
        static_l_desc->extra_base = LITERALS + 1;
        static_l_desc->elems = L_CODES;
        static_l_desc->max_length = MAX_BITS;

        static_d_desc = (static_tree_desc*)allocate(1, sizeof(static_tree_desc));
        static_d_desc->static_tree = static_dtree;
        static_d_desc->extra_bits = extra_dbits;
        static_d_desc->extra_base = 0;
        static_d_desc->elems = D_CODES;
        static_d_desc->max_length = MAX_BITS;

        static_bl_desc = (static_tree_desc*)allocate(1, sizeof(static_tree_desc));
        static_bl_desc->static_tree = null;
        static_bl_desc->extra_bits = extra_blbits;
        static_bl_desc->extra_base = 0;
        static_bl_desc->elems = BL_CODES;
        static_bl_desc->max_length = MAX_BL_BITS;
    }

    static byte d_code(uint dist)
    {
        return (dist) < 256 ? _dist_code[dist] : _dist_code[256 + ((dist) >> 7)];
    }

    static int allocate_size(uint items, int size)
    {
        return checked((int)(items * (uint)size));
    }

    static void* allocate(uint items, int size)
    {
        int bytes = allocate_size(items, size);
        return (void*)Marshal.AllocHGlobal(bytes);
    }

    public static void* calloc(uint items, int size)
    {
        int bytes = allocate_size(items, size);
        byte* memory = (byte*)allocate(items, size);
        for (int i = 0; i < bytes; i++) { memory[i] = 0; }
        return memory;
    }

    static void* ZALLOC(z_stream* strm, uint items, int size)
    {
        try { return allocate(items, size); }
        catch (OutOfMemoryException) { return null; }
    }

    static void TRY_FREE(z_stream* strm, void* ptr)
    {
        free(ptr);
    }

    static void ZFREE(z_stream* strm, void* ptr)
    {
        free(ptr);
    }

    public static void free(void* ptr)
    {
        if (ptr != null) { Marshal.FreeHGlobal((IntPtr)ptr); }
    }
}

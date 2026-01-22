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

using System.Runtime.InteropServices;
using System;

namespace RemoteViewing.Utility;

unsafe static partial class ZLib
{
    const int DEF_MEM_LEVEL = 8;
    const int MAX_MEM_LEVEL = 9;

    const int STORED_BLOCK = 0;
    const int STATIC_TREES = 1;
    const int DYN_TREES = 2;

    const int MIN_MATCH = 3;
    const int MAX_MATCH = 258;
    const int PRESET_DICT = 0x20;

    const int WIN_INIT = MAX_MATCH;

    const int MAX_WBITS = 15;

    const uint MIN_LOOKAHEAD = MAX_MATCH + MIN_MATCH + 1;
    static uint MAX_DIST(deflate_state* s) { return s->w_size - MIN_LOOKAHEAD; }

    const int TOO_FAR = 4096;

    const int LIT_BUFS = 4;

    const int LENGTH_CODES = 29;
    const int LITERALS = 256;
    const int L_CODES = LITERALS + 1 + LENGTH_CODES;
    const int D_CODES = 30;
    const int BL_CODES = 19;
    const int HEAP_SIZE = 2 * L_CODES + 1;
    const int MAX_BITS = 15;
    const int Buf_size = 16;

    const int INIT_STATE = 42;    /* zlib header -> BUSY_STATE */
    const int EXTRA_STATE = 69;    /* gzip extra block -> NAME_STATE */
    const int NAME_STATE = 73;    /* gzip file name -> COMMENT_STATE */
    const int COMMENT_STATE = 91;    /* gzip comment -> HCRC_STATE */
    const int HCRC_STATE = 103;    /* gzip header CRC -> BUSY_STATE */
    const int BUSY_STATE = 113;   /* deflate -> FINISH_STATE */
    const int FINISH_STATE = 666;    /* stream complete */
    /* Stream status */

    static void Assert(int condition, string error)
    {
        Assert(condition != 0, error);
    }

    static void Assert(bool condition, string error)
    {
        if (!condition)
        {
            throw new Exception(error);
        }
    }

    /* Data structure describing a single value and its code string. */
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    internal struct ct_data
    {
        [FieldOffset(0)]
        public ushort Freq;       /* frequency count */

        [FieldOffset(0)]
        public ushort Code;       /* bit string */

        [FieldOffset(2)]
        public ushort Dad;        /* father node in Huffman tree */

        [FieldOffset(2)]
        public ushort Len;        /* length of bit string */
    }

    internal struct tree_desc
    {
        public ct_data* dyn_tree;           /* the dynamic tree */
        public int max_code;            /* largest code with non zero frequency */
        public static_tree_desc* stat_desc;  /* the corresponding static tree */
    }

    /*
         gzip header information passed to and from zlib routines.  See RFC 1952
      for more details on the meanings of these fields.
    */
    internal struct gz_header
    {
        public int text;       /* true if compressed data believed to be text */
        public ulong time;       /* modification time */
        public int xflags;     /* extra flags (not used when writing a gzip file) */
        public int os;         /* operating system */
        public byte* extra;     /* pointer to extra field or Z_NULL if none */
        public uint extra_len;  /* extra field length (valid if extra != Z_NULL) */
        public uint extra_max;  /* space at extra (only when reading header) */
        public byte* name;      /* pointer to zero-terminated file name or Z_NULL */
        public uint name_max;   /* space at name (only when reading header) */
        public byte* comment;   /* pointer to zero-terminated comment or Z_NULL */
        public uint comm_max;   /* space at comment (only when reading header) */
        public int hcrc;       /* true if there was or will be a header crc */
        public int done;       /* true when done reading gzip header (not used
                                  when writing a gzip file) */
    }

    /// <summary>
    /// NOTE: Make sure deflate_state is never in movable memory (GC heap, etc.).
    ///       See dyn_ltree implementation for why. We use ZALLOC currently.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct deflate_state
    {
        public z_stream* strm;      /* pointer back to this zlib stream */
        public int status;        /* as the name implies */
        public byte* pending_buf;  /* output still pending */
        public ulong pending_buf_size; /* size of pending_buf */
        public byte* pending_out;  /* next pending byte to output to the stream */
        public ulong pending;       /* nb of bytes in the pending buffer */
        public int wrap;          /* bit 0 true for zlib, bit 1 true for gzip */
        public gz_header* gzhead;  /* gzip header information to write */
        public ulong gzindex;       /* where in extra, name, or comment */
        public byte method;        /* can only be DEFLATED */
        public int last_flush;    /* value of flush param for previous deflate call */

        /* used by deflate.c: */

        public uint w_size;        /* LZ77 window size (32K by default) */
        public uint w_bits;        /* log2(w_size)  (8..16) */
        public uint w_mask;        /* w_size - 1 */

        public byte* window;
        /* Sliding window. Input bytes are read into the second half of the window,
         * and move to the first half later to keep a dictionary of at least wSize
         * bytes. With this organization, matches are limited to a distance of
         * wSize-MAX_MATCH bytes, but this ensures that IO is always
         * performed with a length multiple of the block size. Also, it limits
         * the window size to 64K, which is quite useful on MSDOS.
         * To do: use the user input buffer as sliding window.
         */

        public ulong window_size;
        /* Actual size of window: 2*wSize, except when the user input buffer
         * is directly used as sliding window.
         */

        public ushort* prev;
        /* Link to older string with same hash index. To limit the size of this
         * array to 64K, this link is maintained only for the last 32K strings.
         * An index in this array is thus a window index modulo 32K.
         */

        public ushort* head; /* Heads of the hash chains or NIL. */

        public uint ins_h;          /* hash index of string to be inserted */
        public uint hash_size;      /* number of elements in hash table */
        public uint hash_bits;      /* log2(hash_size) */
        public uint hash_mask;      /* hash_size-1 */

        public uint hash_shift;
        /* Number of bits by which ins_h must be shifted at each input
         * step. It must be sbyte that after MIN_MATCH steps, the oldest
         * byte no longer takes part in the hash key, that is:
         *   hash_shift * MIN_MATCH >= hash_bits
         */

        public long block_start;
        /* Window position at the beginning of the current output block. Gets
         * negative when the window is moved backwards.
         */

        public uint match_length;           /* length of best match */
        public uint prev_match;             /* previous match */
        public int match_available;         /* set if previous match exists */
        public uint strstart;               /* start of string to insert */
        public uint match_start;            /* start of matching string */
        public uint lookahead;              /* number of valid bytes ahead in window */

        public uint prev_length;
        /* Length of the best match at previous step. Matches not greater than this
         * are discarded. This is used in the lazy match evaluation.
         */

        public uint max_chain_length;
        /* To speed up deflation, hash chains are never searched beyond this
         * length.  A higher limit improves compression ratio but degrades the
         * speed.
         */

        public uint max_lazy_match;
        /* Attempt to find a better match only when the current match is strictly
         * smaller than this value. This mechanism is used only for compression
         * levels >= 4.
         */

        /* Insert new strings in the hash table only if the match length is not
         * greater than this length. This saves time but degrades compression.
         * max_lazy_match is used only for compression levels <= 3.
         */

        public int level;    /* compression level (1..9) */
        public int strategy; /* favor or force Huffman coding*/

        public uint good_match;
        /* Use a faster search when the previous match is longer than this */

        public int nice_match; /* Stop searching when current match exceeds this */

        /* used by trees.c: */
        // NOTE: Unfortunately .NET doesn't let us use fixed in this manner.
        //       Our data isn't allocated by the GC. This is stupid.
        //public fixed ct_data dyn_ltree[HEAP_SIZE];   /* literal and length tree */
        //public fixed ct_data dyn_dtree[2*D_CODES+1]; /* distance tree */
        //public fixed ct_data bl_tree[2*BL_CODES+1];  /* Huffman tree for bit lengths */
        fixed byte dyn_ltree_p[4 * (HEAP_SIZE)];
        fixed byte dyn_dtree_p[4 * (2 * D_CODES + 1)];
        fixed byte bl_tree_p[4 * (2 * BL_CODES + 1)];
        public ct_data* dyn_ltree { get { fixed (byte* x = dyn_ltree_p) { return (ct_data*)x; } } }
        public ct_data* dyn_dtree { get { fixed (byte* x = dyn_dtree_p) { return (ct_data*)x; } } }
        public ct_data* bl_tree { get { fixed (byte* x = bl_tree_p) { return (ct_data*)x; } } }

        public tree_desc l_desc;               /* desc. for literal tree */
        public tree_desc d_desc;               /* desc. for distance tree */
        public tree_desc bl_desc;              /* desc. for bit length tree */

        public fixed ushort bl_count[MAX_BITS + 1];
        /* number of codes at each bit length for an optimal tree */

        public fixed int heap[2 * L_CODES + 1];      /* heap used to build the Huffman trees */
        public int heap_len;               /* number of elements in the heap */
        public int heap_max;               /* element of largest frequency */
        /* The sons of heap[n] are heap[2*n] and heap[2*n+1]. heap[0] is not used.
         * The same heap array is used to build all trees.
         */

        public fixed byte depth[2 * L_CODES + 1];
        /* Depth of each subtree used as tie breaker for trees of equal frequency
         */

        public byte* sym_buf;        /* buffer for distances and literals/lengths */

        public uint lit_bufsize;
        /* Size of match buffer for literals/lengths.  There are 4 reasons for
         * limiting lit_bufsize to 64K:
         *   - frequencies can be kept in 16 bit counters
         *   - if compression is not successful for the first block, all input
         *     data is still in the window so we can still emit a stored block even
         *     when input comes from standard input.  (This can also be done for
         *     all blocks if lit_bufsize is not greater than 32K.)
         *   - if compression is not successful for a file smaller than 64K, we can
         *     even emit a stored file instead of a stored block (saving 5 bytes).
         *     This is applicable only for zip (not gzip or zlib).
         *   - creating new Huffman trees less frequently may not provide fast
         *     adaptation to changes in the input data statistics. (Take for
         *     example a binary file with poorly compressible code followed by
         *     a highly compressible string table.) Smaller buffer sizes give
         *     fast adaptation but have of course the overhead of transmitting
         *     trees more frequently.
         *   - I can't count above 4
         */

        public uint sym_next;      /* running index in symbol buffer */
        public uint sym_end;       /* symbol table full when sym_next reaches this */

        public ulong opt_len;        /* bit length of current block with optimal trees */
        public ulong static_len;     /* bit length of current block with static trees */
        public uint matches;       /* number of string matches in current block */
        public uint insert;        /* bytes at end of window left to insert */

        public ushort bi_buf;
        /* Output buffer. bits are inserted starting at the bottom (least
         * significant bits).
         */
        public int bi_valid;
        /* Number of valid bits in bi_buf.  All bits above the last valid bit
         * are always zero.
         */

        public ulong high_water;
        /* High water mark offset in window for initialized bytes -- bytes above
         * this are set to zero in order to avoid memory check warnings when
         * longest match routines access bytes past the input.  This is then
         * updated to the new high water mark.
         */
    }

    public struct z_stream
    {
        public byte* next_in;     /* next input byte */
        public uint avail_in;  /* number of bytes available at next_in */
        public ulong total_in;  /* total number of input bytes read so far */

        public byte* next_out; /* next output byte will go here */
        public uint avail_out; /* remaining free space at next_out */
        public ulong total_out; /* total number of bytes output so far */

        internal deflate_state* state; /* not visible by applications */

        public void* opaque;  /* private data object passed to zalloc and zfree */

        public int data_type;  /* best guess about the data type: binary or text
                           for deflate, or the decoding state for inflate */
        public ulong adler;      /* Adler-32 or CRC-32 value of the uncompressed data */
        public ulong reserved;   /* reserved for future use */
    }

    static void zmemcpy(byte* dest, byte* source, uint len)
    {
        if (len == 0) return;
        do
        {
            *dest++ = *source++; /* ??? to be unrolled */
        } while (--len != 0);
    }

    static void zmemzero(byte* dest, uint len)
    {
        if (len == 0) return;
        do
        {
            *dest++ = 0;  /* ??? to be unrolled */
        } while (--len != 0);
    }

    public static ulong compressBound(ulong sourceLen)
    {
        return sourceLen + (sourceLen >> 12) + (sourceLen >> 14) + (sourceLen >> 25) + 13;
    }
}
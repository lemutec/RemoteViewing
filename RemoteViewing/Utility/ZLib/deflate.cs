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

// NOT defined: FASTEST, GZIP, LIT_MEM, ZLIB_DEBUG
//     defined: UNALIGNED_OK, Z_SOLO

namespace RemoteViewing.Utility;

unsafe static partial class ZLib
{
    const string deflate_copyright =
       " deflate 1.3.1 Copyright 1995-2024 Jean-loup Gailly and Mark Adler ";

    /*
      If you use the zlib library in a product, an acknowledgment is welcome
      in the documentation of your product. If for some reason you cannot
      include sbyte an acknowledgment, I would appreciate that you keep this
      copyright string in the executable of your product.
     */

    enum block_state
    {
        need_more,      /* block not completed, need more input or more output */
        block_done,     /* block flush performed */
        finish_started, /* finish started, need only more output at next deflate */
        finish_done     /* finish done, accept no more input or output */
    }

    delegate block_state compress_func(deflate_state* s, int flush);
    /* Compression function. Returns the block state after the call. */

    /* ===========================================================================
     * Local data
     */

    /* Values for max_lazy_match, good_match and max_chain_length, depending on
     * the desired pack level (0..9). The values given below have been tuned to
     * exclude worst case performance for pathological files. Better values may be
     * found for specific files.
     */
    struct config
    {
        public ushort good_length; /* reduce lazy search above this match length */
        public ushort max_lazy;    /* do not perform lazy search above this match length */
        public ushort nice_length; /* quit search above this match length */
        public ushort max_chain;
        public compress_func func;
    }

    static config[] configuration_table = new config[] {
/*      good lazy nice chain */
/* 0 */ new config() {good_length=0,    max_lazy=0, nice_length= 0, max_chain=   0,func= deflate_stored},  /* store only */
/* 1 */ new config() {good_length=4,    max_lazy=4, nice_length= 8, max_chain=   4,func= deflate_fast}, /* max speed, no lazy matches */
/* 2 */ new config() {good_length=4, max_lazy=   5, nice_length=16, max_chain=   8,func= deflate_fast},
/* 3 */ new config() {good_length=4, max_lazy=   6, nice_length=32, max_chain=  32, func=deflate_fast},

/* 4 */ new config() {good_length=4,   max_lazy= 4,nice_length= 16, max_chain=  16, func=deflate_slow},  /* lazy matches */
/* 5 */ new config() {good_length=8,  max_lazy= 16,nice_length= 32, max_chain=  32, func=deflate_slow},
/* 6 */ new config() {good_length=8,max_lazy=   16, nice_length=128, max_chain=128,func= deflate_slow},
/* 7 */ new config() {good_length=8,   max_lazy=32, nice_length=128,max_chain= 256,func= deflate_slow},
/* 8 */ new config() {good_length=32, max_lazy=128, nice_length=258, max_chain=1024, func=deflate_slow},
/* 9 */ new config() {good_length=32, max_lazy=258, nice_length=258, max_chain=4096, func=deflate_slow}
}; /* max compression */

    /* Note: the deflate() code requires max_lazy >= MIN_MATCH and max_chain >= 4
     * For deflate_fast() (levels <= 3) good is ignored and lazy has a different
     * meaning.
     */

    /* rank Z_BLOCK between Z_NO_FLUSH and Z_PARTIAL_FLUSH */
    static int RANK(int f) { return (((f) * 2) - ((f) > 4 ? 9 : 0)); }

    /* ===========================================================================
     * Update a hash value with the given input byte
     * IN  assertion: all calls to UPDATE_HASH are made with consecutive input
     *    characters, so that a running hash key can be computed from the previous
     *    key instead of complete recalculation each time.
     */
    static void UPDATE_HASH(deflate_state* s, ref uint h, byte c) { h = (((h) << (int)s->hash_shift) ^ (c)) & s->hash_mask; }


    /* ===========================================================================
     * Insert string str in the dictionary and set match_head to the previous head
     * of the hash chain (the most recent string with same hash key). Return
     * the previous length of the hash chain.
     * If this file is compiled with -DFASTEST, the compression level is forced
     * to 1, and no hash chains are maintained.
     * IN  assertion: all calls to INSERT_STRING are made with consecutive input
     *    characters and the first MIN_MATCH bytes of str are valid (except for
     *    the last MIN_MATCH-1 bytes of the input file).
     */
    static void INSERT_STRING(deflate_state* s, uint str, ref uint match_head)
    {
        UPDATE_HASH(s, ref s->ins_h, s->window[(str) + (MIN_MATCH - 1)]);
        match_head = s->prev[(str) & s->w_mask] = s->head[s->ins_h];
        s->head[s->ins_h] = (ushort)(str);
    }

    /* ===========================================================================
     * Initialize the hash table (avoiding 64K overflow for 16 bit systems).
     * prev[] will be initialized on the fly.
     */
    static void CLEAR_HASH(deflate_state* s)
    {
        s->head[s->hash_size - 1] = 0;
        zmemzero((byte*)s->head, (uint)(s->hash_size - 1) * sizeof(ushort));
    }

    /* ===========================================================================
     * Slide the hash table when sliding the window down (could be avoided with 32
     * bit values at the expense of memory usage). We slide even when level == 0 to
     * keep the hash table consistent if we switch back to level > 0 later.
     */
    static void slide_hash(deflate_state* s)
    {
        uint n, m;
        ushort* p;
        uint wsize = s->w_size;

        n = s->hash_size;
        p = &s->head[n];
        do
        {
            m = *--p;
            *p = (ushort)(m >= wsize ? m - wsize : 0);
        } while ((--n) != 0);
        n = wsize;

        p = &s->prev[n];
        do
        {
            m = *--p;
            *p = (ushort)(m >= wsize ? m - wsize : 0);
            /* If n is not on any hash chain, prev[n] is garbage but
             * its value will never be used.
             */
        } while ((--n) != 0);
    }

    /* ===========================================================================
     * Read a new buffer from the current input stream, update the adler32
     * and total number of bytes read.  All deflate() input goes through
     * this function so some applications may wish to modify it to avoid
     * allocating a large strm->next_in buffer and copying from it.
     * (See also flush_pending()).
     */
    static uint read_buf(z_stream* strm, byte* buf, uint size)
    {
        uint len = strm->avail_in;

        if (len > size) len = size;
        if (len == 0) return 0;

        strm->avail_in -= len;

        zmemcpy(buf, strm->next_in, len);
        if (strm->state->wrap == 1)
        {
            strm->adler = adler32(strm->adler, buf, len);
        }
        strm->next_in += len;
        strm->total_in += len;

        return len;
    }

    /* ===========================================================================
     * Fill the window when the lookahead becomes insufficient.
     * Updates strstart and lookahead.
     *
     * IN assertion: lookahead < MIN_LOOKAHEAD
     * OUT assertions: strstart <= window_size-MIN_LOOKAHEAD
     *    At least one byte has been read, or avail_in == 0; reads are
     *    performed for at least two bytes (required for the zip translate_eol
     *    option -- not supported here).
     */
    static void fill_window(deflate_state* s)
    {
        uint n;
        uint more;    /* Amount of free space at the end of the window. */
        uint wsize = s->w_size;

        Assert(s->lookahead < MIN_LOOKAHEAD, "already enough lookahead");

        do
        {
            more = (uint)(s->window_size - (ulong)s->lookahead - (ulong)s->strstart);

            /* Deal with !@#$% 64K limit: */
            if (sizeof(int) <= 2)
            {
                if (more == 0 && s->strstart == 0 && s->lookahead == 0)
                {
                    more = wsize;

                }
                else if (more == unchecked((uint)(-1)))
                {
                    /* Very unlikely, but possible on 16 bit machine if
                     * strstart == 0 && lookahead == 1 (input done a byte at time)
                     */
                    more--;
                }
            }

            /* If the window is almost full and there is insufficient lookahead,
             * move the upper half to the lower one to make room in the upper half.
             */
            if (s->strstart >= wsize + MAX_DIST(s))
            {

                zmemcpy(s->window, s->window + wsize, (uint)wsize - more);
                s->match_start -= wsize;
                s->strstart -= wsize; /* we now have strstart >= MAX_DIST */
                s->block_start -= (long)wsize;
                if (s->insert > s->strstart)
                    s->insert = s->strstart;
                slide_hash(s);
                more += wsize;
            }
            if (s->strm->avail_in == 0) break;

            /* If there was no sliding:
             *    strstart <= WSIZE+MAX_DIST-1 && lookahead <= MIN_LOOKAHEAD - 1 &&
             *    more == window_size - lookahead - strstart
             * => more >= window_size - (MIN_LOOKAHEAD-1 + WSIZE + MAX_DIST-1)
             * => more >= window_size - 2*WSIZE + 2
             * In the BIG_MEM or MMAP case (not yet supported),
             *   window_size == input_size + MIN_LOOKAHEAD  &&
             *   strstart + s->lookahead <= input_size => more >= MIN_LOOKAHEAD.
             * Otherwise, window_size == 2*WSIZE so more >= 2.
             * If there was sliding, more >= WSIZE. So in all cases, more >= 2.
             */
            Assert(more >= 2, "more < 2");

            n = read_buf(s->strm, s->window + s->strstart + s->lookahead, more);
            s->lookahead += n;

            /* Initialize the hash value now that we have some input: */
            if (s->lookahead + s->insert >= MIN_MATCH)
            {
                uint str = s->strstart - s->insert;
                s->ins_h = s->window[str];
                UPDATE_HASH(s, ref s->ins_h, s->window[str + 1]);

                while (s->insert != 0)
                {
                    UPDATE_HASH(s, ref s->ins_h, s->window[str + MIN_MATCH - 1]);
                    s->prev[str & s->w_mask] = s->head[s->ins_h];
                    s->head[s->ins_h] = (ushort)str;
                    str++;
                    s->insert--;
                    if (s->lookahead + s->insert < MIN_MATCH)
                        break;
                }
            }
            /* If the whole input has less than MIN_MATCH bytes, ins_h is garbage,
             * but this is not important since only literal bytes will be emitted.
             */

        } while (s->lookahead < MIN_LOOKAHEAD && s->strm->avail_in != 0);

        /* If the WIN_INIT bytes after the end of the current data have never been
         * written, then zero those bytes in order to avoid memory check reports of
         * the use of uninitialized (or uninitialised as Julian writes) bytes by
         * the longest match routines.  Update the high water mark for the next
         * time through here.  WIN_INIT is set to MAX_MATCH since the longest match
         * routines allow scanning to strstart + MAX_MATCH, ignoring lookahead.
         */
        if (s->high_water < s->window_size)
        {
            ulong curr = s->strstart + (ulong)(s->lookahead);
            ulong init;

            if (s->high_water < curr)
            {
                /* Previous high water mark below current data -- zero WIN_INIT
                 * bytes or up to end of window, whichever is less.
                 */
                init = s->window_size - curr;
                if (init > WIN_INIT)
                    init = WIN_INIT;
                zmemzero(s->window + curr, (uint)init);
                s->high_water = curr + init;
            }
            else if (s->high_water < (ulong)curr + WIN_INIT)
            {
                /* High water mark at or above current data, but below current data
                 * plus WIN_INIT -- zero out to current data plus WIN_INIT, or up
                 * to end of window, whichever is less.
                 */
                init = (ulong)curr + WIN_INIT - s->high_water;
                if (init > s->window_size - s->high_water)
                    init = s->window_size - s->high_water;
                zmemzero(s->window + s->high_water, (uint)init);
                s->high_water += init;
            }
        }

        Assert((ulong)s->strstart <= s->window_size - MIN_LOOKAHEAD,
               "not enough room for search");
    }

    /* ========================================================================= */
    public static int deflateInit_(z_stream* strm, int level)
    {
        return deflateInit2_(strm, level, Z_DEFLATED, MAX_WBITS, DEF_MEM_LEVEL,
                             Z_DEFAULT_STRATEGY);
        /* To do: ignore strm->next_in if we use it as window */
    }

    /* ========================================================================= */
    public static int deflateInit2_(z_stream* strm, int level, int method,
                              int windowBits, int memLevel, int strategy)
    {
        deflate_state* s;
        int wrap = 1;

        if (strm == null) return Z_STREAM_ERROR;

        if (level == Z_DEFAULT_COMPRESSION) level = 6;

        if (windowBits < 0)
        { /* suppress zlib wrapper */
            wrap = 0;
            if (windowBits < -15)
                return Z_STREAM_ERROR;
            windowBits = -windowBits;
        }
        if (memLevel < 1 || memLevel > MAX_MEM_LEVEL || method != Z_DEFLATED ||
            windowBits < 8 || windowBits > 15 || level < 0 || level > 9 ||
            strategy < 0 || strategy > Z_FIXED || (windowBits == 8 && wrap != 1))
        {
            return Z_STREAM_ERROR;
        }
        if (windowBits == 8) windowBits = 9;  /* until 256-byte window bug fixed */
        s = (deflate_state*)ZALLOC(strm, 1, sizeof(deflate_state));
        if (s == null) return Z_MEM_ERROR;
        strm->state = (deflate_state*)s;
        s->strm = strm;
        s->status = INIT_STATE;     /* to pass state test in deflateReset() */

        s->wrap = wrap;
        s->gzhead = null;
        s->w_bits = (uint)windowBits;
        s->w_size = 1u << (int)s->w_bits;
        s->w_mask = s->w_size - 1;

        s->hash_bits = (uint)memLevel + 7;
        s->hash_size = 1u << (int)s->hash_bits;
        s->hash_mask = s->hash_size - 1;
        s->hash_shift = ((s->hash_bits + MIN_MATCH - 1) / MIN_MATCH);

        s->window = (byte*)ZALLOC(strm, s->w_size, 2 * sizeof(byte));
        s->prev = (ushort*)ZALLOC(strm, s->w_size, sizeof(ushort));
        s->head = (ushort*)ZALLOC(strm, s->hash_size, sizeof(ushort));

        s->high_water = 0;      /* nothing written to s->window yet */

        s->lit_bufsize = 1u << (int)(memLevel + 6); /* 16K elements by default */

        /* We overlay pending_buf and sym_buf. This works since the average size
         * for length/distance pairs over any compressed block is assured to be 31
         * bits or less.
         *
         * Analysis: The longest fixed codes are a length code of 8 bits plus 5
         * extra bits, for lengths 131 to 257. The longest fixed distance codes are
         * 5 bits plus 13 extra bits, for distances 16385 to 32768. The longest
         * possible fixed-codes length/distance pair is then 31 bits total.
         *
         * sym_buf starts one-fourth of the way into pending_buf. So there are
         * three bytes in sym_buf for every four bytes in pending_buf. Each symbol
         * in sym_buf is three bytes -- two for the distance and one for the
         * literal/length. As each symbol is consumed, the pointer to the next
         * sym_buf value to read moves forward three bytes. From that symbol, up to
         * 31 bits are written to pending_buf. The closest the written pending_buf
         * bits gets to the next sym_buf symbol to read is just before the last
         * code is written. At that time, 31*(n - 2) bits have been written, just
         * after 24*(n - 2) bits have been consumed from sym_buf. sym_buf starts at
         * 8*n bits into pending_buf. (Note that the symbol buffer fills when n - 1
         * symbols are written.) The closest the writing gets to what is unread is
         * then n + 14 bits. Here n is lit_bufsize, which is 16384 by default, and
         * can range from 128 to 32768.
         *
         * Therefore, at a minimum, there are 142 bits of space between what is
         * written and what is read in the overlain buffers, so the symbols cannot
         * be overwritten by the compressed data. That space is actually 139 bits,
         * due to the three-bit fixed-code block header.
         *
         * That covers the case where either Z_FIXED is specified, forcing fixed
         * codes, or when the use of fixed codes is chosen, because that choice
         * results in a smaller compressed block than dynamic codes. That latter
         * condition then assures that the above analysis also covers all dynamic
         * blocks. A dynamic-code block will only be chosen to be emitted if it has
         * fewer bits than a fixed-code block would for the same set of symbols.
         * Therefore its average symbol length is assured to be less than 31. So
         * the compressed data for a dynamic block also cannot overwrite the
         * symbols from which it is being constructed.
         */

        s->pending_buf = (byte*)ZALLOC(strm, s->lit_bufsize, LIT_BUFS);
        s->pending_buf_size = (ulong)s->lit_bufsize * 4;

        if (s->window == null || s->prev == null || s->head == null ||
            s->pending_buf == null)
        {
            s->status = FINISH_STATE;
            deflateEnd(strm);
            return Z_MEM_ERROR;
        }
        s->sym_buf = s->pending_buf + s->lit_bufsize;
        s->sym_end = (s->lit_bufsize - 1) * 3;
        /* We avoid equality with lit_bufsize*3 because of wraparound at 64K
         * on 16 bit machines and because stored blocks are restricted to
         * 64K-1 bytes.
         */

        s->level = level;
        s->strategy = strategy;
        s->method = (byte)method;

        return deflateReset(strm);
    }

    /* =========================================================================
     * Check for a valid deflate stream state. Return 0 if ok, 1 if not.
     */
    static bool deflateStateCheck(z_stream* strm)
    {
        deflate_state* s;
        if (strm == null) return true;
        s = strm->state;
        if (s == null || s->strm != strm || (s->status != INIT_STATE &&
                                               s->status != EXTRA_STATE &&
                                               s->status != NAME_STATE &&
                                               s->status != COMMENT_STATE &&
                                               s->status != HCRC_STATE &&
                                               s->status != BUSY_STATE &&
                                               s->status != FINISH_STATE))
            return true;
        return false;
    }

    /* ========================================================================= */
    static int deflateSetDictionary(z_stream* strm, byte* dictionary, uint dictLength)
    {
        deflate_state* s;
        uint str, n;
        int wrap;
        uint avail;
        byte* next;

        if (deflateStateCheck(strm) || dictionary == null)
            return Z_STREAM_ERROR;
        s = strm->state;
        wrap = s->wrap;
        if (wrap == 2 || (wrap == 1 && s->status != INIT_STATE) || s->lookahead != 0)
            return Z_STREAM_ERROR;

        /* when using zlib wrappers, compute Adler-32 for provided dictionary */
        if (wrap == 1)
            strm->adler = adler32(strm->adler, dictionary, dictLength);
        s->wrap = 0;                    /* avoid computing Adler-32 in read_buf */

        /* if dictionary would fill window, just replace the history */
        if (dictLength >= s->w_size)
        {
            if (wrap == 0)
            {            /* already empty otherwise */
                CLEAR_HASH(s);
                s->strstart = 0;
                s->block_start = 0L;
                s->insert = 0;
            }
            dictionary += dictLength - s->w_size;  /* use the tail */
            dictLength = s->w_size;
        }

        /* insert dictionary into window and hash */
        avail = strm->avail_in;
        next = strm->next_in;
        strm->avail_in = dictLength;
        strm->next_in = (byte*)dictionary;
        fill_window(s);
        while (s->lookahead >= MIN_MATCH)
        {
            str = s->strstart;
            n = s->lookahead - (MIN_MATCH - 1);
            do
            {
                UPDATE_HASH(s, ref s->ins_h, s->window[str + MIN_MATCH - 1]);
                s->prev[str & s->w_mask] = s->head[s->ins_h];
                s->head[s->ins_h] = (ushort)str;
                str++;
            } while ((--n) != 0);
            s->strstart = str;
            s->lookahead = MIN_MATCH - 1;
            fill_window(s);
        }
        s->strstart += s->lookahead;
        s->block_start = (long)s->strstart;
        s->insert = s->lookahead;
        s->lookahead = 0;
        s->match_length = s->prev_length = MIN_MATCH - 1;
        s->match_available = 0;
        strm->next_in = next;
        strm->avail_in = avail;
        s->wrap = wrap;
        return Z_OK;
    }

    /* ========================================================================= */
    static int deflateGetDictionary(z_stream* strm, byte* dictionary, uint* dictLength)
    {
        deflate_state* s;
        uint len;

        if (deflateStateCheck(strm))
            return Z_STREAM_ERROR;
        s = strm->state;
        len = s->strstart + s->lookahead;
        if (len > s->w_size)
            len = s->w_size;
        if (dictionary != null && len != 0)
            zmemcpy(dictionary, s->window + s->strstart + s->lookahead - len, len);
        if (dictLength != null)
            *dictLength = len;
        return Z_OK;
    }

    /* ========================================================================= */
    static int deflateResetKeep(z_stream* strm)
    {
        deflate_state* s;

        if (deflateStateCheck(strm))
        {
            return Z_STREAM_ERROR;
        }

        strm->total_in = strm->total_out = 0;
        strm->data_type = Z_UNKNOWN;

        s = (deflate_state*)strm->state;
        s->pending = 0;
        s->pending_out = s->pending_buf;

        if (s->wrap < 0)
        {
            s->wrap = -s->wrap; /* was made negative by deflate(..., Z_FINISH); */
        }
        s->status =
            INIT_STATE;
        strm->adler =
            adler32(0L, null, 0);
        s->last_flush = -2;

        _tr_init(s);

        return Z_OK;
    }

    /* ===========================================================================
     * Initialize the "longest match" routines for a new zlib stream
     */
    static void lm_init(deflate_state* s)
    {
        s->window_size = (ulong)2L * s->w_size;

        CLEAR_HASH(s);

        /* Set the default configuration parameters:
         */
        s->max_lazy_match = configuration_table[s->level].max_lazy;
        s->good_match = configuration_table[s->level].good_length;
        s->nice_match = configuration_table[s->level].nice_length;
        s->max_chain_length = configuration_table[s->level].max_chain;

        s->strstart = 0;
        s->block_start = 0L;
        s->lookahead = 0;
        s->insert = 0;
        s->match_length = s->prev_length = MIN_MATCH - 1;
        s->match_available = 0;
        s->ins_h = 0;
    }

    /* ========================================================================= */
    static int deflateReset(z_stream* strm)
    {
        int ret;

        ret = deflateResetKeep(strm);
        if (ret == Z_OK)
            lm_init(strm->state);
        return ret;
    }

    /* ========================================================================= */
    static int deflatePending(z_stream* strm, uint* pending, int* bits)
    {
        if (deflateStateCheck(strm)) return Z_STREAM_ERROR;
        if (pending != null)
            *pending = checked((uint)strm->state->pending); // JFB: zlib itself does cast ulong into uint here.
        if (bits != null)
            *bits = strm->state->bi_valid;
        return Z_OK;
    }

    /* ========================================================================= */
    static int deflatePrime(z_stream* strm, int bits, int value)
    {
        deflate_state* s;
        int put;

        if (deflateStateCheck(strm)) return Z_STREAM_ERROR;
        s = strm->state;
        if (bits < 0 || bits > 16 ||
            s->sym_buf < s->pending_out + ((Buf_size + 7) >> 3))
            return Z_BUF_ERROR;
        do
        {
            put = Buf_size - s->bi_valid;
            if (put > bits)
                put = bits;
            s->bi_buf |= (ushort)((value & ((1 << put) - 1)) << s->bi_valid);
            s->bi_valid += put;
            _tr_flush_bits(s);
            value >>= put;
            bits -= put;
        } while (bits != 0);
        return Z_OK;
    }

    /* ========================================================================= */
    static int deflateParams(z_stream* strm, int level, int strategy)
    {
        deflate_state* s;
        compress_func func;

        if (deflateStateCheck(strm)) return Z_STREAM_ERROR;
        s = strm->state;

        if (level == Z_DEFAULT_COMPRESSION) level = 6;
        if (level < 0 || level > 9 || strategy < 0 || strategy > Z_FIXED)
        {
            return Z_STREAM_ERROR;
        }
        func = configuration_table[s->level].func;

        if ((strategy != s->strategy || func != configuration_table[level].func) &&
            s->last_flush != -2)
        {
            /* Flush the last buffer: */
            int err = deflate(strm, Z_BLOCK);
            if (err == Z_STREAM_ERROR)
                return err;
            if (strm->avail_in != 0 || (s->strstart - s->block_start) + s->lookahead != 0)
                return Z_BUF_ERROR;
        }
        if (s->level != level)
        {
            if (s->level == 0 && s->matches != 0)
            {
                if (s->matches == 1)
                    slide_hash(s);
                else
                    CLEAR_HASH(s);
                s->matches = 0;
            }
            s->level = level;
            s->max_lazy_match = configuration_table[level].max_lazy;
            s->good_match = configuration_table[level].good_length;
            s->nice_match = configuration_table[level].nice_length;
            s->max_chain_length = configuration_table[level].max_chain;
        }
        s->strategy = strategy;
        return Z_OK;
    }

    /* ========================================================================= */
    static int deflateTune(z_stream* strm, int good_length, int max_lazy,
                            int nice_length, int max_chain)
    {
        deflate_state* s;

        if (deflateStateCheck(strm)) return Z_STREAM_ERROR;
        s = strm->state;
        s->good_match = (uint)good_length;
        s->max_lazy_match = (uint)max_lazy;
        s->nice_match = nice_length;
        s->max_chain_length = (uint)max_chain;
        return Z_OK;
    }

    /* =========================================================================
     * For the default windowBits of 15 and memLevel of 8, this function returns a
     * close to exact, as well as small, upper bound on the compressed size. This
     * is an expansion of ~0.03%, plus a small constant.
     *
     * For any setting other than those defaults for windowBits and memLevel, one
     * of two worst case bounds is returned. This is at most an expansion of ~4% or
     * ~13%, plus a small constant.
     *
     * Both the 0.03% and 4% derive from the overhead of stored blocks. The first
     * one is for stored blocks of 16383 bytes (memLevel == 8), whereas the second
     * is for stored blocks of 127 bytes (the worst case memLevel == 1). The
     * expansion results from five bytes of header for each stored block.
     *
     * The larger expansion of 13% results from a window size less than or equal to
     * the symbols buffer size (windowBits <= memLevel + 7). In that case some of
     * the data being compressed may have slid out of the sliding window, impeding
     * a stored block from being emitted. Then the only choice is a fixed or
     * dynamic block, where a fixed block limits the maximum expansion to 9 bits
     * per 8-bit byte, plus 10 bits for every block. The smallest block size for
     * which this can occur is 255 (memLevel == 2).
     *
     * Shifts are used to approximate divisions, for speed.
     */
    static ulong deflateBound(z_stream* strm, ulong sourceLen)
    {
        deflate_state* s;
        ulong fixedlen, storelen, wraplen;

        /* upper bound for fixed blocks with 9-bit literals and length 255
           (memLevel == 2, which is the lowest that may not use stored blocks) --
           ~13% overhead plus a small constant */
        fixedlen = sourceLen + (sourceLen >> 3) + (sourceLen >> 8) +
                   (sourceLen >> 9) + 4;

        /* upper bound for stored blocks with length 127 (memLevel == 1) --
           ~4% overhead plus a small constant */
        storelen = sourceLen + (sourceLen >> 5) + (sourceLen >> 7) +
                   (sourceLen >> 11) + 7;

        /* if can't get parameters, return larger bound plus a zlib wrapper */
        if (deflateStateCheck(strm))
            return (fixedlen > storelen ? fixedlen : storelen) + 6;

        /* compute wrapper length */
        s = strm->state;
        switch (s->wrap)
        {
            case 0:                                 /* raw deflate */
                wraplen = 0;
                break;
            case 1:                                 /* zlib wrapper */
                wraplen = 6u + (s->strstart != 0 ? 4u : 0);
                break;
            default:                                /* for compiler happiness */
                wraplen = 6;
                break;
        }

        /* if not default parameters, return one of the conservative bounds */
        if (s->w_bits != 15 || s->hash_bits != 8 + 7)
            return (s->w_bits <= s->hash_bits && s->level != 0 ? fixedlen : storelen) +
                   wraplen;

        /* default settings: return tight bound for that case -- ~0.03% overhead
           plus a small constant */
        return sourceLen + (sourceLen >> 12) + (sourceLen >> 14) +
               (sourceLen >> 25) + 13 - 6 + wraplen;
    }

    /* =========================================================================
     * Put a short in the pending buffer. The 16-bit value is put in MSB order.
     * IN assertion: the stream state is correct and there is enough room in
     * pending_buf.
     */
    static void putShortMSB(deflate_state* s, uint b)
    {
        put_byte(s, (byte)(b >> 8));
        put_byte(s, (byte)(b & 0xff));
    }

    /* =========================================================================
     * Flush as mbyte pending output as possible. All deflate() output, except for
     * some deflate_stored() output, goes through this function so some
     * applications may wish to modify it to avoid allocating a large
     * strm->next_out buffer and copying into it. (See also read_buf()).
     */
    static void flush_pending(z_stream* strm)
    {
        uint len;
        deflate_state* s = strm->state;

        _tr_flush_bits(s);
        len = checked((uint)s->pending);
        if (len > strm->avail_out) len = strm->avail_out;
        if (len == 0) return;

        zmemcpy(strm->next_out, s->pending_out, len);
        strm->next_out += len;
        s->pending_out += len;
        strm->total_out += len;
        strm->avail_out -= len;
        s->pending -= len;
        if (s->pending == 0)
        {
            s->pending_out = s->pending_buf;
        }
    }

    /* ========================================================================= */
    public static int deflate(z_stream* strm, int flush)
    {
        int old_flush; /* value of flush param for previous deflate call */
        deflate_state* s;

        if (deflateStateCheck(strm) || flush > Z_BLOCK || flush < 0)
        {
            return Z_STREAM_ERROR;
        }
        s = strm->state;

        if (strm->next_out == null ||
            (strm->avail_in != 0 && strm->next_in == null) ||
            (s->status == FINISH_STATE && flush != Z_FINISH))
        {
            return Z_STREAM_ERROR;
        }
        if (strm->avail_out == 0) return Z_BUF_ERROR;

        old_flush = s->last_flush;
        s->last_flush = flush;

        /* Flush as mbyte pending output as possible */
        if (s->pending != 0)
        {
            flush_pending(strm);
            if (strm->avail_out == 0)
            {
                /* Since avail_out is 0, deflate will be called again with
                 * more output space, but possibly with both pending and
                 * avail_in equal to zero. There won't be anything to do,
                 * but this is not an error situation so make sure we
                 * return OK instead of BUF_ERROR at next call of deflate:
                 */
                s->last_flush = -1;
                return Z_OK;
            }

            /* Make sure there is something to do and avoid duplicate consecutive
             * flushes. For repeated and useless calls with Z_FINISH, we keep
             * returning Z_STREAM_END instead of Z_BUF_ERROR.
             */
        }
        else if (strm->avail_in == 0 && RANK(flush) <= RANK(old_flush) &&
                 flush != Z_FINISH)
        {
            return Z_BUF_ERROR;
        }

        /* User must not provide more input after the first FINISH: */
        if (s->status == FINISH_STATE && strm->avail_in != 0)
        {
            return Z_BUF_ERROR;
        }

        /* Write the header */
        if (s->status == INIT_STATE && s->wrap == 0)
            s->status = BUSY_STATE;
        if (s->status == INIT_STATE)
        {
            /* zlib header */
            uint header = (Z_DEFLATED + ((s->w_bits - 8) << 4)) << 8;
            uint level_flags;

            if (s->strategy >= Z_HUFFMAN_ONLY || s->level < 2)
                level_flags = 0;
            else if (s->level < 6)
                level_flags = 1;
            else if (s->level == 6)
                level_flags = 2;
            else
                level_flags = 3;
            header |= (level_flags << 6);
            if (s->strstart != 0) header |= PRESET_DICT;
            header += 31 - (header % 31);

            putShortMSB(s, header);

            /* Save the adler32 of the preset dictionary: */
            if (s->strstart != 0)
            {
                putShortMSB(s, (uint)(strm->adler >> 16));
                putShortMSB(s, (uint)(strm->adler & 0xffff));
            }
            strm->adler = adler32(0L, null, 0);
            s->status = BUSY_STATE;

            /* Compression must start with an empty pending buffer */
            flush_pending(strm);
            if (s->pending != 0)
            {
                s->last_flush = -1;
                return Z_OK;
            }
        }

        /* Start a new block or continue the current one.
         */
        if (strm->avail_in != 0 || s->lookahead != 0 ||
            (flush != Z_NO_FLUSH && s->status != FINISH_STATE))
        {
            block_state bstate;

            bstate = s->level == 0 ? deflate_stored(s, flush) :
                     s->strategy == Z_HUFFMAN_ONLY ? deflate_huff(s, flush) :
                     s->strategy == Z_RLE ? deflate_rle(s, flush) :
                     configuration_table[s->level].func(s, flush);

            if (bstate == block_state.finish_started || bstate == block_state.finish_done)
            {
                s->status = FINISH_STATE;
            }
            if (bstate == block_state.need_more || bstate == block_state.finish_started)
            {
                if (strm->avail_out == 0)
                {
                    s->last_flush = -1; /* avoid BUF_ERROR next call, see above */
                }
                return Z_OK;
                /* If flush != Z_NO_FLUSH && avail_out == 0, the next call
                 * of deflate should use the same flush parameter to make sure
                 * that the flush is complete. So we don't have to output an
                 * empty block here, this will be done at next call. This also
                 * ensures that for a very small output buffer, we emit at most
                 * one empty block.
                 */
            }
            if (bstate == block_state.block_done)
            {
                if (flush == Z_PARTIAL_FLUSH)
                {
                    _tr_align(s);
                }
                else if (flush != Z_BLOCK)
                { /* FULL_FLUSH or SYNC_FLUSH */
                    _tr_stored_block(s, null, 0L, 0);
                    /* For a full flush, this empty block will be recognized
                     * as a special marker by inflate_sync().
                     */
                    if (flush == Z_FULL_FLUSH)
                    {
                        CLEAR_HASH(s);             /* forget history */
                        if (s->lookahead == 0)
                        {
                            s->strstart = 0;
                            s->block_start = 0L;
                            s->insert = 0;
                        }
                    }
                }
                flush_pending(strm);
                if (strm->avail_out == 0)
                {
                    s->last_flush = -1; /* avoid BUF_ERROR at next call, see above */
                    return Z_OK;
                }
            }
        }

        if (flush != Z_FINISH) return Z_OK;
        if (s->wrap <= 0) return Z_STREAM_END;

        /* Write the trailer */
        {
            putShortMSB(s, (uint)(strm->adler >> 16));
            putShortMSB(s, (uint)(strm->adler & 0xffff));
        }
        flush_pending(strm);
        /* If avail_out is zero, the application will call deflate again
         * to flush the rest.
         */
        if (s->wrap > 0) s->wrap = -s->wrap; /* write the trailer only once! */
        return s->pending != 0 ? Z_OK : Z_STREAM_END;
    }

    /* ========================================================================= */
    public static int deflateEnd(z_stream* strm)
    {
        int status;

        if (deflateStateCheck(strm)) return Z_STREAM_ERROR;

        status = strm->state->status;

        /* Deallocate in reverse order of allocations: */
        TRY_FREE(strm, strm->state->pending_buf);
        TRY_FREE(strm, strm->state->head);
        TRY_FREE(strm, strm->state->prev);
        TRY_FREE(strm, strm->state->window);

        ZFREE(strm, strm->state);
        strm->state = null;

        return status == BUSY_STATE ? Z_DATA_ERROR : Z_OK;
    }

    /* =========================================================================
     * Copy the source state to the destination state.
     * To simplify the source, this is not supported for 16-bit MSDOS (which
     * doesn't have enough memory anyway to duplicate compression states).
     */
    static int deflateCopy(z_stream* dest, z_stream* source)
    {
        deflate_state* ds;
        deflate_state* ss;


        if (deflateStateCheck(source) || dest == null)
        {
            return Z_STREAM_ERROR;
        }

        ss = source->state;

        *dest = *source;

        ds = (deflate_state*)ZALLOC(dest, 1, sizeof(deflate_state));
        if (ds == null) return Z_MEM_ERROR;
        dest->state = (deflate_state*)ds;
        *ds = *ss;
        ds->strm = dest;

        ds->window = (byte*)ZALLOC(dest, ds->w_size, 2 * sizeof(byte));
        ds->prev = (ushort*)ZALLOC(dest, ds->w_size, sizeof(ushort));
        ds->head = (ushort*)ZALLOC(dest, ds->hash_size, sizeof(ushort));
        ds->pending_buf = (byte*)ZALLOC(dest, ds->lit_bufsize, LIT_BUFS);

        if (ds->window == null || ds->prev == null || ds->head == null ||
            ds->pending_buf == null)
        {
            deflateEnd(dest);
            return Z_MEM_ERROR;
        }
        /* following zmemcpy do not work for 16-bit MSDOS */
        zmemcpy(ds->window, ss->window, ds->w_size * 2 * sizeof(byte));
        zmemcpy((byte*)ds->prev, (byte*)ss->prev, ds->w_size * sizeof(ushort));
        zmemcpy((byte*)ds->head, (byte*)ss->head, ds->hash_size * sizeof(ushort));
        zmemcpy(ds->pending_buf, ss->pending_buf, ds->lit_bufsize * LIT_BUFS);

        ds->pending_out = ds->pending_buf + (ss->pending_out - ss->pending_buf);
        ds->sym_buf = ds->pending_buf + ds->lit_bufsize;

        ds->l_desc.dyn_tree = ds->dyn_ltree;
        ds->d_desc.dyn_tree = ds->dyn_dtree;
        ds->bl_desc.dyn_tree = ds->bl_tree;

        return Z_OK;
    }

    /* ===========================================================================
     * Set match_start to the longest match starting at the given string and
     * return its length. Matches shorter or equal to prev_length are discarded,
     * in which case the result is equal to prev_length and match_start is
     * garbage.
     * IN assertions: cur_match is the head of the hash chain for the current
     *   string (strstart) and its distance is <= MAX_DIST, and prev_length >= 1
     * OUT assertion: the match length is not greater than s->lookahead.
     */
    static uint longest_match(deflate_state* s, uint cur_match)
    {
        uint chain_length = s->max_chain_length;/* max hash chain length */
        byte* scan = s->window + s->strstart; /* current string */
        byte* match;                      /* matched string */
        int len;                           /* length of current match */
        int best_len = (int)s->prev_length;         /* best match length so far */
        int nice_match = s->nice_match;             /* stop if match long enough */
        uint limit = s->strstart > (uint)MAX_DIST(s) ?
            s->strstart - (uint)MAX_DIST(s) : 0;
        /* Stop when cur_match becomes <= limit. To simplify the code,
         * we prevent matches with the string of window index 0.
         */
        ushort* prev = s->prev;
        uint wmask = s->w_mask;

        /* Compare two bytes at a time. Note: this is not always beneficial.
         * Try with and without -DUNALIGNED_OK to check.
         */
        byte* strend = s->window + s->strstart + MAX_MATCH - 1;
        ushort scan_start = *(ushort*)scan;
        ushort scan_end = *(ushort*)(scan + best_len - 1);

        /* The code is optimized for HASH_BITS >= 8 and MAX_MATCH-2 multiple of 16.
         * It is easy to get rid of this optimization if necessary.
         */
        Assert(s->hash_bits >= 8 && MAX_MATCH == 258, "Code too clever");

        /* Do not waste too mbyte time if we already have a good match: */
        if (s->prev_length >= s->good_match)
        {
            chain_length >>= 2;
        }
        /* Do not look for matches beyond the end of the input. This is necessary
         * to make deflate deterministic.
         */
        if ((uint)nice_match > s->lookahead) nice_match = (int)s->lookahead;

        Assert((ulong)s->strstart <= s->window_size - MIN_LOOKAHEAD,
               "need lookahead");

        do
        {
            Assert(cur_match < s->strstart, "no future");
            match = s->window + cur_match;

            /* Skip to next match if the match length cannot increase
             * or if the match length is less than 2.  Note that the checks below
             * for insufficient lookahead only occur occasionally for performance
             * reasons.  Therefore uninitialized memory will be accessed, and
             * conditional jumps will be made that depend on those values.
             * However the length of the match is limited to the lookahead, so
             * the output of deflate is not affected by the uninitialized values.
             */

            /* This code assumes sizeof(unsigned short) == 2. Do not use
             * UNALIGNED_OK if your compiler uses a different size.
             */
            if (*(ushort*)(match + best_len - 1) != scan_end ||
                *(ushort*)match != scan_start) continue;

            /* It is not necessary to compare scan[2] and match[2] since they are
             * always equal when the other bytes match, given that the hash keys
             * are equal and that HASH_BITS >= 8. Compare 2 bytes at a time at
             * strstart + 3, + 5, up to strstart + 257. We check for insufficient
             * lookahead only every 4th comparison; the 128th check will be made
             * at strstart + 257. If MAX_MATCH-2 is not a multiple of 8, it is
             * necessary to put more guard bytes at the end of the window, or
             * to check more often for insufficient lookahead.
             */
            Assert(scan[2] == match[2], "scan[2]?");
            scan++; match++;
            do
            {
            } while (*(ushort*)(scan += 2) == *(ushort*)(match += 2) &&
                     *(ushort*)(scan += 2) == *(ushort*)(match += 2) &&
                     *(ushort*)(scan += 2) == *(ushort*)(match += 2) &&
                     *(ushort*)(scan += 2) == *(ushort*)(match += 2) &&
                     scan < strend);
            /* The funny "do {}" generates better code on most compilers */

            /* Here, scan <= window + strstart + 257 */
            Assert(scan <= s->window + (uint)(s->window_size - 1),
                   "wild scan");
            if (*scan == *match) scan++;

            len = (MAX_MATCH - 1) - (int)(strend - scan);
            scan = strend - (MAX_MATCH - 1);

            if (len > best_len)
            {
                s->match_start = cur_match;
                best_len = len;
                if (len >= nice_match) break;
                scan_end = *(ushort*)(scan + best_len - 1);
            }
        } while ((cur_match = prev[cur_match & wmask]) > limit
                 && --chain_length != 0);

        if ((uint)best_len <= s->lookahead) return (uint)best_len;
        return s->lookahead;
    }

    /* ===========================================================================
     * Flush the current block, with given end-of-file flag.
     * IN assertion: strstart is set to the end of the current match.
     */
    static void FLUSH_BLOCK_ONLY(deflate_state* s, int last)
    {
        _tr_flush_block(s, (s->block_start >= 0L ?
                        (byte*)&s->window[(uint)s->block_start] :
                        (byte*)null),
                     (ulong)((long)s->strstart - s->block_start),
                     (last));
        s->block_start = s->strstart;
        flush_pending(s->strm);
    }

    /* Same but force premature exit if necessary. */
    // This returns true if you should do 'return retval;'.
    static bool FLUSH_BLOCK(deflate_state* s, int last, out block_state retval)
    {
        FLUSH_BLOCK_ONLY(s, last);
        if (s->strm->avail_out == 0) { retval = (last != 0) ? block_state.finish_started : block_state.need_more; return true; }
        retval = 0; return false;
    }

    /* Maximum stored block length in deflate format (not including header). */
    const ushort MAX_STORED = 65535;

    /* Minimum of a and b. */
    static uint MIN(uint a, uint b) { return ((a) > (b) ? (b) : (a)); }

    /* ===========================================================================
     * Copy without compression as mbyte as possible from the input stream, return
     * the current block state.
     *
     * In case deflateParams() is used to later switch to a non-zero compression
     * level, s->matches (otherwise unused when storing) keeps track of the number
     * of hash table slides to perform. If s->matches is 1, then one hash table
     * slide will be done when switching. If s->matches is 2, the maximum value
     * allowed here, then the hash table will be cleared, since two or more slides
     * is the same as a clear.
     *
     * deflate_stored() is written to minimize the number of times an input byte is
     * copied. It is most efficient with large input and output buffers, which
     * maximizes the opportunities to have a single copy from next_in to next_out.
     */
    static block_state deflate_stored(deflate_state* s, int flush)
    {
        /* Smallest worthy block size when not flushing or finishing. By default
         * this is 32K. This can be as small as 507 bytes for memLevel == 1. For
         * large input and output buffers, the stored block size will be larger.
         */
        uint min_block = MIN(checked((uint)s->pending_buf_size - 5), s->w_size);

        /* Copy as many min_block or larger stored blocks directly to next_out as
         * possible. If flushing, copy the remaining available input to next_out as
         * stored blocks, if there is enough space.
         */
        uint len, left, have, last = 0;
        uint used = s->strm->avail_in;
        do
        {
            /* Set len to the maximum size block that we can copy directly with the
             * available input data and output space. Set left to how mbyte of that
             * would be copied from what's left in the window.
             */
            len = MAX_STORED;       /* maximum deflate stored block length */
            have = checked((uint)((s->bi_valid + 42) >> 3));         /* number of header bytes */
            if (s->strm->avail_out < have)          /* need room for header */
                break;
            /* maximum stored block length that will fit in avail_out: */
            have = s->strm->avail_out - have;
            left = checked((uint)(s->strstart - s->block_start));    /* bytes left in window */
            if (len > (ulong)left + s->strm->avail_in)
                len = left + s->strm->avail_in;     /* limit len to the input */
            if (len > have)
                len = have;                         /* limit len to the output */

            /* If the stored block would be less than min_block in length, or if
             * unable to copy all of the available input when flushing, then try
             * copying to the window and the pending buffer instead. Also don't
             * write an empty block when flushing -- deflate() does that.
             */
            if (len < min_block && ((len == 0 && flush != Z_FINISH) ||
                                    flush == Z_NO_FLUSH ||
                                    len != left + s->strm->avail_in))
                break;

            /* Make a dummy stored block in pending to get the header bytes,
             * including any pending bits. This also updates the debugging counts.
             */
            last = flush == Z_FINISH && len == left + s->strm->avail_in ? 1u : 0;
            _tr_stored_block(s, null, 0L, (int)last);

            /* Replace the lengths in the dummy stored block with len. */
            s->pending_buf[s->pending - 4] = (byte)len;
            s->pending_buf[s->pending - 3] = (byte)(len >> 8);
            s->pending_buf[s->pending - 2] = (byte)~len;
            s->pending_buf[s->pending - 1] = (byte)(~len >> 8);

            /* Write the stored block header bytes. */
            flush_pending(s->strm);

            /* Copy uncompressed bytes from the window to next_out. */
            if (left != 0)
            {
                if (left > len)
                    left = len;
                zmemcpy(s->strm->next_out, s->window + s->block_start, left);
                s->strm->next_out += left;
                s->strm->avail_out -= left;
                s->strm->total_out += left;
                s->block_start += left;
                len -= left;
            }

            /* Copy uncompressed bytes directly from next_in to next_out, updating
             * the check value.
             */
            if (len != 0)
            {
                read_buf(s->strm, s->strm->next_out, len);
                s->strm->next_out += len;
                s->strm->avail_out -= len;
                s->strm->total_out += len;
            }
        } while (last == 0);

        /* Update the sliding window with the last s->w_size bytes of the copied
         * data, or append all of the copied data to the existing window if less
         * than s->w_size bytes were copied. Also update the number of bytes to
         * insert in the hash tables, in the event that deflateParams() switches to
         * a non-zero compression level.
         */
        used -= s->strm->avail_in;      /* number of input bytes directly copied */
        if (used != 0)
        {
            /* If any input was used, then no unused input remains in the window,
             * therefore s->block_start == s->strstart.
             */
            if (used >= s->w_size)
            {    /* supplant the previous history */
                s->matches = 2;         /* clear hash */
                zmemcpy(s->window, s->strm->next_in - s->w_size, s->w_size);
                s->strstart = s->w_size;
                s->insert = s->strstart;
            }
            else
            {
                if (s->window_size - s->strstart <= used)
                {
                    /* Slide the window down. */
                    s->strstart -= s->w_size;
                    zmemcpy(s->window, s->window + s->w_size, s->strstart);
                    if (s->matches < 2)
                        s->matches++;   /* add a pending slide_hash() */
                    if (s->insert > s->strstart)
                        s->insert = s->strstart;
                }
                zmemcpy(s->window + s->strstart, s->strm->next_in - used, used);
                s->strstart += used;
                s->insert += MIN(used, s->w_size - s->insert);
            }
            s->block_start = s->strstart;
        }
        if (s->high_water < s->strstart)
            s->high_water = s->strstart;

        /* If the last block was written to next_out, then done. */
        if (last != 0)
            return block_state.finish_done;

        /* If flushing and all input has been consumed, then done. */
        if (flush != Z_NO_FLUSH && flush != Z_FINISH &&
            s->strm->avail_in == 0 && (long)s->strstart == s->block_start)
            return block_state.block_done;

        /* Fill the window with any remaining input. */
        have = checked((uint)(s->window_size - s->strstart));
        if (s->strm->avail_in > have && s->block_start >= (long)s->w_size)
        {
            /* Slide the window down. */
            s->block_start -= s->w_size;
            s->strstart -= s->w_size;
            zmemcpy(s->window, s->window + s->w_size, s->strstart);
            if (s->matches < 2)
                s->matches++;           /* add a pending slide_hash() */
            have += s->w_size;          /* more space now */
            if (s->insert > s->strstart)
                s->insert = s->strstart;
        }
        if (have > s->strm->avail_in)
            have = s->strm->avail_in;
        if (have != 0)
        {
            read_buf(s->strm, s->window + s->strstart, have);
            s->strstart += have;
            s->insert += MIN(have, s->w_size - s->insert);
        }
        if (s->high_water < s->strstart)
            s->high_water = s->strstart;

        /* There was not enough avail_out to write a complete worthy or flushed
         * stored block to next_out. Write a stored block to pending instead, if we
         * have enough input for a worthy block, or if flushing and there is enough
         * room for the remaining input as a stored block in the pending buffer.
         */
        have = (uint)((s->bi_valid + 42) >> 3);         /* number of header bytes */
        /* maximum stored block length that will fit in pending: */
        have = MIN(checked((uint)s->pending_buf_size - have), MAX_STORED);
        min_block = MIN(have, s->w_size);
        left = checked((uint)(s->strstart - s->block_start));
        if (left >= min_block ||
            ((left != 0 || flush == Z_FINISH) && flush != Z_NO_FLUSH &&
             s->strm->avail_in == 0 && left <= have))
        {
            len = MIN(left, have);
            last = flush == Z_FINISH && s->strm->avail_in == 0 &&
                   len == left ? 1u : 0;
            _tr_stored_block(s, (byte*)s->window + s->block_start, len, (int)last);
            s->block_start += len;
            flush_pending(s->strm);
        }

        /* We've done all we can with the available input and output. */
        return last != 0 ? block_state.finish_started : block_state.need_more;
    }

    /* ===========================================================================
     * Compress as mbyte as possible from the input stream, return the current
     * block state.
     * This function does not perform lazy evaluation of matches and inserts
     * new strings in the dictionary only for unmatched strings or for short
     * matches. It is used only for the fast compression options.
     */
    static block_state deflate_fast(deflate_state* s, int flush)
    {
        block_state retval;

        uint hash_head;       /* head of the hash chain */
        int bflush;           /* set if current block must be flushed */

        for (; ; )
        {
            /* Make sure that we always have enough lookahead, except
             * at the end of the input file. We need MAX_MATCH bytes
             * for the next match, plus MIN_MATCH bytes to insert the
             * string following the next match.
             */
            if (s->lookahead < MIN_LOOKAHEAD)
            {
                fill_window(s);
                if (s->lookahead < MIN_LOOKAHEAD && flush == Z_NO_FLUSH)
                {
                    return block_state.need_more;
                }
                if (s->lookahead == 0) break; /* flush the current block */
            }

            /* Insert the string window[strstart .. strstart + 2] in the
             * dictionary, and set hash_head to the head of the hash chain:
             */
            hash_head = 0;
            if (s->lookahead >= MIN_MATCH)
            {
                INSERT_STRING(s, s->strstart, ref hash_head);
            }

            /* Find the longest match, discarding those <= prev_length.
             * At this point we have always match_length < MIN_MATCH
             */
            if (hash_head != 0 && s->strstart - hash_head <= MAX_DIST(s))
            {
                /* To simplify the code, we prevent matches with the string
                 * of window index 0 (in particular we have to avoid a match
                 * of the string with itself at the start of the input file).
                 */
                s->match_length = longest_match(s, hash_head);
                /* longest_match() sets match_start */
            }
            if (s->match_length >= MIN_MATCH)
            {
                _tr_tally_dist(s, s->strstart - s->match_start,
                               s->match_length - MIN_MATCH, out bflush);

                s->lookahead -= s->match_length;

                /* Insert new strings in the hash table only if the match length
                 * is not too large. This saves time but degrades compression.
                 */
                if (s->match_length <= s->max_lazy_match &&
                    s->lookahead >= MIN_MATCH)
                {
                    s->match_length--; /* string at strstart already in table */
                    do
                    {
                        s->strstart++;
                        INSERT_STRING(s, s->strstart, ref hash_head);
                        /* strstart never exceeds WSIZE-MAX_MATCH, so there are
                         * always MIN_MATCH bytes ahead.
                         */
                    } while (--s->match_length != 0);
                    s->strstart++;
                }
                else
                {
                    s->strstart += s->match_length;
                    s->match_length = 0;
                    s->ins_h = s->window[s->strstart];
                    UPDATE_HASH(s, ref s->ins_h, s->window[s->strstart + 1]);
                    /* If lookahead < MIN_MATCH, ins_h is garbage, but it does not
                     * matter since it will be recomputed at next deflate call.
                     */
                }
            }
            else
            {
                /* No match, output a literal byte */
                _tr_tally_lit(s, s->window[s->strstart], out bflush);
                s->lookahead--;
                s->strstart++;
            }
            if (bflush != 0)
            {
                if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
            }
        }
        s->insert = s->strstart < MIN_MATCH - 1 ? s->strstart : MIN_MATCH - 1;
        if (flush == Z_FINISH)
        {
            if (FLUSH_BLOCK(s, 1, out retval)) { return retval; }
            return block_state.finish_done;
        }
        if (s->sym_next != 0)
        {
            if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
        }
        return block_state.block_done;
    }

    /* ===========================================================================
     * Same as above, but achieves better compression. We use a lazy
     * evaluation for matches: a match is finally adopted only if there is
     * no better match at the next window position.
     */
    static block_state deflate_slow(deflate_state* s, int flush)
    {
        block_state retval;

        uint hash_head;          /* head of hash chain */
        int bflush;              /* set if current block must be flushed */

        /* Process the input block. */
        for (; ; )
        {
            /* Make sure that we always have enough lookahead, except
             * at the end of the input file. We need MAX_MATCH bytes
             * for the next match, plus MIN_MATCH bytes to insert the
             * string following the next match.
             */
            if (s->lookahead < MIN_LOOKAHEAD)
            {
                fill_window(s);
                if (s->lookahead < MIN_LOOKAHEAD && flush == Z_NO_FLUSH)
                {
                    return block_state.need_more;
                }
                if (s->lookahead == 0) break; /* flush the current block */
            }

            /* Insert the string window[strstart .. strstart + 2] in the
             * dictionary, and set hash_head to the head of the hash chain:
             */
            hash_head = 0;
            if (s->lookahead >= MIN_MATCH)
            {
                INSERT_STRING(s, s->strstart, ref hash_head);
            }

            /* Find the longest match, discarding those <= prev_length.
             */
            s->prev_length = s->match_length; s->prev_match = s->match_start;
            s->match_length = MIN_MATCH - 1;

            if (hash_head != 0 && s->prev_length < s->max_lazy_match &&
                s->strstart - hash_head <= MAX_DIST(s))
            {
                /* To simplify the code, we prevent matches with the string
                 * of window index 0 (in particular we have to avoid a match
                 * of the string with itself at the start of the input file).
                 */
                s->match_length = longest_match(s, hash_head);
                /* longest_match() sets match_start */

                if (s->match_length <= 5 && (s->strategy == Z_FILTERED
                    || (s->match_length == MIN_MATCH &&
                        s->strstart - s->match_start > TOO_FAR)
                    ))
                {

                    /* If prev_match is also MIN_MATCH, match_start is garbage
                     * but we will ignore the current match anyway.
                     */
                    s->match_length = MIN_MATCH - 1;
                }
            }
            /* If there was a match at the previous step and the current
             * match is not better, output the previous match:
             */
            if (s->prev_length >= MIN_MATCH && s->match_length <= s->prev_length)
            {
                uint max_insert = s->strstart + s->lookahead - MIN_MATCH;
                /* Do not insert strings in hash table beyond this. */

                _tr_tally_dist(s, s->strstart - 1 - s->prev_match,
                               s->prev_length - MIN_MATCH, out bflush);

                /* Insert in hash table all strings up to the end of the match.
                 * strstart - 1 and strstart are already inserted. If there is not
                 * enough lookahead, the last two strings are not inserted in
                 * the hash table.
                 */
                s->lookahead -= s->prev_length - 1;
                s->prev_length -= 2;
                do
                {
                    if (++s->strstart <= max_insert)
                    {
                        INSERT_STRING(s, s->strstart, ref hash_head);
                    }
                } while (--s->prev_length != 0);
                s->match_available = 0;
                s->match_length = MIN_MATCH - 1;
                s->strstart++;

                if (bflush != 0)
                {
                    if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
                }

            }
            else if (s->match_available != 0)
            {
                /* If there was no match at the previous position, output a
                 * single literal. If there was a match but the current match
                 * is longer, truncate the previous match to a single literal.
                 */
                _tr_tally_lit(s, s->window[s->strstart - 1], out bflush);
                if (bflush != 0)
                {
                    FLUSH_BLOCK_ONLY(s, 0);
                }
                s->strstart++;
                s->lookahead--;
                if (s->strm->avail_out == 0) return block_state.need_more;
            }
            else
            {
                /* There is no previous match to compare with, wait for
                 * the next step to decide.
                 */
                s->match_available = 1;
                s->strstart++;
                s->lookahead--;
            }
        }
        Assert(flush != Z_NO_FLUSH, "no flush?");
        if (s->match_available != 0)
        {
            _tr_tally_lit(s, s->window[s->strstart - 1], out bflush);
            s->match_available = 0;
        }
        s->insert = s->strstart < MIN_MATCH - 1 ? s->strstart : MIN_MATCH - 1;
        if (flush == Z_FINISH)
        {
            if (FLUSH_BLOCK(s, 1, out retval)) { return retval; }
            return block_state.finish_done;
        }
        if (s->sym_next != 0)
        {
            if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
        }
        return block_state.block_done;
    }

    /* ===========================================================================
     * For Z_RLE, simply look for runs of bytes, generate matches only of distance
     * one.  Do not maintain a hash table.  (It will be regenerated if this run of
     * deflate switches away from Z_RLE.)
     */
    static block_state deflate_rle(deflate_state* s, int flush)
    {
        block_state retval;

        int bflush;             /* set if current block must be flushed */
        uint prev;              /* byte at distance one to match */
        byte* scan, strend;   /* scan goes up to strend for length of run */

        for (; ; )
        {
            /* Make sure that we always have enough lookahead, except
             * at the end of the input file. We need MAX_MATCH bytes
             * for the longest run, plus one for the unrolled loop.
             */
            if (s->lookahead <= MAX_MATCH)
            {
                fill_window(s);
                if (s->lookahead <= MAX_MATCH && flush == Z_NO_FLUSH)
                {
                    return block_state.need_more;
                }
                if (s->lookahead == 0) break; /* flush the current block */
            }

            /* See how many times the previous byte repeats */
            s->match_length = 0;
            if (s->lookahead >= MIN_MATCH && s->strstart > 0)
            {
                scan = s->window + s->strstart - 1;
                prev = *scan;
                if (prev == *++scan && prev == *++scan && prev == *++scan)
                {
                    strend = s->window + s->strstart + MAX_MATCH;
                    do
                    {
                    } while (prev == *++scan && prev == *++scan &&
                             prev == *++scan && prev == *++scan &&
                             prev == *++scan && prev == *++scan &&
                             prev == *++scan && prev == *++scan &&
                             scan < strend);
                    s->match_length = MAX_MATCH - (uint)(strend - scan);
                    if (s->match_length > s->lookahead)
                        s->match_length = s->lookahead;
                }
                Assert(scan <= s->window + (uint)(s->window_size - 1),
                       "wild scan");
            }

            /* Emit match if have run of MIN_MATCH or longer, else emit literal */
            if (s->match_length >= MIN_MATCH)
            {
                _tr_tally_dist(s, 1, s->match_length - MIN_MATCH, out bflush);

                s->lookahead -= s->match_length;
                s->strstart += s->match_length;
                s->match_length = 0;
            }
            else
            {
                /* No match, output a literal byte */
                _tr_tally_lit(s, s->window[s->strstart], out bflush);
                s->lookahead--;
                s->strstart++;
            }
            if (bflush != 0)
            {
                if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
            }
        }
        s->insert = 0;
        if (flush == Z_FINISH)
        {
            if (FLUSH_BLOCK(s, 1, out retval)) { return retval; }
            return block_state.finish_done;
        }
        if (s->sym_next != 0)
        {
            if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
        }
        return block_state.block_done;
    }

    /* ===========================================================================
     * For Z_HUFFMAN_ONLY, do not look for matches.  Do not maintain a hash table.
     * (It will be regenerated if this run of deflate switches away from Huffman.)
     */
    static block_state deflate_huff(deflate_state* s, int flush)
    {
        block_state retval;

        int bflush;             /* set if current block must be flushed */

        for (; ; )
        {
            /* Make sure that we have a literal to write. */
            if (s->lookahead == 0)
            {
                fill_window(s);
                if (s->lookahead == 0)
                {
                    if (flush == Z_NO_FLUSH)
                        return block_state.need_more;
                    break;      /* flush the current block */
                }
            }

            /* Output a literal byte */
            s->match_length = 0;
            _tr_tally_lit(s, s->window[s->strstart], out bflush);
            s->lookahead--;
            s->strstart++;
            if (bflush != 0)
            {
                if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
            }
        }
        s->insert = 0;
        if (flush == Z_FINISH)
        {
            if (FLUSH_BLOCK(s, 1, out retval)) { return retval; }
            return block_state.finish_done;
        }
        if (s->sym_next != 0)
        {
            if (FLUSH_BLOCK(s, 0, out retval)) { return retval; }
        }
        return block_state.block_done;
    }
}
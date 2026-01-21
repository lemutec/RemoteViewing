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

namespace RemoteViewing.Utility
{
    unsafe static partial class ZLib
    {
        const uint BASE = 65521U; /* largest prime smaller than 65536 */
        const uint NMAX = 5552;
        /* NMAX is the largest n such that 255n(n+1)/2 + (n+1)(BASE-1) <= 2^32-1 */

        static void DO16(ref ulong adler, ref ulong sum2, byte* buf)
        {
            for (int i = 0; i < 16; i++) { adler += buf[i]; sum2 += adler; }
        }

        static void MOD(ref ulong u) { u %= BASE; }
        static void MOD28(ref ulong u) { u %= BASE; }
        static void MOD63(ref ulong u) { u %= BASE; }

        /* ========================================================================= */
        static ulong adler32_z(ulong adler, byte* buf, ulong len)
        {
            ulong sum2;
            uint n;

            /* split Adler-32 into component sums */
            sum2 = (adler >> 16) & 0xffff;
            adler &= 0xffff;

            /* in case user likes doing a byte at a time, keep it fast */
            if (len == 1)
            {
                adler += buf[0];
                if (adler >= BASE)
                    adler -= BASE;
                sum2 += adler;
                if (sum2 >= BASE)
                    sum2 -= BASE;
                return adler | (sum2 << 16);
            }

            /* initial Adler-32 value (deferred check for len == 1 speed) */
            if (buf == null)
                return 1L;

            /* in case short lengths are provided, keep it somewhat fast */
            if (len < 16)
            {
                while (len-- != 0)
                {
                    adler += *buf++;
                    sum2 += adler;
                }
                if (adler >= BASE)
                    adler -= BASE;
                MOD28(ref sum2);            /* only added so many BASE's */
                return adler | (sum2 << 16);
            }

            /* do length NMAX blocks -- requires just one modulo operation */
            while (len >= NMAX)
            {
                len -= NMAX;
                n = NMAX / 16;          /* NMAX is divisible by 16 */
                do
                {
                    DO16(ref adler, ref sum2, buf);          /* 16 sums unrolled */
                    buf += 16;
                } while (--n != 0);
                MOD(ref adler);
                MOD(ref sum2);
            }

            /* do remaining bytes (less than NMAX, still just one modulo) */
            if (len != 0)
            {                  /* avoid modulos if none remaining */
                while (len >= 16)
                {
                    len -= 16;
                    DO16(ref adler, ref sum2, buf);
                    buf += 16;
                }
                while (len-- != 0)
                {
                    adler += *buf++;
                    sum2 += adler;
                }
                MOD(ref adler);
                MOD(ref sum2);
            }

            /* return recombined sums */
            return adler | (sum2 << 16);
        }

        /* ========================================================================= */
        static ulong adler32(ulong adler, byte* buf, uint len)
        {
            return adler32_z(adler, buf, len);
        }

        /* ========================================================================= */
        static ulong adler32_combine_(ulong adler1, ulong adler2, ulong len2)
        {
            ulong sum1;
            ulong sum2;
            ulong rem;

            /* for negative len, return invalid adler32 as a clue for debugging */
            if (len2 < 0)
                return 0xffffffffUL;

            /* the derivation of this formula is left as an exercise for the reader */
            MOD63(ref len2);                /* assumes len2 >= 0 */
            rem = (uint)len2;
            sum1 = adler1 & 0xffff;
            sum2 = rem * sum1;
            MOD(ref sum2);
            sum1 += (adler2 & 0xffff) + BASE - 1;
            sum2 += ((adler1 >> 16) & 0xffff) + ((adler2 >> 16) & 0xffff) + BASE - rem;
            if (sum1 >= BASE) sum1 -= BASE;
            if (sum1 >= BASE) sum1 -= BASE;
            if (sum2 >= ((ulong)BASE << 1)) sum2 -= ((ulong)BASE << 1);
            if (sum2 >= BASE) sum2 -= BASE;
            return sum1 | (sum2 << 16);
        }
    }
}
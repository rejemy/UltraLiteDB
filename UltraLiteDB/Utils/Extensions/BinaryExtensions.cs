using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Lexicographic byte array comparison extensions, used for binary BSON value ordering.
    /// </summary>
    internal static class BinaryExtensions
    {
        /// <summary>
        /// Lexicographically compare two byte arrays. Returns -1, 0, or 1.
        /// </summary>
        public static int BinaryCompareTo(this byte[] lh, byte[] rh)
        {
            if (lh == null) return rh == null ? 0 : -1;
            if (rh == null) return 1;

            var result = 0;
            var i = 0;
            var stop = Math.Min(lh.Length, rh.Length);

            for (; 0 == result && i < stop; i++)
                result = lh[i].CompareTo(rh[i]);

            if (result != 0) return result < 0 ? -1 : 1;
            if (i == lh.Length) return i == rh.Length ? 0 : -1;
            return 1;
        }

        /// <summary>
        /// Lexicographically compare two ArraySegment byte spans. Returns -1, 0, or 1.
        /// </summary>
        public static int BinaryCompareTo(this ArraySegment<byte> lh, ArraySegment<byte> rh)
        {
            if (lh.Array == null) return rh.Array == null ? 0 : -1;
            if (rh.Array == null) return 1;

            var result = 0;
            var i = 0;
            var stop = Math.Min(lh.Count, rh.Count);

            for (; 0 == result && i < stop; i++)
                result = lh[i].CompareTo(rh[i]);

            if (result != 0) return result < 0 ? -1 : 1;
            if (i == lh.Count) return i == rh.Count ? 0 : -1;
            return 1;
        }
    }
}
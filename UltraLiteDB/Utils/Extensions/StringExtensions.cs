using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Convenience string extension methods for null/empty/whitespace checks.
    /// </summary>
    internal static class StringExtensions
    {
        public static bool IsNullOrWhiteSpace(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        public static bool IsNullOrEmpty(this string str)
        {
            return str == null || str.Length == 0;
        }


    }
}
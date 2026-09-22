using System;
using System.Text;
using System.Threading;

namespace UltraLiteDB
{
	/// <summary>
	/// Maps the UTF-8 bytes of a BSON field name to a shared string, so reading many documents with the same
	/// keys (a collection's documents) doesn't decode and allocate a new string for every field.
	/// </summary>
	/// <remarks>
	/// A small direct-mapped cache: each slot holds the last key that hashed to it, so a miss just decodes and
	/// replaces that slot. Lock-free and safe to share between threads: entries are immutable and published
	/// with a release write, and a race can only cost a miss.
	/// </remarks>
	internal static class BsonKeyCache
	{
		private const int SLOTS = 256; // power of two
		private const int MAX_KEY_BYTES = 64;

		private sealed class Entry
		{
			public readonly byte[] Utf8;
			public readonly string Key;

			public Entry(byte[] utf8, string key)
			{
				this.Utf8 = utf8;
				this.Key = key;
			}
		}

		private static readonly Entry?[] _entries = new Entry?[SLOTS];

		/// <summary>
		/// Returns the key for the <paramref name="length"/> UTF-8 bytes of <paramref name="buffer"/> at <paramref name="start"/>.
		/// </summary>
		public static string Get(byte[] buffer, int start, int length)
		{
			if (length > MAX_KEY_BYTES) return Encoding.UTF8.GetString(buffer, start, length);

			// FNV-1a
			var hash = 2166136261u;
			for (var i = 0; i < length; i++)
			{
				hash = (hash ^ buffer[start + i]) * 16777619u;
			}

			var slot = (int)((hash ^ (hash >> 16)) & (SLOTS - 1));
			var entry = Volatile.Read(ref _entries[slot]);

			if (entry != null && entry.Utf8.Length == length)
			{
				var utf8 = entry.Utf8;
				var j = 0;
				while (j < length && utf8[j] == buffer[start + j]) j++;
				if (j == length) return entry.Key;
			}

			var key = Encoding.UTF8.GetString(buffer, start, length);
			var bytes = new byte[length];
			System.Buffer.BlockCopy(buffer, start, bytes, 0, length);
			Volatile.Write(ref _entries[slot], new Entry(bytes, key));

			return key;
		}
	}
}

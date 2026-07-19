using System;

namespace UltraLiteDB
{
	/// <summary>
	/// Writes primitive and extended data types sequentially to a destination in little-endian format.
	/// Implemented by both the byte-array-backed <see cref="ByteWriter"/> and the stream-backed
	/// <see cref="StreamByteWriter"/>, allowing the BSON layer to write to either without change.
	/// </summary>
	public interface IByteWriter
	{
		/// <summary>
		/// Gets or sets the current write position. Setting the position is used by the direct
		/// (POCO) serializer to backfill document/array length prefixes, and therefore requires a
		/// seekable destination.
		/// </summary>
		int Position { get; set; }

		/// <summary>
		/// Ensures the destination has room for the specified number of additional bytes.
		/// May be a no-op for destinations that grow automatically (e.g. streams).
		/// </summary>
		void EnsureCapacity(int additionalBytes);

		/// <summary>
		/// Advances the write position by the specified number of bytes, reserving space.
		/// </summary>
		void Skip(int length);

		void Write(byte value);

		void Write(int value);

		void Write(long value);

		void Write(double value);

		void Write(decimal value);

		void Write(byte[] value);

		void Write(ArraySegment<byte> value);
	}
}

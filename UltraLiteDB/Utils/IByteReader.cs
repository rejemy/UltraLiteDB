using System;

namespace UltraLiteDB
{
	/// <summary>
	/// Reads primitive and extended data types sequentially from a source in little-endian format.
	/// Implemented by both the byte-array-backed <see cref="ByteReader"/> and the stream-backed
	/// <see cref="StreamByteReader"/>, allowing the BSON layer to read from either without change.
	/// </summary>
	public interface IByteReader
	{
		/// <summary>
		/// Gets the current read position. Used by the BSON layer only for end-of-document
		/// comparisons; reads are always forward-only.
		/// </summary>
		int Position { get; }

		byte ReadByte();

		bool ReadBoolean();

		int ReadInt32();

		long ReadInt64();

		double ReadDouble();

		decimal ReadDecimal();

		byte[] ReadBytes(int count);

		/// <summary>
		/// Reads a null-terminated C-style string (CString).
		/// </summary>
		string ReadCString();

		/// <summary>
		/// Reads a BSON-encoded string (4-byte length prefix including the trailing null terminator).
		/// </summary>
		string ReadBsonString();

		/// <summary>
		/// Advances the read position by the specified number of bytes without returning data.
		/// </summary>
		void Skip(int length);
	}
}

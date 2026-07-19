using System;
using System.IO;

namespace UltraLiteDB
{
	/// <summary>
	/// Static facade for converting <see cref="BsonDocument"/> to/from binary BSON byte arrays.
	/// Based on the BSON spec (bsonspec.org). Delegates to <see cref="BsonWriter"/> and <see cref="BsonReader"/>.
	/// </summary>
	public static class BsonSerializer
	{
		/// <summary>
		/// Serializes a <see cref="BsonDocument"/> to a new byte array in BSON format.
		/// </summary>
		public static byte[] Serialize(BsonDocument doc)
		{
			if (doc == null) throw new ArgumentNullException(nameof(doc));

			return BsonWriter.Serialize(doc);
		}

		/// <summary>
		/// Serializes a <see cref="BsonDocument"/> into an existing byte array at the specified offset.
		/// </summary>
		/// <returns>The writer position after serialization (offset + bytes written).</returns>
		public static int SerializeTo(BsonDocument doc, byte[] array, int offset = 0)
		{
			if (doc == null) throw new ArgumentNullException(nameof(doc));

			return BsonWriter.SerializeTo(doc, array, offset);
		}

		/// <summary>
		/// Serializes a <see cref="BsonDocument"/> to a standard .NET <see cref="Stream"/> in BSON format.
		/// Writes forward-only, so any writable stream is supported (seekable or not).
		/// </summary>
		/// <param name="doc">The document to serialize.</param>
		/// <param name="stream">The stream to write the BSON data to.</param>
		public static void Serialize(BsonDocument doc, Stream stream)
		{
			if (doc == null) throw new ArgumentNullException(nameof(doc));
			if (stream == null) throw new ArgumentNullException(nameof(stream));

			BsonWriter.WriteDocument(new StreamByteWriter(stream), doc);
		}

		/// <summary>
		/// Deserializes a <see cref="BsonDocument"/> from a BSON byte array.
		/// </summary>
		/// <param name="buffer">The BSON-encoded byte array.</param>
		/// <param name="offset">Byte offset to start reading from.</param>
		public static BsonDocument Deserialize(byte[] buffer, int offset = 0)
		{
			if (buffer == null || buffer.Length == 0) throw new ArgumentNullException(nameof(buffer));

			return BsonReader.Deserialize(buffer, offset);
		}

		/// <summary>
		/// Deserializes a <see cref="BsonDocument"/> from a BSON <see cref="ArraySegment{T}"/>.
		/// </summary>
		public static BsonDocument Deserialize(ArraySegment<byte> buffer)
		{
			if (buffer == null || buffer.Count == 0) throw new ArgumentNullException(nameof(buffer));

			return BsonReader.Deserialize(buffer);
		}

		/// <summary>
		/// Deserializes a <see cref="BsonDocument"/> from a standard .NET <see cref="Stream"/>.
		/// Reads forward-only, so any readable stream is supported (seekable or not).
		/// </summary>
		/// <param name="stream">The stream to read the BSON document from.</param>
		public static BsonDocument Deserialize(Stream stream)
		{
			if (stream == null) throw new ArgumentNullException(nameof(stream));

			return BsonReader.ReadDocument(new StreamByteReader(stream));
		}
	}
}

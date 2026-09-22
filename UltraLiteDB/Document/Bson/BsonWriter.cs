using System;
using System.Collections.Generic;
using System.Text;

namespace UltraLiteDB
{
	/// <summary>
	/// Serializes <see cref="BsonDocument"/> and <see cref="BsonArray"/> objects into binary BSON format.
	/// Uses <see cref="ByteWriter"/> for efficient byte output.
	/// </summary>
	public static class BsonWriter
	{
		/// <summary>
		/// Serializes a <see cref="BsonDocument"/> into a new byte array.
		/// </summary>
		/// <remarks>
		/// Single pass: the document is written into a reusable per-thread buffer with its length prefixes
		/// back-filled, then copied out, instead of sizing the whole tree first. Every document and array written
		/// gets its cached length updated, exactly as the sizing pass (<c>GetBytesCount(true)</c>) used to leave it.
		/// </remarks>
		public static byte[] Serialize(BsonDocument doc)
		{
			var writer = DirectBuffers.RentWriter();

			WriteDocumentDirect(writer, doc);

			var result = new byte[writer.Position];
			System.Buffer.BlockCopy(writer.Buffer, 0, result, 0, writer.Position);

			DirectBuffers.ReturnWriter(writer);
			return result;
		}

		/// <summary>
		/// Serializes a <see cref="BsonDocument"/> into an existing byte array at the specified offset.
		/// </summary>
		/// <returns>The writer position after serialization.</returns>
		public static int SerializeTo(BsonDocument doc, byte[] array, int offset = 0)
		{
			var writer = new ByteWriter(array);
			writer.Skip(offset);

			WriteDocument(writer, doc);

			return writer.Position;
		}

		/// <summary>
		/// Writes a BSON document (length prefix + elements + 0x00 terminator) to the writer.
		/// </summary>
		/// <remarks>
		/// Forward-only (the length prefix comes from <see cref="BsonValue.GetBytesCount(bool)"/>), so it works on
		/// any writer: non-seekable streams, and fixed buffers such as index pages that must not grow.
		/// </remarks>
		public static void WriteDocument(IByteWriter writer, BsonDocument doc)
		{
			writer.Write(doc.GetBytesCount(false));

			foreach (var element in doc.RawValue)
			{
				WriteElement(writer, element.Key, element.Value ?? BsonValue.Null);
			}

			writer.Write((byte)0x00);
		}

		/// <summary>
		/// Writes a BSON array (length prefix + indexed elements + 0x00 terminator) to the writer.
		/// </summary>
		public static void WriteArray(IByteWriter writer, BsonArray array)
		{
			WriteArray(writer, array, array.GetBytesCount(false));
		}

		private static void WriteArray(IByteWriter writer, BsonArray array, int length)
		{
			writer.Write(length);

			var items = array.RawValue;

			for (var i = 0; i < items.Count; i++)
			{
				WriteElement(writer, i.ToString(), items[i] ?? BsonValue.Null);
			}

			writer.Write((byte)0x00);
		}

		/// <summary>
		/// Writes a BSON document into a growable <see cref="ByteWriter"/> in one pass: the length prefix is
		/// reserved and back-filled, and the document's cached length is updated to match. Keys and strings are
		/// encoded straight into the buffer.
		/// </summary>
		internal static void WriteDocumentDirect(ByteWriter writer, BsonDocument doc)
		{
			var start = writer.Position;
			writer.EnsureCapacity(4);
			writer.Skip(4);

			foreach (var element in doc.RawValue)
			{
				var typePos = writer.BeginElement(element.Key);
				writer.SetByte(typePos, DirectBsonWriter.WriteBsonValue(writer, element.Value ?? BsonValue.Null));
			}

			writer.EnsureCapacity(1);
			writer.Write((byte)0x00);

			var length = writer.Position - start;
			writer.SetInt32(start, length);
			doc.SetBytesCount(length);
		}

		/// <summary>
		/// Writes a BSON array into a growable <see cref="ByteWriter"/> in one pass. See <see cref="WriteDocumentDirect"/>.
		/// </summary>
		internal static void WriteArrayDirect(ByteWriter writer, BsonArray array)
		{
			var start = writer.Position;
			writer.EnsureCapacity(4);
			writer.Skip(4);

			var items = array.RawValue;

			for (var i = 0; i < items.Count; i++)
			{
				var typePos = writer.BeginElement(i);
				writer.SetByte(typePos, DirectBsonWriter.WriteBsonValue(writer, items[i] ?? BsonValue.Null));
			}

			writer.EnsureCapacity(1);
			writer.Write((byte)0x00);

			var length = writer.Position - start;
			writer.SetInt32(start, length);
			array.SetBytesCount(length);
		}

		private static void WriteElement(IByteWriter writer, string key, BsonValue value)
		{
			// cast RawValue to avoid one if on As<Type>
			switch (value.Type)
			{
				case BsonType.Double:
					writer.Write((byte)0x01);
					WriteCString(writer, key);
					writer.Write((Double)value.RawValue);
					break;

				case BsonType.String:
					writer.Write((byte)0x02);
					WriteCString(writer, key);
					WriteString(writer, (String)value.RawValue);
					break;

				case BsonType.Document:
					writer.Write((byte)0x03);
					WriteCString(writer, key);
					WriteDocument(writer, (BsonDocument)value);
					break;

				case BsonType.Array:
					writer.Write((byte)0x04);
					WriteCString(writer, key);
					// a nested array's length is always recalculated (never a possibly stale cached value)
					var array = value as BsonArray ?? new BsonArray((List<BsonValue>)value.RawValue);
					WriteArray(writer, array, array.GetBytesCount(true));
					break;

				case BsonType.Binary:
					writer.Write((byte)0x05);
					WriteCString(writer, key);
					var bytes = (ArraySegment<byte>)value.RawValue;
					writer.Write(bytes.Count);
					writer.Write((byte)0x00); // subtype 00 - Generic binary subtype
					writer.Write(bytes);
					break;

				case BsonType.Guid:
					writer.Write((byte)0x05);
					WriteCString(writer, key);
					var guid = ((Guid)value.RawValue).ToByteArray();
					writer.Write(guid.Length);
					writer.Write((byte)0x04); // UUID
					writer.Write(guid);
					break;

				case BsonType.ObjectId:
					writer.Write((byte)0x07);
					WriteCString(writer, key);
					writer.Write(((ObjectId)value.RawValue).ToByteArray());
					break;

				case BsonType.Boolean:
					writer.Write((byte)0x08);
					WriteCString(writer, key);
					writer.Write((byte)(((Boolean)value.RawValue) ? 0x01 : 0x00));
					break;

				case BsonType.DateTime:
					writer.Write((byte)0x09);
					WriteCString(writer, key);
					var date = (DateTime)value.RawValue;
					// do not convert to UTC min/max date values - #19
					var utc = (date == DateTime.MinValue || date == DateTime.MaxValue) ? date : date.ToUniversalTime();
					var ts = utc - BsonValue.UnixEpoch;
					writer.Write(Convert.ToInt64(ts.TotalMilliseconds));
					break;

				case BsonType.Null:
					writer.Write((byte)0x0A);
					WriteCString(writer, key);
					break;

				case BsonType.Int32:
					writer.Write((byte)0x10);
					WriteCString(writer, key);
					writer.Write((Int32)value.RawValue);
					break;

				case BsonType.Int64:
					writer.Write((byte)0x12);
					WriteCString(writer, key);
					writer.Write((Int64)value.RawValue);
					break;

				case BsonType.Decimal:
					writer.Write((byte)0x13);
					WriteCString(writer, key);
					writer.Write((Decimal)value.RawValue);
					break;

				case BsonType.MinValue:
					writer.Write((byte)0xFF);
					WriteCString(writer, key);
					break;

				case BsonType.MaxValue:
					writer.Write((byte)0x7F);
					WriteCString(writer, key);
					break;
			}
		}

		private static void WriteString(IByteWriter writer, string s)
		{
			var bytes = Encoding.UTF8.GetBytes(s);
			writer.Write(bytes.Length + 1);
			writer.Write(bytes);
			writer.Write((byte)0x00);
		}

		private static void WriteCString(IByteWriter writer, string s)
		{
			var bytes = Encoding.UTF8.GetBytes(s);
			writer.Write(bytes);
			writer.Write((byte)0x00);
		}
	}
}

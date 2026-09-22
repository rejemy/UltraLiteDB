using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace UltraLiteDB
{
	/// <summary>
	/// Deserializes binary BSON data into <see cref="BsonDocument"/> and <see cref="BsonArray"/> objects.
	/// Reads the BSON wire format using <see cref="ByteReader"/>.
	/// </summary>
	public static class BsonReader
	{
		/// <summary>
		/// Deserializes a <see cref="BsonDocument"/> from a byte array.
		/// </summary>
		/// <param name="bson">BSON-encoded byte array.</param>
		/// <param name="offset">Byte offset to start reading from.</param>
		public static BsonDocument Deserialize(byte[] bson, int offset = 0)
		{
			ByteReader reader = new ByteReader(bson);
			reader.Skip(offset);
			return ReadDocument(reader);
		}

		/// <summary>
		/// Deserializes a <see cref="BsonDocument"/> from an <see cref="ArraySegment{T}"/>.
		/// </summary>
		public static BsonDocument Deserialize(ArraySegment<byte> bson)
		{
			return ReadDocument(new ByteReader(bson));
		}

		/// <summary>
		/// Reads a complete BSON document (length prefix + elements + terminator) from the reader.
		/// </summary>
		public static BsonDocument ReadDocument(IByteReader reader)
		{
			if (reader is ByteReader bytes) return ReadDocumentDirect(bytes);

			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;
			var obj = new BsonDocument();

			while (reader.Position < end)
			{
				var value = ReadElement(reader, out string name);
				obj.RawValue[name] = value;
			}

			reader.ReadByte(); // zero

			return obj;
		}

		/// <summary>
		/// Reads a complete BSON array (length prefix + indexed elements + terminator) from the reader.
		/// </summary>
		public static BsonArray ReadArray(IByteReader reader)
		{
			if (reader is ByteReader bytes) return ReadArrayDirect(bytes);

			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;
			var arr = new BsonArray();

			while (reader.Position < end)
			{
				var value = ReadElement(reader, out string name);
				arr.Add(value);
			}

			reader.ReadByte(); // zero

			return arr;
		}

		#region In-memory fast path

		// Used whenever the source is a ByteReader (every read the database engine does, and Deserialize(byte[])).
		// Same results as the general path below, with fewer allocations: field names come from a shared key
		// cache, array index keys are skipped without decoding, arrays are allocated at their final size,
		// ObjectId/Guid read without temporary arrays, and common immutable values are shared instances.

		private static BsonDocument ReadDocumentDirect(ByteReader reader)
		{
			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;
			var doc = new BsonDocument();
			var elements = doc.RawValue;
			var buffer = reader.Buffer;

			while (reader.Position < end)
			{
				var type = reader.ReadByte();
				var nameStart = reader.ReadCStringRange(out var nameLength);
				var name = BsonKeyCache.Get(buffer, nameStart, nameLength);

				elements[name] = ReadValueDirect(reader, type);
			}

			reader.ReadByte(); // zero

			return doc;
		}

		private static BsonArray ReadArrayDirect(ByteReader reader)
		{
			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;
			var arr = new BsonArray();
			var items = (List<BsonValue>)arr.RawValue;

			items.Capacity = CountElements(reader, end);

			while (reader.Position < end)
			{
				var type = reader.ReadByte();
				reader.SkipCString(); // array index key: positional, not needed
				items.Add(ReadValueDirect(reader, type));
			}

			reader.ReadByte(); // zero

			return arr;
		}

		private static BsonValue ReadValueDirect(ByteReader reader, byte type)
		{
			switch (type)
			{
				case 0x01: return reader.ReadDouble();
				case 0x02: return reader.ReadBsonString();
				case 0x03: return ReadDocumentDirect(reader);
				case 0x04: return ReadArrayDirect(reader);

				case 0x05: // Binary
					var length = reader.ReadInt32();
					var subType = reader.ReadByte();

					if (subType == 0x04 && length == 16) return reader.ReadGuid();

					var bytes = reader.ReadBytes(length);

					switch (subType)
					{
						case 0x00: return bytes;
						case 0x04: return new Guid(bytes); // wrong length: let Guid report it, as before
					}

					break;

				case 0x07: return reader.ReadObjectId();
				case 0x08: return reader.ReadBoolean() ? _true : _false;

				case 0x09: // DateTime
					var ts = reader.ReadInt64();

					// catch specific values for MaxValue / MinValue #19
					if (ts == 253402300800000) return DateTime.MaxValue;
					if (ts == -62135596800000) return DateTime.MinValue;

					return BsonValue.UnixEpoch.AddMilliseconds(ts);

				case 0x0A: return BsonValue.Null;
				case 0x10: return GetInt32(reader.ReadInt32());
				case 0x12: return reader.ReadInt64();
				case 0x13: return reader.ReadDecimal();
				case 0xFF: return BsonValue.MinValue;
				case 0x7F: return BsonValue.MaxValue;
			}

			throw new NotSupportedException("BSON type not supported");
		}

		/// <summary>
		/// Counts the elements left in the current document/array without decoding them, leaving the position unchanged.
		/// </summary>
		private static int CountElements(ByteReader reader, int end)
		{
			var start = reader.Position;
			var count = 0;

			while (reader.Position < end)
			{
				var type = reader.ReadByte();
				reader.SkipCString();

				switch (type)
				{
					case 0x01: case 0x09: case 0x12: reader.Skip(8); break;
					case 0x02: reader.Skip(reader.ReadInt32()); break;
					case 0x03: case 0x04: reader.Skip(reader.ReadInt32() - 4); break;
					case 0x05: reader.Skip(1 + reader.ReadInt32()); break;
					case 0x07: reader.Skip(12); break;
					case 0x08: reader.Skip(1); break;
					case 0x0A: case 0xFF: case 0x7F: break;
					case 0x10: reader.Skip(4); break;
					case 0x13: reader.Skip(16); break;
					default: throw new NotSupportedException("BSON type not supported");
				}

				count++;
			}

			reader.Position = start;
			return count;
		}

		// BsonValue is immutable, so common values can be shared (BsonValue.Null already is)
		private static readonly BsonValue _true = new BsonValue(true);
		private static readonly BsonValue _false = new BsonValue(false);

		private const int SMALL_INT_MIN = -128;
		private const int SMALL_INT_MAX = 1023;
		private static readonly BsonValue?[] _smallInts = new BsonValue?[SMALL_INT_MAX - SMALL_INT_MIN + 1];

		private static BsonValue GetInt32(int value)
		{
			if (value < SMALL_INT_MIN || value > SMALL_INT_MAX) return value;

			var slot = value - SMALL_INT_MIN;
			var cached = Volatile.Read(ref _smallInts[slot]);

			if (cached == null)
			{
				cached = new BsonValue(value);
				Volatile.Write(ref _smallInts[slot], cached);
			}

			return cached;
		}

		#endregion

		/// <summary>
		/// Reads a single BSON element (type byte + CString key + value) and outputs the field name.
		/// </summary>
		private static BsonValue ReadElement(IByteReader reader, out string name)
		{
			var type = reader.ReadByte();
			name = reader.ReadCString();

			if (type == 0x01) // Double
			{
				return reader.ReadDouble();
			}
			else if (type == 0x02) // String
			{
				return reader.ReadBsonString();
			}
			else if (type == 0x03) // Document
			{
				return ReadDocument(reader);
			}
			else if (type == 0x04) // Array
			{
				return ReadArray(reader);
			}
			else if (type == 0x05) // Binary
			{
				var length = reader.ReadInt32();
				var subType = reader.ReadByte();
				var bytes = reader.ReadBytes(length);

				switch (subType)
				{
					case 0x00: return bytes;
					case 0x04: return new Guid(bytes);
				}
			}
			else if (type == 0x07) // ObjectId
			{
				return new ObjectId(reader.ReadBytes(12));
			}
			else if (type == 0x08) // Boolean
			{
				return reader.ReadBoolean();
			}
			else if (type == 0x09) // DateTime
			{
				var ts = reader.ReadInt64();

				// catch specific values for MaxValue / MinValue #19
				if (ts == 253402300800000) return DateTime.MaxValue;
				if (ts == -62135596800000) return DateTime.MinValue;

				var date = BsonValue.UnixEpoch.AddMilliseconds(ts);

				return date;
			}
			else if (type == 0x0A) // Null
			{
				return BsonValue.Null;
			}
			else if (type == 0x10) // Int32
			{
				return reader.ReadInt32();
			}
			else if (type == 0x12) // Int64
			{
				return reader.ReadInt64();
			}
			else if (type == 0x13) // Decimal
			{
				return reader.ReadDecimal();
			}
			else if (type == 0xFF) // MinKey
			{
				return BsonValue.MinValue;
			}
			else if (type == 0x7F) // MaxKey
			{
				return BsonValue.MaxValue;
			}

			throw new NotSupportedException("BSON type not supported");
		}
	}
}

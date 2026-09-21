using System;
using System.Text;
using System.Buffers.Binary;

namespace UltraLiteDB
{
	/// <summary>
	/// Writes primitive and extended data types sequentially into a byte buffer in little-endian format.
	/// </summary>
	public class ByteWriter : IByteWriter
	{
		private byte[] _buffer;
		private int _pos;

		/// <summary>
		/// Gets the underlying byte array being written to.
		/// </summary>
		public byte[] Buffer { get { return _buffer; } }

		/// <summary>
		/// Gets or sets the current write position within the buffer.
		/// </summary>
		public int Position { get { return _pos; } set { _pos = value; } }


		/// <summary>
		/// Initializes an empty <see cref="ByteWriter"/> with no backing buffer.
		/// </summary>
		public ByteWriter()
		{
			_buffer = null!; // reset state; a buffer must be supplied before writing
			_pos = 0;
		}

		/// <summary>
		/// Initializes a <see cref="ByteWriter"/> with a new byte array of the specified length.
		/// </summary>
		/// <param name="length">The size of the byte array to allocate.</param>
		public ByteWriter(int length)
		{
			_buffer = new byte[length];
			_pos = 0;
		}

		/// <summary>
		/// Initializes a <see cref="ByteWriter"/> with an existing byte array and optional starting offset.
		/// </summary>
		/// <param name="buffer">The byte array to write into.</param>
		/// <param name="offset">The starting position within the buffer (default: 0).</param>
		public ByteWriter(byte[] buffer, int offset = 0)
		{
			_buffer = buffer;
			_pos = offset;
		}

		/// <summary>
		/// Initializes a <see cref="ByteWriter"/> with the specified <see cref="ArraySegment{T}"/>, writing from its offset.
		/// </summary>
		/// <param name="buffer">The array segment to write into.</param>
		public ByteWriter(ArraySegment<byte> buffer)
		{
			_buffer = buffer.Array!;
			_pos = buffer.Offset;
		}

		/// <summary>
		/// Clears the writer state, removing the buffer reference and resetting position to zero.
		/// </summary>
		public void Clear()
		{
			_buffer = null!; // reset state; a buffer must be supplied before writing
			_pos = 0;
		}

		/// <summary>
		/// Resets the writer to use a new byte array, starting from position zero.
		/// </summary>
		/// <param name="buffer">The new byte array to write into.</param>
		public void Reset(byte[] buffer)
		{
			_buffer = buffer;
			_pos = 0;
		}

		/// <summary>
		/// Resets the writer to use a new <see cref="ArraySegment{T}"/>, starting from the segment's offset.
		/// </summary>
		/// <param name="buffer">The new array segment to write into.</param>
		public void Reset(ArraySegment<byte> buffer)
		{
			_buffer = buffer.Array!;
			_pos = buffer.Offset;
		}

		/// <summary>
		/// Advances the write position by the specified number of bytes without writing data.
		/// </summary>
		/// <param name="length">The number of bytes to skip.</param>
		public void Skip(int length)
		{
			_pos += length;
		}

		/// <summary>
		/// Ensures the buffer has room for the specified number of additional bytes, growing it if necessary.
		/// </summary>
		/// <param name="additionalBytes">The number of additional bytes needed.</param>
		public void EnsureCapacity(int additionalBytes)
		{
			if (_buffer == null)
			{
				_buffer = new byte[additionalBytes];
			}
			else if (_pos + additionalBytes > _buffer.Length)
			{
				var newBuffer = new byte[Math.Max(_buffer.Length * 2, _pos + additionalBytes)];
				System.Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _pos);
				_buffer = newBuffer;
			}
		}

		#region Native data types

		public void Write(Byte value)
		{
			_buffer[_pos] = value;

			_pos++;
		}

		public void Write(Boolean value)
		{
			_buffer[_pos] = value ? (byte)1 : (byte)0;

			_pos++;
		}

		public void Write(UInt16 value)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(_buffer, _pos, 2), value);

			_pos += 2;
		}

		public void Write(UInt32 value)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(_buffer, _pos, 4), value);

			_pos += 4;
		}

		public void Write(UInt64 value)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(_buffer, _pos, 8), value);

			_pos += 8;
		}

		public void Write(Int16 value)
		{
			BinaryPrimitives.WriteInt16LittleEndian(new Span<byte>(_buffer, _pos, 2), value);

			_pos += 2;
		}

		public void Write(Int32 value)
		{
			BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_buffer, _pos, 4), value);

			_pos += 4;
		}

		public void Write(Int64 value)
		{
			BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(_buffer, _pos, 8), value);

			_pos += 8;
		}

		public void Write(Single value)
		{
			BitConverter.TryWriteBytes(new Span<byte>(_buffer, _pos, 4), value);

			_pos += 4;
		}

		public void Write(Double value)
		{
			BitConverter.TryWriteBytes(new Span<byte>(_buffer, _pos, 8), value);

			_pos += 8;
		}

		public void Write(Decimal value)
		{
			var array = Decimal.GetBits(value);

			this.Write(array[0]);
			this.Write(array[1]);
			this.Write(array[2]);
			this.Write(array[3]);
		}

		public void Write(Byte[] value)
		{
			System.Buffer.BlockCopy(value, 0, _buffer, _pos, value.Length);

			_pos += value.Length;
		}

		public void Write(ArraySegment<byte> value)
		{
			System.Buffer.BlockCopy(value.Array, value.Offset, _buffer, _pos, value.Count);

			_pos += value.Count;
		}

		#endregion

		#region Extended types

		public void Write(string value)
		{
			var bytes = Encoding.UTF8.GetBytes(value);
			this.Write(bytes.Length);
			this.Write(bytes);
		}

		public void Write(string value, int length)
		{
			var bytes = Encoding.UTF8.GetBytes(value);
			if (bytes.Length != length) throw new ArgumentException("Invalid string length");
			this.Write(bytes);
		}

		public void Write(DateTime value)
		{
			this.Write(value.ToUniversalTime().Ticks);
		}

		public void Write(Guid value)
		{
			value.TryWriteBytes(new Span<byte>(_buffer, _pos, 16));

			_pos += 16;
		}

		public void Write(ObjectId value)
		{
			value.ToByteArray(_buffer, _pos);

			_pos += 12;
		}

		internal void Write(PageAddress value)
		{
			this.Write(value.PageID);
			this.Write(value.Index);
		}

		public void WriteBsonValue(BsonValue value, ushort length)
		{
			this.Write((byte)value.Type);

			switch (value.Type)
			{
				case BsonType.Null:
				case BsonType.MinValue:
				case BsonType.MaxValue:
					break;

				case BsonType.Int32: this.Write((Int32)value.RawValue); break;
				case BsonType.Int64: this.Write((Int64)value.RawValue); break;
				case BsonType.Double: this.Write((Double)value.RawValue); break;
				case BsonType.Decimal: this.Write((Decimal)value.RawValue); break;

				case BsonType.String: this.Write((String)value.RawValue, length); break;

				case BsonType.Document: BsonWriter.WriteDocument(this, value.AsDocument!); break;
				case BsonType.Array: BsonWriter.WriteArray(this, value.AsArray!); break;

				case BsonType.Binary: this.Write((Byte[])value.RawValue); break;
				case BsonType.ObjectId: this.Write((ObjectId)value.RawValue); break;
				case BsonType.Guid: this.Write((Guid)value.RawValue); break;

				case BsonType.Boolean: this.Write((Boolean)value.RawValue); break;
				case BsonType.DateTime: this.Write((DateTime)value.RawValue); break;

				default: throw new NotImplementedException();
			}
		}

		#endregion

		#region Allocation-free BSON helpers (used by DirectBsonWriter)

		/// <summary>
		/// Returns an upper bound on the UTF-8 byte count of <paramref name="s"/>, cheaply for short strings
		/// and exactly for long ones (so large strings don't over-reserve buffer space).
		/// </summary>
		private static int Utf8Capacity(string s)
		{
			return s.Length <= 256 ? Encoding.UTF8.GetMaxByteCount(s.Length) : Encoding.UTF8.GetByteCount(s);
		}

		/// <summary>
		/// Starts a BSON element: reserves the type byte and writes a pre-encoded C-string key.
		/// Returns the position of the type byte, to be filled in with <see cref="SetByte"/>
		/// once the value (and therefore its BSON type) has been written.
		/// </summary>
		internal int BeginElement(byte[] keyCString)
		{
			this.EnsureCapacity(1 + keyCString.Length);
			var typePos = _pos;
			new ReadOnlySpan<byte>(keyCString).CopyTo(new Span<byte>(_buffer, _pos + 1, keyCString.Length));
			_pos += 1 + keyCString.Length;
			return typePos;
		}

		/// <summary>
		/// Starts a BSON array element whose key is <paramref name="index"/>. See <see cref="BeginElement(byte[])"/>.
		/// </summary>
		internal int BeginElement(int index)
		{
			this.EnsureCapacity(1);
			var typePos = _pos++;
			this.WriteIndexCString(index);
			return typePos;
		}

		/// <summary>
		/// Starts a BSON element with a string key. See <see cref="BeginElement(byte[])"/>.
		/// </summary>
		internal int BeginElement(string key)
		{
			this.EnsureCapacity(1);
			var typePos = _pos++;
			this.WriteCString(key);
			return typePos;
		}

		/// <summary>
		/// Overwrites a single byte at an absolute position (used to back-fill element type bytes).
		/// </summary>
		internal void SetByte(int position, byte value)
		{
			_buffer[position] = value;
		}

		/// <summary>
		/// Overwrites an Int32 at an absolute position (used to back-fill document/array lengths).
		/// </summary>
		internal void SetInt32(int position, int value)
		{
			BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_buffer, position, 4), value);
		}

		/// <summary>
		/// Writes raw bytes, growing the buffer if needed.
		/// </summary>
		internal void WriteRaw(byte[] value)
		{
			this.EnsureCapacity(value.Length);
			System.Buffer.BlockCopy(value, 0, _buffer, _pos, value.Length);
			_pos += value.Length;
		}

		/// <summary>
		/// Writes a null-terminated UTF-8 C-string, encoding directly into the buffer.
		/// </summary>
		internal void WriteCString(string value)
		{
			this.EnsureCapacity(Utf8Capacity(value) + 1);
			_pos += Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _pos);
			_buffer[_pos++] = 0x00;
		}

		/// <summary>
		/// Writes a non-negative integer as a null-terminated ASCII C-string (BSON array index keys).
		/// </summary>
		internal void WriteIndexCString(int index)
		{
			this.EnsureCapacity(11);

			var digits = 1;
			for (var v = index; v >= 10; v /= 10) digits++;

			var end = _pos + digits;
			for (var p = end - 1; p >= _pos; p--)
			{
				_buffer[p] = (byte)('0' + (index % 10));
				index /= 10;
			}

			_buffer[end] = 0x00;
			_pos = end + 1;
		}

		/// <summary>
		/// Writes a BSON string value (Int32 length including terminator, UTF-8 bytes, 0x00), encoding
		/// directly into the buffer.
		/// </summary>
		internal void WriteBsonString(string value)
		{
			this.EnsureCapacity(4 + Utf8Capacity(value) + 1);
			var count = Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _pos + 4);
			BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_buffer, _pos, 4), count + 1);
			_pos += 4 + count;
			_buffer[_pos++] = 0x00;
		}

		#endregion
	}
}

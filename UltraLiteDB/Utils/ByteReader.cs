using System;
using System.Text;
using System.Buffers.Binary;

namespace UltraLiteDB
{
    /// <summary>
    /// Reads primitive and extended data types sequentially from a byte buffer in little-endian format.
    /// </summary>
    public class ByteReader
    {
        private byte[] _buffer;
        private int _length;
        private int _pos;

        /// <summary>
        /// Gets or sets the current read position within the buffer.
        /// </summary>
        public int Position { get { return _pos; } set { _pos = value; } }

        /// <summary>
        /// Initializes an empty <see cref="ByteReader"/> with no backing buffer.
        /// </summary>
         public ByteReader()
        {
            _buffer = null!; // reset state; a buffer must be supplied before reading
            _length = 0;
            _pos = 0;
        }

        /// <summary>
        /// Initializes a <see cref="ByteReader"/> with the specified byte array and optional starting offset.
        /// </summary>
        /// <param name="buffer">The byte array to read from.</param>
        /// <param name="offset">The starting position within the buffer (default: 0).</param>
        public ByteReader(byte[] buffer, int offset = 0)
        {
            _buffer = buffer;
            _length = buffer.Length;
            _pos = offset;
        }

        /// <summary>
        /// Initializes a <see cref="ByteReader"/> with the specified <see cref="ArraySegment{T}"/>, reading within its bounds.
        /// </summary>
        /// <param name="buffer">The array segment to read from.</param>
        public ByteReader(ArraySegment<byte> buffer)
        {
            _buffer = buffer.Array!;
            _length = buffer.Offset+buffer.Count;
            _pos = buffer.Offset;
        }

        /// <summary>
        /// Clears the reader state, removing the buffer reference and resetting position to zero.
        /// </summary>
        public void Clear()
        {
            _buffer = null!; // reset state; a buffer must be supplied before reading
            _length = 0;
            _pos = 0;
        }

        /// <summary>
        /// Resets the reader to use a new byte array, starting from position zero.
        /// </summary>
        /// <param name="buffer">The new byte array to read from.</param>
        public void Reset(byte[] buffer)
        {
            _buffer = buffer;
            _length = buffer.Length;
            _pos = 0;
        }

        /// <summary>
        /// Resets the reader to use a new <see cref="ArraySegment{T}"/>, starting from the segment's offset.
        /// </summary>
        /// <param name="buffer">The new array segment to read from.</param>
        public void Reset(ArraySegment<byte> buffer)
        {
            _buffer = buffer.Array!;
            _length = buffer.Offset+buffer.Count;
            _pos = buffer.Offset;
        }

        /// <summary>
        /// Advances the read position by the specified number of bytes without reading data.
        /// </summary>
        /// <param name="length">The number of bytes to skip.</param>
        public void Skip(int length)
        {
            _pos += length;
        }

        #region Native data types

        public Byte ReadByte()
        {
            var value = _buffer[_pos];

            _pos++;

            return value;
        }

        public Boolean ReadBoolean()
        {
            var value = _buffer[_pos];

            _pos++;

            return value == 0 ? false : true;
        }

        public UInt16 ReadUInt16()
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buffer, _pos, 2));
            _pos += 2;
            return value;
        }

        public UInt32 ReadUInt32()
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, _pos, 4));
            _pos += 4;
            return value;
        }

        public UInt64 ReadUInt64()
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(_buffer, _pos, 8));
            _pos += 8;
            return value;
        }

        public Int16 ReadInt16()
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(_buffer, _pos, 2));
            _pos += 2;
            return value;
        }

        public Int32 ReadInt32()
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, _pos, 4));
            _pos += 4;
            return value;
        }

        public Int64 ReadInt64()
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(_buffer, _pos, 8));
            _pos += 8;
            return value;
        }

        public Single ReadSingle()
        {
            _pos += 4;
            return BitConverter.ToSingle(_buffer, _pos - 4);
        }

        public Double ReadDouble()
        {
            _pos += 8;
            return BitConverter.ToDouble(_buffer, _pos - 8);
        }

        public Decimal ReadDecimal()
        {
            _pos += 16;
            var a = BitConverter.ToInt32(_buffer, _pos - 16);
            var b = BitConverter.ToInt32(_buffer, _pos - 12);
            var c = BitConverter.ToInt32(_buffer, _pos - 8);
            var d = BitConverter.ToInt32(_buffer, _pos - 4);
            return new Decimal(new int[] {  a, b, c, d });
        }

        public Byte[] ReadBytes(int count)
        {
            var buffer = new byte[count];

            System.Buffer.BlockCopy(_buffer, _pos, buffer, 0, count);

            _pos += count;

            return buffer;
        }

        #endregion

        #region Extended types

        /// <summary>
        /// Reads a length-prefixed UTF-8 string (4-byte Int32 length prefix followed by string bytes).
        /// </summary>
        /// <returns>The decoded string.</returns>
        public string ReadString()
        {
            var length = this.ReadInt32();
            var str = Encoding.UTF8.GetString(_buffer, _pos, length);
            _pos += length;

            return str;
        }

        /// <summary>
        /// Reads a UTF-8 string of the specified byte length from the buffer.
        /// </summary>
        /// <param name="length">The number of bytes to read as a string.</param>
        /// <returns>The decoded string.</returns>
        public string ReadString(int length)
        {
            var str = Encoding.UTF8.GetString(_buffer, _pos, length);
            _pos += length;

            return str;
        }

        /// <summary>
        /// Reads a BSON-encoded string, which includes a 4-byte length prefix and a trailing null terminator (0x00).
        /// The length prefix includes the null terminator byte.
        /// </summary>
        /// <returns>The decoded string without the null terminator.</returns>
        public string ReadBsonString()
        {
            var length = this.ReadInt32();
            var str = Encoding.UTF8.GetString(_buffer, _pos, length - 1);
            _pos += length;

            return str;
        }

        /// <summary>
        /// Reads a null-terminated C-style string (CString) from the buffer.
        /// </summary>
        /// <returns>The decoded string, or "_" if end of buffer is reached without finding null terminator.</returns>
        public string ReadCString()
        {
            var pos = _pos;
            var length = 0;

            while (true)
            {
                if (_buffer[pos] == 0x00)
                {
                    var str = Encoding.UTF8.GetString(_buffer, _pos, length);
                    _pos += length + 1; // read last 0x00
                    return str;
                }
                else if (pos > _length)
                {
                    return "_";
                }

                pos++;
                length++;
            }
        }

        public DateTime ReadDateTime()
        {
            // fix #921 converting index key into LocalTime
            // this is not best solution because uctDate must be a global parameter
            // this will be review in v5
            var date = new DateTime(this.ReadInt64(), DateTimeKind.Utc);

            return date;
        }

        public Guid ReadGuid()
        {
            return new Guid(this.ReadBytes(16));
        }

        public ObjectId ReadObjectId()
        {
            return new ObjectId(this.ReadBytes(12));
        }

        internal PageAddress ReadPageAddress()
        {
            return new PageAddress(this.ReadUInt32(), this.ReadUInt16());
        }

        public BsonValue ReadBsonValue(ushort length)
        {
            var type = (BsonType)this.ReadByte();

            switch (type)
            {
                case BsonType.Null: return BsonValue.Null;

                case BsonType.Int32: return this.ReadInt32();
                case BsonType.Int64: return this.ReadInt64();
                case BsonType.Double: return this.ReadDouble();
                case BsonType.Decimal: return this.ReadDecimal();

                case BsonType.String: return this.ReadString(length);

                case BsonType.Document: return BsonReader.ReadDocument(this);
                case BsonType.Array: return BsonReader.ReadArray(this);

                case BsonType.Binary: return this.ReadBytes(length);
                case BsonType.ObjectId: return this.ReadObjectId();
                case BsonType.Guid: return this.ReadGuid();

                case BsonType.Boolean: return this.ReadBoolean();
                case BsonType.DateTime: return this.ReadDateTime();

                case BsonType.MinValue: return BsonValue.MinValue;
                case BsonType.MaxValue: return BsonValue.MaxValue;
            }

            throw new NotImplementedException();
        }

        #endregion
    }
}
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UltraLiteDB
{
	/// <summary>
	/// Reads primitive and extended data types sequentially from a standard .NET <see cref="Stream"/>
	/// in little-endian format, preserving BSON wire compatibility. Reads are forward-only, so any
	/// readable stream is supported (seekable or not).
	/// </summary>
	/// <remarks>
	/// <see cref="Position"/> is tracked relative to the stream position when this reader was created,
	/// starting at zero. A single BSON document is bounded by its 4-byte length prefix (&lt; 2 GB),
	/// so an <see cref="int"/> position is sufficient.
	/// </remarks>
	public class StreamByteReader : IByteReader
	{
		private readonly Stream _stream;
		private int _pos;

		private readonly byte[] _scratch = new byte[16];
		private byte[] _stringBuffer = new byte[64];

		/// <inheritdoc/>
		public int Position { get { return _pos; } }

		/// <summary>
		/// Initializes a <see cref="StreamByteReader"/> that reads from the specified stream.
		/// </summary>
		/// <param name="stream">The readable stream to read BSON data from.</param>
		public StreamByteReader(Stream stream)
		{
			_stream = stream ?? throw new ArgumentNullException(nameof(stream));
			if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
			_pos = 0;
		}

		/// <summary>
		/// Fills <paramref name="count"/> bytes into <paramref name="buffer"/>, looping until the
		/// count is satisfied. Throws <see cref="EndOfStreamException"/> if the stream ends early.
		/// </summary>
		private void FillBuffer(byte[] buffer, int count)
		{
			var read = 0;
			while (read < count)
			{
				var n = _stream.Read(buffer, read, count - read);
				if (n == 0) throw new EndOfStreamException("Unexpected end of stream while reading BSON data.");
				read += n;
			}
			_pos += count;
		}

		#region Native data types

		public byte ReadByte()
		{
			var value = _stream.ReadByte();
			if (value < 0) throw new EndOfStreamException("Unexpected end of stream while reading BSON data.");
			_pos++;
			return (byte)value;
		}

		public bool ReadBoolean()
		{
			return this.ReadByte() != 0;
		}

		public int ReadInt32()
		{
			this.FillBuffer(_scratch, 4);
			return BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_scratch, 0, 4));
		}

		public long ReadInt64()
		{
			this.FillBuffer(_scratch, 8);
			return BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(_scratch, 0, 8));
		}

		public double ReadDouble()
		{
			this.FillBuffer(_scratch, 8);
			return BitConverter.ToDouble(_scratch, 0);
		}

		public decimal ReadDecimal()
		{
			this.FillBuffer(_scratch, 16);
			var a = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_scratch, 0, 4));
			var b = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_scratch, 4, 4));
			var c = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_scratch, 8, 4));
			var d = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_scratch, 12, 4));
			return new decimal(new int[] { a, b, c, d });
		}

		public byte[] ReadBytes(int count)
		{
			var buffer = new byte[count];
			if (count > 0) this.FillBuffer(buffer, count);
			return buffer;
		}

		#endregion

		#region Extended types

		/// <summary>
		/// Reads a BSON-encoded string (4-byte length prefix including the trailing null terminator).
		/// </summary>
		public string ReadBsonString()
		{
			var length = this.ReadInt32();
			this.EnsureStringBuffer(length);
			this.FillBuffer(_stringBuffer, length);
			// length includes the trailing null terminator, which is not part of the string
			return Encoding.UTF8.GetString(_stringBuffer, 0, length - 1);
		}

		/// <summary>
		/// Reads a null-terminated C-style string (CString).
		/// </summary>
		public string ReadCString()
		{
			var count = 0;
			while (true)
			{
				var b = _stream.ReadByte();
				if (b < 0)
				{
					// end of stream before the terminator (corrupt/truncated data)
					break;
				}
				_pos++;
				if (b == 0x00) break; // terminator consumed

				if (count >= _stringBuffer.Length) this.GrowStringBuffer(count + 1);
				_stringBuffer[count++] = (byte)b;
			}
			return Encoding.UTF8.GetString(_stringBuffer, 0, count);
		}

		public void Skip(int length)
		{
			if (length <= 0) return;

			if (_stream.CanSeek)
			{
				_stream.Seek(length, SeekOrigin.Current);
				_pos += length;
			}
			else
			{
				// read and discard
				var remaining = length;
				while (remaining > 0)
				{
					var chunk = Math.Min(remaining, _stringBuffer.Length);
					var n = _stream.Read(_stringBuffer, 0, chunk);
					if (n == 0) throw new EndOfStreamException("Unexpected end of stream while skipping BSON data.");
					remaining -= n;
					_pos += n;
				}
			}
		}

		#endregion

		private void EnsureStringBuffer(int size)
		{
			if (_stringBuffer.Length < size) this.GrowStringBuffer(size);
		}

		private void GrowStringBuffer(int size)
		{
			var newSize = Math.Max(_stringBuffer.Length * 2, size);
			var newBuffer = new byte[newSize];
			System.Buffer.BlockCopy(_stringBuffer, 0, newBuffer, 0, _stringBuffer.Length);
			_stringBuffer = newBuffer;
		}
	}
}

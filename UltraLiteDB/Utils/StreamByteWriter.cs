using System;
using System.Buffers.Binary;
using System.IO;

namespace UltraLiteDB
{
	/// <summary>
	/// Writes primitive and extended data types sequentially to a standard .NET <see cref="Stream"/>
	/// in little-endian format, preserving BSON wire compatibility.
	/// </summary>
	/// <remarks>
	/// The direct (POCO) serializer backfills document/array length prefixes by rewinding the write
	/// position, which requires a seekable stream. Setting <see cref="Position"/> throws
	/// <see cref="NotSupportedException"/> when the underlying stream is not seekable. Writing a
	/// <see cref="BsonDocument"/> is forward-only and never sets the position, so it works with any
	/// writable stream.
	/// </remarks>
	public class StreamByteWriter : IByteWriter
	{
		private readonly Stream _stream;
		private readonly long _origin;
		private int _pos;

		private readonly byte[] _scratch = new byte[16];
		private readonly byte[] _zeros = new byte[16];

		/// <inheritdoc/>
		public int Position
		{
			get { return _pos; }
			set
			{
				_stream.Seek(_origin + value, SeekOrigin.Begin);
				_pos = value;
			}
		}

		/// <summary>
		/// Initializes a <see cref="StreamByteWriter"/> that writes to the specified stream.
		/// </summary>
		/// <param name="stream">The writable stream to write BSON data to.</param>
		public StreamByteWriter(Stream stream)
		{
			_stream = stream ?? throw new ArgumentNullException(nameof(stream));
			if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));
			if (!_stream.CanSeek)
			{
				throw new NotSupportedException(
					"Setting the write position requires a seekable stream. Serializing a POCO directly to a " +
					"non-seekable stream (e.g. NetworkStream) is not supported; buffer to a MemoryStream first, " +
					"or serialize a BsonDocument (which writes forward-only).");
			}
			_origin = stream.CanSeek ? stream.Position : 0;
			_pos = 0;
		}

		/// <summary>
		/// No-op for streams, which grow automatically as data is written.
		/// </summary>
		public void EnsureCapacity(int additionalBytes)
		{
		}

		/// <summary>
		/// Reserves <paramref name="length"/> bytes by writing zeros. The direct serializer later
		/// rewinds and overwrites these placeholder bytes with the actual length prefix.
		/// </summary>
		public void Skip(int length)
		{
			var remaining = length;
			while (remaining > 0)
			{
				var chunk = Math.Min(remaining, _zeros.Length);
				_stream.Write(_zeros, 0, chunk);
				remaining -= chunk;
			}
			_pos += length;
		}

		#region Native data types

		public void Write(byte value)
		{
			_stream.WriteByte(value);
			_pos++;
		}

		public void Write(int value)
		{
			BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_scratch, 0, 4), value);
			_stream.Write(_scratch, 0, 4);
			_pos += 4;
		}

		public void Write(long value)
		{
			BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(_scratch, 0, 8), value);
			_stream.Write(_scratch, 0, 8);
			_pos += 8;
		}

		public void Write(double value)
		{
			BitConverter.TryWriteBytes(new Span<byte>(_scratch, 0, 8), value);
			_stream.Write(_scratch, 0, 8);
			_pos += 8;
		}

		public void Write(decimal value)
		{
			var bits = decimal.GetBits(value);
			this.Write(bits[0]);
			this.Write(bits[1]);
			this.Write(bits[2]);
			this.Write(bits[3]);
		}

		public void Write(byte[] value)
		{
			_stream.Write(value, 0, value.Length);
			_pos += value.Length;
		}

		public void Write(ArraySegment<byte> value)
		{
			if (value.Count > 0)
			{
				_stream.Write(value.Array, value.Offset, value.Count);
				_pos += value.Count;
			}
		}

		#endregion
	}
}

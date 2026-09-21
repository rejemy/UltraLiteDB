using System;
using System.Buffers.Binary;
using System.IO;

namespace UltraLiteDB
{
	/// <summary>
	/// Per-thread scratch buffers for the direct (POCO) serializer, so repeated calls don't allocate
	/// and regrow a fresh buffer each time. A buffer is taken out of its slot while in use, which keeps
	/// re-entrant calls (e.g. a custom serializer that serializes another object) safe: they simply
	/// get a new buffer.
	/// </summary>
	internal static class DirectBuffers
	{
		private const int INITIAL_SIZE = 1024;

		/// <summary>
		/// Buffers larger than this are dropped after use instead of being retained by the thread.
		/// </summary>
		private const int MAX_RETAINED_SIZE = 256 * 1024;

		[ThreadStatic]
		private static ByteWriter? _writer;

		[ThreadStatic]
		private static byte[]? _readBuffer;

		public static ByteWriter RentWriter()
		{
			var writer = _writer;

			if (writer == null) return new ByteWriter(INITIAL_SIZE);

			_writer = null;
			writer.Position = 0;
			return writer;
		}

		public static void ReturnWriter(ByteWriter writer)
		{
			if (writer.Buffer.Length <= MAX_RETAINED_SIZE) _writer = writer;
		}

		public static byte[] RentReadBuffer(int size)
		{
			var buffer = _readBuffer;

			if (buffer == null || buffer.Length < size) return new byte[Math.Max(size, INITIAL_SIZE)];

			_readBuffer = null;
			return buffer;
		}

		public static void ReturnReadBuffer(byte[] buffer)
		{
			if (buffer.Length <= MAX_RETAINED_SIZE && (_readBuffer == null || _readBuffer.Length < buffer.Length)) _readBuffer = buffer;
		}

		/// <summary>
		/// Reads exactly one BSON document (as sized by its length prefix) from <paramref name="stream"/> into a
		/// rented buffer, consuming nothing past its end. Return the buffer with <see cref="ReturnReadBuffer"/>.
		/// </summary>
		public static byte[] ReadDocument(Stream stream, out int length)
		{
			var buffer = RentReadBuffer(INITIAL_SIZE);
			var read = 0;

			while (read < 4) read += ReadSome(stream, buffer, read, 4 - read);

			length = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(buffer, 0, 4));
			if (length < 5) throw new InvalidDataException($"Invalid BSON document length: {length}");

			while (read < length)
			{
				if (read == buffer.Length)
				{
					// grow only as data actually arrives, so a corrupt length prefix can't force a huge allocation
					var larger = new byte[(int)Math.Min(length, (long)buffer.Length * 2)];
					System.Buffer.BlockCopy(buffer, 0, larger, 0, read);
					buffer = larger;
				}

				read += ReadSome(stream, buffer, read, Math.Min(buffer.Length, length) - read);
			}

			return buffer;
		}

		private static int ReadSome(Stream stream, byte[] buffer, int offset, int count)
		{
			var n = stream.Read(buffer, offset, count);
			if (n == 0) throw new EndOfStreamException("Unexpected end of stream while reading BSON data.");
			return n;
		}
	}
}

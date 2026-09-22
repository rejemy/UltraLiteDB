using System;
using System.IO;

namespace UltraLiteDB
{
	public partial class BsonMapper
	{
		/// <summary>
		/// Deserializes BSON bytes directly to a <typeparamref name="T"/> instance, bypassing intermediate
		/// <see cref="BsonDocument"/> creation to reduce GC pressure.
		/// </summary>
		/// <typeparam name="T">The target entity type.</typeparam>
		/// <param name="bsonBytes">BSON-encoded byte array.</param>
		/// <param name="offset">Byte offset to start reading from.</param>
		public virtual T DeserializeFromBytes<T>(byte[] bsonBytes, int offset = 0)
		{
			return (T)this.DeserializeFromBytes(typeof(T), bsonBytes, offset);
		}

		/// <summary>
		/// Deserializes BSON bytes from an <see cref="ArraySegment{T}"/> directly to a <typeparamref name="T"/> instance.
		/// </summary>
		/// <typeparam name="T">The target entity type.</typeparam>
		/// <param name="bsonBytes">BSON-encoded byte segment.</param>
		public virtual T DeserializeFromBytes<T>(ArraySegment<byte> bsonBytes)
		{
			return (T)this.DeserializeFromBytes(typeof(T), bsonBytes.Array, bsonBytes.Offset);
		}


		/// <summary>
		/// Deserializes BSON bytes directly to an entity of the specified type. Falls back to
		/// <see cref="BsonReader"/> if the target type is <see cref="BsonDocument"/>.
		/// </summary>
		/// <param name="type">The target CLR type.</param>
		/// <param name="bsonBytes">BSON-encoded byte array.</param>
		/// <param name="offset">Byte offset to start reading from.</param>
		public virtual object DeserializeFromBytes(Type type, byte[] bsonBytes, int offset = 0)
		{
			if (bsonBytes == null) throw new ArgumentNullException(nameof(bsonBytes));

			// If target is BsonDocument, use existing path
			if (type == typeof(BsonDocument))
			{
				return BsonReader.Deserialize(bsonBytes, offset);
			}

			var reader = new ByteReader(bsonBytes);
			if (offset > 0) reader.Skip(offset);

			return DirectBsonReader.ReadObjectDirect(reader, this, type);
		}

		/// <summary>
		/// Deserializes BSON bytes from an <see cref="ArraySegment{T}"/> directly to an entity of the specified type.
		/// </summary>
		/// <param name="type">The target CLR type.</param>
		/// <param name="bsonBytes">BSON-encoded byte segment.</param>
		public virtual object DeserializeFromBytes(Type type, ArraySegment<byte> bsonBytes)
		{
			return DeserializeFromBytes(type, bsonBytes.Array, bsonBytes.Offset);
		}

		/// <summary>
		/// Deserializes BSON from a standard .NET <see cref="Stream"/> directly to a
		/// <typeparamref name="T"/> instance, bypassing intermediate <see cref="BsonDocument"/> creation.
		/// Reads forward-only, so any readable stream is supported.
		/// </summary>
		/// <typeparam name="T">The target entity type.</typeparam>
		/// <param name="stream">The stream to read BSON from.</param>
		public virtual T DeserializeFromStream<T>(Stream stream)
		{
			return (T)this.DeserializeFromStream(typeof(T), stream);
		}

		/// <summary>
		/// Deserializes BSON from a standard .NET <see cref="Stream"/> directly to an entity of the
		/// specified type. Falls back to <see cref="BsonReader"/> if the target type is <see cref="BsonDocument"/>.
		/// </summary>
		/// <param name="type">The target CLR type.</param>
		/// <param name="stream">The stream to read BSON from.</param>
		public virtual object DeserializeFromStream(Type type, Stream stream)
		{
			if (stream == null) throw new ArgumentNullException(nameof(stream));

			// If target is BsonDocument, use existing path
			if (type == typeof(BsonDocument))
			{
				return BsonSerializer.Deserialize(stream);
			}

			// Read exactly one document (its length prefix says how much) into a reusable buffer, then
			// parse from memory: forward-only, and far fewer stream calls than reading field by field
			var buffer = DirectBuffers.ReadDocument(stream, out var length);

			var result = DirectBsonReader.ReadObjectDirect(new ByteReader(new ArraySegment<byte>(buffer, 0, length)), this, type);

			DirectBuffers.ReturnReadBuffer(buffer);
			return result;
		}
	}

}

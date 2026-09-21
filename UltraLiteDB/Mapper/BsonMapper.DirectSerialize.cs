using System;
using System.IO;

namespace UltraLiteDB
{
	public partial class BsonMapper
	{
		/// <summary>
		/// Serializes an entity directly to BSON bytes, bypassing intermediate <see cref="BsonDocument"/> creation to reduce GC pressure.
		/// Returns a trimmed byte array containing the BSON data.
		/// </summary>
		/// <typeparam name="T">The entity type.</typeparam>
		public virtual byte[] SerializeToBytes<T>(T entity)
		{
			return this.SerializeToBytes(typeof(T), entity);
		}

		/// <summary>
		/// Serializes an entity directly to BSON bytes using the provided <see cref="ByteWriter"/>,
		/// allowing callers to reuse buffers and control allocation.
		/// </summary>
		/// <typeparam name="T">The entity type.</typeparam>
		/// <param name="entity">The object to serialize.</param>
		/// <param name="writer">The writer to output BSON bytes to.</param>
		public virtual void SerializeToBytes<T>(T entity, ByteWriter writer)
		{
			this.SerializeToBytes(typeof(T), entity, writer);
		}

		/// <summary>
		/// Serializes an entity directly to BSON bytes, bypassing intermediate <see cref="BsonDocument"/> creation.
		/// Falls back to <see cref="BsonWriter"/> if the entity is already a <see cref="BsonDocument"/>.
		/// </summary>
		/// <param name="type">The declared type (used for polymorphic type resolution).</param>
		/// <param name="entity">The object to serialize.</param>
		public virtual byte[] SerializeToBytes(Type type, object? entity)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));

			// If already a BsonDocument, use existing path
			if (entity is BsonDocument doc)
			{
				return BsonWriter.Serialize(doc);
			}

			var writer = DirectBuffers.RentWriter();

			DirectBsonWriter.WriteObjectDirect(writer, this, type, entity, null, 0);

			// Copy out exactly the bytes written; the scratch buffer is reused by the next call
			var result = new byte[writer.Position];
			System.Buffer.BlockCopy(writer.Buffer, 0, result, 0, writer.Position);

			DirectBuffers.ReturnWriter(writer);
			return result;
		}

		/// <summary>
		/// Serializes an entity directly to BSON bytes using the provided <see cref="ByteWriter"/>.
		/// Falls back to <see cref="BsonWriter"/> if the entity is already a <see cref="BsonDocument"/>.
		/// </summary>
		/// <param name="type">The declared type (used for polymorphic type resolution).</param>
		/// <param name="entity">The object to serialize.</param>
		/// <param name="writer">The writer to output BSON bytes to.</param>
		public virtual void SerializeToBytes(Type type, object? entity, ByteWriter writer)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));

			// If already a BsonDocument, use existing path
			if (entity is BsonDocument doc)
			{
				writer.EnsureCapacity(doc.GetBytesCount(true));
				BsonWriter.WriteDocument(writer, doc);
				return;
			}

			DirectBsonWriter.WriteObjectDirect(writer, this, type, entity, null, 0);
		}

		/// <summary>
		/// Serializes an entity directly to BSON on a standard .NET <see cref="Stream"/>, bypassing
		/// intermediate <see cref="BsonDocument"/> creation.
		/// </summary>
		/// <typeparam name="T">The entity type.</typeparam>
		/// <param name="entity">The object to serialize.</param>
		/// <param name="stream">The stream to write BSON bytes to. Any writable stream is supported: the
		/// document is built in a reusable buffer and written with a single <see cref="Stream.Write(byte[], int, int)"/>.</param>
		public virtual void SerializeToStream<T>(T entity, Stream stream)
		{
			this.SerializeToStream(typeof(T), entity, stream);
		}

		/// <summary>
		/// Serializes an entity directly to BSON on a standard .NET <see cref="Stream"/>. Falls back to
		/// <see cref="BsonWriter"/> if the entity is already a <see cref="BsonDocument"/>.
		/// </summary>
		/// <param name="type">The declared type (used for polymorphic type resolution).</param>
		/// <param name="entity">The object to serialize.</param>
		/// <param name="stream">The stream to write BSON bytes to. Any writable stream is supported: the
		/// document is built in a reusable buffer and written with a single <see cref="Stream.Write(byte[], int, int)"/>.</param>
		public virtual void SerializeToStream(Type type, object? entity, Stream stream)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));
			if (stream == null) throw new ArgumentNullException(nameof(stream));
			if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));

			var writer = DirectBuffers.RentWriter();

			this.SerializeToBytes(type, entity, writer);

			stream.Write(writer.Buffer, 0, writer.Position);

			DirectBuffers.ReturnWriter(writer);
		}
	}
}

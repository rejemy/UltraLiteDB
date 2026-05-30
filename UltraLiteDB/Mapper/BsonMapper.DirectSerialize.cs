using System;

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

            var writer = new ByteWriter(256);

            DirectBsonWriter.WriteObjectDirect(writer, this, type, entity, 0);

            // Trim buffer to actual size
            var result = new byte[writer.Position];
            System.Buffer.BlockCopy(writer.Buffer, 0, result, 0, writer.Position);
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
                BsonWriter.Serialize(doc);
                return;
            }

            DirectBsonWriter.WriteObjectDirect(writer, this, type, entity, 0);
        }
    }
}

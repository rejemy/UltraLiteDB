using System;

namespace UltraLiteDB
{
    public partial class BsonMapper
    {
        /// <summary>
        /// Serialize an entity directly to BSON bytes, bypassing intermediate BsonDocument creation
        /// </summary>
        public virtual byte[] SerializeToBytes<T>(T entity)
        {
            return this.SerializeToBytes(typeof(T), entity);
        }

        /// <summary>
        /// Serialize an entity directly to BSON bytes, bypassing intermediate BsonDocument creation
        /// </summary>
        public virtual void SerializeToBytes<T>(T entity, ByteWriter writer)
        {
            this.SerializeToBytes(typeof(T), entity, writer);
        }

        /// <summary>
        /// Serialize an entity directly to BSON bytes, bypassing intermediate BsonDocument creation
        /// </summary>
        public virtual byte[] SerializeToBytes(Type type, object entity)
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
        /// Serialize an entity directly to BSON bytes, bypassing intermediate BsonDocument creation
        /// </summary>
        public virtual void SerializeToBytes(Type type, object entity, ByteWriter writer)
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

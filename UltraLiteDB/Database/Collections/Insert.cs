using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        /// <summary>
        /// Inserts a new document into this collection. If the _id field is empty or missing, an auto-generated id is assigned
        /// and written back to the entity's id property.
        /// </summary>
        /// <param name="document">The entity to insert.</param>
        /// <returns>The document's _id value (auto-generated if applicable).</returns>
        public BsonValue Insert(T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var doc = _mapper.ToDocument(document);
            var removed = this.RemoveDocId(doc);

            var id = _engine.Value.Insert(_name, doc, _autoId);

            // checks if must update _id value in entity
            if (removed && _id != null && _id.Setter != null)
            {
                _id.Setter(document, id.RawValue);
            }

            return id;
        }

        /// <summary>
        /// Inserts a new document with an explicit _id value.
        /// </summary>
        /// <param name="id">The _id to assign to the document.</param>
        /// <param name="document">The entity to insert.</param>
        public void Insert(BsonValue id, T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            var doc = _mapper.ToDocument(document);

            doc["_id"] = id;

            _engine.Value.Insert(_name, doc);
        }

        /// <summary>
        /// Inserts multiple documents into this collection.
        /// </summary>
        /// <param name="docs">The documents to insert.</param>
        /// <returns>The number of documents inserted.</returns>
        public int Insert(IEnumerable<T> docs)
        {
            if (docs == null) throw new ArgumentNullException(nameof(docs));

            return _engine.Value.Insert(_name, this.GetBsonDocs(docs), _autoId);
        }

        /// <summary>
        /// Bulk-inserts documents, committing in batches to reduce memory pressure.
        /// </summary>
        /// <param name="docs">The documents to insert.</param>
        /// <param name="batchSize">Number of documents per transaction batch.</param>
        /// <returns>The number of documents inserted.</returns>
        public int InsertBulk(IEnumerable<T> docs, int batchSize = 5000)
        {
            if (docs == null) throw new ArgumentNullException(nameof(docs));

            return _engine.Value.InsertBulk(_name, this.GetBsonDocs(docs), batchSize, _autoId);
        }

        /// <summary>
        /// Bulk-upserts documents, committing in batches to reduce memory pressure.
        /// </summary>
        /// <param name="docs">The documents to upsert.</param>
        /// <param name="batchSize">Number of documents per transaction batch.</param>
        /// <returns>The number of documents inserted or updated.</returns>
        public int UpsertBulk(IEnumerable<T> docs, int batchSize = 5000)
        {
            if (docs == null) throw new ArgumentNullException(nameof(docs));

            return _engine.Value.UpsertBulk(_name, this.GetBsonDocs(docs), batchSize, _autoId);
        }

        /// <summary>
        /// Converts each entity to a <see cref="BsonDocument"/>, removing empty _id fields for auto-id generation,
        /// and writing generated ids back to the source entities.
        /// </summary>
        private IEnumerable<BsonDocument> GetBsonDocs(IEnumerable<T> documents)
        {
            foreach (var document in documents)
            {
                var doc = _mapper.ToDocument(document);
                var removed = this.RemoveDocId(doc);

                yield return doc;

                if (removed && _id != null && _id.Setter != null)
                {
                    _id.Setter(document!, doc["_id"].RawValue);
                }
            }
        }

        /// <summary>
        /// Converts a single entity to a <see cref="BsonDocument"/>, yielding it as an enumerable for engine methods
        /// that accept <c>IEnumerable&lt;BsonDocument&gt;</c>.
        /// </summary>
        private IEnumerable<BsonDocument> GetBsonDoc(T document)
        {
            var doc = _mapper.ToDocument(document);
            var removed = this.RemoveDocId(doc);

            yield return doc;

            if (removed && _id != null && _id.Setter != null)
            {
                _id.Setter(document!, doc["_id"].RawValue);
            }
        }

        /// <summary>
        /// Removes the _id field from the document if it contains a default/empty value for its auto-id type
        /// (e.g. 0 for Int32, <see cref="ObjectId.Empty"/> for ObjectId, <see cref="Guid.Empty"/> for Guid).
        /// This signals the engine to auto-generate a new _id on insert.
        /// </summary>
        /// <returns>True if the _id was removed and should be written back after insert.</returns>
        private bool RemoveDocId(BsonDocument doc)
        {
            if (_id != null && doc.TryGetValue("_id", out var id)) 
            {
                // check if exists _autoId and current id is "empty"
                if ((_autoId == BsonAutoId.Int32 && (id.IsInt32 && id.AsInt32 == 0)) ||
                    (_autoId == BsonAutoId.ObjectId && (id.IsNull || (id.IsObjectId && id.AsObjectId == ObjectId.Empty))) ||
                    (_autoId == BsonAutoId.Guid && id.IsGuid && id.AsGuid == Guid.Empty) ||
                    (_autoId == BsonAutoId.Int64 && id.IsInt64 && id.AsInt64 == 0))
                {
                    // in this cases, remove _id and set new value after
                    doc.Remove("_id");
                    return true;
                }
            }

            return false;   
        }
    }
}
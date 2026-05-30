using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Inserts or updates a single document. Attempts update first; if the document has no _id or is not found, inserts it.
        /// Returns true if the document was inserted (false if updated).
        /// </summary>
        public bool Upsert(string collection, BsonDocument doc, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            return this.WriteTransaction<bool>(collection, (col) =>
            {
                var inserted = false;

                // first try update document (if exists _id)
                // if not found, insert
                if (doc["_id"] == BsonValue.Null || this.UpdateDocument(col, doc) == false)
                {
                    this.InsertDocument(col, doc, autoId);
                    inserted = true;
                }

                // returns if document was inserted
                return inserted;
            });
        }

        /// <summary>
        /// Inserts or updates multiple documents. Returns the count of documents that were inserted (not updated).
        /// </summary>
        public int Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (docs == null) throw new ArgumentNullException(nameof(docs));

            return this.WriteTransaction<int>(collection, (col) =>
            {
                var count = 0;

                foreach (var doc in docs)
                {
                    // first try update document (if exists _id)
                    // if not found, insert
                    if (doc["_id"] == BsonValue.Null || this.UpdateDocument(col, doc) == false)
                    {
                        this.InsertDocument(col, doc, autoId);
                        count++;
                    }

                    _trans.CheckPoint();
                }

                // returns how many document was inserted
                return count;
            });
        }
    }
}
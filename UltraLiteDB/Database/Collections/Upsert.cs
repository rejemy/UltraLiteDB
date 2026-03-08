using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        /// <summary>
        /// Inserts the document if its _id doesn't exist, otherwise updates it.
        /// </summary>
        /// <param name="document">The entity to insert or update.</param>
        /// <returns>True if the document was inserted; false if it was updated.</returns>
        public bool Upsert(T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            return _engine.Value.Upsert(_name, this.GetBsonDoc(document), _autoId) == 1;
        }

        /// <summary>
        /// Inserts or updates multiple documents.
        /// </summary>
        /// <param name="documents">The entities to upsert.</param>
        /// <returns>The number of documents inserted (not updated).</returns>
        public int Upsert(IEnumerable<T> documents)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));

            return _engine.Value.Upsert(_name, this.GetBsonDocs(documents), _autoId);
        }

        /// <summary>
        /// Inserts or updates a document using an explicit _id value.
        /// </summary>
        /// <param name="id">The _id to assign to the document.</param>
        /// <param name="document">The entity to insert or update.</param>
        /// <returns>True if the document was inserted; false if it was updated.</returns>
        public bool Upsert(BsonValue id, T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            // get BsonDocument from object
            var doc = _mapper.ToDocument(document);

            // set document _id using id parameter
            doc["_id"] = id;

            return _engine.Value.Upsert(_name, doc);
        }
    }
}
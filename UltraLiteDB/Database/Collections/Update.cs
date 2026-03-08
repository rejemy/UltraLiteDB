using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        /// <summary>
        /// Updates a document in this collection. The document must have a valid _id field.
        /// </summary>
        /// <param name="document">The entity with updated values.</param>
        /// <returns>True if the document was found and updated; false if _id was not found.</returns>
        public bool Update(T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            // get BsonDocument from object
            var doc = _mapper.ToDocument(document);

            return _engine.Value.Update(_name, doc);
        }

        /// <summary>
        /// Updates a document using an explicit _id value, overriding any _id in the entity.
        /// </summary>
        /// <param name="id">The _id of the document to update.</param>
        /// <param name="document">The entity with updated values.</param>
        /// <returns>True if the document was found and updated; false if _id was not found.</returns>
        public bool Update(BsonValue id, T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            // get BsonDocument from object
            var doc = _mapper.ToDocument(document);

            // set document _id using id parameter
            doc["_id"] = id;

            return _engine.Value.Update(_name, new BsonDocument[] { doc }) > 0;
        }

        /// <summary>
        /// Updates multiple documents in this collection.
        /// </summary>
        /// <param name="documents">The entities with updated values. Each must have a valid _id.</param>
        /// <returns>The number of documents updated.</returns>
        public int Update(IEnumerable<T> documents)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));

            return _engine.Value.Update(_name, documents.Select(x => _mapper.ToDocument(x)));
        }
    }
}
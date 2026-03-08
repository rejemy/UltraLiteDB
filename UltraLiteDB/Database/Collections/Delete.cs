using System;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        /// <summary>
        /// Deletes all documents matching a query.
        /// </summary>
        /// <param name="query">The query to match documents for deletion. Requires an index on the queried field.</param>
        /// <returns>The number of documents deleted.</returns>
        public int Delete(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            return _engine.Value.Delete(_name, query);
        }

        /// <summary>
        /// Deletes a single document by its _id value.
        /// </summary>
        /// <param name="id">The _id of the document to delete.</param>
        /// <returns>True if the document was found and deleted; false otherwise.</returns>
        public bool Delete(BsonValue id)
        {
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            return this.Delete(Query.EQ("_id", id)) > 0;
        }
    }
}
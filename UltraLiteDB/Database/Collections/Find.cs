using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        #region Find

        /// <summary>
        /// Finds documents matching a <see cref="Query"/>. Results are lazily deserialized into <typeparamref name="T"/>.
        /// </summary>
        /// <param name="query">The query to filter documents. Requires an index on the queried field.</param>
        /// <param name="skip">Number of documents to skip from the beginning of the result set.</param>
        /// <param name="limit">Maximum number of documents to return.</param>
        /// <returns>Lazily enumerated matching documents.</returns>
        public IEnumerable<T> Find(Query query, int skip = 0, int limit = int.MaxValue)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var docs = _engine.Value.Find(_name, query, skip, limit);

            foreach(var doc in docs)
            {
                // get object from BsonDocument
                var obj = _mapper.ToObject<T>(doc);

                yield return obj;
            }
        }


        #endregion

        #region FindById + One + All

        /// <summary>
        /// Finds a single document by its _id value. Returns null/default if not found.
        /// </summary>
        /// <param name="id">The document _id to look up.</param>
        public T FindById(BsonValue id)
        {
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            return this.Find(Query.EQ("_id", id)).SingleOrDefault();
        }

        /// <summary>
        /// Returns the first document matching the query, or null/default if none match.
        /// Requires an index on the queried field.
        /// </summary>
        public T FindOne(Query query)
        {
            return this.Find(query).FirstOrDefault();
        }


        /// <summary>
        /// Returns all documents inside collection order by _id index.
        /// </summary>
        public IEnumerable<T> FindAll()
        {
            return this.Find(Query.All());
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Finds documents in a collection matching a <see cref="Query"/>, with optional skip/limit pagination.
        /// Uses a <see cref="QueryCursor"/> to stream results in batches.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="query">The query filter.</param>
        /// <param name="skip">Number of documents to skip.</param>
        /// <param name="limit">Maximum number of documents to return.</param>
        public IEnumerable<BsonDocument> Find(string collection, Query query, int skip = 0, int limit = int.MaxValue)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (query == null) throw new ArgumentNullException(nameof(query));

            _log.Write(Logger.COMMAND, "query documents in '{0}' => {1}", collection, query);

            using (var cursor = new QueryCursor(query, skip, limit))
            {

                // get my collection page
                var col = this.GetCollectionPage(collection, false);

                // no collection, no documents
                if (col == null) yield break;

                // get nodes from query executor to get all IndexNodes
                cursor.Initialize(query.Run(col, _indexer).GetEnumerator());

                _log.Write(Logger.QUERY, "{0} :: {1}", collection, query);

                // fill buffer with documents 
                cursor.Fetch(_trans, _data);
            

                // returing first documents in buffer
                foreach (var doc in cursor.Documents) yield return doc;

                // if still documents to read, continue
                while (cursor.HasMore)
                {
  
                    cursor.Fetch(_trans, _data);
                    

                    // return documents from buffer
                    foreach (var doc in cursor.Documents) yield return doc;
                }
            }
        }

        #region FindOne/FindById

        /// <summary>
        /// Returns the first document matching the query, or null if none found.
        /// </summary>
        public BsonDocument FindOne(string collection, Query query)
        {
            return this.Find(collection, query).FirstOrDefault();
        }

        /// <summary>
        /// Returns the document with the specified _id value, or null if not found.
        /// </summary>
        public BsonDocument FindById(string collection, BsonValue id)
        {
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            return this.Find(collection, Query.EQ("_id", id)).FirstOrDefault();
        }


        /// <summary>
        /// Returns all documents in the collection ordered by the _id index.
        /// </summary>
        public IEnumerable<BsonDocument> FindAll(string collection)
        {
            return this.Find(collection, Query.All());
        }

        #endregion
    }
}
using System;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Returns the minimum value from the specified index field in the given collection.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="field">The indexed field name to retrieve the minimum value from.</param>
        /// <returns>The minimum <see cref="BsonValue"/> in the index, or <see cref="BsonValue.MinValue"/> if the collection or index does not exist.</returns>
        public BsonValue Min(string collection, string field)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var col = this.GetCollectionPage(collection, false);

            if (col == null) return BsonValue.MinValue;

            // get index (no index, no min)
            var index = col.GetIndex(field);

            if (index == null) return BsonValue.MinValue;

            var head = _indexer.GetExistingNode(index.HeadNode);
            var next = _indexer.GetExistingNode(head.Next[0]);

            if (next.IsHeadTail(index)) return BsonValue.MinValue;

            return next.Key;
            
        }

        /// <summary>
        /// Returns the maximum value from the specified index field in the given collection.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="field">The indexed field name to retrieve the maximum value from.</param>
        /// <returns>The maximum <see cref="BsonValue"/> in the index, or <see cref="BsonValue.MaxValue"/> if the collection or index does not exist.</returns>
        public BsonValue Max(string collection, string field)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            var col = this.GetCollectionPage(collection, false);

            if (col == null) return BsonValue.MaxValue;

            // get index (no index, no max)
            var index = col.GetIndex(field);

            if (index == null) return BsonValue.MaxValue;

            var tail = _indexer.GetExistingNode(index.TailNode);
            var prev = _indexer.GetExistingNode(tail.Prev[0]);

            if (prev.IsHeadTail(index)) return BsonValue.MaxValue;

            return prev.Key;
        
        }

        /// <summary>
        /// Counts documents matching a query without fully deserializing them (unless the query requires filtering).
        /// If <paramref name="query"/> is <c>null</c>, returns the collection's stored document count.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="query">Optional query to filter documents. If <c>null</c>, returns total document count.</param>
        /// <returns>The number of matching documents, or 0 if the collection does not exist.</returns>
        public long Count(string collection, Query? query = null)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));

            var col = this.GetCollectionPage(collection, false);

            if (col == null) return 0;

            if (query == null) return col.DocumentCount;

            // run query in this collection
            var nodes = query.Run(col, _indexer);

            if (query.UseFilter)
            {
                // count distinct documents
                return nodes
                    .Select(x => BsonReader.Deserialize(_data.Read(x.DataBlock)).AsDocument!)
                    .Where(x => query.FilterDocument(x))
                    .Distinct()
                    .LongCount();
            }
            else
            {
                // count distinct nodes based on DataBlock
                return nodes
                    .Select(x => x.DataBlock)
                    .Distinct()
                    .LongCount();
            }
        }

        /// <summary>
        /// Checks whether at least one document matching the query exists in the collection.
        /// Avoids full deserialization when an index is used without filtering.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="query">The query to evaluate.</param>
        /// <returns><c>true</c> if at least one matching document exists; otherwise <c>false</c>.</returns>
        public bool Exists(string collection, Query query)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (query == null) throw new ArgumentNullException(nameof(query));


            var col = this.GetCollectionPage(collection, false);

            if (col == null) return false;

            // run query in this collection
            var nodes = query.Run(col, _indexer);

            if (query.UseFilter)
            {
                // check if has at least first document
                return nodes
                    .Select(x => BsonReader.Deserialize(_data.Read(x.DataBlock)).AsDocument!)
                    .Where(x => query.FilterDocument(x))
                    .Any();
            }
            else
            {
                var first = nodes.FirstOrDefault();

                // check if has at least first node
                return first != null;
            }
        
        }
    }
}

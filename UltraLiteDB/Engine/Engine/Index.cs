using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {

        /// <summary>
        /// Creates a new index on the specified field, or returns false if the index already exists.
        /// Reads all existing documents to populate the new index. Maximum 16 indexes per collection.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="field">The field expression to index (supports dot notation).</param>
        /// <param name="unique">If true, enforces unique values for this index.</param>
        public bool EnsureIndex(string collection, string field, bool unique = false)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (!CollectionIndex.IndexPattern.IsMatch(field)) throw new ArgumentException("Invalid field format pattern: " + CollectionIndex.IndexPattern.ToString(), "field");
            if (field == "_id") return false; // always exists

            return this.Transaction<bool>(collection, true, (col) =>
            {
                // check if index already exists
                var current = col.GetIndex(field);

                // if already exists, just exit
                if (current != null)
                {
                    // do not test any difference between current index and new defition
                    return false;
                }

                // create index head
                var index = _indexer.CreateIndex(col);

                index.Field = field;
                index.Unique = unique;

                _log.Write(Logger.COMMAND, "create index on '{0}' :: {1} unique: {2}", collection, index.Field, unique);

                // read all objects (read from PK index)
                foreach (var pkNode in new QueryAll("_id", Query.Ascending).Run(col, _indexer))
                {
                    // read binary and deserialize document
                    var buffer = _data.Read(pkNode.DataBlock);
                    var doc = BsonReader.Deserialize(buffer).AsDocument;
                    var expr = new BsonFields(index.Field);

                    // get value from document
                    var keys = expr.Execute(doc, true);

                    // adding index node for each value
                    foreach (var key in keys)
                    {
                        // insert new index node
                        var node = _indexer.AddNode(index, key, pkNode);

                        // link index node to datablock
                        node.DataBlock = pkNode.DataBlock;
                    }

                    // check memory usage
                    _trans.CheckPoint();
                }

                return true;
            });
        }

        /// <summary>
        /// Removes an index from a collection. Returns false if the collection or index does not exist.
        /// The _id index cannot be dropped.
        /// </summary>
        public bool DropIndex(string collection, string field)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            if (field == "_id") throw UltraLiteException.IndexDropId();

            return this.Transaction<bool>(collection, false, (col) =>
            {
                // no collection, no index
                if (col == null) return false;

                // search for index reference
                var index = col.GetIndex(field);

                // no index, no drop
                if (index == null) return false;

                _log.Write(Logger.COMMAND, "drop index on '{0}' :: '{1}'", collection, field);

                // delete all data pages + indexes pages
                _indexer.DropIndex(index);

                // clear index reference
                index.Clear();

                // mark collection page as dirty
                _pager.SetDirty(col);

                return true;
            });
        }

        /// <summary>
        /// Enumerates all indexes defined on a collection, including the _id primary key index.
        /// </summary>
        public IEnumerable<IndexInfo> GetIndexes(string collection)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));

            var col = this.GetCollectionPage(collection, false);

            if (col == null) yield break;

            foreach (var index in col.GetIndexes(true))
            {
                yield return new IndexInfo(index);
            }
        }
    }
}
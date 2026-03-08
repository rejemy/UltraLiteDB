using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Updates a single document in a collection by its _id. Returns true if the document was found and updated.
        /// </summary>
        public bool Update(string collection, BsonDocument doc)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            return this.Transaction<bool>(collection, false, (col) =>
            {
                // no collection, no updates
                if (col == null) return false;

                var updated = false;

                if (this.UpdateDocument(col, doc))
                {
                    updated = true;
                }

                return updated;
            });
        }

        /// <summary>
        /// Updates multiple documents in a collection. Returns the number of documents successfully updated.
        /// </summary>
        public int Update(string collection, IEnumerable<BsonDocument> docs)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (docs == null) throw new ArgumentNullException(nameof(docs));

            return this.Transaction<int>(collection, false, (col) =>
            {
                // no collection, no updates
                if (col == null) return 0;

                var count = 0;

                foreach (var doc in docs)
                {
                    if (this.UpdateDocument(col, doc))
                    {
                        _trans.CheckPoint();
                        count++;
                    }
                }

                return count;
            });
        }

        /// <summary>
        /// Core update logic: finds the document by _id, re-serializes it, updates data storage,
        /// and reconciles all secondary index nodes (deleting stale keys, inserting new ones).
        /// </summary>
        private bool UpdateDocument(CollectionPage col, BsonDocument doc)
        {
            // normalize id before find
            var id = doc["_id"];

            // validate id for null, min/max values
            if (id.IsNull || id.IsMinValue || id.IsMaxValue)
            {
                throw UltraLiteException.InvalidDataType("_id", id);
            }

            _log.Write(Logger.COMMAND, "update document on '{0}' :: _id = {1}", col.CollectionName, id.RawValue);

            // find indexNode from pk index
            var pkNode = _indexer.Find(col.PK, id, false, Query.Ascending);

            // if not found document, no updates
            if (pkNode == null) return false;

            // serialize document in bytes
            var bytes = BsonWriter.Serialize(doc);

            // update data storage
            var dataBlock = _data.Update(col, pkNode.DataBlock, bytes);

            // get all non-pk index nodes from this data block
            var allNodes = _indexer.GetNodeList(pkNode, false).ToArray();

            // delete/insert indexes - do not touch on PK
            foreach (var index in col.GetIndexes(false))
            {
                var expr = new BsonFields(index.Field);

                // getting all keys do check
                var keys = expr.Execute(doc).ToArray();

                // get a list of to delete nodes (using ToArray to resolve now)
                var toDelete = allNodes
                    .Where(x => x.Slot == index.Slot && !keys.Any(k => k == x.Key))
                    .ToArray();

                // get a list of to insert nodes (using ToArray to resolve now)
                var toInsert = keys
                    .Where(x => !allNodes.Any(k => k.Slot == index.Slot && k.Key == x))
                    .ToArray();

                // delete changed index nodes
                foreach (var node in toDelete)
                {
                    _indexer.Delete(index, node.Position);
                }

                // insert new nodes
                foreach (var key in toInsert)
                {
                    // and add a new one
                    var node = _indexer.AddNode(index, key, pkNode);

                    // link my node to data block
                    node.DataBlock = dataBlock.Position;
                }
            }

            return true;
        }
    }
}
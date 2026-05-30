using System;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Deletes a single document by its <c>_id</c> value.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="id">The <c>_id</c> value of the document to delete.</param>
        /// <returns><c>true</c> if the document was found and deleted; otherwise <c>false</c>.</returns>
        public bool Delete(string collection, BsonValue id)
        {
            return this.Delete(collection, Query.EQ("_id", id)) == 1;
        }

        /// <summary>
        /// Deletes all documents matching a query. Removes associated index nodes and data pages.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="query">The query selecting documents to delete.</param>
        /// <returns>The number of documents deleted.</returns>
        public int Delete(string collection, Query query)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (query == null) throw new ArgumentNullException(nameof(query));

            return this.Transaction<int>(collection, (col) =>
            {
                if (col == null) return 0;

                _log.Write(Logger.COMMAND, "delete documents in '{0}'", collection);

                var nodes = query.Run(col, _indexer);

                _log.Write(Logger.QUERY, "{0} :: {1}", collection, query);

                var count = 0;

                foreach (var node in nodes)
                {
                    // checks if cache are full
                    _trans.CheckPoint();

                    // if use filter need deserialize document
                    if (query.UseFilter)
                    {
                        var buffer = _data.Read(node.DataBlock);
                        var doc = BsonReader.Deserialize(buffer).AsDocument!;

                        if (query.FilterDocument(doc) == false) continue;
                    }
                    
                    _log.Write(Logger.COMMAND, "delete document :: _id = {0}", node.Key.RawValue);

                    // get all indexes nodes from this data block
                    var allNodes = _indexer.GetNodeList(node, true).ToArray();

                    // lets remove all indexes that point to this in dataBlock
                    foreach (var linkNode in allNodes)
                    {
                        var index = col.Indexes[linkNode.Slot];

                        _indexer.Delete(index, linkNode.Position);
                    }

                    // remove object data
                    _data.Delete(col, node.DataBlock);

                    count++;
                }

                return count;
            });
        }
    }
}
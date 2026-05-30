using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    /// <summary>
    /// Manages collection lifecycle: creating, retrieving, listing, and dropping collections.
    /// Coordinates with <see cref="PageService"/>, <see cref="IndexService"/>, and the header page.
    /// </summary>
    internal class CollectionService
    {
        private PageService _pager;
        private IndexService _indexer;
        private DataService _data;
        private TransactionService _trans;
        private Logger _log;

        public CollectionService(PageService pager, IndexService indexer, DataService data, TransactionService trans, Logger log)
        {
            _pager = pager;
            _indexer = indexer;
            _data = data;
            _trans = trans;
            _log = log;
        }

        /// <summary>
        /// Gets a collection page by name. Returns null if the collection does not exist.
        /// </summary>
        public CollectionPage? Get(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            var header = _pager.GetPage<HeaderPage>(0);

            uint pageID;

            if (header.CollectionPages.TryGetValue(name, out pageID))
            {
                return _pager.GetPage<CollectionPage>(pageID);
            }

            return null;
        }

        /// <summary>
        /// Creates a new collection with a _id primary key index. Validates name format and collection count limits.
        /// </summary>
        public CollectionPage Add(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (!CollectionPage.NamePattern.IsMatch(name)) throw UltraLiteException.InvalidFormat(name);

            _log.Write(Logger.COMMAND, "creating new collection '{0}'", name);

            // get header marked as dirty because I will use header after (and NewPage can get another header instance)
            var header = _pager.GetPage<HeaderPage>(0);

            // check limit count (8 bytes per collection = 4 to string length, 4 for uint pageID)
            if (header.CollectionPages.Sum(x => x.Key.Length + 8) + name.Length + 8 >= CollectionPage.MAX_COLLECTIONS_SIZE)
            {
                throw UltraLiteException.CollectionLimitExceeded(CollectionPage.MAX_COLLECTIONS_SIZE);
            }

            // get new collection page (marked as dirty)
            var col = _pager.NewPage<CollectionPage>();

            // add this page to header page collection
            header.CollectionPages.Add(name, col.PageID);

            col.CollectionName = name;

            // set header page as dirty
            _pager.SetDirty(header);

            // create PK index
            var pk = _indexer.CreateIndex(col);

            pk.Field = "_id";
            pk.Unique = true;

            return col;
        }

        /// <summary>
        /// Enumerates all collection pages in the database.
        /// </summary>
        public IEnumerable<CollectionPage> GetAll()
        {
            var header = _pager.GetPage<HeaderPage>(0);

            foreach (var pageID in header.CollectionPages.Values)
            {
                yield return _pager.GetPage<CollectionPage>(pageID);
            }
        }

        /// <summary>
        /// Drops a collection by deleting all its data pages, index pages, extend pages, and the collection page itself.
        /// </summary>
        public void Drop(CollectionPage col)
        {
            // add all pages to delete
            var pages = new HashSet<uint>();

            // search for all data page and index page
            foreach (var index in col.GetIndexes(true))
            {
                // get all nodes from index
                var nodes = _indexer.FindAll(index, Query.Ascending);

                foreach (var node in nodes)
                {
                    // if is PK index, add dataPages
                    if (index.Slot == 0)
                    {
                        pages.Add(node.DataBlock.PageID);

                        // read datablock to check if there is any extended page
                        var block = _data.GetBlock(node.DataBlock);

                        if (block.ExtendPageID != uint.MaxValue)
                        {
                            _pager.DeletePage(block.ExtendPageID, true);
                        }
                    }

                    // memory checkpoint
                    _trans.CheckPoint();

                    // add index page to delete list page
                    pages.Add(node.Position.PageID);
                }

                // remove head+tail nodes in all indexes
                pages.Add(index.HeadNode.PageID);
                pages.Add(index.TailNode.PageID);
            }

            // and now, lets delete all this pages
            foreach (var pageID in pages)
            {
                // delete page
                _pager.DeletePage(pageID);

                // memory checkpoint
                _trans.CheckPoint();
            }

            // get header page to remove from collection list links
            var header = _pager.GetPage<HeaderPage>(0);

            header.CollectionPages.Remove(col.CollectionName);

            // set header as dirty after remove
            _pager.SetDirty(header);

            _pager.DeletePage(col.PageID);
        }
    }
}
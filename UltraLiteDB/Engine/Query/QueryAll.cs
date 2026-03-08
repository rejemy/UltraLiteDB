using System.Collections.Generic;

namespace UltraLiteDB
{
    /// <summary>
    /// Returns all documents by scanning an index in the specified order. Used for unfiltered enumeration.
    /// </summary>
    internal class QueryAll : Query
    {
        private int _order;

        public QueryAll(string field, int order)
            : base(field)
        {
            _order = order;
        }

        internal override IEnumerable<IndexNode> ExecuteIndex(IndexService indexer, CollectionIndex index)
        {
            return indexer.FindAll(index, _order);
        }

        internal override bool FilterDocument(BsonDocument doc)
        {
            return true;
        }

        public override string ToString()
        {
            return string.Format("{0}({1})",
                this.UseFilter ? "Filter" : this.UseIndex ? "Scan" : "",
                this.Field);
        }
    }
}
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
	/// <summary>
	/// Not-equal (!=) query. Performs an Index Scan, filtering out nodes that match the value.
	/// </summary>
	internal class QueryNotEquals : Query
    {
        private BsonValue _value;

        public QueryNotEquals(string field, BsonValue value)
            : base(field)
        {
            _value = value;
        }


        internal override IEnumerable<IndexNode> ExecuteIndex(IndexService indexer, CollectionIndex index)
        {
            return indexer
                .FindAll(index, Query.Ascending)
                .Where(x => x.Key.CompareTo(_value) != 0);
        }


        internal override bool FilterDocument(BsonDocument doc)
        {
            return this.Expression.Execute(doc, true)
                .Any(x => x.CompareTo(_value) != 0);
        }

        public override string ToString()
        {
            return string.Format("{0}({1} != {2})",
                this.UseFilter ? "Filter" : this.UseIndex ? "Scan" : "",
                this.Field,
                _value);
        }

    }
}
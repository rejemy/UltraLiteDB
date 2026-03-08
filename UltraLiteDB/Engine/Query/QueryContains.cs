using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
	/// <summary>
	/// Substring match query. Always performs an Index Scan since substring matching cannot use seek.
	/// </summary>
	internal class QueryContains : Query
    {
        private BsonValue _value;

        public QueryContains(string field, BsonValue value)
            : base(field)
        {
            _value = value;
        }

        internal override IEnumerable<IndexNode> ExecuteIndex(IndexService indexer, CollectionIndex index)
        {
            return indexer
                .FindAll(index, Query.Ascending)
                .Where(x => x.Key.IsString && x.Key.AsString.Contains(_value));
        }

        internal override bool FilterDocument(BsonDocument doc)
        {
            return this.Expression.Execute(doc, false)
                .Where(x => x.IsString)
                .Any(x => x.AsString.Contains(_value));
        }

        public override string ToString()
        {
            return string.Format("{0}({1} contains {2})",
                this.UseFilter ? "Filter" : this.UseIndex ? "Scan" : "",
                this.Field,
                _value);
        }
    }
}
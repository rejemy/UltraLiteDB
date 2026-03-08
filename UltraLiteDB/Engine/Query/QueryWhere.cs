using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    /// <summary>
    /// Predicate-based query that applies a <c>Func&lt;BsonValue, bool&gt;</c> against index values.
    /// Performs a full Index Scan but avoids deserializing documents.
    /// </summary>
    internal class QueryWhere : Query
    {
        private Func<BsonValue, bool> _func;
        private int _order;

        public QueryWhere(string field, Func<BsonValue, bool> func, int order)
            : base(field)
        {
            _func = func;
            _order = order;
        }

        internal override IEnumerable<IndexNode> ExecuteIndex(IndexService indexer, CollectionIndex index)
        {
            return indexer
                .FindAll(index, _order)
                .Where(i => _func(i.Key));
        }

        internal override bool FilterDocument(BsonDocument doc)
        {
            return this.Expression.Execute(doc, true)
                .Any(x => _func(x));
        }

        public override string ToString()
        {
            return string.Format("{0}({1}[{2}])",
                this.UseFilter ? "Filter" : this.UseIndex ? "Scan" : "",
                _func.ToString(),
                this.Field);
        }
    }
}
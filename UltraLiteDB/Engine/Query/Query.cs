using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    /// <summary>
    /// Abstract base class for all query types. Provides static factory methods to build composable queries.
    /// Execution strategies: Index Seek (fast, exact match), Index Scan (walks index range), Full Scan (checks every document).
    /// </summary>
    public abstract class Query
    {
        /// <summary>The field name this query operates on.</summary>
        public string? Field { get; private set; }

        /// <summary>Parsed field expression used for evaluating document values during full scan.</summary>
        internal BsonFields Expression { get; set; } = null!;
        /// <summary>True if this query will use an index for execution.</summary>
        internal virtual bool UseIndex { get; set; }
        /// <summary>True if this query requires full-scan document filtering.</summary>
        internal virtual bool UseFilter { get; set; }

        internal Query(string? field)
        {
            this.Field = field;
        }

        #region Static Methods

        /// <summary>Ascending index traversal order.</summary>
        public const int Ascending = 1;

        /// <summary>Descending index traversal order.</summary>
        public const int Descending = -1;

        /// <summary>
        /// Returns all documents ordered by the _id index.
        /// </summary>
        public static Query All(int order = Ascending)
        {
            return new QueryAll("_id", order);
        }

        /// <summary>
        /// Returns all documents ordered by the specified field's index.
        /// </summary>
        public static Query All(string field, int order = Ascending)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryAll(field, order);
        }

        /// <summary>
        /// Returns all documents where the field equals the value (=). Uses Index Seek when an index exists.
        /// </summary>
        public static Query EQ(string field, BsonValue? value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryEquals(field, value ?? BsonValue.Null);
        }

        /// <summary>
        /// Returns all documents where the field is less than the value (&lt;).
        /// </summary>
        public static Query LT(string field, BsonValue? value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryLess(field, value ?? BsonValue.Null, false);
        }

        /// <summary>
        /// Returns all documents where the field is less than or equal to the value (&lt;=).
        /// </summary>
        public static Query LTE(string field, BsonValue? value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryLess(field, value ?? BsonValue.Null, true);
        }

        /// <summary>
        /// Returns all documents where the field is greater than the value (&gt;).
        /// </summary>
        public static Query GT(string field, BsonValue? value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryGreater(field, value ?? BsonValue.Null, false);
        }

        /// <summary>
        /// Returns all documents where the field is greater than or equal to the value (&gt;=).
        /// </summary>
        public static Query GTE(string field, BsonValue? value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryGreater(field, value ?? BsonValue.Null, true);
        }

        /// <summary>
        /// Returns all documents where the field value falls between start and end (inclusive/exclusive configurable).
        /// </summary>
        public static Query Between(string field, BsonValue ?start, BsonValue? end, bool startEquals = true, bool endEquals = true)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryBetween(field, start ?? BsonValue.Null, end ?? BsonValue.Null, startEquals, endEquals);
        }

        /// <summary>
        /// Returns all documents where the string field starts with the specified prefix. Uses Index Seek.
        /// </summary>
        public static Query StartsWith(string field, string value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (value.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(value));

            return new QueryStartsWith(field, value);
        }

        /// <summary>
        /// Returns all documents where the string field contains the specified substring. Always uses Index Scan.
        /// </summary>
        public static Query Contains(string field, string value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (value.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(value));

            return new QueryContains(field, value);
        }

        /// <summary>
        /// Returns all documents where the field does not equal the value (!=). Uses Index Scan.
        /// </summary>
        public static Query Not(string field, BsonValue? value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            return new QueryNotEquals(field, value ?? BsonValue.Null);
        }

        /// <summary>
        /// Negates a query — returns all documents NOT in the inner query's result set.
        /// </summary>
        public static Query Not(Query query, int order = Query.Ascending)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            return new QueryNot(query, order);
        }

        /// <summary>
        /// Returns all documents where the field matches any value in the list (IN).
        /// </summary>
        public static Query In(string field, BsonArray value)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return new QueryIn(field, value.RawValue);
        }

        /// <summary>
        /// Returns all documents where the field matches any value in the list (IN).
        /// </summary>
        public static Query In(string field, params BsonValue[] values)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (values == null) throw new ArgumentNullException(nameof(values));

            return new QueryIn(field, values);
        }

        /// <summary>
        /// Returns all documents where the field matches any value in the list (IN).
        /// </summary>
        public static Query In(string field, IEnumerable<BsonValue> values)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (values == null) throw new ArgumentNullException(nameof(values));

            return new QueryIn(field, values);
        }

        /// <summary>
        /// Applies a predicate function against index values. Performs an Index Scan (faster than full document deserialization).
        /// </summary>
        public static Query Where(string field, Func<BsonValue, bool> predicate, int order = Query.Ascending)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return new QueryWhere(field, predicate, order);
        }

        /// <summary>
        /// Returns documents matching BOTH queries (intersection). The left query has index preference;
        /// the right side falls back to full scan. Automatically optimizes GT+LT on the same field into Between.
        /// </summary>
        public static Query And(Query left, Query right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            // test if can use QueryBetween because it's more efficient
            if (left is QueryGreater && right is QueryLess && left.Field == right.Field)
            {
                var l = left as QueryGreater;
                var r = right as QueryLess;

                return Between(l!.Field!, l.Value, r!.Value, l.IsEquals, r.IsEquals);
            }

            return new QueryAnd(left, right);
        }

        /// <summary>
        /// Returns documents matching ALL queries by chaining pairwise And operations.
        /// </summary>
        public static Query And(params Query[] queries)
        {
            if (queries == null || queries.Length < 2) throw new ArgumentException("At least two Query should be passed");

            var left = queries[0];

            for (int i = 1; i < queries.Length; i++)
            {
                left = And(left, queries[i]);
            }
            return left;
        }

        /// <summary>
        /// Returns documents matching EITHER query (union).
        /// </summary>
        public static Query Or(Query left, Query right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            return new QueryOr(left, right);
        }

        /// <summary>
        /// Returns documents matching ANY query by chaining pairwise Or operations.
        /// </summary>
        public static Query Or(params Query[] queries)
        {
            if (queries == null || queries.Length < 2) throw new ArgumentException("At least two Query should be passed");

            var left = queries[0];

            for (int i = 1; i < queries.Length; i++)
            {
                left = Or(left, queries[i]);
            }
            return left;
        }

        #endregion

        #region Executing Query

        /// <summary>
        /// Determines the execution strategy (index vs full scan) and returns matching <see cref="IndexNode"/> entries.
        /// </summary>
        internal virtual IEnumerable<IndexNode> Run(CollectionPage col, IndexService indexer)
        {
            // get index for this query
            var index = col.GetIndex(this.Field);

            // if index not found, must use Filter (full scan)
            if (index == null)
            {
                this.UseFilter = true;

                // create expression based on Field
                this.Expression = new BsonFields(this.Field);

                // returns all index nodes - (will use Filter method later)
                return indexer.FindAll(col.PK, Query.Ascending);
            }
            else
            {
                this.UseIndex = true;

                this.Expression = new BsonFields(index.Field);

                // execute query to get all IndexNodes
                // do DistinctBy datablock to not duplicate same document in results
                return this.ExecuteIndex(indexer, index)
                    .DistinctBy(x => x.DataBlock, null);
            }
        }

        /// <summary>
        /// Executes the query against a specific index, returning matching <see cref="IndexNode"/> entries.
        /// </summary>
        internal abstract IEnumerable<IndexNode> ExecuteIndex(IndexService indexer, CollectionIndex index);

        /// <summary>
        /// Full-scan filter: returns true if the deserialized document satisfies this query's condition.
        /// </summary>
        internal abstract bool FilterDocument(BsonDocument doc);

        #endregion
    }
}
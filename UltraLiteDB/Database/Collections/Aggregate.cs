using System;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        #region Count

        /// <summary>
        /// Returns the total number of documents in this collection.
        /// </summary>
        public int Count()
        {
            // do not use indexes - collections has DocumentCount property
            return (int)_engine.Value.Count(_name, null);
        }

        /// <summary>
        /// Counts documents matching a query without deserializing them. Requires an index on the queried field.
        /// </summary>
        public int Count(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            return (int)_engine.Value.Count(_name, query);
        }


        #endregion

        #region LongCount

        /// <summary>
        /// Returns the total number of documents in this collection as a 64-bit integer.
        /// </summary>
        public long LongCount()
        {
            // do not use indexes - collections has DocumentCount property
            return _engine.Value.Count(_name, null);
        }

        /// <summary>
        /// Counts documents matching a query as a 64-bit integer, without deserializing them. Requires an index on the queried field.
        /// </summary>
        public long LongCount(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            return _engine.Value.Count(_name, query);
        }


        #endregion

        #region Exists

        /// <summary>
        /// Returns true if at least one document matches the query, without deserializing. Requires an index on the queried field.
        /// </summary>
        public bool Exists(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            return _engine.Value.Exists(_name, query);
        }


        #endregion

        #region Min/Max

        /// <summary>
        /// Returns the minimum value from an indexed field.
        /// </summary>
        /// <param name="field">The indexed field name.</param>
        public BsonValue Min(string field)
        {
            if (string.IsNullOrEmpty(field)) throw new ArgumentNullException(nameof(field));

            return _engine.Value.Min(_name, field);
        }


        /// <summary>
        /// Returns the maximum value from an indexed field.
        /// </summary>
        /// <param name="field">The indexed field name.</param>
        public BsonValue Max(string field)
        {
            if (string.IsNullOrEmpty(field)) throw new ArgumentNullException(nameof(field));

            return _engine.Value.Max(_name, field);
        }



        #endregion
    }
}
using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    public partial class UltraLiteCollection<T>
    {
        /// <summary>
        /// Creates an index on the specified field if it doesn't already exist. Indexes existing documents in the collection.
        /// </summary>
        /// <param name="field">Document field name (case sensitive). Use dot notation for nested fields (e.g. "Address.City").</param>
        /// <param name="unique">If true, duplicate values in this field are rejected.</param>
        /// <returns>True if the index was created; false if it already existed.</returns>
        public bool EnsureIndex(string field, bool unique = false)
        {
            if (string.IsNullOrEmpty(field)) throw new ArgumentNullException(nameof(field));

            return _engine.Value.EnsureIndex(_name, field, unique);
        }


        /// <summary>
        /// Returns all indexes information
        /// </summary>
        public IEnumerable<IndexInfo> GetIndexes()
        {
            return _engine.Value.GetIndexes(_name);
        }

        /// <summary>
        /// Drops an index on the specified field, freeing the index slot (max 16 indexes per collection).
        /// </summary>
        /// <param name="field">The indexed field name to drop.</param>
        /// <returns>True if the index was dropped; false if it didn't exist.</returns>
        public bool DropIndex(string field)
        {
            return _engine.Value.DropIndex(_name, field);
        }
    }
}
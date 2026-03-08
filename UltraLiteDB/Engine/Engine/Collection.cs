using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Returns all collection names stored in the database header page.
        /// </summary>
        /// <returns>An enumerable of collection name strings.</returns>
        public IEnumerable<string> GetCollectionNames()
        {

            var header = _pager.GetPage<HeaderPage>(0);

            return header.CollectionPages.Keys.AsEnumerable();
            
        }

        /// <summary>
        /// Drops a collection including all its documents, indexes, and extended pages.
        /// </summary>
        /// <param name="collection">The collection name to drop.</param>
        /// <returns><c>true</c> if the collection was found and dropped; <c>false</c> if it did not exist.</returns>
        public bool DropCollection(string collection)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));

            return this.Transaction<bool>(collection, false, (col) =>
            {
                if (col == null) return false;

                _log.Write(Logger.COMMAND, "drop collection {0}", collection);

                _collections.Drop(col);

                return true;
            });
        }

        /// <summary>
        /// Renames a collection. Throws if <paramref name="newName"/> already exists.
        /// </summary>
        /// <param name="collection">The current collection name.</param>
        /// <param name="newName">The new collection name.</param>
        /// <returns><c>true</c> if the collection was found and renamed; <c>false</c> if it did not exist.</returns>
        public bool RenameCollection(string collection, string newName)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (newName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(newName));

            return this.Transaction<bool>(collection, false, (col) =>
            {
                if (col == null) return false;

                _log.Write(Logger.COMMAND, "rename collection '{0}' -> '{1}'", collection, newName);

                // check if newName already exists
                if (this.GetCollectionNames().Contains(newName, StringComparer.OrdinalIgnoreCase))
                {
                    throw UltraLiteException.AlreadyExistsCollectionName(newName);
                }

                // change collection name and save
                col.CollectionName = newName;

                // set collection page as dirty
                _pager.SetDirty(col);

                // update header collection reference
                var header = _pager.GetPage<HeaderPage>(0);

                header.CollectionPages.Remove(collection);
                header.CollectionPages.Add(newName, col.PageID);

                _pager.SetDirty(header);

                return true;
            });
        }
    }
}
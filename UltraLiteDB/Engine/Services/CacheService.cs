using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    /// <summary>
    /// In-memory page cache that separates clean (read-only) and dirty (modified) pages.
    /// Thread-safe for concurrent read/write access.
    /// </summary>
    internal class CacheService
    {
        /// <summary>Clean (unmodified) pages cached from disk reads.</summary>
        private Dictionary<uint, BasePage> _clean = new Dictionary<uint, BasePage>();

        /// <summary>Dirty (modified) pages pending write to disk. Pages move here from _clean when modified.</summary>
        private Dictionary<uint, BasePage> _dirty = new Dictionary<uint, BasePage>();

        private Logger _log;

        public CacheService(Logger log)
        {
            _log = log;
        }

        /// <summary>
        /// Gets a page from the dirty or clean cache. Returns null if not cached. Thread-safe.
        /// </summary>
        public BasePage GetPage(uint pageID)
        {
            // try get page from dirty cache or from clean list
            lock(this)
            {
                var page =
                    _dirty.GetOrDefault(pageID) ??
                    _clean.GetOrDefault(pageID);
                return page;
            }
        }

        /// <summary>
        /// Adds a clean page to the cache. Thread-safe.
        /// </summary>
        public void AddPage(BasePage page)
        {
            if (page.IsDirty) throw new NotSupportedException("Page can't be dirty");

            lock(this)
            {
                _clean[page.PageID] = page;
            }
        }

        /// <summary>
        /// Marks a page as dirty, moving it from the clean cache to the dirty cache. Thread-safe.
        /// </summary>
        public void SetDirty(BasePage page)
        {
            lock(this)
            {
                _clean.Remove(page.PageID);
                page.IsDirty = true;
                _dirty[page.PageID] = page;
            }
        }

        /// <summary>
        /// Returns all dirty pages for flushing to disk.
        /// </summary>
        public ICollection<BasePage> GetDirtyPages()
        {
            return _dirty.Values;
        }

        /// <summary>
        /// Number of clean pages currently cached.
        /// </summary>
        public int CleanUsed { get { return _clean.Count; } }

        /// <summary>
        /// Number of dirty pages pending write.
        /// </summary>
        public int DirtyUsed { get { return _dirty.Count; } }

        /// <summary>
        /// Discards all dirty pages (used on transaction rollback). Thread-safe.
        /// </summary>
        public void DiscardDirtyPages()
        {
            _log.Write(Logger.CACHE, "clearing dirty pages from cache");

            lock(this)
            {
                _dirty.Clear();
            }
        }

        /// <summary>
        /// Promotes all dirty pages to clean status after a successful flush. Thread-safe.
        /// </summary>
        public void MarkDirtyAsClean()
        {
            lock(this)
            {
                foreach(var p in _dirty)
                {
                    p.Value.IsDirty = false;
                    _clean[p.Key] = p.Value;
                }

                _dirty.Clear();
            }
        }

        /// <summary>
        /// Evicts all clean pages from cache to free memory. Thread-safe.
        /// </summary>
        public void ClearPages()
        {
            lock(this)
            {
                _log.Write(Logger.CACHE, "cleaning cache");
                _clean.Clear();
            }
        }
    }
}
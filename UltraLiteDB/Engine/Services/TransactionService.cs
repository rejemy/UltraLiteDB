using System;
using System.Linq;

namespace UltraLiteDB
{
    /// <summary>
    /// Manages write transactions: checkpointing dirty pages to disk, journal-based crash recovery,
    /// and cache eviction when memory limits are reached.
    /// </summary>
    internal class TransactionService
    {
        private IDiskService _disk;
        private AesEncryption? _crypto;
        private PageService _pager;
        private CacheService _cache;
        private Logger _log;
        private int _cacheSize;

        internal TransactionService(IDiskService disk, AesEncryption? crypto, PageService pager, CacheService cache, int cacheSize, Logger log)
        {
            _disk = disk;
            _crypto = crypto;
            _cache = cache;
            _pager = pager;
            _cacheSize = cacheSize;
            _log = log;
        }

        /// <summary>
        /// Evicts clean pages from cache when usage exceeds the configured limit.
        /// Called after each document operation to bound memory usage. Returns true if pages were evicted.
        /// </summary>
        public bool CheckPoint()
        {
            if (_cache.CleanUsed > _cacheSize)
            {
                _log.Write(Logger.CACHE, "cache size reached {0} pages, will clear now", _cache.CleanUsed);

                _cache.ClearPages();

                return true;
            }

            return false;
        }

        /// <summary>
        /// Flushes all dirty pages to disk. Writes the journal first (if enabled), then the data pages,
        /// then clears the recovery flag. Ensures crash-safe writes via write-ahead journaling.
        /// </summary>
        public void PersistDirtyPages()
        {
            // get header page
            var header = _pager.GetPage<HeaderPage>(0);

            // increase file changeID (back to 0 when overflow)
            header.ChangeID = header.ChangeID == ushort.MaxValue ? (ushort)0 : (ushort)(header.ChangeID + (ushort)1);

            // mark header as dirty
            _pager.SetDirty(header);

            _log.Write(Logger.DISK, "begin disk operations - changeID: {0}", header.ChangeID);

            // write journal file in desc order to header be last page in disk
            if (_disk.IsJournalEnabled)
            {
                _disk.WriteJournal(_cache.GetDirtyPages()
                    .OrderByDescending(x => x.PageID)
                    .Select(x => x.DiskData)
                    .Where(x => x.Length > 0)
                    .ToList(), header.LastPageID);

                // mark header as recovery before start writing (in journal, must keep recovery = false)
                header.Recovery = true;

                // flush to disk to ensure journal is committed to disk before proceeding
                _disk.Flush();
            }
            else
            {
                // if no journal extend, resize file here to fast writes
                _disk.SetLength(BasePage.GetSizeOfPages(header.LastPageID + 1));
            }

            // write header page first. if header.Recovery == true, this ensures it's written to disk *before* we start changing pages
            // page 0 (header) is always cached during a transaction
            var headerPage = _cache.GetPage(0)!;
            var headerBuffer = headerPage.WritePage();
            _disk.WritePage(0, headerBuffer);
            _disk.Flush();

            // get all dirty page stating from Header page (SortedList)
            // header page (id=0) always must be first page to write on disk because it's will mark disk as "in recovery"
            foreach (var page in _cache.GetDirtyPages())
            {
                // we've already written the header, so skip it
                if (page.PageID == 0)
                {
                    continue;
                }

                // page.WritePage() updated DiskData with new rendered buffer
                var buffer = _crypto == null || page.PageID == 0 ? 
                    page.WritePage() : 
                    _crypto.Encrypt(page.WritePage());

                _disk.WritePage(page.PageID, buffer);
            }

            if (_disk.IsJournalEnabled)
            {
                // ensure changed pages are persisted to disk
                _disk.Flush();

                // re-write header page but now with recovery=false
                header.Recovery = false;

                _log.Write(Logger.DISK, "re-write header page now with recovery = false");

                _disk.WritePage(0, header.WritePage());
            }

            // mark all dirty pages as clean pages (all are persisted in disk and are valid pages)
            _cache.MarkDirtyAsClean();

            // flush all data direct to disk
            _disk.Flush();

            // discard journal file
            _disk.ClearJournal(header.LastPageID);
        }

        /// <summary>
        /// Restores the data file from the journal after a crash. Reads journal pages and writes them
        /// back to their original positions in the data file.
        /// </summary>
        public void Recovery()
        {
            _log.Write(Logger.RECOVERY, "initializing recovery mode");


            // double check in header need recovery (could be already recover from another thread)
            var header = (HeaderPage)BasePage.ReadPage(_disk.ReadPage(0));

            if (header.Recovery == false) return;

            // read all journal pages
            foreach (var buffer in _disk.ReadJournal(header.LastPageID))
            {
                // read pageID (first 4 bytes)
                var pageID = BitConverter.ToUInt32(buffer, 0);

                _log.Write(Logger.RECOVERY, "recover page #{0:0000}", pageID);

                // write in stream (encrypt if datafile is encrypted)
                _disk.WritePage(pageID, _crypto == null || pageID == 0 ? buffer : _crypto.Encrypt(buffer));
            }

            // shrink datafile
            _disk.ClearJournal(header.LastPageID);
            
        }
    }
}
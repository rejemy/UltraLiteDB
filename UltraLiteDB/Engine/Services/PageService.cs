using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
	/// <summary>
	/// Core page management service: reads pages from disk/cache, creates new pages, deletes pages,
	/// and manages free-list linked lists for efficient page reuse. Handles encryption/decryption transparently.
	/// </summary>
	internal class PageService
    {
        private CacheService _cache;
        private IDiskService _disk;
        private AesEncryption? _crypto;
        private Logger _log;

        public PageService(IDiskService disk, AesEncryption? crypto, CacheService cache, Logger log)
        {
            _disk = disk;
            _crypto = crypto;
            _cache = cache;
            _log = log;
        }

        /// <summary>
        /// Gets a page by ID, reading from cache first and falling back to disk. Decrypts if encryption is enabled.
        /// </summary>
        public T GetPage<T>(uint pageID)
            where T : BasePage
        {
            lock(_disk)
            {
                var page = _cache.GetPage(pageID);

                // is not on cache? load from disk
                if (page == null)
                {
                    var buffer = _disk.ReadPage(pageID);

                    // if datafile are encrypted, decrypt buffer (header are not encrypted)
                    if (_crypto != null && pageID > 0)
                    {
                        buffer = _crypto.Decrypt(buffer);
                    }

                    page = BasePage.ReadPage(buffer);

                    _cache.AddPage(page);
                }

                return (T)page;
            }
        }

        /// <summary>
        /// Marks a page as dirty so it will be written to disk on the next checkpoint/commit.
        /// </summary>
        public void SetDirty(BasePage page)
        {
            _cache.SetDirty(page);
        }

        /// <summary>
        /// Follows the <see cref="BasePage.NextPageID"/> chain starting from the given page, yielding each page in sequence.
        /// </summary>
        public IEnumerable<T> GetSeqPages<T>(uint firstPageID)
            where T : BasePage
        {
            var pageID = firstPageID;

            while (pageID != uint.MaxValue)
            {
                var page = this.GetPage<T>(pageID);

                pageID = page.NextPageID;

                yield return page;
            }
        }

        /// <summary>
        /// Allocates a new page, either by reusing an <see cref="EmptyPage"/> from the free list or by extending the data file.
        /// Optionally links the new page to a previous page in a sequence.
        /// </summary>
        public T NewPage<T>(BasePage? prevPage = null)
            where T : BasePage
        {
            // get header
            var header = this.GetPage<HeaderPage>(0);
            var pageID = (uint)0;
            var diskData = new byte[0];

            // try get page from Empty free list
            if (header.FreeEmptyPageID != uint.MaxValue)
            {
                var free = this.GetPage<BasePage>(header.FreeEmptyPageID);

                // remove page from empty list
                this.AddOrRemoveToFreeList(false, free, header, ref header.FreeEmptyPageID);

                pageID = free.PageID;

                // if used page has original disk data, copy to my new page
                if (free.DiskData.Length > 0)
                {
                    diskData = free.DiskData;
                }
            }
            else
            {
                pageID = ++header.LastPageID;

                // set header page as dirty after increment LastPageID
                this.SetDirty(header);
            }

            var page = BasePage.CreateInstance<T>(pageID);

            // copy disk data from re-used page (or be an empty)
            page.DiskData = diskData;

            // add page to cache with correct T type (could be an old Empty page type)
            this.SetDirty(page);

            // if there a page before, just fix NextPageID pointer
            if (prevPage != null)
            {
                page.PrevPageID = prevPage.PageID;
                prevPage.NextPageID = page.PageID;

                this.SetDirty(prevPage);
            }

            return page;
        }

        /// <summary>
        /// Converts a page (or a chain of pages) to <see cref="EmptyPage"/> and adds them to the free empty page list.
        /// </summary>
        /// <param name="pageID">The page ID to delete.</param>
        /// <param name="addSequence">If true, follows the NextPageID chain and deletes all pages in the sequence.</param>
        public void DeletePage(uint pageID, bool addSequence = false)
        {
            // get all pages in sequence or a single one
            var pages = addSequence ? this.GetSeqPages<BasePage>(pageID).ToArray() : new BasePage[] { this.GetPage<BasePage>(pageID) };

            // get my header page
            var header = this.GetPage<HeaderPage>(0);

            // adding all pages to FreeList
            foreach (var page in pages)
            {
                // create a new empty page based on a normal page
                var empty = new EmptyPage(page.PageID);

                // add empty page to cache (with now EmptyPage type) and mark as dirty
                this.SetDirty(empty);

                // add to empty free list
                this.AddOrRemoveToFreeList(true, empty, header, ref header.FreeEmptyPageID);
            }
        }

        /// <summary>
        /// Returns a page with at least <paramref name="size"/> free bytes, or creates a new page if none available.
        /// </summary>
        public T GetFreePage<T>(uint startPageID, int size)
            where T : BasePage
        {
            if (startPageID != uint.MaxValue)
            {
                // get the first page
                var page = this.GetPage<T>(startPageID);

                // check if there space in this page
                var free = page.FreeBytes;

                // first, test if there is space on this page
                if (free >= size)
                {
                    return page;
                }
            }

            // if not has space on first page, there is no page with space (pages are ordered), create a new one
            return this.NewPage<T>();
        }

        #region Add Or Remove do empty list

        /// <summary>
        /// Adds or removes a page from a free-list doubly-linked list, maintaining descending free-space order.
        /// </summary>
        /// <param name="add">True to add/reposition; false to remove.</param>
        /// <param name="page">The page to add or remove.</param>
        /// <param name="startPage">The page containing the list head pointer.</param>
        /// <param name="fieldPageID">Reference to the head pointer field on <paramref name="startPage"/>.</param>
        public void AddOrRemoveToFreeList(bool add, BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            if (add)
            {
                // if page has no prev/next it's not on list - lets add
                if (page.PrevPageID == uint.MaxValue && page.NextPageID == uint.MaxValue)
                {
                    this.AddToFreeList(page, startPage, ref fieldPageID);
                }
                else
                {
                    // otherwise this page is already in this list, lets move do put in free size desc order
                    this.MoveToFreeList(page, startPage, ref fieldPageID);
                }
            }
            else
            {
                // if this page is not in sequence, its not on freelist
                if (page.PrevPageID == uint.MaxValue && page.NextPageID == uint.MaxValue)
                    return;

                this.RemoveToFreeList(page, startPage, ref fieldPageID);
            }
        }

        /// <summary>
        /// Inserts a page into the free list, maintaining descending free-space order.
        /// </summary>
        private void AddToFreeList(BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            var free = page.FreeBytes;
            var nextPageID = fieldPageID;
            BasePage? next = null;

            // let's page in desc order
            while (nextPageID != uint.MaxValue)
            {
                next = this.GetPage<BasePage>(nextPageID);

                if (free >= next.FreeBytes)
                {
                    // assume my page in place of next page
                    page.PrevPageID = next.PrevPageID;
                    page.NextPageID = next.PageID;

                    // link next page to my page
                    next.PrevPageID = page.PageID;

                    // mark next page as dirty
                    this.SetDirty(next);
                    this.SetDirty(page);

                    // my page is the new first page on list
                    if (page.PrevPageID == 0)
                    {
                        fieldPageID = page.PageID;
                        this.SetDirty(startPage); // fieldPageID is from startPage
                    }
                    else
                    {
                        // if not the first, ajust links from previous page (set as dirty)
                        var prev = this.GetPage<BasePage>(page.PrevPageID);
                        prev.NextPageID = page.PageID;
                        this.SetDirty(prev);
                    }

                    return; // job done - exit
                }

                nextPageID = next.NextPageID;
            }

            // empty list, be the first
            if (next == null)
            {
                // it's first page on list
                page.PrevPageID = 0;
                fieldPageID = page.PageID;

                this.SetDirty(startPage);
            }
            else
            {
                // it's last position on list (next = last page on list)
                page.PrevPageID = next.PageID;
                next.NextPageID = page.PageID;

                this.SetDirty(next);
            }

            // set current page as dirty
            this.SetDirty(page);
        }

        /// <summary>
        /// Removes a page from the free list by re-linking its neighbors.
        /// </summary>
        private void RemoveToFreeList(BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            // this page is the first of list
            if (page.PrevPageID == 0)
            {
                fieldPageID = page.NextPageID;
                this.SetDirty(startPage); // fieldPageID is from startPage
            }
            else
            {
                // if not the first, get previous page to remove NextPageId
                var prevPage = this.GetPage<BasePage>(page.PrevPageID);
                prevPage.NextPageID = page.NextPageID;
                this.SetDirty(prevPage);
            }

            // if my page is not the last on sequence, adjust the last page (set as dirty)
            if (page.NextPageID != uint.MaxValue)
            {
                var nextPage = this.GetPage<BasePage>(page.NextPageID);
                nextPage.PrevPageID = page.PrevPageID;
                this.SetDirty(nextPage);
            }

            page.PrevPageID = page.NextPageID = uint.MaxValue;

            // mark page that will be removed as dirty
            this.SetDirty(page);
        }

        /// <summary>
        /// Repositions a page already on the free list by removing and re-inserting it.
        /// </summary>
        private void MoveToFreeList(BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            //TODO: write a better solution
            this.RemoveToFreeList(page, startPage, ref fieldPageID);
            this.AddToFreeList(page, startPage, ref fieldPageID);
        }

        #endregion
    }
}
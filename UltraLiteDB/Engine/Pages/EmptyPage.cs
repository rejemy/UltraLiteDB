using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Represents a deallocated page that has been returned to the free list for reuse.
    /// Empty pages contain no meaningful content and are recycled when new pages are needed.
    /// </summary>
    internal class EmptyPage : BasePage
    {
        /// <summary>
        /// Page type = Empty
        /// </summary>
        public override PageType PageType { get { return PageType.Empty; } }

        /// <summary>
        /// Initializes a new empty page with the given page identifier.
        /// </summary>
        /// <param name="pageID">The page identifier to assign.</param>
        public EmptyPage(uint pageID)
            : base(pageID)
        {
            this.ItemCount = 0;
            this.FreeBytes = PAGE_AVAILABLE_BYTES;
        }

        /// <summary>
        /// Creates an empty page from an existing page, preserving its disk data for journaling.
        /// </summary>
        /// <param name="page">The source page to convert to empty.</param>
        public EmptyPage(BasePage page)
            : base(page.PageID)
        {
            if(page.DiskData.Length > 0)
            {
                this.DiskData = new byte[BasePage.PAGE_SIZE];
                Buffer.BlockCopy(page.DiskData, 0, this.DiskData, 0, BasePage.PAGE_SIZE);
            }
        }

        #region Read/Write pages

        protected override void ReadContent(ByteReader reader)
        {
        }

        protected override void WriteContent(ByteWriter writer)
        {
        }

        #endregion
    }
}
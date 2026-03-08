using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Stores overflow document data that does not fit in a <see cref="DataPage"/>. Multiple extend pages
    /// are chained via <see cref="BasePage.NextPageID"/> to hold arbitrarily large documents.
    /// </summary>
    internal class ExtendPage : BasePage
    {
        /// <summary>
        /// Page type = Extend
        /// </summary>
        public override PageType PageType { get { return PageType.Extend; } }

        /// <summary>
        /// Represent the part or full of the object - if this page has NextPageID the object is bigger than this page
        /// </summary>
        private byte[] _data = new byte[0];

        /// <summary>
        /// Initializes a new extend page with the given page identifier.
        /// </summary>
        /// <param name="pageID">The page identifier to assign.</param>
        public ExtendPage(uint pageID)
            : base(pageID)
        {
        }

        /// <summary>
        /// Set slice of byte array source  into this page area
        /// </summary>
        public void SetData(byte[] data, int offset, int length)
        {
            this.ItemCount = length;
            this.FreeBytes = PAGE_AVAILABLE_BYTES - length; // not used on ExtendPage

            _data = new byte[length];

            Buffer.BlockCopy(data, offset, _data, 0, length);
        }

        /// <summary>
        /// Get internal page byte array data
        /// </summary>
        public byte[] GetData()
        {
            return _data;
        }

        #region Read/Write pages

        protected override void ReadContent(ByteReader reader)
        {
            _data = reader.ReadBytes(this.ItemCount);
        }

        protected override void WriteContent(ByteWriter writer)
        {
            writer.Write(_data);
        }

        #endregion
    }
}
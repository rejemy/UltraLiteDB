namespace UltraLiteDB
{
	/// <summary>
	/// Represents a document's data storage within a <see cref="DataPage"/>. Contains the serialized BSON bytes
	/// inline, or references an <see cref="ExtendPage"/> chain for documents exceeding one page.
	/// </summary>
	internal class DataBlock
    {
        public const int DATA_BLOCK_FIXED_SIZE = 2 + // Position.Index (ushort)
                                                 4 + // ExtendedPageID (uint)
                                                 2; // block.Data.Length (ushort)

        /// <summary>
        /// Position of this data block within its <see cref="DataPage"/>.
        /// </summary>
        public PageAddress Position { get; set; }

        /// <summary>
        /// Page ID of the first <see cref="ExtendPage"/> for overflow data, or <c>uint.MaxValue</c> if data fits inline.
        /// </summary>
        public uint ExtendPageID { get; set; }

        /// <summary>
        /// Serialized BSON bytes of the document. Empty if data overflows to <see cref="ExtendPage"/>s.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Reference to the parent <see cref="DataPage"/>.
        /// </summary>
        public DataPage Page { get; set; } = null!;

        /// <summary>
        /// Total byte length of this data block on disk (fixed header + data bytes).
        /// </summary>
        public int Length
        {
            get { return DataBlock.DATA_BLOCK_FIXED_SIZE + this.Data.Length; }
        }

        public DataBlock()
        {
            this.Position = PageAddress.Empty;
            this.ExtendPageID = uint.MaxValue;
            this.Data = new byte[0];
        }
    }
}
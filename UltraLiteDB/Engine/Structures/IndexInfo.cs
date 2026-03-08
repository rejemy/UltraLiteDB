namespace UltraLiteDB
{
	/// <summary>
	/// Public read-only view of a collection index's metadata, returned by <see cref="UltraLiteEngine.GetIndexes"/>.
	/// </summary>
	public class IndexInfo
    {
        internal IndexInfo(CollectionIndex index)
        {
            this.Slot = index.Slot;
            this.Field = index.Field;
            this.Unique = index.Unique;
            this.MaxLevel = index.MaxLevel;
        }

        /// <summary>
        /// Slot position (0–15) in the collection's index array.
        /// </summary>
        public int Slot { get; private set; }

        /// <summary>
        /// The indexed field name.
        /// </summary>
        public string Field { get; private set; }

        /// <summary>
        /// Whether this index enforces unique values.
        /// </summary>
        public bool Unique { get; private set; }

        /// <summary>
        /// Maximum skip-list level currently in use.
        /// </summary>
        public byte MaxLevel { get; private set; }
    }
}
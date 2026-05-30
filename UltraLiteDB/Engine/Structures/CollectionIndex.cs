using System.Text.RegularExpressions;

namespace UltraLiteDB
{
	/// <summary>
	/// Metadata for a single index on a collection. Stores the field name, uniqueness constraint,
	/// head/tail skip-list node pointers, and the free page list for index pages.
	/// </summary>
	internal class CollectionIndex
    {
        /// <summary>Regex pattern for valid index field names (supports dot notation, up to 60 segments).</summary>
        public static Regex IndexPattern = new Regex(@"^[\w](\.?[\w\$][\w-]*){0,59}$", RegexOptions.Compiled);

        /// <summary>
        /// Maximum number of indexes per collection (fixed at 16).
        /// </summary>
        public const int INDEX_PER_COLLECTION = 16;

        /// <summary>
        /// Slot position (0–15) in the collection's index array.
        /// </summary>
        public int Slot { get; set; }

        /// <summary>
        /// The indexed field name (supports dot notation for nested fields).
        /// </summary>
        public string Field { get; set; } = null!;

        /// <summary>
        /// If true, duplicate key values are rejected on insert.
        /// </summary>
        public bool Unique { get; set; }

        /// <summary>
        /// Address of the skip-list head sentinel node (Key = MinValue).
        /// </summary>
        public PageAddress HeadNode { get; set; }

        /// <summary>
        /// Address of the skip-list tail sentinel node (Key = MaxValue).
        /// </summary>
        public PageAddress TailNode { get; set; }

        /// <summary>
        /// Head of the free-list for index pages with available space. Must be a field (not property) for use as a ref parameter.
        /// </summary>
        public uint FreeIndexPageID;

        /// <summary>
        /// True if this index slot is unused and available for a new index.
        /// </summary>
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(this.Field); }
        }

        /// <summary>
        /// Maximum skip-list level currently in use for this index.
        /// </summary>
        public byte MaxLevel { get; set; }

        /// <summary>
        /// Reference to the parent <see cref="CollectionPage"/> that owns this index.
        /// </summary>
        public CollectionPage Page { get; set; } = null!;

        public CollectionIndex()
        {
            this.Clear();
        }

        /// <summary>
        /// Resets all index metadata to empty/default state.
        /// </summary>
        public void Clear()
        {
            this.Field = string.Empty;
            this.Unique = false;
            this.HeadNode = PageAddress.Empty;
            this.FreeIndexPageID = uint.MaxValue;
            this.MaxLevel = 1;
        }
    }
}
using System.Collections.Generic;

namespace UltraLiteDB
{
	/// <summary>
	/// Pairs a sort key with its document, used internally during query result sorting.
	/// </summary>
	internal class KeyDocument
    {
        public BsonValue Key { get; set; } = null!;
        public BsonDocument Document { get; set; } = null!;
    }

    /// <summary>
    /// Compares <see cref="KeyDocument"/> instances by their <see cref="KeyDocument.Key"/> value.
    /// </summary>
    internal class KeyDocumentComparer : IComparer<KeyDocument>
    {
        public int Compare(KeyDocument x, KeyDocument y)
        {
            return x.Key.CompareTo(y.Key);
        }

        public int GetHashCode(KeyDocument obj)
        {
            return obj.Key.GetHashCode();
        }
    }
}

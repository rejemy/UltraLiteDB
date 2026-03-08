using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Declares that this property should be indexed. Deprecated — use
    /// <see cref="UltraLiteCollection{T}.EnsureIndex(string, bool)"/> at database creation time instead.
    /// </summary>
    [Obsolete("Do not use Index attribute, use EnsureIndex on database creation")]
    public class BsonIndexAttribute : Attribute
    {
        public bool Unique { get; private set; }

        public BsonIndexAttribute()
            : this(false)
        {
        }

        public BsonIndexAttribute(bool unique)
        {
            this.Unique = unique;
        }
    }
}
using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Marks a property as the document's "_id" field. The mapper will serialize this member
    /// as the BSON <c>_id</c> key and use it for document identity in collections.
    /// </summary>
    public class BsonIdAttribute : Attribute
    {
        /// <summary>Whether the engine should auto-generate an ID value on insert when the field has its default value.</summary>
        public bool AutoId { get; private set; }

        /// <summary>
        /// Marks the property as the document ID with auto-generation enabled.
        /// </summary>
        public BsonIdAttribute()
        {
            this.AutoId = true;
        }

        /// <summary>
        /// Marks the property as the document ID, optionally disabling auto-generation.
        /// </summary>
        /// <param name="autoId">If false, the caller must supply the ID value before insert.</param>
        public BsonIdAttribute(bool autoId)
        {
            this.AutoId = autoId;
        }
    }
}
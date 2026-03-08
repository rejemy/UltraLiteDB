using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Excludes a property or field from BSON serialization and deserialization.
    /// The mapper will skip any member decorated with this attribute.
    /// </summary>
    public class BsonIgnoreAttribute : Attribute
    {
    }
}
using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Overrides the BSON field name used when serializing/deserializing this property.
    /// Without this attribute, the property name is used (with optional camelCase conversion from <see cref="BsonMapper"/>).
    /// </summary>
    public class BsonFieldAttribute : Attribute
    {
        /// <summary>The BSON field name to use instead of the CLR property name.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Maps this property to the specified BSON field name.
        /// </summary>
        public BsonFieldAttribute(string name)
        {
            this.Name = name;
        }

        /// <summary>
        /// Marks the property for field-name customization via <see cref="Name"/> property setter.
        /// </summary>
        public BsonFieldAttribute()
        {
        }
    }
}
using System;
using System.Collections;

namespace UltraLiteDB
{
    /// <summary>
    /// Describes how a single .NET property or field maps to a BSON document field.
    /// Used by <see cref="EntityMapper"/> to drive serialization/deserialization.
    /// </summary>
    public class MemberMapper
    {
        /// <summary>
        /// Whether this member should auto-generate an _id value on insert. Only relevant when <see cref="FieldName"/> is "_id".
        /// </summary>
        public bool AutoId { get; set; }

        /// <summary>
        /// The CLR property/field name (e.g. "FirstName").
        /// </summary>
        public string MemberName { get; set; } = null!;

        /// <summary>
        /// The CLR type of this member (e.g. typeof(string), typeof(int)).
        /// </summary>
        public Type DataType { get; set; } = null!;

        /// <summary>
        /// The BSON document field name this member maps to (e.g. "first_name", "_id").
        /// Set to null to exclude from mapping.
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// Delegate that reads this member's value from an entity instance.
        /// </summary>
        public GenericGetter? Getter { get; set; }

        /// <summary>
        /// Delegate that writes a value to this member on an entity instance.
        /// </summary>
        public GenericSetter? Setter { get; set; }

        /// <summary>
        /// Optional custom serialization function. When set, bypasses the default serialization logic for this member.
        /// </summary>
        public Func<object?, BsonMapper, BsonValue>? Serialize { get; set; }

        /// <summary>
        /// Optional custom deserialization function. When set, bypasses the default deserialization logic for this member.
        /// </summary>
        public Func<BsonValue, BsonMapper, object>? Deserialize { get; set; }

		/// <summary>
		/// Whether this member's type implements <see cref="IEnumerable"/> (arrays, lists, collections).
		/// </summary>
		public bool IsList { get; set; }

        /// <summary>
        /// For collection types, the element type (e.g. typeof(int) for List&lt;int&gt;). For non-collection types, same as <see cref="DataType"/>.
        /// </summary>
        public Type UnderlyingType { get; set; } = null!;
    }
}
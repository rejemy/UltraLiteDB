using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace UltraLiteDB
{
    /// <summary>
    /// Describes how a CLR type maps to/from a <see cref="BsonDocument"/>.
    /// Contains the list of <see cref="MemberMapper"/> entries for each mapped property/field.
    /// </summary>
    public class EntityMapper
    {
        /// <summary>
        /// All mapped members (properties/fields) for this entity type.
        /// </summary>
        public List<MemberMapper> Members { get; set; }

        /// <summary>
        /// Gets the member mapped to the "_id" field, or null if no _id member exists.
        /// </summary>
        public MemberMapper Id { get { return this.Members.SingleOrDefault(x => x.FieldName == "_id"); } }

        /// <summary>
        /// The CLR type this mapper describes.
        /// </summary>
        public Type ForType { get; set; }

        /// <summary>
        /// Lazily-built lookup from BSON field name to <see cref="MemberMapper"/> for O(1) access during deserialization.
        /// Keys are case-insensitive.
        /// </summary>
        private Dictionary<string, MemberMapper> _fieldLookup;

        /// <summary>
        /// Gets the field-name-to-member lookup dictionary, building it on first access.
        /// </summary>
        public Dictionary<string, MemberMapper> FieldLookup
        {
            get
            {
                if (_fieldLookup == null)
                {
                    var lookup = new Dictionary<string, MemberMapper>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in Members)
                    {
                        if (m.FieldName != null)
                            lookup[m.FieldName] = m;
                    }
                    _fieldLookup = lookup;
                }
                return _fieldLookup;
            }
        }

        /// <summary>
        /// Finds a <see cref="MemberMapper"/> by resolving the member path from a LINQ expression.
        /// </summary>
        public MemberMapper GetMember(Expression expr)
        {
            return this.Members.FirstOrDefault(x => x.MemberName == expr.GetPath());
        }
    }
}
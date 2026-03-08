using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace UltraLiteDB
{
    /// <summary>
    /// Fluent API for configuring how type <typeparamref name="T"/> maps to a <see cref="BsonDocument"/>.
    /// An alternative to using <see cref="BsonIdAttribute"/>, <see cref="BsonFieldAttribute"/>, and other mapping attributes.
    /// Obtain via <see cref="BsonMapper.Entity{T}"/>.
    /// </summary>
    public class EntityBuilder<T>
    {
        private BsonMapper _mapper;
        private EntityMapper _entity;

        internal EntityBuilder(BsonMapper mapper)
        {
            _mapper = mapper;
            _entity = mapper.GetEntityMapper(typeof(T));
        }

        /// <summary>
        /// Excludes a property from the BSON mapping.
        /// </summary>
        public EntityBuilder<T> Ignore<K>(Expression<Func<T, K>> property)
        {
            return this.GetProperty(property, (p) =>
            {
                _entity.Members.Remove(p);
            });
        }

        /// <summary>
        /// Maps a property to a custom BSON field name.
        /// </summary>
        public EntityBuilder<T> Field<K>(Expression<Func<T, K>> property, string field)
        {
            if (field.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(field));

            return this.GetProperty(property, (p) =>
            {
                p.FieldName = field;
            });
        }

        /// <summary>
        /// Designates a property as the document _id (primary key).
        /// </summary>
        /// <param name="property">Expression selecting the property.</param>
        /// <param name="autoId">If true, auto-generates _id values for empty/default values on insert.</param>
        public EntityBuilder<T> Id<K>(Expression<Func<T, K>> property, bool autoId = true)
        {
            return this.GetProperty(property, (p) =>
            {
                p.FieldName = "_id";
                p.AutoId = autoId;
            });
        }

        /// <summary>
        /// Get a property based on a expression. Eg.: 'x => x.UserId' return string "UserId"
        /// </summary>
        private EntityBuilder<T> GetProperty<TK, K>(Expression<Func<TK, K>> property, Action<MemberMapper> action)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));

            var prop = _entity.GetMember(property);

            if (prop == null) throw new ArgumentNullException(property.GetPath());

            action(prop);

            return this;
        }
    }
}
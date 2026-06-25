using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UltraLiteDB
{
    /// <summary>
    /// Maps .NET entity classes to and from <see cref="BsonDocument"/> representations.
    /// If you use a new instance rather than <see cref="Global"/>, cache it for better performance.
    /// <para>Serialization rules:</para>
    /// <list type="bullet">
    ///   <item>Classes must be public with a public parameterless constructor</item>
    ///   <item>Properties must have a public getter (can be read-only)</item>
    ///   <item>Entity class must have an Id property, [ClassName]Id property, or <see cref="BsonIdAttribute"/></item>
    ///   <item>No circular references</item>
    ///   <item>Fields are not mapped by default (see <see cref="IncludeFields"/>)</item>
    ///   <item><see cref="IList"/>, Array, <see cref="IDictionary"/> types are supported</item>
    /// </list>
    /// </summary>
    public partial class BsonMapper
    {
        private const int MAX_DEPTH = 20;

        #region Properties

        /// <summary>
        /// Cache of entity type mappings, keyed by CLR type.
        /// </summary>
        private Dictionary<Type, EntityMapper> _entities = new Dictionary<Type, EntityMapper>();

        /// <summary>
        /// Custom serialization functions registered for specific types.
        /// </summary>
        private Dictionary<Type, Func<object, BsonValue>> _customSerializer = new Dictionary<Type, Func<object, BsonValue>>();

        /// <summary>
        /// Custom deserialization functions registered for specific types.
        /// </summary>
        private Dictionary<Type, Func<BsonValue, object?>> _customDeserializer = new Dictionary<Type, Func<BsonValue, object?>>();

        /// <summary>
        /// Maps CLR types to compact BSON type identifiers for polymorphic serialization.
        /// </summary>
        private Dictionary<Type, BsonValue> _customTypeToId = new Dictionary<Type, BsonValue>();

        /// <summary>
        /// Reverse lookup from compact BSON type identifiers back to CLR types.
        /// </summary>
        private Dictionary<BsonValue, Type> _customIdToType = new Dictionary<BsonValue, Type>();

        /// <summary>
        /// Factory function used to create instances of types, supporting IoC containers.
        /// </summary>
        private readonly Func<Type, object> _typeInstantiator;

        /// <summary>
        /// Global singleton instance used when no <see cref="BsonMapper"/> is passed to the database constructor.
        /// </summary>
        public static BsonMapper Global = new BsonMapper();

        /// <summary>
        /// Function that resolves a .NET property name to a BSON document field name.
        /// Defaults to identity (no transformation).
        /// </summary>
        public Func<string, string> ResolveFieldName;

        /// <summary>
        /// When <c>true</c>, null values are included in serialized BSON documents. Default is <c>false</c>.
        /// </summary>
        public bool SerializeNullValues { get; set; }

        /// <summary>
        /// When <c>true</c>, applies <see cref="string.Trim()"/> to string values during serialization. Default is <c>false</c>.
        /// </summary>
        public bool TrimWhitespace { get; set; }

        /// <summary>
        /// When <c>true</c>, empty strings are converted to <see cref="BsonValue.Null"/> during serialization. Default is <c>false</c>.
        /// </summary>
        public bool EmptyStringToNull { get; set; }

        /// <summary>
        /// When <c>true</c>, public fields (not just properties) are included in the mapping. Default is <c>false</c>.
        /// </summary>
        public bool IncludeFields { get; set; }

        /// <summary>
        /// When <c>true</c>, non-public members (private, protected, internal) are included in the mapping. Default is <c>false</c>.
        /// </summary>
        public bool IncludeNonPublic { get; set; }

        /// <summary>
        /// When <c>true</c>, the serializer includes the assembly-qualified type name (<c>_type</c> field) for derived types to support polymorphic deserialization. Default is <c>true</c>.
        /// </summary>
        public bool IncludeFullType { get; set; }

        /// <summary>
        /// Optional callback invoked for each member during entity mapping, allowing customization of the <see cref="MemberMapper"/>.
        /// Set <see cref="MemberMapper.FieldName"/> to <c>null</c> to exclude the member from the mapped document.
        /// </summary>
        public Action<Type, MemberInfo, MemberMapper> ResolveMember;

        /// <summary>
        /// Function that resolves a collection name from a CLR type. Defaults to using the type name.
        /// </summary>
        public Func<Type, string> ResolveCollectionName;

        // Internal accessors for DirectBsonWriter/DirectBsonReader
        internal Dictionary<Type, Func<object, BsonValue>> CustomSerializer => _customSerializer;
        internal Dictionary<Type, Func<BsonValue, object?>> CustomDeserializer => _customDeserializer;
        internal Dictionary<Type, BsonValue> CustomTypeToId => _customTypeToId;
        internal Dictionary<BsonValue, Type> CustomIdToType => _customIdToType;
        internal Func<Type, object> TypeInstantiator => _typeInstantiator;

        #endregion

        /// <summary>
        /// Initializes a new instance of <see cref="BsonMapper"/> with default settings and optional IoC support.
        /// </summary>
        /// <param name="customTypeInstantiator">Optional factory function for creating type instances, enabling IoC integration. If <c>null</c>, uses <see cref="Reflection.CreateInstance"/>.</param>
        public BsonMapper(Func<Type, object>? customTypeInstantiator = null)
        {
            this.SerializeNullValues = false;
            this.TrimWhitespace = false;
            this.EmptyStringToNull = false;
            this.ResolveFieldName = (s) => s;
            this.ResolveMember = (t, mi, mm) => { };
            this.ResolveCollectionName = (t) => Reflection.IsList(t) ? Reflection.GetListItemType(t).Name : t.Name;
            this.IncludeFields = false;
            this.IncludeFullType = true;

            _typeInstantiator = customTypeInstantiator ?? Reflection.CreateInstance;

            #region Register CustomTypes

            RegisterType<Uri>(uri => uri.AbsoluteUri, bson => new Uri(bson.AsString));
            RegisterType<DateTimeOffset>(value => new BsonValue(value.UtcDateTime), bson => bson.AsDateTime.ToUniversalTime());
            RegisterType<TimeSpan>(value => new BsonValue(value.Ticks), bson => new TimeSpan(bson.AsInt64));
            RegisterType<Regex>(
                r => r.Options == RegexOptions.None ? new BsonValue(r.ToString()) : new BsonDocument { { "p", r.ToString() }, { "o", (int)r.Options } },
                value => value.IsString ? new Regex(value) : new Regex(value.AsDocument!["p"].AsString, (RegexOptions)value.AsDocument!["o"].AsInt32)
            );


            #endregion

        }

        #region Register CustomType

        /// <summary>
        /// Registers custom serialization and deserialization functions for type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type to register custom conversion for.</typeparam>
        /// <param name="serialize">Function that converts an instance of <typeparamref name="T"/> to a <see cref="BsonValue"/>.</param>
        /// <param name="deserialize">Function that converts a <see cref="BsonValue"/> back to an instance of <typeparamref name="T"/>.</param>
        public void RegisterType<T>(Func<T, BsonValue> serialize, Func<BsonValue, T> deserialize)
        {
            _customSerializer[typeof(T)] = (o) => serialize((T)o);
            _customDeserializer[typeof(T)] = (b) => (T)deserialize(b);
        }

        /// <summary>
        /// Registers custom serialization and deserialization functions for the specified type (non-generic overload).
        /// </summary>
        /// <param name="type">The type to register custom conversion for.</param>
        /// <param name="serialize">Function that converts an object instance to a <see cref="BsonValue"/>.</param>
        /// <param name="deserialize">Function that converts a <see cref="BsonValue"/> back to an object instance.</param>
        public void RegisterType(Type type, Func<object, BsonValue> serialize, Func<BsonValue, object> deserialize)
        {
            _customSerializer[type] = (o) => serialize(o);
            _customDeserializer[type] = (b) => deserialize(b);
        }

        /// <summary>
        /// Registers a compact type identifier for polymorphic serialization. The type will be stored
        /// as a <c>_t</c> field instead of the full assembly-qualified name, reducing document size.
        /// </summary>
        /// <param name="t">The CLR type to register.</param>
        /// <param name="id">The compact <see cref="BsonValue"/> identifier for this type.</param>
        public void RegisterTypeId(Type t, BsonValue id)
        {
            _customTypeToId.Add(t, id);
            _customIdToType.Add(id, t);
        }

        #endregion

        /// <summary>
        /// Returns a fluent <see cref="EntityBuilder{T}"/> for configuring how type <typeparamref name="T"/> maps to a <see cref="BsonDocument"/>.
        /// </summary>
        /// <typeparam name="T">The entity type to configure.</typeparam>
        /// <returns>An <see cref="EntityBuilder{T}"/> for chaining mapping configuration.</returns>
        public EntityBuilder<T> Entity<T>()
        {
            return new EntityBuilder<T>(this);
        }


        #region Predefinded Property Resolvers

        /// <summary>
        /// Use lower camel case resolution for convert property names to field names
        /// </summary>
        public BsonMapper UseCamelCase()
        {
            this.ResolveFieldName = (s) => char.ToLower(s[0]) + s.Substring(1);

            return this;
        }

        private Regex _lowerCaseDelimiter = new Regex("(?!(^[A-Z]))([A-Z])", RegexOptions.Compiled);

        /// <summary>
        /// Uses lower camel case with delimiter to convert property names to field names
        /// </summary>
        public BsonMapper UseLowerCaseDelimiter(char delimiter = '_')
        {
            this.ResolveFieldName = (s) => _lowerCaseDelimiter.Replace(s, delimiter + "$2").ToLower();

            return this;
        }

        #endregion

        #region GetEntityMapper

        /// <summary>
        /// Get property mapper between typed .NET class and BsonDocument - Cache results
        /// </summary>
        internal EntityMapper GetEntityMapper(Type type)
        {
            //TODO: needs check if Type if BsonDocument? Returns empty EntityMapper?

            if (!_entities.TryGetValue(type, out EntityMapper mapper))
            {
                lock (_entities)
                {
                    if (!_entities.TryGetValue(type, out mapper))
                    {
                        return _entities[type] = BuildEntityMapper(type);
                    }
                }
            }

            return mapper;
        }

        /// <summary>
        /// Use this method to override how your class can be, by default, mapped from entity to Bson document.
        /// Returns an EntityMapper from each requested Type
        /// </summary>
        protected virtual EntityMapper BuildEntityMapper(Type type)
        {
            var mapper = new EntityMapper
            {
                Members = new List<MemberMapper>(),
                ForType = type
            };

            var idAttr = typeof(BsonIdAttribute);
            var ignoreAttr = typeof(BsonIgnoreAttribute);
            var fieldAttr = typeof(BsonFieldAttribute);
            var indexAttr = typeof(BsonIndexAttribute);

            var members = this.GetTypeMembers(type);
            var id = this.GetIdMember(members);

            foreach (var memberInfo in members)
            {
                // checks [BsonIgnore]
                if (memberInfo.IsDefined(ignoreAttr, true)) continue;

                // checks field name conversion
                var name = this.ResolveFieldName(memberInfo.Name);

                // check if property has [BsonField]
                var field = (BsonFieldAttribute)memberInfo.GetCustomAttributes(fieldAttr, false).FirstOrDefault();

                // check if property has [BsonField] with a custom field name
                if (field != null && field.Name != null)
                {
                    name = field.Name;
                }

                // checks if memberInfo is id field
                if (memberInfo == id)
                {
                    name = "_id";
                }

                // create getter/setter function
                var getter = Reflection.CreateGenericGetter(type, memberInfo);
                var setter = Reflection.CreateGenericSetter(type, memberInfo);

                // check if property has [BsonId] to get with was setted AutoId = true
                var autoId = (BsonIdAttribute)memberInfo.GetCustomAttributes(idAttr, false).FirstOrDefault();

                // get data type
                var dataType = memberInfo is PropertyInfo ?
                    ((PropertyInfo)memberInfo).PropertyType :
                    ((FieldInfo)memberInfo).FieldType;

                // check if datatype is list/array
                var isList = Reflection.IsList(dataType);

                // create a property mapper
                var member = new MemberMapper
                {
                    AutoId = autoId == null ? true : autoId.AutoId,
                    FieldName = name,
                    MemberName = memberInfo.Name,
                    DataType = dataType,
                    IsList = isList,
                    UnderlyingType = isList ? Reflection.GetListItemType(dataType) : dataType,
                    Getter = getter,
                    Setter = setter
                };

                // support callback to user modify member mapper
                if (this.ResolveMember != null)
                {
                    this.ResolveMember(type, memberInfo, member);
                }

                // test if has name and there is no duplicate field
                if (member.FieldName != null && mapper.Members.Any(x => x.FieldName == name) == false)
                {
                    mapper.Members.Add(member);
                }
            }

            return mapper;
        }

        /// <summary>
        /// Gets MemberInfo that refers to Id from a document object.
        /// </summary>
        protected virtual MemberInfo? GetIdMember(IEnumerable<MemberInfo> members)
        {
            return Reflection.SelectMember(members,
                x => Attribute.IsDefined(x, typeof(BsonIdAttribute), true),
                x => x.Name.Equals("Id", StringComparison.OrdinalIgnoreCase),
                x => x.Name.Equals(x.DeclaringType.Name + "Id", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns all member that will be have mapper between POCO class to document
        /// </summary>
        protected virtual IEnumerable<MemberInfo> GetTypeMembers(Type type)
        {
            var members = new List<MemberInfo>();

            var flags = this.IncludeNonPublic ?
                (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) :
                (BindingFlags.Public | BindingFlags.Instance);

            members.AddRange(type.GetProperties(flags)
                .Where(x => x.CanRead && x.GetIndexParameters().Length == 0)
                .Select(x => x as MemberInfo));

            if(this.IncludeFields)
            {
                members.AddRange(type.GetFields(flags).Where(x => !x.Name.EndsWith("k__BackingField") && x.IsStatic == false).Select(x => x as MemberInfo));
            }

            return members;
        }

        #endregion

     
    }
}
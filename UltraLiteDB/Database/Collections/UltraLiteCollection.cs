using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Represents a typed collection of documents in the database.
    /// Provides CRUD operations, indexing, and aggregate queries with automatic POCO-to-BSON mapping.
    /// For untyped access, use <c>UltraLiteCollection&lt;BsonDocument&gt;</c>.
    /// </summary>
    /// <typeparam name="T">The document type. Can be a POCO class or <see cref="BsonDocument"/>.</typeparam>
    public sealed partial class UltraLiteCollection<T>
    {
        private string _name;
        private LazyLoad<UltraLiteEngine> _engine;
        private BsonMapper _mapper;
        private readonly EntityMapper _entity;
        private Logger _log;
        private MemberMapper _id;
        private BsonAutoId _autoId;

        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string Name { get { return _name; } }

        /// <summary>
        /// Gets the entity mapper for <typeparamref name="T"/>. Returns null when <typeparamref name="T"/> is <see cref="BsonDocument"/>.
        /// </summary>
        public EntityMapper EntityMapper => _entity;

        /// <summary>
        /// Initializes a new collection instance. Automatically determines the auto-id type from the mapped _id member.
        /// </summary>
        /// <param name="name">Collection name, or null to resolve from <typeparamref name="T"/>.</param>
        /// <param name="autoId">Default auto-id strategy (used for <see cref="BsonDocument"/> collections).</param>
        /// <param name="engine">Lazy-loaded engine instance.</param>
        /// <param name="mapper">The BSON mapper for POCO serialization.</param>
        /// <param name="log">Logger instance.</param>
        public UltraLiteCollection(string name, BsonAutoId autoId, LazyLoad<UltraLiteEngine> engine, BsonMapper mapper, Logger log)
        {
            _name = name ?? mapper.ResolveCollectionName(typeof(T));
            _engine = engine;
            _mapper = mapper;
            _log = log;

            // if strong typed collection, get _id member mapped (if exists)
            if (typeof(T) == typeof(BsonDocument))
            {
                _entity = null;
                _id = null;
                _autoId = autoId;
            }
            else
            {
                _entity = mapper.GetEntityMapper(typeof(T));
                _id = _entity.Id;

                if (_id != null && _id.AutoId)
                {
                    _autoId =
                        _id.DataType == typeof(Int32) || _id.DataType == typeof(Int32?) ? BsonAutoId.Int32 :
                        _id.DataType == typeof(Int64) || _id.DataType == typeof(Int64?) ? BsonAutoId.Int64 :
                        _id.DataType == typeof(Guid) || _id.DataType == typeof(Guid?) ? BsonAutoId.Guid :
                        BsonAutoId.ObjectId;
                }
                else
                {
                    _autoId = BsonAutoId.ObjectId;
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UltraLiteDB
{
    /// <summary>
    /// Main database class. Opens an UltraLiteDB connection and provides access to collections, database management, and compaction.
    /// Wraps <see cref="UltraLiteEngine"/> with higher-level POCO mapping via <see cref="BsonMapper"/>.
    /// </summary>
    public partial class UltraLiteDatabase : IDisposable
    {
        #region Properties

        private LazyLoad<UltraLiteEngine> _engine;
        private BsonMapper _mapper = BsonMapper.Global;
        private Logger _log;
        private ConnectionString? _connectionString = null;

        /// <summary>
        /// Gets the logger instance for this database.
        /// </summary>
        public Logger Log { get { return _log; } }

        /// <summary>
        /// Gets the <see cref="BsonMapper"/> used by this database instance. Defaults to <see cref="BsonMapper.Global"/>.
        /// </summary>
        public BsonMapper Mapper { get { return _mapper; } }

        /// <summary>
        /// Gets the underlying <see cref="UltraLiteEngine"/>. The engine operates on raw <see cref="BsonDocument"/> values
        /// without POCO mapping, and is lazily initialized on first access.
        /// </summary>
        public UltraLiteEngine Engine { get { return _engine.Value; } }

        #endregion

        #region Ctor

        /// <summary>
        /// Opens a file-based database using a connection string.
        /// </summary>
        /// <param name="connectionString">Connection string (e.g. "Filename=mydb.db;Password=secret"). See <see cref="ConnectionString"/> for supported keys.</param>
        /// <param name="mapper">Optional mapper for POCO serialization. Defaults to <see cref="BsonMapper.Global"/>.</param>
        /// <param name="log">Optional logger. A default logger is created if null.</param>
        public UltraLiteDatabase(string connectionString, BsonMapper? mapper = null, Logger? log = null)
            : this(new ConnectionString(connectionString), mapper, log)
        {
        }

        /// <summary>
        /// Opens a file-based database using a parsed <see cref="ConnectionString"/>.
        /// </summary>
        /// <param name="connectionString">Parsed connection string with database options.</param>
        /// <param name="mapper">Optional mapper for POCO serialization. Defaults to <see cref="BsonMapper.Global"/>.</param>
        /// <param name="log">Optional logger. A default logger is created if null.</param>
        public UltraLiteDatabase(ConnectionString connectionString, BsonMapper? mapper = null, Logger? log = null)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            _connectionString = connectionString;
            _log = log ?? new Logger();
            _log.Level = log?.Level ?? _connectionString.Log;

            _mapper = mapper ?? BsonMapper.Global;

            var options = new FileOptions
            {
                Async = _connectionString.Async,
                Flush = _connectionString.Flush,
                InitialSize = _connectionString.InitialSize,
                LimitSize = _connectionString.LimitSize,
                Journal = _connectionString.Journal,
            };

            _engine = new LazyLoad<UltraLiteEngine>(() => new UltraLiteEngine(new FileDiskService(_connectionString.Filename, options), _connectionString.Password, _connectionString.Timeout, _connectionString.CacheSize, _log));
        }

        /// <summary>
        /// Opens a database backed by a <see cref="Stream"/> (e.g. <see cref="MemoryStream"/>).
        /// Useful for in-memory databases or custom storage.
        /// </summary>
        /// <param name="stream">The stream to use as the data store.</param>
        /// <param name="mapper">Optional mapper for POCO serialization. Defaults to <see cref="BsonMapper.Global"/>.</param>
        /// <param name="password">Optional password for AES encryption of the data file.</param>
        /// <param name="disposeStream">If true, the stream is disposed when the database is disposed.</param>
        public UltraLiteDatabase(Stream stream, BsonMapper? mapper = null, string? password = null, bool disposeStream = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            _mapper = mapper ?? BsonMapper.Global;
            _log = new Logger();

            _engine = new LazyLoad<UltraLiteEngine>(() => new UltraLiteEngine(new StreamDiskService(stream, disposeStream), password: password, log: _log));
        }

        /// <summary>
        /// Opens a database using a custom <see cref="IDiskService"/> implementation.
        /// </summary>
        /// <param name="diskService">Custom disk service for data persistence.</param>
        /// <param name="mapper">Optional mapper for POCO serialization. Defaults to <see cref="BsonMapper.Global"/>.</param>
        /// <param name="password">Optional password for AES encryption of the data file.</param>
        /// <param name="timeout">Lock timeout for concurrent access. Null uses the engine default.</param>
        /// <param name="cacheSize">Maximum number of memory pages cached before flushing to the journal file.</param>
        /// <param name="log">Optional logger. A default logger is created if null.</param>
        public UltraLiteDatabase(IDiskService diskService, BsonMapper? mapper = null, string? password = null, TimeSpan? timeout = null, int cacheSize = 5000, Logger? log = null)
        {
            if (diskService == null) throw new ArgumentNullException(nameof(diskService));

            _mapper = mapper ?? BsonMapper.Global;
            _log = log ?? new Logger();

            _engine = new LazyLoad<UltraLiteEngine>(() => new UltraLiteEngine(diskService, password: password, timeout: timeout, cacheSize: cacheSize, log: _log ));
        }

        #endregion

        #region Collections

        /// <summary>
        /// Gets a typed collection by name. Creates the collection on first insert if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The POCO type mapped to documents in this collection.</typeparam>
        /// <param name="name">Collection name (case insensitive).</param>
        /// <returns>A collection instance for querying and modifying documents.</returns>
        public UltraLiteCollection<T> GetCollection<T>(string name)
        {
            return new UltraLiteCollection<T>(name, BsonAutoId.ObjectId, _engine, _mapper, _log);
        }

        /// <summary>
        /// Gets a typed collection using the name resolved from <typeparamref name="T"/> via <see cref="BsonMapper.ResolveCollectionName"/>.
        /// </summary>
        public UltraLiteCollection<T> GetCollection<T>()
        {
            return new UltraLiteCollection<T>(null, BsonAutoId.ObjectId, _engine, _mapper, _log);
        }

        /// <summary>
        /// Gets an untyped <see cref="BsonDocument"/> collection by name. Creates the collection on first insert if it doesn't exist.
        /// </summary>
        /// <param name="name">Collection name (case insensitive).</param>
        /// <param name="autoId">The auto-generated _id type when a document has no _id field.</param>
        public UltraLiteCollection<BsonDocument> GetCollection(string name, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            return new UltraLiteCollection<BsonDocument>(name, autoId, _engine, _mapper, _log);
        }

        #endregion


        #region Shortcut

        /// <summary>
        /// Get all collections name inside this database.
        /// </summary>
        public IEnumerable<string> GetCollectionNames()
        {
            return _engine.Value.GetCollectionNames();
        }

        /// <summary>
        /// Checks if a collection exists on database. Collection name is case insensitive
        /// </summary>
        public bool CollectionExists(string name)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            return _engine.Value.GetCollectionNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Drop a collection and all data + indexes
        /// </summary>
        public bool DropCollection(string name)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            return _engine.Value.DropCollection(name);
        }

        /// <summary>
        /// Rename a collection. Returns false if oldName does not exists or newName already exists
        /// </summary>
        public bool RenameCollection(string oldName, string newName)
        {
            if (oldName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(oldName));
            if (newName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(newName));

            return _engine.Value.RenameCollection(oldName, newName);
        }

        #endregion

        #region Shrink

        /// <summary>
        /// Compacts the database file by re-arranging unused space. Uses the current password if set.
        /// </summary>
        /// <returns>The number of bytes reduced.</returns>
        public long Shrink()
        {
            return this.Shrink(_connectionString?.Password);
        }

        /// <summary>
        /// Compacts the database file by re-arranging unused space, optionally changing the encryption password.
        /// For file-based databases, uses a temporary file; for stream-based databases, uses a <see cref="MemoryStream"/>.
        /// </summary>
        /// <param name="password">New password for the compacted file, or null for no encryption.</param>
        /// <returns>The number of bytes reduced.</returns>
        public long Shrink(string? password)
        {
            // if has connection string, use same path
            if (_connectionString != null)
            {
                // get temp file ("-temp" suffix)
                var tempFile = FileHelper.GetTempFile(_connectionString.Filename);
                var reduced = 0L;

                try
                {
                    // get temp disk based on temp file
                    var tempDisk = new FileDiskService(tempFile, new FileOptions { Journal = false });

                    reduced = _engine.Value.Shrink(password, tempDisk);
                }
                finally
                {
                    // delete temp file
                    File.Delete(tempFile);
                }

                return reduced;
            }
            else
            {
                return _engine.Value.Shrink(password);
            }
        }

        #endregion

        /// <summary>
        /// Disposes the database engine if it has been initialized, releasing file locks and flushing pending data.
        /// </summary>
        public void Dispose()
        {
            if (_engine.IsValueCreated) _engine.Value.Dispose();
        }
    }
}

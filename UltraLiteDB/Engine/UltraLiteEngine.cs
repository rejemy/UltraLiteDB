using System;
using System.IO;

namespace UltraLiteDB
{
    /// <summary>
    /// Core database engine that manages all low-level data structure access for UltraLiteDB.
    /// Operates directly on BSON objects without LINQ or POCO support. Provides CRUD operations,
    /// indexing, collection management, and transaction handling over an <see cref="IDiskService"/>.
    /// This class is implemented as a partial class with operations split across multiple files.
    /// </summary>
    public partial class UltraLiteEngine : IDisposable
    {
        #region Services instances

        private Logger _log;

        private IDiskService _disk;

        // The following services are all created in InitializeServices(), called from the
        // constructor, and are never re-assigned to null. = null! tells the compiler they
        // are effectively non-nullable after construction.
        private CacheService _cache = null!;

        private PageService _pager = null!;

        private TransactionService _trans = null!;

        private IndexService _indexer = null!;

        private DataService _data = null!;

        private CollectionService _collections = null!;

        private AesEncryption? _crypto;

        private int _cacheSize;

        private TimeSpan _timeout;

        /// <summary>
        /// Get log instance for debug operations
        /// </summary>
        public Logger Log { get { return _log; } }

        /// <summary>
        /// Gets the memory cache size limit in pages. Only enforced when journaling is enabled;
        /// if journaling is disabled, cached pages may exceed this limit. Default is 5000 pages.
        /// </summary>
        public int CacheSize { get { return _cacheSize; } }

        /// <summary>
        /// Gets the number of clean (non-dirty) pages currently held in cache.
        /// </summary>
        public int CacheUsed { get { return _cache.CleanUsed; } }

        /// <summary>
        /// Gets the maximum time to wait for a write lock before throwing a timeout <see cref="UltraLiteException"/>.
        /// </summary>
        public TimeSpan Timeout { get { return _timeout; } }


        #endregion

        #region Ctor

        /// <summary>
        /// Initializes a new <see cref="UltraLiteEngine"/> using the default <see cref="FileDiskService"/>.
        /// </summary>
        /// <param name="filename">Path to the database file.</param>
        /// <param name="journal">Whether to enable write-ahead journaling for crash recovery.</param>
        public UltraLiteEngine(string filename, bool journal = true)
            : this(new FileDiskService(filename, journal))
        {
        }

        /// <summary>
        /// Initializes a new <see cref="UltraLiteEngine"/> with AES password encryption using the default <see cref="FileDiskService"/>.
        /// </summary>
        /// <param name="filename">Path to the database file.</param>
        /// <param name="password">Password used for AES encryption of data pages.</param>
        /// <param name="journal">Whether to enable write-ahead journaling for crash recovery.</param>
        public UltraLiteEngine(string filename, string? password, bool journal = true)
            : this(new FileDiskService(filename, new FileOptions { Journal = journal }), password)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="UltraLiteEngine"/> backed by a <see cref="Stream"/> via <see cref="StreamDiskService"/>.
        /// </summary>
        /// <param name="stream">The stream to use as the database storage.</param>
        /// <param name="password">Optional password for AES encryption of data pages.</param>
        public UltraLiteEngine(Stream stream, string? password = null)
            : this(new StreamDiskService(stream), password)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="UltraLiteEngine"/> using a custom <see cref="IDiskService"/> implementation with full engine options.
        /// </summary>
        /// <param name="disk">The disk service implementation for storage I/O.</param>
        /// <param name="password">Optional password for AES encryption of data pages.</param>
        /// <param name="timeout">Maximum time to wait for a write lock. Defaults to 1 minute.</param>
        /// <param name="cacheSize">Maximum number of pages to hold in the memory cache. Defaults to 5000.</param>
        /// <param name="log">Optional <see cref="Logger"/> instance for diagnostic output.</param>
        public UltraLiteEngine(IDiskService disk, string? password = null, TimeSpan? timeout = null, int cacheSize = 5000, Logger? log = null)
        {
            if (disk == null) throw new ArgumentNullException(nameof(disk));

            _timeout = timeout ?? TimeSpan.FromMinutes(1);
            _cacheSize = cacheSize;
            _disk = disk;
            _log = log ?? new Logger();

            try
            {
                // initialize datafile (create) and set log instance
                _disk.Initialize(_log, password);

                var buffer = _disk.ReadPage(0);

                // create header instance from array bytes
                var header = (HeaderPage)BasePage.ReadPage(buffer);

                // hash password with sha1 or keep as empty byte[20]
                var sha1 = password == null ? new byte[20] : AesEncryption.HashSHA1(password);

                // compare header password with user password even if not passed password (datafile can have password)
                if (sha1.BinaryCompareTo(header.Password) != 0)
                {
                    throw UltraLiteException.DatabaseWrongPassword();
                }

                // initialize AES encryptor
                if (password != null)
                {
                    _crypto = new AesEncryption(password, header.Salt);
                }

                // initialize all services
                this.InitializeServices();

                // if header are marked with recovery, do it now
                if (header.Recovery)
                {
                    _trans.Recovery();
                }
            }
            catch (Exception)
            {
                // explicit dispose
                this.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Create instances for all engine services
        /// </summary>
        private void InitializeServices()
        {
            _cache = new CacheService(_log);
            _pager = new PageService(_disk, _crypto, _cache, _log);
            _indexer = new IndexService(_pager, _log);
            _data = new DataService(_pager, _log);
            _trans = new TransactionService(_disk, _crypto, _pager, _cache, _cacheSize, _log);
            _collections = new CollectionService(_pager, _indexer, _data, _trans, _log);
        }

        #endregion

        /// <summary>
        /// Retrieves the <see cref="CollectionPage"/> for the given collection name, optionally creating it if it does not exist.
        /// Always reads from the pager to guarantee the latest version (the page ID never changes for a collection).
        /// </summary>
        /// <param name="name">The collection name, or <c>null</c> to return <c>null</c>.</param>
        /// <param name="addIfNotExits">If <c>true</c>, creates the collection when it does not exist.</param>
        /// <returns>The <see cref="CollectionPage"/>, or <c>null</c> if not found and <paramref name="addIfNotExits"/> is <c>false</c>.</returns>
        private CollectionPage? GetCollectionPage(string? name, bool addIfNotExits)
        {
            if (name == null) return null;

            if (addIfNotExits) return this.GetOrAddCollectionPage(name);

            // search my page on collection service
            return _collections.Get(name);
        }

        /// <summary>
        /// Retrieves the <see cref="CollectionPage"/> for the given collection name, creating the collection if it
        /// does not yet exist. Never returns <c>null</c>.
        /// </summary>
        private CollectionPage GetOrAddCollectionPage(string name)
        {
            var col = _collections.Get(name);

            if (col == null)
            {
                _log.Write(Logger.COMMAND, "create new collection '{0}'", name);

                col = _collections.Add(name);
            }

            return col;
        }

        /// <summary>
        /// Executes an operation within a write transaction. Persists dirty pages on success or discards them on failure.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="action">The operation to execute.</param>
        /// <returns>The result of the <paramref name="action"/>.</returns>
        private T Transaction<T>(Func<T> action)
        {
            try
            {
                var result = action();

                _trans.PersistDirtyPages();

                _trans.CheckPoint();

                return result;
            }
            catch (Exception ex)
            {
                _log.Write(Logger.ERROR, ex.Message);

                // if an error occurs during an operation, rollback must be called to avoid datafile inconsistent
                _cache.DiscardDirtyPages();

                throw;
            }
        }

        /// <summary>
        /// Executes an operation against an existing collection (read/optional path). The <see cref="CollectionPage"/>
        /// passed to <paramref name="action"/> is <c>null</c> when the collection does not exist, or for
        /// database-level operations where <paramref name="collection"/> is <c>null</c>.
        /// </summary>
        private T Transaction<T>(string? collection, Func<CollectionPage?, T> action)
        {
            return this.Transaction(() => action(this.GetCollectionPage(collection, false)));
        }

        /// <summary>
        /// Executes a write operation against a collection, creating it if it does not exist. The
        /// <see cref="CollectionPage"/> passed to <paramref name="action"/> is always non-null.
        /// </summary>
        private T WriteTransaction<T>(string collection, Func<CollectionPage, T> action)
        {
            return this.Transaction(() => action(this.GetOrAddCollectionPage(collection)));
        }

        /// <summary>
        /// Releases all resources used by the engine, including the underlying disk service and any AES encryption state.
        /// </summary>
        public void Dispose()
        {
            // dispose datafile and journal file
            _disk.Dispose();

            // dispose crypto
            if (_crypto != null) _crypto.Dispose();
        }

        /// <summary>
        /// Creates a new empty database in the provided stream, writing the header page and lock-reserved area.
        /// Optionally pre-allocates empty pages and sets up AES encryption.
        /// </summary>
        /// <param name="stream">The stream to write the new database into.</param>
        /// <param name="password">Optional password for AES encryption.</param>
        /// <param name="initialSize">Optional initial file size in bytes. If greater than two pages, empty pages are pre-allocated.</param>
        public static void CreateDatabase(Stream stream, string? password = null, long initialSize = 0)
        {
            // calculate how many empty pages will be added on disk
            var emptyPages = initialSize == 0 ? 0 : (initialSize - (2 * BasePage.PAGE_SIZE)) / BasePage.PAGE_SIZE;

            // if too small size (less than 2 pages), assume no initial size
            if (emptyPages < 0) emptyPages = 0;

            // create a new header page in bytes (keep second page empty)
            var header = new HeaderPage
            {
                LastPageID = initialSize == 0 ? 1 : (uint)emptyPages + 1,
                FreeEmptyPageID = initialSize == 0 ? uint.MaxValue : 2
            };

            if (password != null)
            {
                header.Password = AesEncryption.HashSHA1(password);
                header.Salt = AesEncryption.Salt();
            }

            // point to begin file
            stream.Seek(0, SeekOrigin.Begin);

            // get header page in bytes
            var buffer = header.WritePage();

            stream.Write(buffer, 0, BasePage.PAGE_SIZE);

            // write second page as an empty AREA (it's not a page) just to use as lock control
            stream.Write(new byte[BasePage.PAGE_SIZE], 0, BasePage.PAGE_SIZE);

            // create crypto class if has password
            var crypto = password != null ? new AesEncryption(password, header.Salt) : null;

            // if initial size is defined, lets create empty pages in a linked list
            if (emptyPages > 0)
            {
                stream.SetLength(initialSize);

                var pageID = 1u;

                while(++pageID < (emptyPages + 2))
                {
                    var empty = new EmptyPage(pageID)
                    {
                        PrevPageID = pageID == 2 ? 0 : pageID - 1,
                        NextPageID = pageID == emptyPages + 1 ? uint.MaxValue : pageID + 1
                    };

                    var bytes = empty.WritePage();

                    if (password != null)
                    {
                        bytes = crypto!.Encrypt(bytes);
                    }

                    stream.Write(bytes, 0, BasePage.PAGE_SIZE);
                }
            }

            if (crypto != null) crypto.Dispose();
        }
    }
}
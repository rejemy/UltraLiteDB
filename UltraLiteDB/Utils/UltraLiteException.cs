using System;
using System.Reflection;

namespace UltraLiteDB
{
    /// <summary>
    /// Base exception type for all UltraLiteDB errors. Each error has a numeric <see cref="ErrorCode"/>
    /// and static factory methods for common failure scenarios (index violations, lock timeouts, etc.).
    /// </summary>
    public class UltraLiteException : Exception
    {
        #region Errors code

        public const int FILE_NOT_FOUND = 101;
        public const int INVALID_DATABASE = 103;
        public const int INVALID_DATABASE_VERSION = 104;
        public const int FILE_SIZE_EXCEEDED = 105;
        public const int COLLECTION_LIMIT_EXCEEDED = 106;
        public const int INDEX_DROP_IP = 108;
        public const int INDEX_LIMIT_EXCEEDED = 109;
        public const int INDEX_DUPLICATE_KEY = 110;
        public const int INDEX_KEY_TOO_LONG = 111;
        public const int INDEX_NOT_FOUND = 112;
        public const int LOCK_TIMEOUT = 120;
        public const int INVALID_COMMAND = 121;
        public const int ALREADY_EXISTS_COLLECTION_NAME = 122;
        public const int DATABASE_WRONG_PASSWORD = 123;
        public const int SYNTAX_ERROR = 127;

        public const int INVALID_FORMAT = 200;
        public const int DOCUMENT_MAX_DEPTH = 201;
        public const int INVALID_CTOR = 202;
        public const int UNEXPECTED_TOKEN = 203;
        public const int INVALID_DATA_TYPE = 204;
        public const int PROPERTY_NOT_MAPPED = 206;
        public const int INVALID_TYPED_NAME = 207;



        #endregion

        #region Ctor

        public int ErrorCode { get; private set; }
        public string? Line { get; private set; }
        public long Position { get; private set; }

        public UltraLiteException(string message)
            : base(message)
        {
        }

        internal UltraLiteException(int code, string message, params object[] args)
            : base(string.Format(message, args))
        {
            this.ErrorCode = code;
        }

        internal UltraLiteException (int code, Exception? inner, string message, params object[] args)
        : base (string.Format (message, args), inner)
        {
            this.ErrorCode = code;
        }

        #endregion

        #region Method Errors

        internal static UltraLiteException FileNotFound(string fileId)
        {
            return new UltraLiteException(FILE_NOT_FOUND, "File '{0}' not found.", fileId);
        }

        internal static UltraLiteException InvalidDatabase()
        {
            return new UltraLiteException(INVALID_DATABASE, "Datafile is not a LiteDB database.");
        }

        internal static UltraLiteException InvalidDatabaseVersion(int version)
        {
            return new UltraLiteException(INVALID_DATABASE_VERSION, "Invalid database version: {0}", version);
        }

        internal static UltraLiteException FileSizeExceeded(long limit)
        {
            return new UltraLiteException(FILE_SIZE_EXCEEDED, "Database size exceeds limit of {0}.", StorageUnitHelper.FormatFileSize(limit));
        }

        internal static UltraLiteException CollectionLimitExceeded(int limit)
        {
            return new UltraLiteException(COLLECTION_LIMIT_EXCEEDED, "This database exceeded the maximum limit of collection names size: {0} bytes", limit);
        }

        internal static UltraLiteException IndexDropId()
        {
            return new UltraLiteException(INDEX_DROP_IP, "Primary key index '_id' can't be dropped.");
        }

        internal static UltraLiteException IndexLimitExceeded(string collection)
        {
            return new UltraLiteException(INDEX_LIMIT_EXCEEDED, "Collection '{0}' exceeded the maximum limit of indices: {1}", collection, CollectionIndex.INDEX_PER_COLLECTION);
        }

        internal static UltraLiteException IndexDuplicateKey(string field, BsonValue key)
        {
            return new UltraLiteException(INDEX_DUPLICATE_KEY, "Cannot insert duplicate key in unique index '{0}'. The duplicate value is '{1}'.", field, key);
        }

        internal static UltraLiteException IndexKeyTooLong()
        {
            return new UltraLiteException(INDEX_KEY_TOO_LONG, "Index key must be less than {0} bytes.", IndexService.MAX_INDEX_LENGTH);
        }

        internal static UltraLiteException IndexNotFound(string collection, string field)
        {
            return new UltraLiteException(INDEX_NOT_FOUND, "Index not found on '{0}.{1}'.", collection, field);
        }

        internal static UltraLiteException LockTimeout(TimeSpan ts)
        {
            return new UltraLiteException(LOCK_TIMEOUT, "Timeout. Database is locked for more than {0}.", ts.ToString());
        }

        internal static UltraLiteException AlreadyExistsCollectionName(string newName)
        {
            return new UltraLiteException(ALREADY_EXISTS_COLLECTION_NAME, "New collection name '{0}' already exists.", newName);
        }

        internal static UltraLiteException DatabaseWrongPassword()
        {
            return new UltraLiteException(DATABASE_WRONG_PASSWORD, "Invalid database password.");
        }

        internal static UltraLiteException InvalidFormat(string field)
        {
            return new UltraLiteException(INVALID_FORMAT, "Invalid format: {0}", field);
        }

        internal static UltraLiteException SyntaxError(StringScanner s, string message = "Unexpected token")
        {
            return new UltraLiteException(SYNTAX_ERROR, message)
            {
                Line = s.Source,
                Position = s.Index
            };
        }

        internal static UltraLiteException UnexpectedToken(Token? token, string? expected = null)
        {
            var position = (token?.Position - (token?.Value?.Length ?? 0)) ?? 0;
            var str = token?.Type == TokenType.EOF ? "[EOF]" : token?.Value ?? "";
            var exp = expected == null ? "" : $" Expected `{expected}`.";

            return new UltraLiteException(UNEXPECTED_TOKEN, $"Unexpected token `{str}` in position {position}.{exp}")
            {
                Position = position
            };
        }

        #endregion

        #region Document/Mapper Errors

        internal static UltraLiteException InvalidFormat(string field, string format)
        {
            return new UltraLiteException(INVALID_FORMAT, "Invalid format: {0}", field);
        }

        internal static UltraLiteException DocumentMaxDepth(int depth, Type type)
        {
            return new UltraLiteException(DOCUMENT_MAX_DEPTH, "Document has more than {0} nested documents in '{1}'. Check for circular references.", depth, type == null ? "-" : type.Name);
        }

        internal static UltraLiteException InvalidCtor(Type type, Exception? inner)
        {
            return new UltraLiteException(INVALID_CTOR, inner, "Failed to create instance for type '{0}' from assembly '{1}'. Checks if the class has a public constructor with no parameters.", type.FullName, type.AssemblyQualifiedName);
        }

        internal static UltraLiteException UnexpectedToken(string token)
        {
            return new UltraLiteException(UNEXPECTED_TOKEN, "Unexpected JSON token: {0}", token);
        }

        internal static UltraLiteException InvalidDataType(string field, BsonValue value)
        {
            return new UltraLiteException(INVALID_DATA_TYPE, "Invalid BSON data type '{0}' on field '{1}'.", value.Type, field);
        }

        public const int PROPERTY_READ_WRITE = 204;

        internal static UltraLiteException PropertyReadWrite(PropertyInfo prop)
        {
            return new UltraLiteException(PROPERTY_READ_WRITE, "'{0}' property must have public getter and setter.", prop.Name);
        }

        internal static UltraLiteException PropertyNotMapped(string name)
        {
            return new UltraLiteException(PROPERTY_NOT_MAPPED, "Property '{0}' was not mapped into BsonDocument.", name);
        }

        internal static UltraLiteException InvalidTypedName(string type)
        {
            return new UltraLiteException(INVALID_TYPED_NAME, "Type '{0}' not found in current domain (_type format is 'Type.FullName, AssemblyName').", type);
        }

        internal static UltraLiteException InvalidTypedId(BsonValue typeId)
        {
            return new UltraLiteException(INVALID_TYPED_NAME, "Type '{0}' not found in custom type registry.", typeId);
        }

        #endregion
    }
}
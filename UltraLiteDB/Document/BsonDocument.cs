using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB
{
    /// <summary>
    /// Represents a BSON document — an ordered set of string-keyed <see cref="BsonValue"/> fields.
    /// Keys are case-insensitive. Implements <see cref="IDictionary{TKey,TValue}"/> for convenient field access.
    /// This is the primary unit of storage in UltraLiteDB.
    /// </summary>
    public class BsonDocument : BsonValue, IDictionary<string, BsonValue>
    {
        /// <summary>
        /// Creates an empty BSON document.
        /// </summary>
        public BsonDocument()
            : base(BsonType.Document, new Dictionary<string, BsonValue>(StringComparer.OrdinalIgnoreCase))
        {
        }

        /// <summary>
        /// Creates a BSON document from a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
        /// </summary>
        public BsonDocument(ConcurrentDictionary<string, BsonValue> dict)
            : this()
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            foreach(var element in dict)
            {
                this.Add(element);
            }
        }

        /// <summary>
        /// Creates a BSON document from a dictionary of string keys to <see cref="BsonValue"/>.
        /// </summary>
        public BsonDocument(IDictionary<string, BsonValue> dict)
            : this()
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            foreach (var element in dict)
            {
                this.Add(element);
            }
        }

        /// <summary>
        /// Creates a BSON document from a non-generic dictionary. Values are converted via <see cref="BsonValue.FromObject"/>.
        /// </summary>
        public BsonDocument(IDictionary dict)
            : this()
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            foreach (var key in dict.Keys)
            {
                this.Add(key.ToString(), BsonValue.FromObject(dict[key]));
            }
        }

        internal new Dictionary<string, BsonValue> RawValue => base.RawValue as Dictionary<string, BsonValue>;

        /// <summary>
        /// Internal page address of this document within the data file. Populated during Find operations.
        /// </summary>
        internal PageAddress RawId { get; set; } = PageAddress.Empty;

        /// <summary>
        /// Gets or sets a field by key (case-insensitive). Returns <see cref="BsonValue.Null"/> for missing keys.
        /// Setting a null value stores <see cref="BsonValue.Null"/>.
        /// </summary>
        public override BsonValue this[string key]
        {
            get
            {
                return this.RawValue.GetOrDefault(key, BsonValue.Null);
            }
            set
            {
                this.RawValue[key] = value ?? BsonValue.Null;
            }
        }

        /// <summary>
        /// Gets a boolean field value, or null if the key is missing or not a boolean.
        /// </summary>
        public bool? GetBool(string key)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsBoolean) return value;
            }
            return null;
        }

        /// <summary>
        /// Gets a boolean field value, or <paramref name="def"/> if the key is missing or not a boolean.
        /// </summary>
        public bool GetBoolOrDefault(string key, bool def)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsBoolean) return value;
            }
            return def;
        }

        /// <summary>
        /// Gets a string field value, or null if the key is missing or not a string.
        /// </summary>
        public string GetString(string key)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsString) return value;
            }
            return null;
        }

        /// <summary>
        /// Gets a string field value, or <paramref name="def"/> if the key is missing or not a string.
        /// </summary>
        public string GetStringOrDefault(string key, string def)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsString) return value;
            }
            return def;
        }

        /// <summary>
        /// Gets a numeric field value as Int32, or null if the key is missing or not numeric.
        /// </summary>
        public int? GetInt32(string key)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsInt32;
            }
            return null;
        }

        /// <summary>
        /// Gets a numeric field value as Int32, or <paramref name="def"/> if the key is missing or not numeric.
        /// </summary>
        public int GetInt32OrDefault(string key, int def)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsInt32;
            }
            return def;
        }

        /// <summary>
        /// Gets a numeric field value as Int64, or null if the key is missing or not numeric.
        /// </summary>
        public long? GetInt64(string key)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsInt64;
            }
            return null;
        }

        /// <summary>
        /// Gets a numeric field value as Int64, or <paramref name="def"/> if the key is missing or not numeric.
        /// </summary>
        public long GetInt64OrDefault(string key, long def)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsInt64;
            }
            return def;
        }

        /// <summary>
        /// Gets a numeric field value as Single, or null if the key is missing or not numeric.
        /// </summary>
        public float? GetSingle(string key)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsSingle;
            }
            return null;
        }

        /// <summary>
        /// Gets a numeric field value as Single, or <paramref name="def"/> if the key is missing or not numeric.
        /// </summary>
        public float GetSingleOrDefault(string key, float def)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsSingle;
            }
            return def;
        }

        /// <summary>
        /// Gets a numeric field value as Double, or null if the key is missing or not numeric.
        /// </summary>
        public double? GetDouble(string key)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsDouble;
            }
            return null;
        }
        /// <summary>
        /// Gets a numeric field value as Double, or <paramref name="def"/> if the key is missing or not numeric.
        /// </summary>
        public double GetDoubleOrDefault(string key, double def)
        {
            BsonValue value;
            if(this.RawValue.TryGetValue(key, out value))
            {
                if(value.IsNumber) return value.AsDouble;
            }
            return def;
        }

        #region CompareTo

        public override int CompareTo(BsonValue other)
        {
            // if types are different, returns sort type order
            if (other.Type != BsonType.Document) return this.Type.CompareTo(other.Type);

            var thisKeys = this.Keys.ToArray();
            var thisLength = thisKeys.Length;

            var otherDoc = other.AsDocument;
            var otherKeys = otherDoc.Keys.ToArray();
            var otherLength = otherKeys.Length;

            var result = 0;
            var i = 0;
            var stop = Math.Min(thisLength, otherLength);

            for (; 0 == result && i < stop; i++)
                result = this[thisKeys[i]].CompareTo(otherDoc[otherKeys[i]]);

            // are different
            if (result != 0) return result;

            // test keys length to check which is bigger
            if (i == thisLength) return i == otherLength ? 0 : -1;

            return 1;
        }

        #endregion

        #region IDictionary

        public ICollection<string> Keys => this.RawValue.Keys;

        public ICollection<BsonValue> Values => this.RawValue.Values;

        public int Count => this.RawValue.Count;

        public bool IsReadOnly => false;

        public bool ContainsKey(string key) => this.RawValue.ContainsKey(key);

        /// <summary>
        /// Enumerates all document fields, yielding "_id" first (if present), then remaining fields.
        /// </summary>
        public IEnumerable<KeyValuePair<string, BsonValue>> GetElements()
        {
            if(this.RawValue.TryGetValue("_id", out var id))
            {
                yield return new KeyValuePair<string, BsonValue>("_id", id);
            }

            foreach(var item in this.RawValue.Where(x => x.Key != "_id"))
            {
                yield return item;
            }
        }

        public void Add(string key, BsonValue value) => this.RawValue.Add(key, value ?? BsonValue.Null);

        public bool Remove(string key) => this.RawValue.Remove(key);

        public void Clear() => this.RawValue.Clear();

        public bool TryGetValue(string key, out BsonValue value) => this.RawValue.TryGetValue(key, out value);

        public void Add(KeyValuePair<string, BsonValue> item) => this.Add(item.Key, item.Value);
        public void Add(KeyValuePair<string, object> item) => this.Add(item.Key, BsonValue.FromObject(item.Value));

        public bool Contains(KeyValuePair<string, BsonValue> item) => this.RawValue.Contains(item);

        public bool Remove(KeyValuePair<string, BsonValue> item) => this.Remove(item.Key);

        public IEnumerator<KeyValuePair<string, BsonValue>> GetEnumerator() => this.RawValue.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.RawValue.GetEnumerator();

        public void CopyTo(KeyValuePair<string, BsonValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, BsonValue>>)this.RawValue).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Copies all fields from this document into <paramref name="other"/>, overwriting existing keys.
        /// </summary>
        public void CopyTo(BsonDocument other)
        {
            foreach(var element in this)
            {
                other[element.Key] = element.Value;
            }
        }

        #endregion

        private int _length = 0;

        public override int GetBytesCount(bool recalc)
        {
            if (recalc == false && _length > 0) return _length;

            var length = 5;

            foreach(var element in this.RawValue)
            {
                length += this.GetBytesCountElement(element.Key, element.Value);
            }

            return _length = length;
        }
    }
}

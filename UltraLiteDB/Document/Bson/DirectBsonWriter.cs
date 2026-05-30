using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace UltraLiteDB
{
    /// <summary>
    /// Serializes C# objects directly to BSON bytes, bypassing intermediate <see cref="BsonDocument"/> creation.
    /// Reduces GC pressure for high-throughput serialization. Supports polymorphism via _t/_type discriminators.
    /// </summary>
    internal static class DirectBsonWriter
    {
        private const int MAX_DEPTH = 20;

        /// <summary>
        /// Write a C# object as a BSON document directly to the ByteWriter
        /// </summary>
        public static void WriteObjectDirect(ByteWriter writer, BsonMapper mapper, Type declaredType, object obj, int depth)
        {
            if (++depth > MAX_DEPTH) throw UltraLiteException.DocumentMaxDepth(MAX_DEPTH, declaredType);

            var t = obj.GetType();
            var entity = mapper.GetEntityMapper(t);

            // Record position for length backfill
            var startPos = writer.Position;
            writer.EnsureCapacity(4);
            writer.Skip(4); // reserve space for document length

            // Add _type/_t for derived types
            if (declaredType != t)
            {
                if (mapper.CustomTypeToId.TryGetValue(t, out BsonValue customTypeId))
                {
                    WriteElementFromBsonValue(writer, "_t", customTypeId);
                }
                else if (mapper.IncludeFullType)
                {
                    var typeName = t.FullName + ", " + t.GetTypeInfo().Assembly.GetName().Name;
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount("_type") + 1 + 4 + Encoding.UTF8.GetByteCount(typeName) + 1);
                    writer.Write((byte)0x02);
                    WriteCString(writer, "_type");
                    WriteString(writer, typeName);
                }
            }

            foreach (var member in entity.Members)
            {
                if (member.Getter == null) continue;

                // members excluded from mapping have a null FieldName
                if (member.FieldName == null) continue;

                var value = member.Getter(obj);

                if (value == null && !mapper.SerializeNullValues && member.FieldName != "_id") continue;

                if (member.Serialize != null)
                {
                    // Custom member serializer — falls back to BsonValue
                    var bsonValue = member.Serialize(value, mapper);
                    WriteElementFromBsonValue(writer, member.FieldName, bsonValue ?? BsonValue.Null);
                }
                else
                {
                    WriteElementDirect(writer, mapper, member.FieldName, member.DataType, value, depth);
                }
            }

            // Write terminator
            writer.EnsureCapacity(1);
            writer.Write((byte)0x00);

            // Backfill document length
            var endPos = writer.Position;
            var length = endPos - startPos;
            writer.Position = startPos;
            writer.Write((Int32)length);
            writer.Position = endPos;
        }

        /// <summary>
        /// Write a single value as a BSON element (type byte + CString key + value bytes)
        /// </summary>
        public static void WriteElementDirect(ByteWriter writer, BsonMapper mapper, string key, Type declaredType, object? value, int depth)
        {
            if (value == null)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                writer.Write((byte)0x0A);
                WriteCString(writer, key);
                return;
            }

            // Already a BsonValue — use existing writer
            if (value is BsonValue bv)
            {
                WriteElementFromBsonValue(writer, key, bv);
                return;
            }

            if (value is String strVal)
            {
                var str = mapper.TrimWhitespace ? strVal.Trim() : strVal;

                if (mapper.EmptyStringToNull && str.Length == 0)
                {
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                    writer.Write((byte)0x0A);
                    WriteCString(writer, key);
                    return;
                }

                var strBytes = Encoding.UTF8.GetBytes(str);
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + strBytes.Length + 1);
                writer.Write((byte)0x02);
                WriteCString(writer, key);
                writer.Write(strBytes.Length + 1);
                writer.Write(strBytes);
                writer.Write((byte)0x00);
            }
            else if (value is Int32 i32)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4);
                writer.Write((byte)0x10);
                WriteCString(writer, key);
                writer.Write(i32);
            }
            else if (value is Int64 i64)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                writer.Write((byte)0x12);
                WriteCString(writer, key);
                writer.Write(i64);
            }
            else if (value is Double dbl)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                writer.Write((byte)0x01);
                WriteCString(writer, key);
                writer.Write(dbl);
            }
            else if (value is Decimal dec)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 16);
                writer.Write((byte)0x13);
                WriteCString(writer, key);
                writer.Write(dec);
            }
            else if (value is Byte[] byteArr)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + 1 + byteArr.Length);
                writer.Write((byte)0x05);
                WriteCString(writer, key);
                writer.Write(byteArr.Length);
                writer.Write((byte)0x00); // Generic binary subtype
                writer.Write(byteArr);
            }
            else if (value is ArraySegment<byte> segment)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + 1 + segment.Count);
                writer.Write((byte)0x05);
                WriteCString(writer, key);
                writer.Write(segment.Count);
                writer.Write((byte)0x00);
                if (segment.Count > 0)
                {
                    writer.Write(segment);
                }
            }
            else if (value is ObjectId oid)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 12);
                writer.Write((byte)0x07);
                WriteCString(writer, key);
                writer.Write(oid.ToByteArray());
            }
            else if (value is Guid guid)
            {
                var guidBytes = guid.ToByteArray();
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + 1 + guidBytes.Length);
                writer.Write((byte)0x05);
                WriteCString(writer, key);
                writer.Write(guidBytes.Length);
                writer.Write((byte)0x04); // UUID subtype
                writer.Write(guidBytes);
            }
            else if (value is Boolean boolVal)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 1);
                writer.Write((byte)0x08);
                WriteCString(writer, key);
                writer.Write((byte)(boolVal ? 0x01 : 0x00));
            }
            else if (value is DateTime dateVal)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                writer.Write((byte)0x09);
                WriteCString(writer, key);
                var utc = (dateVal == DateTime.MinValue || dateVal == DateTime.MaxValue) ? dateVal : dateVal.ToUniversalTime();
                var ts = utc - BsonValue.UnixEpoch;
                writer.Write(Convert.ToInt64(ts.TotalMilliseconds));
            }
            // Converted types
            else if (value is Int16 || value is UInt16 || value is Byte || value is SByte)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4);
                writer.Write((byte)0x10);
                WriteCString(writer, key);
                writer.Write(Convert.ToInt32(value));
            }
            else if (value is UInt32 u32)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                writer.Write((byte)0x12);
                WriteCString(writer, key);
                writer.Write(Convert.ToInt64(u32));
            }
            else if (value is UInt64 u64)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                writer.Write((byte)0x12);
                WriteCString(writer, key);
                writer.Write(unchecked((Int64)u64));
            }
            else if (value is Single sng)
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                writer.Write((byte)0x01);
                WriteCString(writer, key);
                writer.Write(Convert.ToDouble(sng));
            }
            else if (value is Char || value is Enum)
            {
                var str = value.ToString();
                var strBytes = Encoding.UTF8.GetBytes(str);
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + strBytes.Length + 1);
                writer.Write((byte)0x02);
                WriteCString(writer, key);
                writer.Write(strBytes.Length + 1);
                writer.Write(strBytes);
                writer.Write((byte)0x00);
            }
            // Custom serializers
            else if (mapper.CustomSerializer.TryGetValue(declaredType, out var custom) ||
                     mapper.CustomSerializer.TryGetValue(value.GetType(), out custom))
            {
                var bsonValue = custom(value);
                WriteElementFromBsonValue(writer, key, bsonValue ?? BsonValue.Null);
            }
            // Dictionary
            else if (value is IDictionary dict)
            {
                var valueType = declaredType == typeof(object) ? value.GetType() : declaredType;
                var itemType = valueType.GetTypeInfo().GetGenericArguments()[1];

                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                writer.Write((byte)0x03);
                WriteCString(writer, key);
                WriteDictionaryDirect(writer, mapper, itemType, dict, depth);
            }
            // IEnumerable (arrays, lists)
            else if (value is IEnumerable enumerable)
            {
                var itemType = Reflection.GetListItemType(value.GetType());

                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                writer.Write((byte)0x04);
                WriteCString(writer, key);
                WriteArrayDirect(writer, mapper, itemType, enumerable, depth);
            }
            // Complex object
            else
            {
                writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                writer.Write((byte)0x03);
                WriteCString(writer, key);
                WriteObjectDirect(writer, mapper, declaredType, value, depth);
            }
        }

        /// <summary>
        /// Write an IEnumerable as a BSON array directly
        /// </summary>
        public static void WriteArrayDirect(ByteWriter writer, BsonMapper mapper, Type itemType, IEnumerable items, int depth)
        {
            if (++depth > MAX_DEPTH) throw UltraLiteException.DocumentMaxDepth(MAX_DEPTH, itemType);

            var startPos = writer.Position;
            writer.EnsureCapacity(4);
            writer.Skip(4); // reserve for length

            var i = 0;
            foreach (var item in items)
            {
                WriteElementDirect(writer, mapper, i.ToString(), itemType, item, depth);
                i++;
            }

            writer.EnsureCapacity(1);
            writer.Write((byte)0x00);

            var endPos = writer.Position;
            var length = endPos - startPos;
            writer.Position = startPos;
            writer.Write((Int32)length);
            writer.Position = endPos;
        }

        /// <summary>
        /// Write an IDictionary as a BSON document directly
        /// </summary>
        public static void WriteDictionaryDirect(ByteWriter writer, BsonMapper mapper, Type valueType, IDictionary dict, int depth)
        {
            if (++depth > MAX_DEPTH) throw UltraLiteException.DocumentMaxDepth(MAX_DEPTH, valueType);

            var startPos = writer.Position;
            writer.EnsureCapacity(4);
            writer.Skip(4); // reserve for length

            foreach (var key in dict.Keys)
            {
                var val = dict[key];
                WriteElementDirect(writer, mapper, key.ToString(), valueType, val, depth);
            }

            writer.EnsureCapacity(1);
            writer.Write((byte)0x00);

            var endPos = writer.Position;
            var length = endPos - startPos;
            writer.Position = startPos;
            writer.Write((Int32)length);
            writer.Position = endPos;
        }

        /// <summary>
        /// Write a BsonValue as an element using the existing BsonWriter format.
        /// Used as fallback for custom serializers and BsonValue-typed properties.
        /// </summary>
        private static void WriteElementFromBsonValue(ByteWriter writer, string key, BsonValue value)
        {
            switch (value.Type)
            {
                case BsonType.Double:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                    writer.Write((byte)0x01);
                    WriteCString(writer, key);
                    writer.Write((Double)value.RawValue);
                    break;

                case BsonType.String:
                    var strBytes = Encoding.UTF8.GetBytes((String)value.RawValue);
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + strBytes.Length + 1);
                    writer.Write((byte)0x02);
                    WriteCString(writer, key);
                    writer.Write(strBytes.Length + 1);
                    writer.Write(strBytes);
                    writer.Write((byte)0x00);
                    break;

                case BsonType.Document:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                    writer.Write((byte)0x03);
                    WriteCString(writer, key);
                    BsonWriter.WriteDocument(writer, (BsonDocument)value);
                    break;

                case BsonType.Array:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                    writer.Write((byte)0x04);
                    WriteCString(writer, key);
                    BsonWriter.WriteArray(writer, new BsonArray((List<BsonValue>)value.RawValue));
                    break;

                case BsonType.Binary:
                    var bytes = (ArraySegment<byte>)value.RawValue;
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + 1 + bytes.Count);
                    writer.Write((byte)0x05);
                    WriteCString(writer, key);
                    writer.Write(bytes.Count);
                    writer.Write((byte)0x00);
                    writer.Write(bytes);
                    break;

                case BsonType.Guid:
                    var guidBytes = ((Guid)value.RawValue).ToByteArray();
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4 + 1 + guidBytes.Length);
                    writer.Write((byte)0x05);
                    WriteCString(writer, key);
                    writer.Write(guidBytes.Length);
                    writer.Write((byte)0x04);
                    writer.Write(guidBytes);
                    break;

                case BsonType.ObjectId:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 12);
                    writer.Write((byte)0x07);
                    WriteCString(writer, key);
                    writer.Write(((ObjectId)value.RawValue).ToByteArray());
                    break;

                case BsonType.Boolean:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 1);
                    writer.Write((byte)0x08);
                    WriteCString(writer, key);
                    writer.Write((byte)(((Boolean)value.RawValue) ? 0x01 : 0x00));
                    break;

                case BsonType.DateTime:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                    writer.Write((byte)0x09);
                    WriteCString(writer, key);
                    var date = (DateTime)value.RawValue;
                    var utc = (date == DateTime.MinValue || date == DateTime.MaxValue) ? date : date.ToUniversalTime();
                    var ts = utc - BsonValue.UnixEpoch;
                    writer.Write(Convert.ToInt64(ts.TotalMilliseconds));
                    break;

                case BsonType.Null:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                    writer.Write((byte)0x0A);
                    WriteCString(writer, key);
                    break;

                case BsonType.Int32:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 4);
                    writer.Write((byte)0x10);
                    WriteCString(writer, key);
                    writer.Write((Int32)value.RawValue);
                    break;

                case BsonType.Int64:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 8);
                    writer.Write((byte)0x12);
                    WriteCString(writer, key);
                    writer.Write((Int64)value.RawValue);
                    break;

                case BsonType.Decimal:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1 + 16);
                    writer.Write((byte)0x13);
                    WriteCString(writer, key);
                    writer.Write((Decimal)value.RawValue);
                    break;

                case BsonType.MinValue:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                    writer.Write((byte)0xFF);
                    WriteCString(writer, key);
                    break;

                case BsonType.MaxValue:
                    writer.EnsureCapacity(1 + Encoding.UTF8.GetByteCount(key) + 1);
                    writer.Write((byte)0x7F);
                    WriteCString(writer, key);
                    break;
            }
        }

        private static void WriteString(ByteWriter writer, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writer.Write(bytes.Length + 1);
            writer.Write(bytes);
            writer.Write((byte)0x00);
        }

        private static void WriteCString(ByteWriter writer, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writer.Write(bytes);
            writer.Write((byte)0x00);
        }
    }
}

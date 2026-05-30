using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace UltraLiteDB
{
    /// <summary>
    /// Deserializes BSON bytes directly into C# objects, bypassing intermediate <see cref="BsonDocument"/> creation.
    /// Reduces GC pressure for high-throughput deserialization. Supports polymorphism via _t/_type discriminators.
    /// </summary>
    internal static class DirectBsonReader
    {
        /// <summary>
        /// Read a BSON document from bytes directly into a C# object
        /// </summary>
        public static object ReadObjectDirect(ByteReader reader, BsonMapper mapper, Type type)
        {
            var length = reader.ReadInt32();
            var end = reader.Position + length - 5;

            // We need to peek at the first element to check for _t/_type
            // Save position in case we need to re-read
            Type resolvedType = type;
            object? obj = null;
            EntityMapper? entity = null;
            Dictionary<string, MemberMapper>? fieldLookup = null;
            bool typeResolved = false;

            while (reader.Position < end)
            {
                var bsonType = reader.ReadByte();
                var name = reader.ReadCString();

                // Check for type discriminator fields before we create the object
                if (!typeResolved)
                {
                    if (name == "_t")
                    {
                        var typeIdValue = ReadBsonValue(reader, bsonType);
                        if (mapper.CustomIdToType.TryGetValue(typeIdValue, out Type mappedType))
                        {
                            resolvedType = mappedType;
                        }
                        typeResolved = true;
                        continue;
                    }
                    else if (name == "_type")
                    {
                        var typeNameValue = ReadBsonValue(reader, bsonType);
                        var resolved = Type.GetType(typeNameValue.AsString);
                        if (resolved == null) throw UltraLiteException.InvalidTypedName(typeNameValue.AsString);
                        resolvedType = resolved;
                        typeResolved = true;
                        continue;
                    }

                    // No type discriminator — use declared type
                    typeResolved = true;
                }

                // Lazy-initialize the object and mapper on first real field
                if (obj == null)
                {
                    // Handle special case: typeof(object) -> Dictionary<string, object>
                    if (resolvedType == typeof(object))
                    {
                        resolvedType = typeof(Dictionary<string, object>);
                    }

                    // Check if we should deserialize as a dictionary
                    if (typeof(IDictionary).IsAssignableFrom(resolvedType) && resolvedType.GetTypeInfo().IsGenericType)
                    {
                        return ReadDictionaryFromPosition(reader, mapper, resolvedType, bsonType, name, end);
                    }

                    obj = mapper.TypeInstantiator(resolvedType);
                    entity = mapper.GetEntityMapper(resolvedType);
                    fieldLookup = entity.FieldLookup;
                }

                // Look up the member for this field
                if (fieldLookup!.TryGetValue(name, out MemberMapper member) && member.Setter != null)
                {
                    if (member.Deserialize != null)
                    {
                        // Custom member deserializer — fall back to BsonValue
                        var bsonValue = ReadBsonValue(reader, bsonType);
                        member.Setter(obj, member.Deserialize(bsonValue, mapper));
                    }
                    else
                    {
                        var value = ReadValueDirect(reader, mapper, bsonType, member.DataType);
                        if (value != null)
                        {
                            member.Setter(obj, value);
                        }
                    }
                }
                else
                {
                    // Unknown field — skip it
                    SkipValue(reader, bsonType);
                }
            }

            reader.ReadByte(); // terminating 0x00

            // If no fields were read, still create the object
            if (obj == null)
            {
                if (resolvedType == typeof(object))
                {
                    resolvedType = typeof(Dictionary<string, object>);
                }
                obj = mapper.TypeInstantiator(resolvedType);
            }

            return obj;
        }

        /// <summary>
        /// Helper to finish reading a dictionary when we discover mid-document that the target is IDictionary
        /// </summary>
        private static object ReadDictionaryFromPosition(ByteReader reader, BsonMapper mapper,
            Type dictType, byte firstBsonType, string firstName, int end)
        {
            var keyType = dictType.GetTypeInfo().GetGenericArguments()[0];
            var valueType = dictType.GetTypeInfo().GetGenericArguments()[1];

            var dict = (IDictionary)mapper.TypeInstantiator(dictType);

            // Process the first element we already peeked at
            var firstVal = ReadValueDirect(reader, mapper, firstBsonType, valueType);
            var firstKey = keyType.GetTypeInfo().IsEnum ? Enum.Parse(keyType, firstName) : Convert.ChangeType(firstName, keyType);
            dict.Add(firstKey, firstVal);

            // Continue reading remaining elements
            while (reader.Position < end)
            {
                var bsonType = reader.ReadByte();
                var name = reader.ReadCString();
                var val = ReadValueDirect(reader, mapper, bsonType, valueType);
                var key = keyType.GetTypeInfo().IsEnum ? Enum.Parse(keyType, name) : Convert.ChangeType(name, keyType);
                dict.Add(key, val);
            }

            reader.ReadByte(); // terminating 0x00
            return dict;
        }

        /// <summary>
        /// Read a BSON value and convert directly to the target .NET type
        /// </summary>
        public static object? ReadValueDirect(ByteReader reader, BsonMapper mapper, byte bsonType, Type targetType)
        {
            // Handle nullable types
            if (Reflection.IsNullable(targetType))
            {
                targetType = Reflection.UnderlyingTypeOf(targetType);
            }

            // Check for custom deserializer first
            if (mapper.CustomDeserializer.TryGetValue(targetType, out var custom))
            {
                var bsonValue = ReadBsonValue(reader, bsonType);
                return custom(bsonValue);
            }

            // Target wants a BsonValue/BsonDocument/BsonArray
            if (targetType == typeof(BsonValue) || targetType == typeof(BsonDocument) || targetType == typeof(BsonArray))
            {
                return ReadBsonValue(reader, bsonType);
            }

            switch (bsonType)
            {
                case 0x01: // Double
                    var dbl = reader.ReadDouble();
                    if (targetType == typeof(Double)) return dbl;
                    if (targetType == typeof(Single)) return (Single)dbl;
                    if (targetType == typeof(Decimal)) return (Decimal)dbl;
                    if (targetType == typeof(Int32)) return (Int32)dbl;
                    if (targetType == typeof(Int64)) return (Int64)dbl;
                    return dbl;

                case 0x02: // String
                    var str = reader.ReadBsonString();
                    if (targetType == typeof(String)) return str;
                    if (targetType == typeof(Char) && str.Length > 0) return str[0];
                    if (targetType.GetTypeInfo().IsEnum) return Enum.Parse(targetType, str);
                    return str;

                case 0x03: // Document
                    if (typeof(IDictionary).IsAssignableFrom(targetType) && targetType.GetTypeInfo().IsGenericType)
                    {
                        return ReadDictionaryDirect(reader, mapper, targetType);
                    }
                    return ReadObjectDirect(reader, mapper, targetType);

                case 0x04: // Array
                    return ReadArrayDirect(reader, mapper, targetType);

                case 0x05: // Binary
                    var binLen = reader.ReadInt32();
                    var subType = reader.ReadByte();
                    if (binLen == 0)
                    {
                        return new ArraySegment<byte>();
                    }
                    var binBytes = reader.ReadBytes(binLen);
                    if (subType == 0x04) // UUID
                    {
                        return new Guid(binBytes);
                    }
                    // Return as ArraySegment<byte> because the reflection setters for byte[]
                    // properties expect ArraySegment<byte> (see Reflection.CreateGenericSetter)
                    return new ArraySegment<byte>(binBytes);

                case 0x07: // ObjectId
                    return new ObjectId(reader.ReadBytes(12));

                case 0x08: // Boolean
                    var boolVal = reader.ReadBoolean();
                    return boolVal;

                case 0x09: // DateTime
                    var ts = reader.ReadInt64();
                    if (ts == 253402300800000) return DateTime.MaxValue;
                    if (ts == -62135596800000) return DateTime.MinValue;
                    return BsonValue.UnixEpoch.AddMilliseconds(ts);

                case 0x0A: // Null
                    return null;

                case 0x10: // Int32
                    var i32 = reader.ReadInt32();
                    if (targetType == typeof(Int32)) return i32;
                    if (targetType == typeof(Int16)) return (Int16)i32;
                    if (targetType == typeof(UInt16)) return (UInt16)i32;
                    if (targetType == typeof(Byte)) return (Byte)i32;
                    if (targetType == typeof(SByte)) return (SByte)i32;
                    if (targetType == typeof(Int64)) return (Int64)i32;
                    if (targetType == typeof(Double)) return (Double)i32;
                    if (targetType == typeof(Decimal)) return (Decimal)i32;
                    return i32;

                case 0x12: // Int64
                    var i64 = reader.ReadInt64();
                    if (targetType == typeof(Int64)) return i64;
                    if (targetType == typeof(UInt32)) return (UInt32)i64;
                    if (targetType == typeof(UInt64)) return unchecked((UInt64)i64);
                    if (targetType == typeof(Int32)) return (Int32)i64;
                    if (targetType == typeof(Double)) return (Double)i64;
                    if (targetType == typeof(Decimal)) return (Decimal)i64;
                    return i64;

                case 0x13: // Decimal
                    var dec = reader.ReadDecimal();
                    return dec;

                case 0xFF: // MinValue
                    return null;

                case 0x7F: // MaxValue
                    return null;

                default:
                    throw new NotSupportedException($"BSON type 0x{bsonType:X2} not supported");
            }
        }

        /// <summary>
        /// Read a BSON array directly into an Array or IList
        /// </summary>
        public static object ReadArrayDirect(ByteReader reader, BsonMapper mapper, Type targetType)
        {
            var length = reader.ReadInt32();
            var end = reader.Position + length - 5;

            // Determine the item type
            Type itemType;
            bool isArray;

            if (targetType == typeof(object))
            {
                itemType = typeof(object);
                isArray = true;
            }
            else if (targetType.IsArray)
            {
                itemType = targetType.GetElementType();
                isArray = true;
            }
            else
            {
                itemType = Reflection.GetListItemType(targetType);
                isArray = false;
            }

            if (isArray)
            {
                // Read into a temporary list, then convert to array
                var tempList = new List<object?>();

                while (reader.Position < end)
                {
                    var bsonType = reader.ReadByte();
                    reader.ReadCString(); // array index key — ignored
                    tempList.Add(ReadValueDirect(reader, mapper, bsonType, itemType));
                }

                reader.ReadByte(); // terminating 0x00

                var arr = Array.CreateInstance(itemType, tempList.Count);
                for (var i = 0; i < tempList.Count; i++)
                {
                    arr.SetValue(tempList[i], i);
                }
                return arr;
            }
            else
            {
                // IList or custom collection
                var enumerable = (IEnumerable)Reflection.CreateInstance(targetType);
                var list = enumerable as IList;

                if (list != null)
                {
                    while (reader.Position < end)
                    {
                        var bsonType = reader.ReadByte();
                        reader.ReadCString();
                        list.Add(ReadValueDirect(reader, mapper, bsonType, itemType));
                    }
                }
                else
                {
                    var addMethod = targetType.GetMethod("Add");

                    while (reader.Position < end)
                    {
                        var bsonType = reader.ReadByte();
                        reader.ReadCString();
                        addMethod.Invoke(enumerable, new[] { ReadValueDirect(reader, mapper, bsonType, itemType) });
                    }
                }

                reader.ReadByte(); // terminating 0x00
                return enumerable;
            }
        }

        /// <summary>
        /// Read a BSON document directly into an IDictionary
        /// </summary>
        public static object ReadDictionaryDirect(ByteReader reader, BsonMapper mapper, Type dictType)
        {
            var length = reader.ReadInt32();
            var end = reader.Position + length - 5;

            var keyType = dictType.GetTypeInfo().GetGenericArguments()[0];
            var valueType = dictType.GetTypeInfo().GetGenericArguments()[1];

            var dict = (IDictionary)mapper.TypeInstantiator(dictType);

            while (reader.Position < end)
            {
                var bsonType = reader.ReadByte();
                var name = reader.ReadCString();

                var val = ReadValueDirect(reader, mapper, bsonType, valueType);
                var key = keyType.GetTypeInfo().IsEnum ? Enum.Parse(keyType, name) : Convert.ChangeType(name, keyType);
                dict.Add(key, val);
            }

            reader.ReadByte(); // terminating 0x00
            return dict;
        }

        /// <summary>
        /// Read a BSON element value as a BsonValue (fallback for custom serializers)
        /// </summary>
        private static BsonValue ReadBsonValue(ByteReader reader, byte bsonType)
        {
            switch (bsonType)
            {
                case 0x01: return reader.ReadDouble();
                case 0x02: return reader.ReadBsonString();
                case 0x03: return BsonReader.ReadDocument(reader);
                case 0x04: return BsonReader.ReadArray(reader);
                case 0x05:
                    var len = reader.ReadInt32();
                    var sub = reader.ReadByte();
                    var bytes = reader.ReadBytes(len);
                    if (sub == 0x04) return new Guid(bytes);
                    return bytes;
                case 0x07: return new ObjectId(reader.ReadBytes(12));
                case 0x08: return reader.ReadBoolean();
                case 0x09:
                    var ts = reader.ReadInt64();
                    if (ts == 253402300800000) return DateTime.MaxValue;
                    if (ts == -62135596800000) return DateTime.MinValue;
                    return BsonValue.UnixEpoch.AddMilliseconds(ts);
                case 0x0A: return BsonValue.Null;
                case 0x10: return reader.ReadInt32();
                case 0x12: return reader.ReadInt64();
                case 0x13: return reader.ReadDecimal();
                case 0xFF: return BsonValue.MinValue;
                case 0x7F: return BsonValue.MaxValue;
                default: throw new NotSupportedException($"BSON type 0x{bsonType:X2} not supported");
            }
        }

        /// <summary>
        /// Skip past a BSON element's value bytes
        /// </summary>
        private static void SkipValue(ByteReader reader, byte bsonType)
        {
            switch (bsonType)
            {
                case 0x01: reader.Skip(8); break;              // Double
                case 0x02:                                       // String
                    var strLen = reader.ReadInt32();
                    reader.Skip(strLen); break;
                case 0x03:                                       // Document
                case 0x04:                                       // Array
                    var docLen = reader.ReadInt32();
                    reader.Skip(docLen - 4); break;
                case 0x05:                                       // Binary
                    var binLen = reader.ReadInt32();
                    reader.Skip(1 + binLen); break;              // subtype + data
                case 0x07: reader.Skip(12); break;               // ObjectId
                case 0x08: reader.Skip(1); break;                // Boolean
                case 0x09: reader.Skip(8); break;                // DateTime
                case 0x0A: break;                                // Null
                case 0x10: reader.Skip(4); break;                // Int32
                case 0x12: reader.Skip(8); break;                // Int64
                case 0x13: reader.Skip(16); break;               // Decimal
                case 0xFF: break;                                // MinValue
                case 0x7F: break;                                // MaxValue
                default: throw new NotSupportedException($"Cannot skip BSON type 0x{bsonType:X2}");
            }
        }
    }
}

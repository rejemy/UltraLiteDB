using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;

namespace UltraLiteDB
{
	/// <summary>
	/// Deserializes BSON bytes directly into C# objects, bypassing intermediate <see cref="BsonDocument"/> creation.
	/// Reduces GC pressure for high-throughput deserialization. Supports polymorphism via _t/_type discriminators.
	/// </summary>
	/// <remarks>
	/// Hot-path notes: field names are matched as raw UTF-8 bytes against the bytes cached on
	/// <see cref="MemberMapper"/> (trying members in declaration order first, which is the order the
	/// serializer writes them), so no strings are allocated for keys; array index keys are skipped
	/// without decoding; per-target-type conversion info is cached in <see cref="DirectReadInfo"/>.
	/// </remarks>
	internal static class DirectBsonReader
	{
		/// <summary>
		/// Read a BSON document from bytes directly into a C# object. <c>info</c> is the cached read info
		/// for <c>type</c> when the caller already has it, otherwise null.
		/// </summary>
		public static object ReadObjectDirect(ByteReader reader, BsonMapper mapper, Type type, DirectReadInfo? info = null)
		{
			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;

			Type resolvedType = type;
			object? obj = null;
			EntityMapper? entity = null;
			List<MemberMapper>? members = null;
			var nextMember = 0;
			bool typeResolved = false;

			while (reader.Position < end)
			{
				var bsonType = reader.ReadByte();
				var name = reader.ReadCStringSpan();

				// Check for type discriminator fields before we create the object
				if (!typeResolved)
				{
					typeResolved = true;

					if (IsKey(name, (byte)'_', (byte)'t'))
					{
						var typeIdValue = ReadBsonValue(reader, bsonType);
						if (mapper.CustomIdToType.TryGetValue(typeIdValue, out Type mappedType))
						{
							resolvedType = mappedType;
						}
						continue;
					}
					else if (IsTypeNameKey(name))
					{
						var typeNameValue = ReadBsonValue(reader, bsonType);
						var resolved = Type.GetType(typeNameValue.AsString);
						if (resolved == null) throw UltraLiteException.InvalidTypedName(typeNameValue.AsString);
						resolvedType = resolved;
						continue;
					}
				}

				// Lazy-initialize the object and mapper on first real field
				if (obj == null)
				{
					// Handle special case: typeof(object) -> Dictionary<string, object>
					if (resolvedType == typeof(object))
					{
						resolvedType = typeof(Dictionary<string, object>);
					}

					var typeInfo = info != null && info.Type == resolvedType ? info : mapper.GetDirectReadInfo(resolvedType);

					// Check if we should deserialize as a dictionary
					if (typeInfo.IsGenericDictionary)
					{
						return ReadDictionaryFromPosition(reader, mapper, typeInfo, bsonType, Encoding.UTF8.GetString(name), end);
					}

					obj = mapper.TypeInstantiator(resolvedType);
					entity = mapper.GetEntityMapper(resolvedType);
					members = entity.Members;
				}

				// Look up the member for this field (exact bytes first, then the case-insensitive lookup)
				var member = FindMember(members!, name, ref nextMember);

				if (member == null)
				{
					entity!.FieldLookup.TryGetValue(Encoding.UTF8.GetString(name), out member);
				}

				if (member != null && member.Setter != null)
				{
					if (member.Deserialize != null)
					{
						// Custom member deserializer — fall back to BsonValue
						var bsonValue = ReadBsonValue(reader, bsonType);
						member.Setter(obj, member.Deserialize(bsonValue, mapper));
					}
					else
					{
						var cached = member.ReadInfo;
						var memberInfo = DirectReadInfo.Get(mapper, cached, member.DataType);
						if (memberInfo != cached) Volatile.Write(ref member.ReadInfo, memberInfo);

						var value = ReadValueDirect(reader, mapper, bsonType, memberInfo);
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
		/// Finds the member whose field name bytes equal <paramref name="name"/>, starting the search at
		/// <paramref name="next"/> (the member after the previous match) so in-order documents match on the first compare.
		/// </summary>
		private static MemberMapper? FindMember(List<MemberMapper> members, ReadOnlySpan<byte> name, ref int next)
		{
			var count = members.Count;

			for (var n = 0; n < count; n++)
			{
				var idx = next + n;
				if (idx >= count) idx -= count;

				var key = members[idx].FieldNameCString;

				// key includes the null terminator
				if (key != null && key.Length == name.Length + 1 && name.SequenceEqual(new ReadOnlySpan<byte>(key, 0, name.Length)))
				{
					next = idx + 1;
					return members[idx];
				}
			}

			return null;
		}

		private static bool IsKey(ReadOnlySpan<byte> name, byte c0, byte c1)
		{
			return name.Length == 2 && name[0] == c0 && name[1] == c1;
		}

		private static bool IsTypeNameKey(ReadOnlySpan<byte> name)
		{
			return name.Length == 5 && name[0] == '_' && name[1] == 't' && name[2] == 'y' && name[3] == 'p' && name[4] == 'e';
		}

		/// <summary>
		/// Helper to finish reading a dictionary when we discover mid-document that the target is IDictionary
		/// </summary>
		private static object ReadDictionaryFromPosition(ByteReader reader, BsonMapper mapper,
			DirectReadInfo dictInfo, byte firstBsonType, string firstName, int end)
		{
			var dict = (IDictionary)mapper.TypeInstantiator(dictInfo.Type);
			var valueInfo = GetValueInfo(mapper, dictInfo);

			// Process the first element we already peeked at
			var firstVal = ReadValueDirect(reader, mapper, firstBsonType, valueInfo);
			dict.Add(ConvertKey(dictInfo, firstName), firstVal);

			// Continue reading remaining elements
			ReadDictionaryElements(reader, mapper, dictInfo, valueInfo, dict, end);

			reader.ReadByte(); // terminating 0x00
			return dict;
		}

		private static void ReadDictionaryElements(ByteReader reader, BsonMapper mapper, DirectReadInfo dictInfo,
			DirectReadInfo valueInfo, IDictionary dict, int end)
		{
			while (reader.Position < end)
			{
				var bsonType = reader.ReadByte();
				var name = Encoding.UTF8.GetString(reader.ReadCStringSpan());
				var val = ReadValueDirect(reader, mapper, bsonType, valueInfo);
				dict.Add(ConvertKey(dictInfo, name), val);
			}
		}

		private static object ConvertKey(DirectReadInfo dictInfo, string name)
		{
			var keyType = dictInfo.KeyType!;
			if (keyType == typeof(string)) return name;
			return dictInfo.KeyIsEnum ? Enum.Parse(keyType, name) : Convert.ChangeType(name, keyType);
		}

		private static DirectReadInfo GetValueInfo(BsonMapper mapper, DirectReadInfo dictInfo)
		{
			var cached = dictInfo.ValueInfo;
			var info = DirectReadInfo.Get(mapper, cached, dictInfo.ValueType!);
			if (info != cached) Volatile.Write(ref dictInfo.ValueInfo, info);
			return info;
		}

		private static DirectReadInfo GetItemInfo(BsonMapper mapper, DirectReadInfo listInfo)
		{
			var cached = listInfo.ItemInfo;
			var info = DirectReadInfo.Get(mapper, cached, listInfo.ItemType);
			if (info != cached) Volatile.Write(ref listInfo.ItemInfo, info);
			return info;
		}

		/// <summary>
		/// Read a BSON value and convert directly to the target .NET type
		/// </summary>
		public static object? ReadValueDirect(ByteReader reader, BsonMapper mapper, byte bsonType, DirectReadInfo info)
		{
			// Check for custom deserializer first
			if (info.Custom != null)
			{
				var bsonValue = ReadBsonValue(reader, bsonType);
				return info.Custom(bsonValue);
			}

			// Target wants a BsonValue/BsonDocument/BsonArray
			if (info.IsBsonValue)
			{
				return ReadBsonValue(reader, bsonType);
			}

			switch (bsonType)
			{
				case 0x01: // Double
					var dbl = reader.ReadDouble();
					switch (info.Code)
					{
						case TypeCode.Double: return dbl;
						case TypeCode.Single: return (Single)dbl;
						case TypeCode.Decimal: return (Decimal)dbl;
						case TypeCode.Int32: return (Int32)dbl;
						case TypeCode.Int64: return (Int64)dbl;
						default: return dbl;
					}

				case 0x02: // String
					if (info.Enum != null)
					{
						// length prefix includes the trailing null terminator
						var strLength = reader.ReadInt32();
						return info.Enum.Parse(reader.ReadSpan(strLength).Slice(0, strLength - 1));
					}
					var str = reader.ReadBsonString();
					if (info.Code == TypeCode.String) return str;
					if (info.Code == TypeCode.Char && str.Length > 0) return str[0];
					return str;

				case 0x03: // Document
					if (info.IsGenericDictionary)
					{
						return ReadDictionaryDirect(reader, mapper, info);
					}
					return ReadObjectDirect(reader, mapper, info.Type, info);

				case 0x04: // Array
					return ReadArrayDirect(reader, mapper, info);

				case 0x05: // Binary
					var binLen = reader.ReadInt32();
					var subType = reader.ReadByte();
					if (binLen == 0)
					{
						return new ArraySegment<byte>();
					}
					if (subType == 0x04 && binLen == 16) // UUID
					{
						return reader.ReadGuid();
					}
					var binBytes = reader.ReadBytes(binLen);
					if (subType == 0x04) // UUID of the wrong size: let Guid report the error
					{
						return new Guid(binBytes);
					}
					// Return as ArraySegment<byte> because the reflection setters for byte[]
					// properties expect ArraySegment<byte> (see Reflection.CreateGenericSetter)
					return new ArraySegment<byte>(binBytes);

				case 0x07: // ObjectId
					return reader.ReadObjectId();

				case 0x08: // Boolean
					return reader.ReadBoolean();

				case 0x09: // DateTime
					return ReadDateTime(reader);

				case 0x0A: // Null
					return null;

				case 0x10: // Int32
					var i32 = reader.ReadInt32();
					switch (info.Code)
					{
						case TypeCode.Int32: return i32;
						case TypeCode.Int16: return (Int16)i32;
						case TypeCode.UInt16: return (UInt16)i32;
						case TypeCode.Byte: return (Byte)i32;
						case TypeCode.SByte: return (SByte)i32;
						case TypeCode.Int64: return (Int64)i32;
						case TypeCode.Double: return (Double)i32;
						case TypeCode.Decimal: return (Decimal)i32;
						default: return i32;
					}

				case 0x12: // Int64
					var i64 = reader.ReadInt64();
					switch (info.Code)
					{
						case TypeCode.Int64: return i64;
						case TypeCode.UInt32: return (UInt32)i64;
						case TypeCode.UInt64: return unchecked((UInt64)i64);
						case TypeCode.Int32: return (Int32)i64;
						case TypeCode.Double: return (Double)i64;
						case TypeCode.Decimal: return (Decimal)i64;
						default: return i64;
					}

				case 0x13: // Decimal
					return reader.ReadDecimal();

				case 0xFF: // MinValue
					return null;

				case 0x7F: // MaxValue
					return null;

				default:
					throw new NotSupportedException($"BSON type 0x{bsonType:X2} not supported");
			}
		}

		private static DateTime ReadDateTime(ByteReader reader)
		{
			var ts = reader.ReadInt64();
			if (ts == 253402300800000) return DateTime.MaxValue;
			if (ts == -62135596800000) return DateTime.MinValue;
			return BsonValue.UnixEpoch.AddMilliseconds(ts);
		}

		/// <summary>
		/// Read a BSON array directly into an Array or IList
		/// </summary>
		private static object ReadArrayDirect(ByteReader reader, BsonMapper mapper, DirectReadInfo info)
		{
			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;

			info.ResolveCollection();
			var itemType = info.ItemType;
			var itemInfo = GetItemInfo(mapper, info);

			if (info.IsArray)
			{
				// Count first (cheap: values are skipped, not decoded) so the array is allocated once at its final size
				var arr = Array.CreateInstance(itemType, CountElements(reader, end));
				var i = 0;

				switch (arr)
				{
					case int[] ints:
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							if (bsonType == 0x10) ints[i] = reader.ReadInt32();
							else arr.SetValue(ReadValueDirect(reader, mapper, bsonType, itemInfo), i);
							i++;
						}
						break;

					case double[] doubles:
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							if (bsonType == 0x01) doubles[i] = reader.ReadDouble();
							else arr.SetValue(ReadValueDirect(reader, mapper, bsonType, itemInfo), i);
							i++;
						}
						break;

					case float[] floats:
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							if (bsonType == 0x01) floats[i] = (Single)reader.ReadDouble();
							else arr.SetValue(ReadValueDirect(reader, mapper, bsonType, itemInfo), i);
							i++;
						}
						break;

					case long[] longs:
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							if (bsonType == 0x12) longs[i] = reader.ReadInt64();
							else arr.SetValue(ReadValueDirect(reader, mapper, bsonType, itemInfo), i);
							i++;
						}
						break;

					case bool[] bools:
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							if (bsonType == 0x08) bools[i] = reader.ReadBoolean();
							else arr.SetValue(ReadValueDirect(reader, mapper, bsonType, itemInfo), i);
							i++;
						}
						break;

					default:
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							arr.SetValue(ReadValueDirect(reader, mapper, bsonType, itemInfo), i);
							i++;
						}
						break;
				}

				reader.ReadByte(); // terminating 0x00
				return arr;
			}
			else
			{
				// IList or custom collection
				var enumerable = (IEnumerable)Reflection.CreateInstance(info.Type);

				if (enumerable is IList list)
				{
					if (!TryReadPrimitiveList(reader, mapper, itemInfo, list, end))
					{
						while (reader.Position < end)
						{
							var bsonType = ReadArrayElementHeader(reader);
							list.Add(ReadValueDirect(reader, mapper, bsonType, itemInfo));
						}
					}
				}
				else
				{
					var addMethod = info.AddMethod ??= info.Type.GetMethod("Add");

					while (reader.Position < end)
					{
						var bsonType = ReadArrayElementHeader(reader);
						addMethod.Invoke(enumerable, new[] { ReadValueDirect(reader, mapper, bsonType, itemInfo) });
					}
				}

				reader.ReadByte(); // terminating 0x00
				return enumerable;
			}
		}

		/// <summary>
		/// Fills List&lt;int/long/double/float/bool&gt; without boxing when the stored BSON type matches;
		/// other stored types go through the general conversion, exactly as the non-typed path does.
		/// </summary>
		private static bool TryReadPrimitiveList(ByteReader reader, BsonMapper mapper, DirectReadInfo itemInfo, IList list, int end)
		{
			switch (list)
			{
				case List<int> ints:
					ints.Capacity = CountElements(reader, end);
					while (reader.Position < end)
					{
						var bsonType = ReadArrayElementHeader(reader);
						if (bsonType == 0x10) ints.Add(reader.ReadInt32());
						else list.Add(ReadValueDirect(reader, mapper, bsonType, itemInfo));
					}
					return true;

				case List<double> doubles:
					doubles.Capacity = CountElements(reader, end);
					while (reader.Position < end)
					{
						var bsonType = ReadArrayElementHeader(reader);
						if (bsonType == 0x01) doubles.Add(reader.ReadDouble());
						else list.Add(ReadValueDirect(reader, mapper, bsonType, itemInfo));
					}
					return true;

				case List<float> floats:
					floats.Capacity = CountElements(reader, end);
					while (reader.Position < end)
					{
						var bsonType = ReadArrayElementHeader(reader);
						if (bsonType == 0x01) floats.Add((Single)reader.ReadDouble());
						else list.Add(ReadValueDirect(reader, mapper, bsonType, itemInfo));
					}
					return true;

				case List<long> longs:
					longs.Capacity = CountElements(reader, end);
					while (reader.Position < end)
					{
						var bsonType = ReadArrayElementHeader(reader);
						if (bsonType == 0x12) longs.Add(reader.ReadInt64());
						else list.Add(ReadValueDirect(reader, mapper, bsonType, itemInfo));
					}
					return true;

				case List<bool> bools:
					bools.Capacity = CountElements(reader, end);
					while (reader.Position < end)
					{
						var bsonType = ReadArrayElementHeader(reader);
						if (bsonType == 0x08) bools.Add(reader.ReadBoolean());
						else list.Add(ReadValueDirect(reader, mapper, bsonType, itemInfo));
					}
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// Reads an array element's type byte and skips its index key (array keys are positional and ignored).
		/// </summary>
		private static byte ReadArrayElementHeader(ByteReader reader)
		{
			var bsonType = reader.ReadByte();
			reader.SkipCString();
			return bsonType;
		}

		/// <summary>
		/// Counts the elements remaining in the current document/array without decoding them, leaving the position unchanged.
		/// </summary>
		private static int CountElements(ByteReader reader, int end)
		{
			var start = reader.Position;
			var count = 0;

			while (reader.Position < end)
			{
				var bsonType = reader.ReadByte();
				reader.SkipCString();
				SkipValue(reader, bsonType);
				count++;
			}

			reader.Position = start;
			return count;
		}

		/// <summary>
		/// Read a BSON document directly into an IDictionary
		/// </summary>
		private static object ReadDictionaryDirect(ByteReader reader, BsonMapper mapper, DirectReadInfo dictInfo)
		{
			var length = reader.ReadInt32();
			var end = reader.Position + length - 5;

			var dict = (IDictionary)mapper.TypeInstantiator(dictInfo.Type);

			ReadDictionaryElements(reader, mapper, dictInfo, GetValueInfo(mapper, dictInfo), dict, end);

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
					if (sub == 0x04 && len == 16) return reader.ReadGuid();
					var bytes = reader.ReadBytes(len);
					if (sub == 0x04) return new Guid(bytes);
					return bytes;
				case 0x07: return reader.ReadObjectId();
				case 0x08: return reader.ReadBoolean();
				case 0x09: return ReadDateTime(reader);
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

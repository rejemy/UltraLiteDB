using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;

namespace UltraLiteDB
{
	/// <summary>
	/// How the direct serializer writes a value, resolved once per (declared type, runtime type) pair
	/// instead of re-running the type-test chain for every value.
	/// </summary>
	internal enum DirectKind : byte
	{
		Object,
		BsonValue,
		String,
		Int32,
		Int64,
		Double,
		Decimal,
		Binary,
		ArraySegment,
		ObjectId,
		Guid,
		Boolean,
		DateTime,
		Int16,
		UInt16,
		Byte,
		SByte,
		UInt32,
		UInt64,
		Single,
		Char,
		Enum,
		Custom,
		Dictionary,
		Enumerable
	}

	/// <summary>
	/// Cached write dispatch for values whose runtime type is <see cref="RuntimeType"/> in a slot declared
	/// as <see cref="DeclaredType"/>. Instances are immutable apart from the lazily-filled cache fields,
	/// which are single reference writes and therefore safe to race.
	/// </summary>
	internal sealed class DirectWriteInfo
	{
		public readonly Type DeclaredType;
		public readonly Type RuntimeType;
		public readonly int Version;
		public readonly DirectKind Kind;

		/// <summary>Custom serializer (<see cref="DirectKind.Custom"/>).</summary>
		public readonly Func<object, BsonValue>? Custom;

		/// <summary>Item type (<see cref="DirectKind.Enumerable"/>) or value type (<see cref="DirectKind.Dictionary"/>).</summary>
		public readonly Type? ItemType;

		/// <summary>Enum name cache (<see cref="DirectKind.Enum"/>).</summary>
		public readonly DirectEnumInfo? Enum;

		/// <summary>Entity mapper for <see cref="RuntimeType"/> (<see cref="DirectKind.Object"/>), resolved on first use.</summary>
		public EntityMapper? Entity;

		/// <summary>Monomorphic cache of the write info for this collection's items.</summary>
		public DirectWriteInfo? ItemInfo;

		private DirectWriteInfo(Type declaredType, Type runtimeType, int version, DirectKind kind,
			Func<object, BsonValue>? custom = null, Type? itemType = null, DirectEnumInfo? enumInfo = null)
		{
			this.DeclaredType = declaredType;
			this.RuntimeType = runtimeType;
			this.Version = version;
			this.Kind = kind;
			this.Custom = custom;
			this.ItemType = itemType;
			this.Enum = enumInfo;
		}

		/// <summary>
		/// Returns <paramref name="cached"/> if it still describes <paramref name="runtimeType"/>, otherwise builds a new info.
		/// </summary>
		public static DirectWriteInfo Get(BsonMapper mapper, DirectWriteInfo? cached, Type declaredType, Type runtimeType)
		{
			if (cached != null && cached.RuntimeType == runtimeType && cached.Version == mapper.CustomTypesVersion)
			{
				return cached;
			}

			return Create(mapper, declaredType, runtimeType);
		}

		/// <summary>
		/// Classifies a value exactly as the original per-value type-test chain did: built-in BSON types
		/// first, then custom serializers (declared type, then runtime type), dictionaries, enumerables,
		/// and finally plain objects.
		/// </summary>
		private static DirectWriteInfo Create(BsonMapper mapper, Type declaredType, Type rt)
		{
			var version = mapper.CustomTypesVersion;

			DirectKind kind;

			if (typeof(BsonValue).IsAssignableFrom(rt)) kind = DirectKind.BsonValue;
			else if (rt == typeof(String)) kind = DirectKind.String;
			else if (rt == typeof(Int32)) kind = DirectKind.Int32;
			else if (rt == typeof(Int64)) kind = DirectKind.Int64;
			else if (rt == typeof(Double)) kind = DirectKind.Double;
			else if (rt == typeof(Decimal)) kind = DirectKind.Decimal;
			else if (rt == typeof(Byte[])) kind = DirectKind.Binary;
			else if (rt == typeof(ArraySegment<byte>)) kind = DirectKind.ArraySegment;
			else if (typeof(ObjectId).IsAssignableFrom(rt)) kind = DirectKind.ObjectId;
			else if (rt == typeof(Guid)) kind = DirectKind.Guid;
			else if (rt == typeof(Boolean)) kind = DirectKind.Boolean;
			else if (rt == typeof(DateTime)) kind = DirectKind.DateTime;
			else if (rt == typeof(Int16)) kind = DirectKind.Int16;
			else if (rt == typeof(UInt16)) kind = DirectKind.UInt16;
			else if (rt == typeof(Byte)) kind = DirectKind.Byte;
			else if (rt == typeof(SByte)) kind = DirectKind.SByte;
			else if (rt == typeof(UInt32)) kind = DirectKind.UInt32;
			else if (rt == typeof(UInt64)) kind = DirectKind.UInt64;
			else if (rt == typeof(Single)) kind = DirectKind.Single;
			else if (rt == typeof(Char)) kind = DirectKind.Char;
			else if (rt.GetTypeInfo().IsEnum)
			{
				return new DirectWriteInfo(declaredType, rt, version, DirectKind.Enum, enumInfo: DirectEnumInfo.Get(rt));
			}
			else if (mapper.CustomSerializer.TryGetValue(declaredType, out var custom) ||
					 mapper.CustomSerializer.TryGetValue(rt, out custom))
			{
				return new DirectWriteInfo(declaredType, rt, version, DirectKind.Custom, custom: custom);
			}
			else if (typeof(IDictionary).IsAssignableFrom(rt))
			{
				var dictType = declaredType == typeof(object) ? rt : declaredType;
				var valueType = dictType.GetTypeInfo().GetGenericArguments()[1];
				return new DirectWriteInfo(declaredType, rt, version, DirectKind.Dictionary, itemType: valueType);
			}
			else if (typeof(IEnumerable).IsAssignableFrom(rt))
			{
				return new DirectWriteInfo(declaredType, rt, version, DirectKind.Enumerable, itemType: Reflection.GetListItemType(rt));
			}
			else kind = DirectKind.Object;

			return new DirectWriteInfo(declaredType, rt, version, kind);
		}
	}

	/// <summary>
	/// Cached read conversion info for a target CLR type, so the direct deserializer doesn't repeat
	/// nullable unwrapping, custom-deserializer lookups, and interface scans for every value.
	/// </summary>
	internal sealed class DirectReadInfo
	{
		/// <summary>Target type with <see cref="Nullable{T}"/> unwrapped.</summary>
		public readonly Type Type;
		public readonly int Version;

		/// <summary><see cref="TypeCode"/> of exact primitive targets; <see cref="TypeCode.Object"/> for everything else (including enums).</summary>
		public readonly TypeCode Code;

		public readonly Func<BsonValue, object?>? Custom;
		public readonly bool IsBsonValue;
		public readonly DirectEnumInfo? Enum;

		public readonly bool IsGenericDictionary;
		public readonly Type? KeyType;
		public readonly Type? ValueType;
		public readonly bool KeyIsEnum;

		// collection info, resolved on first array read
		private volatile bool _collectionResolved;
		public Type ItemType = null!;
		public bool IsArray;
		public MethodInfo? AddMethod;

		/// <summary>Cached info for collection items.</summary>
		public DirectReadInfo? ItemInfo;

		/// <summary>Cached info for dictionary values.</summary>
		public DirectReadInfo? ValueInfo;

		private DirectReadInfo(BsonMapper mapper, Type targetType)
		{
			var type = Reflection.IsNullable(targetType) ? Reflection.UnderlyingTypeOf(targetType) : targetType;
			var typeInfo = type.GetTypeInfo();

			this.Type = type;
			this.Version = mapper.CustomTypesVersion;
			this.Custom = mapper.CustomDeserializer.TryGetValue(type, out var custom) ? custom : null;
			this.IsBsonValue = type == typeof(BsonValue) || type == typeof(BsonDocument) || type == typeof(BsonArray);
			this.Enum = typeInfo.IsEnum ? DirectEnumInfo.Get(type) : null;
			this.Code = typeInfo.IsEnum ? TypeCode.Object : System.Type.GetTypeCode(type);

			if (typeof(IDictionary).IsAssignableFrom(type) && typeInfo.IsGenericType)
			{
				var args = typeInfo.GetGenericArguments();
				this.IsGenericDictionary = true;
				this.KeyType = args[0];
				this.ValueType = args[1];
				this.KeyIsEnum = args[0].GetTypeInfo().IsEnum;
			}
		}

		/// <summary>
		/// Returns <paramref name="cached"/> if it is still current, otherwise builds a new info for <paramref name="targetType"/>.
		/// </summary>
		public static DirectReadInfo Get(BsonMapper mapper, DirectReadInfo? cached, Type targetType)
		{
			if (cached != null && cached.Version == mapper.CustomTypesVersion)
			{
				return cached;
			}

			return new DirectReadInfo(mapper, targetType);
		}

		/// <summary>
		/// Resolves item type / array-ness the same way the per-call code did: <c>object</c> reads as
		/// <c>object[]</c>, arrays use their element type, anything else is a list-like collection.
		/// </summary>
		public void ResolveCollection()
		{
			if (_collectionResolved) return;

			if (this.Type == typeof(object))
			{
				this.ItemType = typeof(object);
				this.IsArray = true;
			}
			else if (this.Type.IsArray)
			{
				this.ItemType = this.Type.GetElementType()!;
				this.IsArray = true;
			}
			else
			{
				this.ItemType = Reflection.GetListItemType(this.Type);
				this.IsArray = false;
			}

			_collectionResolved = true;
		}
	}

	/// <summary>
	/// Per-enum-type caches of name bytes, so enum values serialize without <see cref="Enum.ToString()"/>
	/// and deserialize without <see cref="Enum.Parse(Type, string)"/> (both allocate, and are slow under Mono/IL2CPP).
	/// Caches are copy-on-write and bounded, so concurrent readers never lock.
	/// </summary>
	internal sealed class DirectEnumInfo
	{
		private const int MAX_CACHED = 256;

		private static readonly Dictionary<Type, DirectEnumInfo> _cache = new Dictionary<Type, DirectEnumInfo>();

		private readonly Type _type;
		private readonly TypeCode _underlying;
		private readonly object _sync = new object();

		/// <summary>Underlying value → full BSON string payload (length prefix, UTF-8 bytes, terminator).</summary>
		private Dictionary<long, byte[]> _payloads = new Dictionary<long, byte[]>();

		/// <summary>UTF-8 names seen on read → boxed enum value (boxed enums are immutable, so sharing is safe).</summary>
		private KeyValuePair<byte[], object>[] _parsed = Array.Empty<KeyValuePair<byte[], object>>();

		private DirectEnumInfo(Type type)
		{
			_type = type;
			_underlying = System.Type.GetTypeCode(System.Enum.GetUnderlyingType(type));
		}

		public static DirectEnumInfo Get(Type type)
		{
			lock (_cache)
			{
				if (!_cache.TryGetValue(type, out var info))
				{
					_cache[type] = info = new DirectEnumInfo(type);
				}
				return info;
			}
		}

		/// <summary>
		/// Reads the numeric value of a boxed enum without allocating (a boxed enum may be unboxed as its underlying type).
		/// </summary>
		private long GetKey(object value)
		{
			switch (_underlying)
			{
				case TypeCode.SByte: return (sbyte)value;
				case TypeCode.Byte: return (byte)value;
				case TypeCode.Int16: return (short)value;
				case TypeCode.UInt16: return (ushort)value;
				case TypeCode.Int32: return (int)value;
				case TypeCode.UInt32: return (uint)value;
				case TypeCode.Int64: return (long)value;
				case TypeCode.UInt64: return unchecked((long)(ulong)value);
				default: return 0;
			}
		}

		/// <summary>
		/// Returns the BSON string payload for <paramref name="value"/> (identical bytes to encoding <c>value.ToString()</c>).
		/// </summary>
		public byte[] GetPayload(object value)
		{
			var key = this.GetKey(value);
			var payloads = _payloads;

			if (payloads.TryGetValue(key, out var payload)) return payload;

			var str = value.ToString();
			var count = Encoding.UTF8.GetByteCount(str);
			payload = new byte[4 + count + 1];
			payload[0] = (byte)(count + 1);
			payload[1] = (byte)((count + 1) >> 8);
			payload[2] = (byte)((count + 1) >> 16);
			payload[3] = (byte)((count + 1) >> 24);
			Encoding.UTF8.GetBytes(str, 0, str.Length, payload, 4);

			lock (_sync)
			{
				if (_payloads.Count < MAX_CACHED && !_payloads.ContainsKey(key))
				{
					var copy = new Dictionary<long, byte[]>(_payloads);
					copy[key] = payload;
					Volatile.Write(ref _payloads, copy);
				}
			}

			return payload;
		}

		/// <summary>
		/// Converts UTF-8 name bytes to a boxed enum value, with the same result as <c>Enum.Parse(type, name)</c>.
		/// </summary>
		public object Parse(ReadOnlySpan<byte> utf8)
		{
			var parsed = _parsed;

			for (var i = 0; i < parsed.Length; i++)
			{
				if (utf8.SequenceEqual(parsed[i].Key)) return parsed[i].Value;
			}

			var value = System.Enum.Parse(_type, Encoding.UTF8.GetString(utf8));

			lock (_sync)
			{
				if (_parsed.Length < MAX_CACHED)
				{
					var copy = new KeyValuePair<byte[], object>[_parsed.Length + 1];
					Array.Copy(_parsed, copy, _parsed.Length);
					copy[_parsed.Length] = new KeyValuePair<byte[], object>(utf8.ToArray(), value);
					Volatile.Write(ref _parsed, copy);
				}
			}

			return value;
		}
	}
}

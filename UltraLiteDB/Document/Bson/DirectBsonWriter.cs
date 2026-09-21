using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;

namespace UltraLiteDB
{
	/// <summary>
	/// Serializes C# objects directly to BSON bytes, bypassing intermediate <see cref="BsonDocument"/> creation.
	/// Reduces GC pressure for high-throughput serialization. Supports polymorphism via _t/_type discriminators.
	/// </summary>
	/// <remarks>
	/// Hot-path notes: field names are written from bytes cached on <see cref="MemberMapper"/>, array index
	/// keys and strings are encoded straight into the buffer, and per-value type dispatch is cached in
	/// <see cref="DirectWriteInfo"/>. Each element's type byte is reserved up front and back-filled once
	/// the value has been written, so key handling is independent of value dispatch.
	/// </remarks>
	internal static class DirectBsonWriter
	{
		private const int MAX_DEPTH = 20;

		private static readonly byte[] _typeIdKey = { (byte)'_', (byte)'t', 0x00 };
		private static readonly byte[] _typeNameKey = { (byte)'_', (byte)'t', (byte)'y', (byte)'p', (byte)'e', 0x00 };

		/// <summary>
		/// Write a C# object as a BSON document directly to the ByteWriter. <c>entity</c> is the entity
		/// mapper for <c>obj.GetType()</c> when the caller already has it, otherwise null.
		/// </summary>
		public static void WriteObjectDirect(ByteWriter writer, BsonMapper mapper, Type declaredType, object obj, EntityMapper? entity, int depth)
		{
			if (++depth > MAX_DEPTH) throw UltraLiteException.DocumentMaxDepth(MAX_DEPTH, declaredType);

			var t = obj.GetType();
			entity ??= mapper.GetEntityMapper(t);

			// Record position for length backfill
			var startPos = writer.Position;
			writer.EnsureCapacity(4);
			writer.Skip(4); // reserve space for document length

			// Add _type/_t for derived types
			if (declaredType != t)
			{
				if (mapper.CustomTypeToId.TryGetValue(t, out BsonValue customTypeId))
				{
					var typePos = writer.BeginElement(_typeIdKey);
					writer.SetByte(typePos, WriteBsonValue(writer, customTypeId));
				}
				else if (mapper.IncludeFullType)
				{
					var payload = entity.TypeNamePayload;
					if (payload == null)
					{
						var typeName = t.FullName + ", " + t.GetTypeInfo().Assembly.GetName().Name;
						var scratch = new ByteWriter(64);
						scratch.WriteBsonString(typeName);
						payload = new byte[scratch.Position];
						System.Buffer.BlockCopy(scratch.Buffer, 0, payload, 0, payload.Length);
						Volatile.Write(ref entity.TypeNamePayload, payload);
					}

					var typePos = writer.BeginElement(_typeNameKey);
					writer.SetByte(typePos, 0x02);
					writer.WriteRaw(payload);
				}
			}

			var members = entity.Members;

			for (var i = 0; i < members.Count; i++)
			{
				var member = members[i];
				var getter = member.Getter;
				if (getter == null) continue;

				// members excluded from mapping have a null FieldName
				var key = member.FieldNameCString;
				if (key == null) continue;

				var value = getter(obj);

				if (value == null && !mapper.SerializeNullValues && member.FieldName != "_id") continue;

				var typePos = writer.BeginElement(key);
				byte bsonType;

				if (member.Serialize != null)
				{
					// Custom member serializer — falls back to BsonValue
					bsonType = WriteBsonValue(writer, member.Serialize(value, mapper) ?? BsonValue.Null);
				}
				else if (value == null)
				{
					bsonType = 0x0A;
				}
				else
				{
					var cached = member.WriteInfo;
					var info = DirectWriteInfo.Get(mapper, cached, member.DataType, value.GetType());
					if (info != cached) Volatile.Write(ref member.WriteInfo, info);

					bsonType = WriteValue(writer, mapper, info, value, depth);
				}

				writer.SetByte(typePos, bsonType);
			}

			// Write terminator
			writer.EnsureCapacity(1);
			writer.Write((byte)0x00);

			// Backfill document length
			writer.SetInt32(startPos, writer.Position - startPos);
		}

		/// <summary>
		/// Writes a non-null value's bytes (the element's type byte and key have already been handled)
		/// and returns its BSON type byte.
		/// </summary>
		private static byte WriteValue(ByteWriter writer, BsonMapper mapper, DirectWriteInfo info, object value, int depth)
		{
			switch (info.Kind)
			{
				case DirectKind.String:
					var str = mapper.TrimWhitespace ? ((String)value).Trim() : (String)value;
					if (mapper.EmptyStringToNull && str.Length == 0) return 0x0A;
					writer.WriteBsonString(str);
					return 0x02;

				case DirectKind.Int32:
					writer.EnsureCapacity(4);
					writer.Write((Int32)value);
					return 0x10;

				case DirectKind.Int64:
					writer.EnsureCapacity(8);
					writer.Write((Int64)value);
					return 0x12;

				case DirectKind.Double:
					writer.EnsureCapacity(8);
					writer.Write((Double)value);
					return 0x01;

				case DirectKind.Decimal:
					writer.EnsureCapacity(16);
					writer.Write((Decimal)value);
					return 0x13;

				case DirectKind.Binary:
					var byteArr = (Byte[])value;
					writer.EnsureCapacity(4 + 1 + byteArr.Length);
					writer.Write(byteArr.Length);
					writer.Write((byte)0x00); // Generic binary subtype
					writer.Write(byteArr);
					return 0x05;

				case DirectKind.ArraySegment:
					var segment = (ArraySegment<byte>)value;
					writer.EnsureCapacity(4 + 1 + segment.Count);
					writer.Write(segment.Count);
					writer.Write((byte)0x00);
					if (segment.Count > 0) writer.Write(segment);
					return 0x05;

				case DirectKind.ObjectId:
					writer.EnsureCapacity(12);
					writer.Write((ObjectId)value);
					return 0x07;

				case DirectKind.Guid:
					writer.EnsureCapacity(4 + 1 + 16);
					writer.Write(16);
					writer.Write((byte)0x04); // UUID subtype
					writer.Write((Guid)value);
					return 0x05;

				case DirectKind.Boolean:
					writer.EnsureCapacity(1);
					writer.Write((byte)((Boolean)value ? 0x01 : 0x00));
					return 0x08;

				case DirectKind.DateTime:
					writer.EnsureCapacity(8);
					writer.Write(ToUnixMilliseconds((DateTime)value));
					return 0x09;

				// Converted types
				case DirectKind.Int16:
					writer.EnsureCapacity(4);
					writer.Write((Int32)(Int16)value);
					return 0x10;

				case DirectKind.UInt16:
					writer.EnsureCapacity(4);
					writer.Write((Int32)(UInt16)value);
					return 0x10;

				case DirectKind.Byte:
					writer.EnsureCapacity(4);
					writer.Write((Int32)(Byte)value);
					return 0x10;

				case DirectKind.SByte:
					writer.EnsureCapacity(4);
					writer.Write((Int32)(SByte)value);
					return 0x10;

				case DirectKind.UInt32:
					writer.EnsureCapacity(8);
					writer.Write((Int64)(UInt32)value);
					return 0x12;

				case DirectKind.UInt64:
					writer.EnsureCapacity(8);
					writer.Write(unchecked((Int64)(UInt64)value));
					return 0x12;

				case DirectKind.Single:
					writer.EnsureCapacity(8);
					writer.Write((Double)(Single)value);
					return 0x01;

				case DirectKind.Char:
					writer.WriteBsonString(value.ToString());
					return 0x02;

				case DirectKind.Enum:
					writer.WriteRaw(info.Enum!.GetPayload(value));
					return 0x02;

				case DirectKind.BsonValue:
					return WriteBsonValue(writer, (BsonValue)value);

				case DirectKind.Custom:
					return WriteBsonValue(writer, info.Custom!(value) ?? BsonValue.Null);

				case DirectKind.Dictionary:
					WriteDictionaryDirect(writer, mapper, info, (IDictionary)value, depth);
					return 0x03;

				case DirectKind.Enumerable:
					WriteArrayDirect(writer, mapper, info, (IEnumerable)value, depth);
					return 0x04;

				default:
					// Complex object
					info.Entity ??= mapper.GetEntityMapper(info.RuntimeType);
					WriteObjectDirect(writer, mapper, info.DeclaredType, value, info.Entity, depth);
					return 0x03;
			}
		}

		/// <summary>
		/// Writes a collection item or dictionary value, reusing the collection's cached item dispatch.
		/// </summary>
		private static byte WriteItem(ByteWriter writer, BsonMapper mapper, DirectWriteInfo collection, object? item, int depth)
		{
			if (item == null) return 0x0A;

			var cached = collection.ItemInfo;
			var info = DirectWriteInfo.Get(mapper, cached, collection.ItemType!, item.GetType());
			if (info != cached) Volatile.Write(ref collection.ItemInfo, info);

			return WriteValue(writer, mapper, info, item, depth);
		}

		/// <summary>
		/// Write an IEnumerable as a BSON array directly
		/// </summary>
		private static void WriteArrayDirect(ByteWriter writer, BsonMapper mapper, DirectWriteInfo info, IEnumerable items, int depth)
		{
			if (++depth > MAX_DEPTH) throw UltraLiteException.DocumentMaxDepth(MAX_DEPTH, info.ItemType!);

			var startPos = writer.Position;
			writer.EnsureCapacity(4);
			writer.Skip(4); // reserve for length

			if (!TryWritePrimitiveArray(writer, items))
			{
				if (items is IList list)
				{
					// indexed access avoids allocating an enumerator
					for (var i = 0; i < list.Count; i++)
					{
						var typePos = writer.BeginElement(i);
						writer.SetByte(typePos, WriteItem(writer, mapper, info, list[i], depth));
					}
				}
				else
				{
					var i = 0;
					foreach (var item in items)
					{
						var typePos = writer.BeginElement(i++);
						writer.SetByte(typePos, WriteItem(writer, mapper, info, item, depth));
					}
				}
			}

			writer.EnsureCapacity(1);
			writer.Write((byte)0x00);

			writer.SetInt32(startPos, writer.Position - startPos);
		}

		/// <summary>
		/// Writes arrays/lists of common primitive element types without boxing each element.
		/// Output is identical to the general path (same BSON types and conversions).
		/// </summary>
		private static bool TryWritePrimitiveArray(ByteWriter writer, IEnumerable items)
		{
			var type = items.GetType();

			if (type == typeof(int[]))
			{
				var a = (int[])items;
				for (var i = 0; i < a.Length; i++) WriteInt32Item(writer, i, a[i]);
			}
			else if (type == typeof(List<int>))
			{
				var l = (List<int>)items;
				for (var i = 0; i < l.Count; i++) WriteInt32Item(writer, i, l[i]);
			}
			else if (type == typeof(float[]))
			{
				var a = (float[])items;
				for (var i = 0; i < a.Length; i++) WriteDoubleItem(writer, i, a[i]);
			}
			else if (type == typeof(List<float>))
			{
				var l = (List<float>)items;
				for (var i = 0; i < l.Count; i++) WriteDoubleItem(writer, i, l[i]);
			}
			else if (type == typeof(double[]))
			{
				var a = (double[])items;
				for (var i = 0; i < a.Length; i++) WriteDoubleItem(writer, i, a[i]);
			}
			else if (type == typeof(List<double>))
			{
				var l = (List<double>)items;
				for (var i = 0; i < l.Count; i++) WriteDoubleItem(writer, i, l[i]);
			}
			else if (type == typeof(long[]))
			{
				var a = (long[])items;
				for (var i = 0; i < a.Length; i++) WriteInt64Item(writer, i, a[i]);
			}
			else if (type == typeof(List<long>))
			{
				var l = (List<long>)items;
				for (var i = 0; i < l.Count; i++) WriteInt64Item(writer, i, l[i]);
			}
			else if (type == typeof(bool[]))
			{
				var a = (bool[])items;
				for (var i = 0; i < a.Length; i++) WriteBooleanItem(writer, i, a[i]);
			}
			else if (type == typeof(List<bool>))
			{
				var l = (List<bool>)items;
				for (var i = 0; i < l.Count; i++) WriteBooleanItem(writer, i, l[i]);
			}
			else
			{
				return false;
			}

			return true;
		}

		private static void WriteInt32Item(ByteWriter writer, int index, int value)
		{
			writer.SetByte(writer.BeginElement(index), 0x10);
			writer.EnsureCapacity(4);
			writer.Write(value);
		}

		private static void WriteInt64Item(ByteWriter writer, int index, long value)
		{
			writer.SetByte(writer.BeginElement(index), 0x12);
			writer.EnsureCapacity(8);
			writer.Write(value);
		}

		private static void WriteDoubleItem(ByteWriter writer, int index, double value)
		{
			writer.SetByte(writer.BeginElement(index), 0x01);
			writer.EnsureCapacity(8);
			writer.Write(value);
		}

		private static void WriteBooleanItem(ByteWriter writer, int index, bool value)
		{
			writer.SetByte(writer.BeginElement(index), 0x08);
			writer.EnsureCapacity(1);
			writer.Write((byte)(value ? 0x01 : 0x00));
		}

		/// <summary>
		/// Write an IDictionary as a BSON document directly
		/// </summary>
		private static void WriteDictionaryDirect(ByteWriter writer, BsonMapper mapper, DirectWriteInfo info, IDictionary dict, int depth)
		{
			if (++depth > MAX_DEPTH) throw UltraLiteException.DocumentMaxDepth(MAX_DEPTH, info.ItemType!);

			var startPos = writer.Position;
			writer.EnsureCapacity(4);
			writer.Skip(4); // reserve for length

			// enumerate entries directly instead of Keys + indexer (one hash lookup per key saved)
			var e = dict.GetEnumerator();
			try
			{
				while (e.MoveNext())
				{
					var typePos = writer.BeginElement(e.Key.ToString());
					writer.SetByte(typePos, WriteItem(writer, mapper, info, e.Value, depth));
				}
			}
			finally
			{
				(e as IDisposable)?.Dispose();
			}

			writer.EnsureCapacity(1);
			writer.Write((byte)0x00);

			writer.SetInt32(startPos, writer.Position - startPos);
		}

		/// <summary>
		/// Converts a DateTime to BSON UTC milliseconds since the Unix epoch.
		/// </summary>
		private static long ToUnixMilliseconds(DateTime date)
		{
			var utc = (date == DateTime.MinValue || date == DateTime.MaxValue) ? date : date.ToUniversalTime();
			var ts = utc - BsonValue.UnixEpoch;
			return Convert.ToInt64(ts.TotalMilliseconds);
		}

		/// <summary>
		/// Writes a BsonValue's bytes using the BsonWriter format and returns its BSON type byte.
		/// Used as fallback for custom serializers and BsonValue-typed properties.
		/// </summary>
		private static byte WriteBsonValue(ByteWriter writer, BsonValue value)
		{
			switch (value.Type)
			{
				case BsonType.Double:
					writer.EnsureCapacity(8);
					writer.Write((Double)value.RawValue);
					return 0x01;

				case BsonType.String:
					writer.WriteBsonString((String)value.RawValue);
					return 0x02;

				case BsonType.Document:
					var doc = (BsonDocument)value;
					writer.EnsureCapacity(doc.GetBytesCount(true));
					BsonWriter.WriteDocument(writer, doc);
					return 0x03;

				case BsonType.Array:
					var array = value as BsonArray ?? new BsonArray((List<BsonValue>)value.RawValue);
					writer.EnsureCapacity(array.GetBytesCount(true));
					BsonWriter.WriteArray(writer, array);
					return 0x04;

				case BsonType.Binary:
					var bytes = (ArraySegment<byte>)value.RawValue;
					writer.EnsureCapacity(4 + 1 + bytes.Count);
					writer.Write(bytes.Count);
					writer.Write((byte)0x00);
					writer.Write(bytes);
					return 0x05;

				case BsonType.Guid:
					writer.EnsureCapacity(4 + 1 + 16);
					writer.Write(16);
					writer.Write((byte)0x04);
					writer.Write((Guid)value.RawValue);
					return 0x05;

				case BsonType.ObjectId:
					writer.EnsureCapacity(12);
					writer.Write((ObjectId)value.RawValue);
					return 0x07;

				case BsonType.Boolean:
					writer.EnsureCapacity(1);
					writer.Write((byte)(((Boolean)value.RawValue) ? 0x01 : 0x00));
					return 0x08;

				case BsonType.DateTime:
					writer.EnsureCapacity(8);
					writer.Write(ToUnixMilliseconds((DateTime)value.RawValue));
					return 0x09;

				case BsonType.Null:
					return 0x0A;

				case BsonType.Int32:
					writer.EnsureCapacity(4);
					writer.Write((Int32)value.RawValue);
					return 0x10;

				case BsonType.Int64:
					writer.EnsureCapacity(8);
					writer.Write((Int64)value.RawValue);
					return 0x12;

				case BsonType.Decimal:
					writer.EnsureCapacity(16);
					writer.Write((Decimal)value.RawValue);
					return 0x13;

				case BsonType.MinValue:
					return 0xFF;

				case BsonType.MaxValue:
					return 0x7F;

				default:
					throw new NotSupportedException($"BSON type {value.Type} not supported");
			}
		}
	}
}

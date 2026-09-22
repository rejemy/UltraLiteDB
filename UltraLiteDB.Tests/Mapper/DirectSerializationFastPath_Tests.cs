using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB.Tests.Mapper
{
	#region Models

	public enum FastPathColor { Red, Green, Blue }

	[Flags]
	public enum FastPathFlags { None = 0, A = 1, B = 2, C = 4 }

	public enum FastPathLongEnum : long { Small = 1, Huge = long.MaxValue }

	public enum FastPathByteEnum : byte { Low = 1, High = 250 }

	public class PrimitiveCollectionsModel
	{
		public int Id { get; set; }
		public int[]? IntArray { get; set; }
		public List<int>? IntList { get; set; }
		public float[]? FloatArray { get; set; }
		public List<float>? FloatList { get; set; }
		public double[]? DoubleArray { get; set; }
		public List<double>? DoubleList { get; set; }
		public long[]? LongArray { get; set; }
		public List<long>? LongList { get; set; }
		public bool[]? BoolArray { get; set; }
		public List<bool>? BoolList { get; set; }
		public int?[]? NullableIntArray { get; set; }
		public string?[]? StringArray { get; set; }
		public IList<int>? IntInterfaceList { get; set; }
		public HashSet<int>? IntSet { get; set; }
	}

	public class ArrayLookalikesModel
	{
		public int Id { get; set; }
		// the CLR reports these as `is int[]`; they must not take the int[] fast path
		public uint[]? UIntArray { get; set; }
		public FastPathColor[]? ColorArray { get; set; }
		public object? BoxedArray { get; set; }
	}

	public class EnumModel
	{
		public int Id { get; set; }
		public FastPathColor Color { get; set; }
		public FastPathColor? NullableColor { get; set; }
		public FastPathFlags Flags { get; set; }
		public FastPathLongEnum LongEnum { get; set; }
		public FastPathByteEnum ByteEnum { get; set; }
		public List<FastPathColor>? ColorList { get; set; }
		public Dictionary<FastPathColor, int>? ColorKeyDict { get; set; }
	}

	public class StringsModel
	{
		public int Id { get; set; }
		public string? Short { get; set; }
		public string? Long { get; set; }
		public string? Unicode { get; set; }
		public Dictionary<string, string>? UnicodeKeys { get; set; }
	}

	public class BsonValueHolderModel
	{
		public int Id { get; set; }
		public BsonDocument? Doc { get; set; }
		public BsonArray? Arr { get; set; }
	}

	public class ObjectDictModel
	{
		public int Id { get; set; }
		public Dictionary<string, object>? Values { get; set; }
	}

	public class FastPathPoint
	{
		public int X { get; set; }
		public int Y { get; set; }
	}

	public class ReentrantModel
	{
		public int Id { get; set; }
		public FastPathPoint? Point { get; set; }
	}

	public class OrderModel
	{
		public int Id { get; set; }
		public string? First { get; set; }
		public string? Second { get; set; }
		public int Third { get; set; }
	}

	public class AccessorLogicBase
	{
		public int Id { get; set; }
		public string? BaseAuto { get; set; }
	}

	public class AccessorLogicModel : AccessorLogicBase
	{
		private string? _upper;
		private int _getterCalls;

		// setter with logic: must run on deserialize
		public string? Upper { get => _upper; set => _upper = value?.ToUpperInvariant(); }

		// getter with logic: must run on serialize
		public int Computed { get { _getterCalls++; return 7; } set { } }

		public int InitOnly { get; init; }

		public int GetterCalls => _getterCalls;
	}

	public struct AccessorStruct
	{
		public int X { get; set; }
		public string? Name { get; set; }
	}

	public class AccessorStructHolder
	{
		public int Id { get; set; }
		public AccessorStruct Value { get; set; }
	}

	#endregion

	/// <summary>
	/// Covers the cached / allocation-free paths of the direct serializer: every case is checked for
	/// byte-equivalence against the BsonDocument path and for round-tripping.
	/// </summary>
	[TestClass]
	public class DirectSerializationFastPath_Tests
	{
		private static void AssertByteEquivalent<T>(BsonMapper mapper, T obj)
		{
			var expected = BsonWriter.Serialize(mapper.ToDocument(obj));

			// twice: the second call runs entirely on cached dispatch info
			CollectionAssert.AreEqual(expected, mapper.SerializeToBytes(obj));
			CollectionAssert.AreEqual(expected, mapper.SerializeToBytes(obj));
		}

		private static T RoundTrip<T>(BsonMapper mapper, T obj)
		{
			var bytes = mapper.SerializeToBytes(obj);
			var result = mapper.DeserializeFromBytes<T>(bytes);

			// and a second time on warm caches
			result = mapper.DeserializeFromBytes<T>(bytes);

			CollectionAssert.AreEqual(bytes, mapper.SerializeToBytes(result));
			return result;
		}

		private static PrimitiveCollectionsModel CreatePrimitiveCollections()
		{
			return new PrimitiveCollectionsModel
			{
				Id = 1,
				IntArray = Enumerable.Range(-5, 150).ToArray(), // index keys of 1, 2 and 3 digits
				IntList = new List<int> { int.MinValue, 0, int.MaxValue },
				FloatArray = new[] { 1.5f, -0.25f, float.MaxValue },
				FloatList = new List<float> { 3.25f },
				DoubleArray = new[] { Math.PI, double.MinValue },
				DoubleList = new List<double> { Math.E },
				LongArray = new[] { long.MinValue, 42L },
				LongList = new List<long> { long.MaxValue },
				BoolArray = new[] { true, false, true },
				BoolList = new List<bool> { false },
				NullableIntArray = new int?[] { 1, null, 3 },
				StringArray = new[] { "a", null, "" },
				IntInterfaceList = new List<int> { 7, 8, 9 },
				IntSet = new HashSet<int> { 4, 5 },
			};
		}

		[TestMethod]
		public void PrimitiveCollections_ByteEquivalent()
		{
			AssertByteEquivalent(new BsonMapper(), CreatePrimitiveCollections());
		}

		[TestMethod]
		public void PrimitiveCollections_RoundTrip()
		{
			var obj = CreatePrimitiveCollections();
			var result = RoundTrip(new BsonMapper(), obj);

			CollectionAssert.AreEqual(obj.IntArray, result.IntArray);
			CollectionAssert.AreEqual(obj.IntList, result.IntList);
			CollectionAssert.AreEqual(obj.FloatArray, result.FloatArray);
			CollectionAssert.AreEqual(obj.FloatList, result.FloatList);
			CollectionAssert.AreEqual(obj.DoubleArray, result.DoubleArray);
			CollectionAssert.AreEqual(obj.DoubleList, result.DoubleList);
			CollectionAssert.AreEqual(obj.LongArray, result.LongArray);
			CollectionAssert.AreEqual(obj.LongList, result.LongList);
			CollectionAssert.AreEqual(obj.BoolArray, result.BoolArray);
			CollectionAssert.AreEqual(obj.BoolList, result.BoolList);
			CollectionAssert.AreEqual(obj.NullableIntArray, result.NullableIntArray);
			CollectionAssert.AreEqual(obj.StringArray, result.StringArray);
			CollectionAssert.AreEqual(obj.IntInterfaceList!.ToList(), result.IntInterfaceList!.ToList());
			CollectionAssert.AreEquivalent(obj.IntSet!.ToList(), result.IntSet!.ToList());
		}

		[TestMethod]
		public void PrimitiveCollections_ConvertStoredTypes()
		{
			// values stored as one BSON numeric type and read into collections of another
			var doc = new BsonDocument
			{
				["_id"] = 1,
				["IntArray"] = new BsonArray(new BsonValue[] { 1, 2L, 3.9 }),
				["LongList"] = new BsonArray(new BsonValue[] { 1, 2L }),
				["FloatArray"] = new BsonArray(new BsonValue[] { 1, 2.5 }),
				["DoubleList"] = new BsonArray(new BsonValue[] { 1, 2L, 0.5 }),
			};

			var result = new BsonMapper().DeserializeFromBytes<PrimitiveCollectionsModel>(BsonWriter.Serialize(doc));

			CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.IntArray);
			CollectionAssert.AreEqual(new List<long> { 1, 2 }, result.LongList);
			CollectionAssert.AreEqual(new[] { 1f, 2.5f }, result.FloatArray);
			CollectionAssert.AreEqual(new List<double> { 1, 2, 0.5 }, result.DoubleList);
		}

		[TestMethod]
		public void ArrayLookalikes_NotTreatedAsIntArrays()
		{
			var obj = new ArrayLookalikesModel
			{
				Id = 1,
				UIntArray = new uint[] { 1, uint.MaxValue },
				ColorArray = new[] { FastPathColor.Green, FastPathColor.Blue },
				BoxedArray = new uint[] { 7 },
			};

			AssertByteEquivalent(new BsonMapper(), obj);

			var result = RoundTrip(new BsonMapper(), obj);
			CollectionAssert.AreEqual(obj.UIntArray, result.UIntArray);
			CollectionAssert.AreEqual(obj.ColorArray, result.ColorArray);
		}

		private static EnumModel CreateEnums()
		{
			return new EnumModel
			{
				Id = 1,
				Color = FastPathColor.Blue,
				NullableColor = FastPathColor.Green,
				Flags = FastPathFlags.A | FastPathFlags.C,
				LongEnum = FastPathLongEnum.Huge,
				ByteEnum = FastPathByteEnum.High,
				ColorList = new List<FastPathColor> { FastPathColor.Red, (FastPathColor)99, FastPathColor.Red },
				ColorKeyDict = new Dictionary<FastPathColor, int> { { FastPathColor.Green, 2 }, { FastPathColor.Blue, 3 } },
			};
		}

		[TestMethod]
		public void Enums_ByteEquivalent()
		{
			AssertByteEquivalent(new BsonMapper(), CreateEnums());
		}

		[TestMethod]
		public void Enums_RoundTrip()
		{
			var obj = CreateEnums();
			var result = RoundTrip(new BsonMapper(), obj);

			Assert.AreEqual(obj.Color, result.Color);
			Assert.AreEqual(obj.NullableColor, result.NullableColor);
			Assert.AreEqual(obj.Flags, result.Flags);
			Assert.AreEqual(obj.LongEnum, result.LongEnum);
			Assert.AreEqual(obj.ByteEnum, result.ByteEnum);
			CollectionAssert.AreEqual(obj.ColorList, result.ColorList);
			CollectionAssert.AreEquivalent(obj.ColorKeyDict!.ToList(), result.ColorKeyDict!.ToList());
		}

		[TestMethod]
		public void Enums_ParseNonCanonicalNames()
		{
			// names Enum.Parse accepts but that aren't the cached canonical spelling
			var doc = new BsonDocument
			{
				["_id"] = 1,
				["Color"] = "2",
				["Flags"] = "C, A",
			};

			var result = new BsonMapper().DeserializeFromBytes<EnumModel>(BsonWriter.Serialize(doc));

			Assert.AreEqual(FastPathColor.Blue, result.Color);
			Assert.AreEqual(FastPathFlags.A | FastPathFlags.C, result.Flags);
		}

		private static StringsModel CreateStrings()
		{
			return new StringsModel
			{
				Id = 1,
				Short = "hello",
				Long = new string('x', 5000) + "é",
				Unicode = "héllo wörld — 日本語 🎮",
				UnicodeKeys = new Dictionary<string, string> { { "ключ", "значение" }, { "🔑", "✓" } },
			};
		}

		[TestMethod]
		public void Strings_ByteEquivalent()
		{
			AssertByteEquivalent(new BsonMapper(), CreateStrings());
		}

		[TestMethod]
		public void Strings_RoundTrip()
		{
			var obj = CreateStrings();
			var result = RoundTrip(new BsonMapper(), obj);

			Assert.AreEqual(obj.Short, result.Short);
			Assert.AreEqual(obj.Long, result.Long);
			Assert.AreEqual(obj.Unicode, result.Unicode);
			CollectionAssert.AreEquivalent(obj.UnicodeKeys!.ToList(), result.UnicodeKeys!.ToList());
		}

		[TestMethod]
		public void UnicodeFieldNames_ByteEquivalentAndRoundTrip()
		{
			var mapper = new BsonMapper();
			mapper.Entity<OrderModel>().Field(x => x.First, "préféré");

			var obj = new OrderModel { Id = 1, First = "a", Second = "b", Third = 3 };

			AssertByteEquivalent(mapper, obj);
			Assert.AreEqual("a", RoundTrip(mapper, obj).First);
		}

		[TestMethod]
		public void LargeNestedBsonDocument_Serializes()
		{
			// nested BsonDocument/BsonArray values much larger than the writer's initial buffer
			var doc = new BsonDocument();
			for (var i = 0; i < 200; i++) doc["key" + i] = new string('v', 50);

			var arr = new BsonArray();
			for (var i = 0; i < 200; i++) arr.Add(new string('a', 50));

			var obj = new BsonValueHolderModel { Id = 1, Doc = doc, Arr = arr };
			var mapper = new BsonMapper();

			AssertByteEquivalent(mapper, obj);

			var result = mapper.DeserializeFromBytes<BsonValueHolderModel>(mapper.SerializeToBytes(obj));
			Assert.AreEqual(200, result.Doc!.Keys.Count);
			Assert.AreEqual(200, result.Arr!.Count);
		}

		[TestMethod]
		public void LargeBsonDocumentEntity_IntoSmallWriter()
		{
			var doc = new BsonDocument();
			for (var i = 0; i < 100; i++) doc["key" + i] = new string('v', 50);

			var writer = new ByteWriter(16);
			new BsonMapper().SerializeToBytes(doc, writer);

			var expected = BsonWriter.Serialize(doc);
			CollectionAssert.AreEqual(expected, writer.Buffer.Take(writer.Position).ToArray());
		}

		[TestMethod]
		public void ObjectDictionary_MixedValues_ByteEquivalent()
		{
			var obj = new ObjectDictModel
			{
				Id = 1,
				Values = new Dictionary<string, object>
				{
					{ "int", 1 },
					{ "str", "two" },
					{ "dbl", 3.5 },
					{ "list", new List<int> { 1, 2 } },
					{ "color", FastPathColor.Green },
					{ "point", new FastPathPoint { X = 1, Y = 2 } },
					{ "int2", 5 },
				}
			};

			AssertByteEquivalent(new BsonMapper(), obj);
		}

		[TestMethod]
		public void OutOfOrderAndCaseInsensitiveFields_Deserialize()
		{
			var doc = new BsonDocument
			{
				["third"] = 3,       // different case
				["Unknown"] = "skip",
				["Second"] = "b",
				["_id"] = 1,
				["FIRST"] = "a",     // different case
			};

			var result = new BsonMapper().DeserializeFromBytes<OrderModel>(BsonWriter.Serialize(doc));

			Assert.AreEqual(1, result.Id);
			Assert.AreEqual("a", result.First);
			Assert.AreEqual("b", result.Second);
			Assert.AreEqual(3, result.Third);
		}

		[TestMethod]
		public void RegisterType_AfterFirstUse_IsHonored()
		{
			var mapper = new BsonMapper();
			var obj = new ReentrantModel { Id = 1, Point = new FastPathPoint { X = 1, Y = 2 } };

			// warm the cached dispatch with the default (object) mapping
			var before = mapper.SerializeToBytes(obj);
			mapper.DeserializeFromBytes<ReentrantModel>(before);

			mapper.RegisterType<FastPathPoint>(p => p.X + "," + p.Y, b => new FastPathPoint { X = int.Parse(b.AsString.Split(',')[0]), Y = int.Parse(b.AsString.Split(',')[1]) });

			var after = mapper.SerializeToBytes(obj);
			var doc = BsonReader.Deserialize(after);

			Assert.AreEqual("1,2", doc["Point"].AsString);
			Assert.AreEqual(2, mapper.DeserializeFromBytes<ReentrantModel>(after).Point!.Y);
		}

		[TestMethod]
		public void CustomSerializer_ReentrantSerializeToBytes()
		{
			var mapper = new BsonMapper();
			var inner = new BsonMapper();

			// a custom serializer that itself uses the direct serializer on the same thread
			mapper.RegisterType<FastPathPoint>(
				p => new BsonValue(inner.SerializeToBytes(p)),
				b => inner.DeserializeFromBytes<FastPathPoint>(b.AsBinary));

			var obj = new ReentrantModel { Id = 1, Point = new FastPathPoint { X = 3, Y = 4 } };

			var result = mapper.DeserializeFromBytes<ReentrantModel>(mapper.SerializeToBytes(obj));

			Assert.AreEqual(3, result.Point!.X);
			Assert.AreEqual(4, result.Point!.Y);
		}

		[TestMethod]
		public void Accessors_LogicIsNotBypassed()
		{
			var mapper = new BsonMapper();
			var obj = new AccessorLogicModel { Id = 1, BaseAuto = "base", Upper = "shout", InitOnly = 5 };

			var bytes = mapper.SerializeToBytes(obj);
			Assert.AreEqual(1, obj.GetterCalls);

			var doc = BsonReader.Deserialize(bytes);
			Assert.AreEqual(7, doc["Computed"].AsInt32);

			// stored lower-case: the setter must still upper-case it
			doc["Upper"] = "quiet";
			var result = mapper.DeserializeFromBytes<AccessorLogicModel>(BsonWriter.Serialize(doc));

			Assert.AreEqual("QUIET", result.Upper);
			Assert.AreEqual("base", result.BaseAuto);
			Assert.AreEqual(5, result.InitOnly);

			// same through the BsonDocument path, which shares the accessors
			Assert.AreEqual("QUIET", mapper.ToObject<AccessorLogicModel>(doc).Upper);
		}

		[TestMethod]
		public void Accessors_StructAutoProperties_RoundTrip()
		{
			var obj = new AccessorStructHolder { Id = 1, Value = new AccessorStruct { X = 42, Name = "s" } };
			var result = RoundTrip(new BsonMapper(), obj);

			Assert.AreEqual(42, result.Value.X);
			Assert.AreEqual("s", result.Value.Name);
		}

		[TestMethod]
		public void ConcurrentColdCaches_ProduceSameBytes()
		{
			var expectedMapper = new BsonMapper();
			var collections = CreatePrimitiveCollections();
			var enums = CreateEnums();
			var expectedCollections = BsonWriter.Serialize(expectedMapper.ToDocument(collections));
			var expectedEnums = BsonWriter.Serialize(expectedMapper.ToDocument(enums));

			for (var round = 0; round < 5; round++)
			{
				// a fresh mapper each round so the lazily-built caches are raced from many threads
				var mapper = new BsonMapper();

				System.Threading.Tasks.Parallel.For(0, 64, i =>
				{
					CollectionAssert.AreEqual(expectedCollections, mapper.SerializeToBytes(collections));
					CollectionAssert.AreEqual(expectedEnums, mapper.SerializeToBytes(enums));

					var back = mapper.DeserializeFromBytes<EnumModel>(expectedEnums);
					Assert.AreEqual(enums.Flags, back.Flags);
					CollectionAssert.AreEqual(collections.IntArray, mapper.DeserializeFromBytes<PrimitiveCollectionsModel>(expectedCollections).IntArray);
				});
			}
		}

		[TestMethod]
		public void DerivedType_RepeatedSerialize_ByteEquivalent()
		{
			var obj = new DerivedContainer
			{
				Id = 1,
				Item = new DerivedChild { Id = 2, BaseProp = "base", ChildProp = "child" }
			};

			AssertByteEquivalent(new BsonMapper(), obj);

			var result = RoundTrip(new BsonMapper(), obj);
			Assert.IsInstanceOfType(result.Item, typeof(DerivedChild));
		}
	}
}

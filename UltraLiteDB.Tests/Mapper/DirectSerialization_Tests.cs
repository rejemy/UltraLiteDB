using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Drawing;

namespace UltraLiteDB.Tests.Mapper
{
    #region Models

    public class SimplePrimitivesModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int IntVal { get; set; }
        public long LongVal { get; set; }
        public double DoubleVal { get; set; }
        public decimal DecimalVal { get; set; }
        public bool BoolVal { get; set; }
        public DateTime DateVal { get; set; }
        public Guid GuidVal { get; set; }
        public ObjectId? OidVal { get; set; }
        public byte[]? BinaryVal { get; set; }
    }

    public class ConvertedTypesModel
    {
        public int Id { get; set; }
        public short ShortVal { get; set; }
        public ushort UShortVal { get; set; }
        public uint UIntVal { get; set; }
        public ulong ULongVal { get; set; }
        public float FloatVal { get; set; }
        public byte ByteVal { get; set; }
        public sbyte SByteVal { get; set; }
        public char CharVal { get; set; }
        public MyEnum EnumVal { get; set; }
    }

    public class NestedModel
    {
        public int Id { get; set; }
        public InnerModel? Inner { get; set; }
    }

    public class InnerModel
    {
        public string? Value { get; set; }
        public int Number { get; set; }
    }

    public class CollectionsModel
    {
        public int Id { get; set; }
        public string[]? StringArray { get; set; }
        public List<int>? IntList { get; set; }
        public Dictionary<string, string>? StringDict { get; set; }
        public Dictionary<int, string>? IntKeyDict { get; set; }
    }

    public class CustomSerializerModel
    {
        public int Id { get; set; }
        public Uri? UriVal { get; set; }
        public TimeSpan TimeSpanVal { get; set; }
    }

    public class NullableModel
    {
        public int Id { get; set; }
        public int? NullableInt { get; set; }
        public DateTime? NullableDate { get; set; }
        public string? NullString { get; set; }
    }

    public class DerivedBase
    {
        public int Id { get; set; }
        public string? BaseProp { get; set; }
    }

    public class DerivedChild : DerivedBase
    {
        public string? ChildProp { get; set; }
    }

    public class DerivedContainer
    {
        public int Id { get; set; }
        public DerivedBase? Item { get; set; }
    }

    #endregion

    [TestClass]
    public class DirectSerialization_Tests
    {
        private BsonMapper CreateMapper()
        {
            return new BsonMapper();
        }

        #region Byte-level equivalence tests

        [TestMethod]
        public void Direct_Serialize_SimplePrimitives_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new SimplePrimitivesModel
            {
                Id = 42,
                Name = "Hello World",
                IntVal = -100,
                LongVal = long.MaxValue,
                DoubleVal = 3.14159,
                DecimalVal = 19.9m,
                BoolVal = true,
                DateVal = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
                GuidVal = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                OidVal = ObjectId.NewObjectId(),
                BinaryVal = new byte[] { 1, 2, 3, 4, 5 }
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_ConvertedTypes_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new ConvertedTypesModel
            {
                Id = 1,
                ShortVal = -300,
                UShortVal = 500,
                UIntVal = 100000,
                ULongVal = ulong.MaxValue,
                FloatVal = 2.5f,
                ByteVal = 255,
                SByteVal = -99,
                CharVal = 'Z',
                EnumVal = MyEnum.Second
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_NestedObject_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new NestedModel
            {
                Id = 1,
                Inner = new InnerModel { Value = "test", Number = 42 }
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_Collections_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new CollectionsModel
            {
                Id = 1,
                StringArray = new[] { "one", "two", "three" },
                IntList = new List<int> { 10, 20, 30 },
                StringDict = new Dictionary<string, string> { { "key1", "val1" }, { "key2", "val2" } },
                IntKeyDict = new Dictionary<int, string> { { 1, "Row1" }, { 2, "Row2" } }
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_CustomSerializers_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new CustomSerializerModel
            {
                Id = 1,
                UriVal = new Uri("http://www.example.com"),
                TimeSpanVal = TimeSpan.FromHours(2.5)
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_NullValues_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new NullableModel
            {
                Id = 1,
                NullableInt = null,
                NullableDate = null,
                NullString = null
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_NullValues_SerializeNullsTrue_ByteEquivalent()
        {
            var mapper = CreateMapper();
            mapper.SerializeNullValues = true;
            var obj = new NullableModel
            {
                Id = 1,
                NullableInt = null,
                NullableDate = null,
                NullString = null
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_DerivedType_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new DerivedContainer
            {
                Id = 1,
                Item = new DerivedChild { BaseProp = "base", ChildProp = "child" }
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_EmptyStringToNull_ByteEquivalent()
        {
            var mapper = CreateMapper();
            // EmptyStringToNull defaults to true
            var obj = new SimplePrimitivesModel
            {
                Id = 1,
                Name = ""
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_TrimWhitespace_ByteEquivalent()
        {
            var mapper = CreateMapper();
            // TrimWhitespace defaults to true
            var obj = new SimplePrimitivesModel
            {
                Id = 1,
                Name = "  hello  "
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_DateTimeMinMax_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new SimplePrimitivesModel
            {
                Id = 1,
                DateVal = DateTime.MinValue
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_Serialize_FullModel_ByteEquivalent()
        {
            var mapper = CreateMapper();
            mapper.IncludeFields = true;

            var obj = new MyClass
            {
                MyId = 123,
                MyString = "John",
                MyGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                MyDateTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                MyIntNullable = 999,
                MyStringList = new List<string> { "String-1", "String-2" },
                MyWriteOnly = "write-only",
                MyDict = new Dictionary<int, string> { { 1, "Row1" }, { 2, "Row2" } },
                MyDictEnum = new Dictionary<StringComparison, string> { { StringComparison.Ordinal, "ordinal" } },
                MyStringArray = new[] { "One", "Two" },
                MyStringEnumerable = new[] { "One", "Two" },
                CustomStringEnumerable = new CustomStringEnumerable(new[] { "One", "Two" }),
                MyByteArray = new byte[] { 1, 2, 3 },
                MyArraySegment = new ArraySegment<byte>(new byte[] { 0, 1, 2, 3, 4 }, 1, 3),
                MyCollectionPoint = new List<Point> { new Point(1, 1), Point.Empty },
                MyListPoint = new List<Point> { new Point(1, 1), Point.Empty },
                MyEnumerablePoint = new[] { new Point(1, 1), Point.Empty },
                MyEnumProp = MyEnum.Second,
                MyChar = 'Y',
                MyUri = new Uri("http://www.numeria.com.br"),
                MyByte = 255,
                MySByte = -99,
                MyField = "Field test",
                MyBinaryField = new byte[] { 1, 2, 3 },
                MyArraySegmentField = new ArraySegment<byte>(new byte[] { 0, 1, 2, 3, 4 }, 1, 3),
                MyTimespan = TimeSpan.FromDays(1),
                MyDecimal = 19.9m,
                MyDecimalNullable = 25.5m,
                MyInterface = new MyImpl { Name = "John" },
                MyListInterface = new List<IMyInterface> { new MyImpl { Name = "John" } },
                MyIListInterface = new List<IMyInterface> { new MyImpl { Name = "John" } },
                MyObjectString = "MyString",
                MyObjectInt = 123,
                MyObjectImpl = new MyImpl { Name = "John" },
                MyObjectList = new List<object> { 1, "ola", new MyImpl { Name = "John" }, new Uri("http://www.cnn.com") }
            };

            var expected = BsonWriter.Serialize(mapper.ToDocument(typeof(MyClass), obj));
            var actual = mapper.SerializeToBytes(typeof(MyClass), obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        #endregion

        #region Round-trip tests

        [TestMethod]
        public void Direct_RoundTrip_SimplePrimitives()
        {
            var mapper = CreateMapper();
            var obj = new SimplePrimitivesModel
            {
                Id = 42,
                Name = "Hello",
                IntVal = -100,
                LongVal = long.MaxValue,
                DoubleVal = 3.14159,
                DecimalVal = 19.9m,
                BoolVal = true,
                DateVal = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
                GuidVal = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                OidVal = ObjectId.NewObjectId(),
                BinaryVal = new byte[] { 1, 2, 3 }
            };

            var bytes = mapper.SerializeToBytes(obj);
            var result = mapper.DeserializeFromBytes<SimplePrimitivesModel>(bytes);

            Assert.AreEqual(obj.Id, result.Id);
            Assert.AreEqual(obj.Name, result.Name);
            Assert.AreEqual(obj.IntVal, result.IntVal);
            Assert.AreEqual(obj.LongVal, result.LongVal);
            Assert.AreEqual(obj.DoubleVal, result.DoubleVal);
            Assert.AreEqual(obj.DecimalVal, result.DecimalVal);
            Assert.AreEqual(obj.BoolVal, result.BoolVal);
            Assert.AreEqual(obj.DateVal.ToString(), result.DateVal.ToString());
            Assert.AreEqual(obj.GuidVal, result.GuidVal);
            Assert.AreEqual(obj.OidVal, result.OidVal);
            CollectionAssert.AreEqual(obj.BinaryVal, result.BinaryVal);
        }

        [TestMethod]
        public void Direct_RoundTrip_ConvertedTypes()
        {
            var mapper = CreateMapper();
            var obj = new ConvertedTypesModel
            {
                Id = 1,
                ShortVal = -300,
                UShortVal = 500,
                UIntVal = 100000,
                ULongVal = ulong.MaxValue,
                FloatVal = 2.5f,
                ByteVal = 255,
                SByteVal = -99,
                CharVal = 'Z',
                EnumVal = MyEnum.Second
            };

            var bytes = mapper.SerializeToBytes(obj);
            var result = mapper.DeserializeFromBytes<ConvertedTypesModel>(bytes);

            Assert.AreEqual(obj.Id, result.Id);
            Assert.AreEqual(obj.ShortVal, result.ShortVal);
            Assert.AreEqual(obj.UShortVal, result.UShortVal);
            Assert.AreEqual(obj.UIntVal, result.UIntVal);
            Assert.AreEqual(obj.ULongVal, result.ULongVal);
            Assert.AreEqual(obj.FloatVal, result.FloatVal);
            Assert.AreEqual(obj.ByteVal, result.ByteVal);
            Assert.AreEqual(obj.SByteVal, result.SByteVal);
            Assert.AreEqual(obj.CharVal, result.CharVal);
            Assert.AreEqual(obj.EnumVal, result.EnumVal);
        }

        [TestMethod]
        public void Direct_RoundTrip_Collections()
        {
            var mapper = CreateMapper();
            var obj = new CollectionsModel
            {
                Id = 1,
                StringArray = new[] { "one", "two" },
                IntList = new List<int> { 10, 20 },
                StringDict = new Dictionary<string, string> { { "k1", "v1" } },
                IntKeyDict = new Dictionary<int, string> { { 1, "r1" } }
            };

            var bytes = mapper.SerializeToBytes(obj);
            var result = mapper.DeserializeFromBytes<CollectionsModel>(bytes);

            Assert.AreEqual(obj.Id, result.Id);
            CollectionAssert.AreEqual(obj.StringArray, result.StringArray);
            CollectionAssert.AreEqual(obj.IntList, result.IntList);
            Assert.AreEqual(obj.StringDict["k1"], result.StringDict!["k1"]);
            Assert.AreEqual(obj.IntKeyDict[1], result.IntKeyDict![1]);
        }

        [TestMethod]
        public void Direct_RoundTrip_CustomSerializers()
        {
            var mapper = CreateMapper();
            var obj = new CustomSerializerModel
            {
                Id = 1,
                UriVal = new Uri("http://www.example.com"),
                TimeSpanVal = TimeSpan.FromHours(2.5)
            };

            var bytes = mapper.SerializeToBytes(obj);
            var result = mapper.DeserializeFromBytes<CustomSerializerModel>(bytes);

            Assert.AreEqual(obj.Id, result.Id);
            Assert.AreEqual(obj.UriVal, result.UriVal);
            Assert.AreEqual(obj.TimeSpanVal, result.TimeSpanVal);
        }

        [TestMethod]
        public void Direct_RoundTrip_NestedObject()
        {
            var mapper = CreateMapper();
            var obj = new NestedModel
            {
                Id = 1,
                Inner = new InnerModel { Value = "test", Number = 42 }
            };

            var bytes = mapper.SerializeToBytes(obj);
            var result = mapper.DeserializeFromBytes<NestedModel>(bytes);

            Assert.AreEqual(obj.Id, result.Id);
            Assert.AreEqual(obj.Inner.Value, result.Inner!.Value);
            Assert.AreEqual(obj.Inner.Number, result.Inner.Number);
        }

        #endregion

        #region Cross-path compatibility tests

        [TestMethod]
        public void Direct_CrossPath_DeserializeFromBsonWriterBytes()
        {
            var mapper = CreateMapper();
            var obj = new SimplePrimitivesModel
            {
                Id = 1,
                Name = "cross-path",
                IntVal = 42,
                BoolVal = true,
                DateVal = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                GuidVal = Guid.NewGuid(),
                OidVal = ObjectId.NewObjectId()
            };

            // Serialize via old path, deserialize via new path
            var bytes = BsonWriter.Serialize(mapper.ToDocument(obj));
            var result = mapper.DeserializeFromBytes<SimplePrimitivesModel>(bytes);

            Assert.AreEqual(obj.Id, result.Id);
            Assert.AreEqual(obj.Name, result.Name);
            Assert.AreEqual(obj.IntVal, result.IntVal);
            Assert.AreEqual(obj.BoolVal, result.BoolVal);
            Assert.AreEqual(obj.GuidVal, result.GuidVal);
            Assert.AreEqual(obj.OidVal, result.OidVal);
        }

        [TestMethod]
        public void Direct_CrossPath_SerializeToBsonReaderDoc()
        {
            var mapper = CreateMapper();
            var obj = new SimplePrimitivesModel
            {
                Id = 1,
                Name = "cross-path",
                IntVal = 42,
                BoolVal = true,
                DateVal = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                GuidVal = Guid.NewGuid()
            };

            // Serialize via new path, deserialize via old path
            var bytes = mapper.SerializeToBytes(obj);
            var doc = BsonReader.Deserialize(bytes);
            var result = mapper.ToObject<SimplePrimitivesModel>(doc);

            Assert.AreEqual(obj.Id, result.Id);
            Assert.AreEqual(obj.Name, result.Name);
            Assert.AreEqual(obj.IntVal, result.IntVal);
            Assert.AreEqual(obj.BoolVal, result.BoolVal);
            Assert.AreEqual(obj.GuidVal, result.GuidVal);
        }

        #endregion

        #region Edge case tests

        [TestMethod]
        public void Direct_Serialize_EmptyObject_ByteEquivalent()
        {
            var mapper = CreateMapper();
            var obj = new InnerModel();

            var expected = BsonWriter.Serialize(mapper.ToDocument(obj));
            var actual = mapper.SerializeToBytes(obj);

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Direct_RoundTrip_UnknownFieldsSkipped()
        {
            var mapper = CreateMapper();

            // Create a BsonDocument with extra fields
            var doc = new BsonDocument();
            doc["_id"] = 1;
            doc["Name"] = "test";
            doc["UnknownField1"] = "should be skipped";
            doc["UnknownField2"] = 999;

            var bytes = BsonWriter.Serialize(doc);
            var result = mapper.DeserializeFromBytes<SimplePrimitivesModel>(bytes);

            Assert.AreEqual(1, result.Id);
            Assert.AreEqual("test", result.Name);
        }

        #endregion
    }
}

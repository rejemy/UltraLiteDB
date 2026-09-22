using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace UltraLiteDB.Tests.Document
{
	/// <summary>
	/// The BsonDocument codec has two writers (single-pass into a growable buffer for <c>BsonWriter.Serialize</c>,
	/// forward-only for streams and fixed buffers) and two readers (in-memory fast path, general IByteReader path).
	/// Each pair must agree byte for byte.
	/// </summary>
	[TestClass]
	public class BsonCodec_Tests
	{
		private static BsonDocument CreateRichDocument()
		{
			var nested = new BsonDocument
			{
				["name"] = "nested",
				["deeper"] = new BsonDocument { ["x"] = 1, ["list"] = new BsonArray(new BsonValue[] { 1, "two", new BsonDocument { ["three"] = 3 } }) },
				["empty"] = new BsonDocument(),
			};

			var big = new BsonArray();
			for (var i = 0; i < 150; i++) big.Add(i * 37); // index keys of 1, 2 and 3 digits

			return new BsonDocument
			{
				["_id"] = ObjectId.NewObjectId(),
				["double"] = Math.PI,
				["ascii"] = "hello world",
				["unicode"] = "héllo wörld — 日本語 🎮",
				["long_string"] = new string('x', 5000) + "é",
				["empty_string"] = "",
				["ключ"] = "unicode key",
				["nested"] = nested,
				["array"] = big,
				["empty_array"] = new BsonArray(),
				["binary"] = new byte[] { 1, 2, 3, 250 },
				["empty_binary"] = new byte[0],
				["guid"] = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
				["true"] = true,
				["false"] = false,
				["date"] = new DateTime(2026, 9, 21, 1, 2, 3, 456, DateTimeKind.Utc),
				["min_date"] = DateTime.MinValue,
				["max_date"] = DateTime.MaxValue,
				["null"] = BsonValue.Null,
				["int_min"] = int.MinValue,
				["int_-129"] = -129,
				["int_-128"] = -128,
				["int_0"] = 0,
				["int_1023"] = 1023,
				["int_1024"] = 1024,
				["int_max"] = int.MaxValue,
				["long"] = long.MinValue,
				["decimal"] = 19.95m,
				["minvalue"] = BsonValue.MinValue,
				["maxvalue"] = BsonValue.MaxValue,
			};
		}

		private static byte[] SerializeForward(BsonDocument doc)
		{
			var buffer = new byte[doc.GetBytesCount(true)];
			var written = BsonWriter.SerializeTo(doc, buffer);
			Assert.AreEqual(buffer.Length, written);
			return buffer;
		}

		private static BsonDocument ReadGeneral(byte[] bytes)
		{
			// a non-ByteReader source takes the general path
			using var stream = new MemoryStream(bytes);
			return BsonReader.ReadDocument(new StreamByteReader(stream));
		}

		[TestMethod]
		public void Writers_AreByteIdentical()
		{
			var doc = CreateRichDocument();

			var singlePass = BsonWriter.Serialize(doc);
			var forward = SerializeForward(doc);

			using var stream = new MemoryStream();
			BsonSerializer.Serialize(doc, stream);

			CollectionAssert.AreEqual(forward, singlePass);
			CollectionAssert.AreEqual(forward, stream.ToArray());
			Assert.AreEqual(singlePass.Length, doc.GetBytesCount(true));
		}

		[TestMethod]
		public void Readers_ProduceTheSameDocument()
		{
			var bytes = BsonWriter.Serialize(CreateRichDocument());

			var fast = BsonReader.Deserialize(bytes);
			var general = ReadGeneral(bytes);

			CollectionAssert.AreEqual(bytes, BsonWriter.Serialize(fast));
			CollectionAssert.AreEqual(bytes, BsonWriter.Serialize(general));
			CollectionAssert.AreEqual(general.Keys.ToList(), fast.Keys.ToList());

			foreach (var key in general.Keys)
			{
				Assert.AreEqual(general[key].Type, fast[key].Type, key);
				Assert.AreEqual(general[key], fast[key], key);
			}

			Assert.AreEqual(1023, fast["int_1023"].AsInt32);
			Assert.AreEqual(-129, fast["int_-129"].AsInt32);
			Assert.AreEqual(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), fast["guid"].AsGuid);
			Assert.AreEqual(0, fast["empty_binary"].AsBinary.Count);
		}

		[TestMethod]
		public void Serialize_RefreshesCachedLengths()
		{
			// the forward-only writer trusts cached lengths; Serialize must leave them current, as the old sizing pass did
			var doc = CreateRichDocument();
			doc.GetBytesCount(true);

			doc["nested"].AsDocument!["added"] = "a new field";
			doc["nested"].AsDocument!["deeper"].AsDocument!["y"] = 2;
			doc["array"].AsArray!.Add("appended");

			var singlePass = BsonWriter.Serialize(doc);

			var buffer = new byte[doc.GetBytesCount(false)];
			BsonWriter.SerializeTo(doc, buffer);

			CollectionAssert.AreEqual(singlePass, buffer);
		}

		[TestMethod]
		public void KeyCache_ManyDistinctAndCollidingKeys()
		{
			// far more distinct keys than cache slots: constant eviction and hash collisions
			var random = new Random(12345);
			var keys = Enumerable.Range(0, 1000).Select(i => "key_" + i + (i % 7 == 0 ? "_ü" : "")).ToArray();

			for (var n = 0; n < 300; n++)
			{
				var doc = new BsonDocument();
				for (var k = 0; k < 6; k++) doc[keys[random.Next(keys.Length)]] = n * 10 + k;

				var bytes = BsonWriter.Serialize(doc);
				var read = BsonReader.Deserialize(bytes);

				CollectionAssert.AreEqual(doc.Keys.ToList(), read.Keys.ToList());
				CollectionAssert.AreEqual(bytes, BsonWriter.Serialize(read));
			}
		}

		[TestMethod]
		public void KeyCache_LongUnicodeAndEmptyKeys()
		{
			var doc = new BsonDocument
			{
				[new string('k', 200)] = 1,           // longer than the cache's key limit
				[new string('é', 40)] = 2,            // 80 UTF-8 bytes
				["🎮🎮"] = 3,
				[""] = 4,
			};

			for (var n = 0; n < 3; n++)
			{
				var read = BsonReader.Deserialize(BsonWriter.Serialize(doc));
				CollectionAssert.AreEqual(doc.Keys.ToList(), read.Keys.ToList());
				Assert.AreEqual(4, read[""].AsInt32);
			}
		}

		[TestMethod]
		public void Reader_SharesRepeatedKeysAndSmallValues()
		{
			var bytes = BsonWriter.Serialize(new BsonDocument { ["shared_key"] = 7, ["flag"] = true, ["big"] = 5000 });

			var a = BsonReader.Deserialize(bytes);
			var b = BsonReader.Deserialize(bytes);

			Assert.AreSame(a.Keys.First(), b.Keys.First());
			Assert.AreSame(a["shared_key"], b["shared_key"]);
			Assert.AreSame(a["flag"], b["flag"]);
			Assert.AreEqual(a["big"], b["big"]);
		}

		[TestMethod]
		public void Reader_UnsupportedBinarySubtype_Throws()
		{
			// { "b": binary subtype 0x02 } -- unsupported by this library
			var element = new byte[] { 0x05, (byte)'b', 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0xAB };
			var bytes = new byte[4 + element.Length + 1];
			BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);
			element.CopyTo(bytes, 4);

			Assert.Throws<NotSupportedException>(() => BsonReader.Deserialize(bytes));
			Assert.Throws<NotSupportedException>(() => ReadGeneral(bytes));
		}

		[TestMethod]
		public void StreamDeserialize_ReadsExactlyOneDocument()
		{
			var first = new BsonDocument { ["n"] = 1, ["s"] = "first" };
			var second = new BsonDocument { ["n"] = 2, ["s"] = "second" };

			using var stream = new MemoryStream();
			BsonSerializer.Serialize(first, stream);
			BsonSerializer.Serialize(second, stream);
			stream.Position = 0;

			Assert.AreEqual("first", BsonSerializer.Deserialize(stream)["s"].AsString);
			Assert.AreEqual("second", BsonSerializer.Deserialize(stream)["s"].AsString);
			Assert.AreEqual(stream.Length, stream.Position);
		}

		[TestMethod]
		public void IndexKeys_DocumentAndArrayValues_RoundTrip()
		{
			// index keys go through the forward-only writer into a fixed page buffer, and back through ReadBsonValue
			var values = new BsonValue[]
			{
				new BsonDocument { ["a"] = 1, ["b"] = new BsonArray(new BsonValue[] { "x", 2 }) },
				new BsonArray(new BsonValue[] { 1, new BsonDocument { ["c"] = "d" }, new BsonArray(new BsonValue[] { true }) }),
			};

			foreach (var value in values)
			{
				var page = new byte[4096];
				var writer = new ByteWriter(page);
				writer.WriteBsonValue(value, (ushort)value.GetBytesCount(true));

				var read = new ByteReader(page).ReadBsonValue((ushort)value.GetBytesCount(false));

				Assert.AreEqual(value, read);
				Assert.AreEqual(1 + value.GetBytesCount(false), writer.Position);
			}
		}
	}
}

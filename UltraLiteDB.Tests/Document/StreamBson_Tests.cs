using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using UltraLiteDB.Tests.Mapper;

namespace UltraLiteDB.Tests.Document
{
	[TestClass]
	public class StreamBson_Tests
	{
		#region Helpers

		private static BsonDocument CreateDoc()
		{
			var doc = new BsonDocument();
			doc["_id"] = 123;
			doc["FirstString"] = "BEGIN this string \" has \" \t and this \f \n\r END";
			doc["CustomerId"] = Guid.NewGuid();
			doc["Date"] = DateTime.UtcNow;
			doc["MyNull"] = null;
			doc["EmptyObj"] = new BsonDocument();
			doc["EmptyString"] = "";
			doc["maxDate"] = DateTime.MaxValue;
			doc["minDate"] = DateTime.MinValue;
			doc["Decimal"] = 19.9m;
			doc["Double"] = (double)10 / (double)3;
			doc["Long"] = long.MaxValue;
			doc["Binary"] = new byte[] { 1, 2, 3, 4, 5, 250, 251, 252 };

			doc["Items"] = new BsonArray();
			doc["Items"].AsArray!.Add(new BsonDocument());
			doc["Items"].AsArray![0].AsDocument!["Qtd"] = 3;
			doc["Items"].AsArray![0].AsDocument!["Description"] = "Big beer package";
			doc["Items"].AsArray![0].AsDocument!["Unit"] = (double)10 / (double)3;
			doc["Items"].AsArray!.Add("string-one");
			doc["Items"].AsArray!.Add(null);
			doc["Items"].AsArray!.Add(true);
			doc["Items"].AsArray!.Add(DateTime.UtcNow);

			return doc;
		}

		private static SimplePrimitivesModel CreateSimple() => new SimplePrimitivesModel
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

		private static CollectionsModel CreateCollections() => new CollectionsModel
		{
			Id = 7,
			StringArray = new[] { "a", "b", "c" },
			IntList = new List<int> { 1, 2, 3, 4 },
			StringDict = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
			IntKeyDict = new Dictionary<int, string> { [1] = "one", [2] = "two" }
		};

		#endregion

		#region BsonDocument stream path

		[TestMethod]
		public void Document_Stream_Serialize_ByteEquivalent()
		{
			var doc = CreateDoc();

			var expected = BsonSerializer.Serialize(doc);

			using var ms = new MemoryStream();
			BsonSerializer.Serialize(doc, ms);

			CollectionAssert.AreEqual(expected, ms.ToArray());
		}

		[TestMethod]
		public void Document_Stream_RoundTrip()
		{
			var doc = CreateDoc();

			using var ms = new MemoryStream();
			BsonSerializer.Serialize(doc, ms);
			ms.Position = 0;

			var doc2 = BsonSerializer.Deserialize(ms);

			CollectionAssert.AreEqual(BsonSerializer.Serialize(doc), BsonSerializer.Serialize(doc2));
		}

		[TestMethod]
		public void Document_Stream_Deserialize_FromArrayBytes()
		{
			// bytes produced by the array writer must be readable by the stream reader
			var doc = CreateDoc();
			var bytes = BsonSerializer.Serialize(doc);

			using var ms = new MemoryStream(bytes);
			var doc2 = BsonSerializer.Deserialize(ms);

			CollectionAssert.AreEqual(bytes, BsonSerializer.Serialize(doc2));
		}

		#endregion

		#region POCO (direct) stream path

		[TestMethod]
		public void Poco_Stream_Serialize_ByteEquivalent()
		{
			var mapper = new BsonMapper();

			foreach (object obj in new object[] { CreateSimple(), CreateCollections() })
			{
				var expected = mapper.SerializeToBytes(obj.GetType(), obj);

				using var ms = new MemoryStream();
				mapper.SerializeToStream(obj.GetType(), obj, ms);

				CollectionAssert.AreEqual(expected, ms.ToArray(), $"mismatch for {obj.GetType().Name}");
			}
		}

		[TestMethod]
		public void Poco_Stream_RoundTrip()
		{
			var mapper = new BsonMapper();
			var obj = CreateSimple();

			using var ms = new MemoryStream();
			mapper.SerializeToStream(obj, ms);
			var streamBytes = ms.ToArray();

			ms.Position = 0;
			var obj2 = mapper.DeserializeFromStream<SimplePrimitivesModel>(ms);

			CollectionAssert.AreEqual(streamBytes, mapper.SerializeToBytes(obj2));
		}

		[TestMethod]
		public void Poco_Stream_RoundTrip_Collections()
		{
			var mapper = new BsonMapper();
			var obj = CreateCollections();

			using var ms = new MemoryStream();
			mapper.SerializeToStream(obj, ms);
			var streamBytes = ms.ToArray();

			ms.Position = 0;
			var obj2 = mapper.DeserializeFromStream<CollectionsModel>(ms);

			CollectionAssert.AreEqual(streamBytes, mapper.SerializeToBytes(obj2));
		}

		[TestMethod]
		public void Poco_FileStream_RoundTrip()
		{
			var mapper = new BsonMapper();
			var obj = CreateSimple();
			var path = Path.Combine(Path.GetTempPath(), $"ulite-stream-{Guid.NewGuid()}.bson");

			try
			{
				byte[] streamBytes;
				using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
				{
					mapper.SerializeToStream(obj, fs);
				}

				streamBytes = File.ReadAllBytes(path);
				CollectionAssert.AreEqual(mapper.SerializeToBytes(obj), streamBytes);

				using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
				{
					var obj2 = mapper.DeserializeFromStream<SimplePrimitivesModel>(fs);
					CollectionAssert.AreEqual(streamBytes, mapper.SerializeToBytes(obj2));
				}
			}
			finally
			{
				if (File.Exists(path)) File.Delete(path);
			}
		}

		#endregion

		#region Non-seekable streams

		[TestMethod]
		public void NonSeekable_Poco_Serialize_Succeeds()
		{
			var mapper = new BsonMapper();
			var obj = CreateSimple();

			using var inner = new MemoryStream();
			using var wrapper = new NonSeekableStream(inner);

			// the document is buffered and written in one call, so no seeking is needed
			mapper.SerializeToStream(obj, wrapper);

			CollectionAssert.AreEqual(mapper.SerializeToBytes(obj), inner.ToArray());
		}

		[TestMethod]
		public void Poco_Deserialize_CorruptLengthPrefix_Throws()
		{
			// claims ~2 GB but the stream ends almost immediately: must fail without allocating the claimed size
			var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 0x10, (byte)'a', 0x00, 0x01, 0x00, 0x00, 0x00 };

			using var stream = new MemoryStream(bytes);

			Assert.Throws<EndOfStreamException>(() => new BsonMapper().DeserializeFromStream<SimplePrimitivesModel>(stream));
		}

		[TestMethod]
		public void NonSeekable_Poco_Deserialize_Succeeds()
		{
			var mapper = new BsonMapper();
			var obj = CreateSimple();
			var bytes = mapper.SerializeToBytes(obj);

			using var inner = new MemoryStream(bytes);
			using var wrapper = new NonSeekableStream(inner);

			var obj2 = mapper.DeserializeFromStream<SimplePrimitivesModel>(wrapper);

			CollectionAssert.AreEqual(bytes, mapper.SerializeToBytes(obj2));
		}


		[TestMethod]
		public void NonSeekable_Document_Serialize_Succeeds()
		{
			var doc = CreateDoc();

			using var inner = new MemoryStream();
			using var wrapper = new NonSeekableStream(inner);

			// writing a document is forward-only, so no seeking is needed
			BsonSerializer.Serialize(doc, wrapper);

			CollectionAssert.AreEqual(BsonSerializer.Serialize(doc), inner.ToArray());
		}

		[TestMethod]
		public void NonSeekable_StreamByteWriter_SetPosition_Throws()
		{
			using var inner = new MemoryStream();
			using var wrapper = new NonSeekableStream(inner);

			var writer = new StreamByteWriter(wrapper);
			writer.Write(42);

			Assert.Throws<NotSupportedException>(() => writer.Position = 0);
		}

		[TestMethod]
		public void NonSeekable_Document_Deserialize_Succeeds()
		{
			var doc = CreateDoc();
			var bytes = BsonSerializer.Serialize(doc);

			using var inner = new MemoryStream(bytes);
			using var wrapper = new NonSeekableStream(inner);

			// forward-only reader must work on a non-seekable stream
			var doc2 = BsonSerializer.Deserialize(wrapper);

			CollectionAssert.AreEqual(bytes, BsonSerializer.Serialize(doc2));
		}

		/// <summary>
		/// Minimal stream wrapper that forwards reads/writes to an inner stream but reports
		/// itself as non-seekable, to exercise the forward-only code paths.
		/// </summary>
		private sealed class NonSeekableStream : Stream
		{
			private readonly Stream _inner;

			public NonSeekableStream(Stream inner) { _inner = inner; }

			public override bool CanRead => _inner.CanRead;
			public override bool CanWrite => _inner.CanWrite;
			public override bool CanSeek => false;

			public override long Length => throw new NotSupportedException();
			public override long Position
			{
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}

			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();

			public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
			public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
			public override void Flush() => _inner.Flush();
		}

		#endregion
	}
}

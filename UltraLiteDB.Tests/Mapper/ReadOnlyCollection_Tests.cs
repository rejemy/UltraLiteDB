using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace UltraLiteDB.Tests.Mapper
{
	#region Model

	public class ReadOnlyContainer
	{
		public int Id { get; set; }
		public IReadOnlyList<string> Tags { get; set; } = new List<string>();
		public IReadOnlyCollection<int> Scores { get; set; } = new List<int>();
		public IReadOnlyDictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
	}

	public enum DictionaryKeyKind
	{
		Alpha,
		Beta,
		Gamma
	}

	public class DictionaryItem
	{
		public string? Name { get; set; }
		public int Level { get; set; }
	}

	public class DictionaryInterfaceContainer
	{
		public int Id { get; set; }
		public IDictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
		public IReadOnlyDictionary<string, int> ReadOnlyCounts { get; set; } = new Dictionary<string, int>();
		public IDictionary<DictionaryKeyKind, string> ByKind { get; set; } = new Dictionary<DictionaryKeyKind, string>();
		public IReadOnlyDictionary<DictionaryKeyKind, int> ReadOnlyByKind { get; set; } = new Dictionary<DictionaryKeyKind, int>();
		public IDictionary<int, DictionaryItem> Items { get; set; } = new Dictionary<int, DictionaryItem>();
		public IReadOnlyDictionary<string, List<int>> Lists { get; set; } = new Dictionary<string, List<int>>();
	}

	#endregion

	[TestClass]
	public class ReadOnlyCollection_Tests
	{
		[TestMethod]
		public void RoundTrip_IReadOnlyList_Property()
		{
			var mapper = new BsonMapper();

			var source = new ReadOnlyContainer { Id = 1, Tags = new List<string> { "a", "b", "c" } };

			var doc = mapper.ToDocument(source);
			var result = mapper.ToObject<ReadOnlyContainer>(doc);

			Assert.IsInstanceOfType(result.Tags, typeof(List<string>));
			CollectionAssert.AreEqual(new[] { "a", "b", "c" }, (List<string>)result.Tags);
		}

		[TestMethod]
		public void RoundTrip_IReadOnlyCollection_Property()
		{
			var mapper = new BsonMapper();

			var source = new ReadOnlyContainer { Id = 1, Scores = new List<int> { 10, 20 } };

			var doc = mapper.ToDocument(source);
			var result = mapper.ToObject<ReadOnlyContainer>(doc);

			Assert.IsInstanceOfType(result.Scores, typeof(List<int>));
			CollectionAssert.AreEqual(new[] { 10, 20 }, new List<int>(result.Scores));
		}

		[TestMethod]
		public void RoundTrip_IReadOnlyDictionary_Property()
		{
			var mapper = new BsonMapper();

			var source = new ReadOnlyContainer
			{
				Id = 1,
				Counts = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 }
			};

			var doc = mapper.ToDocument(source);
			var result = mapper.ToObject<ReadOnlyContainer>(doc);

			Assert.IsInstanceOfType(result.Counts, typeof(Dictionary<string, int>));
			Assert.AreEqual(2, result.Counts.Count);
			Assert.AreEqual(1, result.Counts["x"]);
			Assert.AreEqual(2, result.Counts["y"]);
		}

		#region Dictionary interfaces through every deserialization path

		private static DictionaryInterfaceContainer CreateDictionaryInterfaceContainer()
		{
			return new DictionaryInterfaceContainer
			{
				Id = 1,
				Counts = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
				ReadOnlyCounts = new Dictionary<string, int> { ["c"] = 3 },
				ByKind = new Dictionary<DictionaryKeyKind, string> { [DictionaryKeyKind.Alpha] = "first", [DictionaryKeyKind.Gamma] = "third" },
				ReadOnlyByKind = new Dictionary<DictionaryKeyKind, int> { [DictionaryKeyKind.Beta] = 20 },
				Items = new Dictionary<int, DictionaryItem> { [7] = new DictionaryItem { Name = "sword", Level = 3 } },
				Lists = new Dictionary<string, List<int>> { ["primes"] = new List<int> { 2, 3, 5 } },
			};
		}

		/// <summary>
		/// Reads the same BSON through the direct bytes path, the direct stream path and the BsonDocument path.
		/// </summary>
		private static IEnumerable<(string Path, DictionaryInterfaceContainer Result)> ReadAllPaths(BsonMapper mapper, byte[] bytes)
		{
			yield return ("DeserializeFromBytes", mapper.DeserializeFromBytes<DictionaryInterfaceContainer>(bytes));
			yield return ("DeserializeFromStream", mapper.DeserializeFromStream<DictionaryInterfaceContainer>(new MemoryStream(bytes)));
			yield return ("ToObject", mapper.ToObject<DictionaryInterfaceContainer>(BsonSerializer.Deserialize(bytes)));
		}

		private static void AssertDictionaryInterfaceContainer(string path, DictionaryInterfaceContainer result)
		{
			Assert.IsInstanceOfType<Dictionary<string, int>>(result.Counts, path);
			Assert.AreEqual(2, result.Counts.Count, path);
			Assert.AreEqual(1, result.Counts["a"], path);
			Assert.AreEqual(2, result.Counts["b"], path);

			Assert.IsInstanceOfType<Dictionary<string, int>>(result.ReadOnlyCounts, path);
			Assert.AreEqual(1, result.ReadOnlyCounts.Count, path);
			Assert.AreEqual(3, result.ReadOnlyCounts["c"], path);

			Assert.AreEqual(2, result.ByKind.Count, path);
			Assert.AreEqual("first", result.ByKind[DictionaryKeyKind.Alpha], path);
			Assert.AreEqual("third", result.ByKind[DictionaryKeyKind.Gamma], path);

			Assert.AreEqual(1, result.ReadOnlyByKind.Count, path);
			Assert.AreEqual(20, result.ReadOnlyByKind[DictionaryKeyKind.Beta], path);

			Assert.AreEqual(1, result.Items.Count, path);
			Assert.AreEqual("sword", result.Items[7].Name, path);
			Assert.AreEqual(3, result.Items[7].Level, path);

			Assert.AreEqual(1, result.Lists.Count, path);
			CollectionAssert.AreEqual(new[] { 2, 3, 5 }, result.Lists["primes"], path);
		}

		[TestMethod]
		public void Dictionary_Interfaces_RoundTrip_From_Direct_Serializer()
		{
			var mapper = new BsonMapper();
			var bytes = mapper.SerializeToBytes(CreateDictionaryInterfaceContainer());

			foreach (var (path, result) in ReadAllPaths(mapper, bytes))
			{
				AssertDictionaryInterfaceContainer(path, result);
			}
		}

		[TestMethod]
		public void Dictionary_Interfaces_RoundTrip_From_Document_Serializer()
		{
			var mapper = new BsonMapper();
			var bytes = BsonSerializer.Serialize(mapper.ToDocument(CreateDictionaryInterfaceContainer()));

			foreach (var (path, result) in ReadAllPaths(mapper, bytes))
			{
				AssertDictionaryInterfaceContainer(path, result);
			}
		}

		[TestMethod]
		public void Empty_Dictionary_Interfaces_Read_As_Empty_Dictionaries()
		{
			var mapper = new BsonMapper();
			var doc = new BsonDocument
			{
				["_id"] = 1,
				["Counts"] = new BsonDocument(),
				["ReadOnlyByKind"] = new BsonDocument(),
			};

			foreach (var (path, result) in ReadAllPaths(mapper, BsonSerializer.Serialize(doc)))
			{
				Assert.IsInstanceOfType<Dictionary<string, int>>(result.Counts, path);
				Assert.AreEqual(0, result.Counts.Count, path);
				Assert.IsInstanceOfType<Dictionary<DictionaryKeyKind, int>>(result.ReadOnlyByKind, path);
				Assert.AreEqual(0, result.ReadOnlyByKind.Count, path);
			}
		}

		[TestMethod]
		public void Dictionary_Interface_As_Root_Type()
		{
			var mapper = new BsonMapper();
			var bytes = BsonSerializer.Serialize(new BsonDocument { ["Alpha"] = 1, ["Gamma"] = 3 });

			var fromBytes = mapper.DeserializeFromBytes<IReadOnlyDictionary<DictionaryKeyKind, int>>(bytes);
			var fromStream = mapper.DeserializeFromStream<IDictionary<DictionaryKeyKind, int>>(new MemoryStream(bytes));

			Assert.AreEqual(2, fromBytes.Count);
			Assert.AreEqual(3, fromBytes[DictionaryKeyKind.Gamma]);
			Assert.AreEqual(2, fromStream.Count);
			Assert.AreEqual(1, fromStream[DictionaryKeyKind.Alpha]);
		}

		#endregion
	}
}

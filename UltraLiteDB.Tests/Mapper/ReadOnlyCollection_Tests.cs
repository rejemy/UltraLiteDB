using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

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
	}
}

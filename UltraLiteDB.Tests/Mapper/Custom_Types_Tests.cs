using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UltraLiteDB.Tests.Mapper
{
	#region Model

	public class CustomType
	{
		public Regex? Re1 { get; set; }
		public Regex? Re2 { get; set; }

		public Uri? Url { get; set; }
		public TimeSpan Ts { get; set; }
	}

	public enum CustomEnum
	{
		First,
		SecondValue
	}

	public class CustomEnumHolder
	{
		public CustomEnum Value { get; set; }
	}

	#endregion

	[TestClass]
	public class Custom_Types_Tests
	{
		[TestMethod]
		public void Custom_Types()
		{
			var mapper = new BsonMapper();

			var o = new CustomType
			{
				Re1 = new Regex("^a+"),
				Re2 = new Regex("^a*", RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace),
				Url = new Uri("http://www.litedb.org"),
				Ts = TimeSpan.FromSeconds(10)
			};

			var doc = mapper.ToDocument(o);
			var no = mapper.ToObject<CustomType>(doc);

			Assert.AreEqual("^a+", doc["Re1"].AsString);
			Assert.AreEqual("^a*", doc["Re2"].AsDocument!["p"].AsString);

			Assert.AreEqual(o.Re1.ToString(), no.Re1!.ToString());
			Assert.AreEqual(o.Re2.ToString(), no.Re2!.ToString());
			Assert.AreEqual(o.Re2.Options, no.Re2.Options);
			Assert.AreEqual(o.Url, no.Url);
			Assert.AreEqual(o.Ts, no.Ts);
		}

		// A custom (de)serializer registered for an enum type takes precedence over the built-in
		// member-name form, on both the BsonDocument and the direct byte paths.
		[TestMethod]
		public void Custom_Enum_Serializer_Takes_Precedence()
		{
			var mapper = new BsonMapper();
			mapper.RegisterType<CustomEnum>(
				e => e == CustomEnum.First ? "first" : "second-value",
				b => b.AsString == "first" ? CustomEnum.First : CustomEnum.SecondValue);

			var o = new CustomEnumHolder { Value = CustomEnum.SecondValue };

			var doc = mapper.ToDocument(o);
			Assert.AreEqual("second-value", doc["Value"].AsString);
			Assert.AreEqual(CustomEnum.SecondValue, mapper.ToObject<CustomEnumHolder>(doc).Value);

			var bytes = mapper.SerializeToBytes(o);
			CollectionAssert.AreEqual(BsonSerializer.Serialize(doc), bytes);
			Assert.AreEqual(CustomEnum.SecondValue, mapper.DeserializeFromBytes<CustomEnumHolder>(bytes).Value);
		}

		[TestMethod]
		public void Enum_Without_Custom_Serializer_Uses_Member_Name()
		{
			var mapper = new BsonMapper();
			var o = new CustomEnumHolder { Value = CustomEnum.SecondValue };

			var doc = mapper.ToDocument(o);
			Assert.AreEqual("SecondValue", doc["Value"].AsString);
			Assert.AreEqual(CustomEnum.SecondValue, mapper.ToObject<CustomEnumHolder>(doc).Value);
			Assert.AreEqual(CustomEnum.SecondValue, mapper.DeserializeFromBytes<CustomEnumHolder>(mapper.SerializeToBytes(o)).Value);
		}
	}
}

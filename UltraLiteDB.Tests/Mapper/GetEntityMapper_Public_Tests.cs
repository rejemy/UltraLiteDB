using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace UltraLiteDB.Tests.Mapper
{
	#region Model

	public class GemEntity
	{
		public int Id { get; set; }
		public string? Name { get; set; }
	}

	#endregion

	[TestClass]
	public class GetEntityMapper_Public_Tests
	{
		[TestMethod]
		public void GetEntityMapper_IsPubliclyAccessible()
		{
			var mapper = new BsonMapper();

			// this call compiles only if GetEntityMapper is public (change #1)
			var entity = mapper.GetEntityMapper(typeof(GemEntity));

			Assert.IsNotNull(entity);
			Assert.AreEqual(typeof(GemEntity), entity.ForType);

			var members = entity.Members.ToList();

			Assert.IsTrue(members.Any(m => m.MemberName == "Name"));
			// the Id member is mapped to the "_id" field by convention
			Assert.IsTrue(members.Any(m => m.MemberName == "Id"));
		}

		[TestMethod]
		public void GetEntityMapper_ReturnsCachedInstance()
		{
			var mapper = new BsonMapper();

			var first = mapper.GetEntityMapper(typeof(GemEntity));
			var second = mapper.GetEntityMapper(typeof(GemEntity));

			Assert.AreSame(first, second);
		}
	}
}

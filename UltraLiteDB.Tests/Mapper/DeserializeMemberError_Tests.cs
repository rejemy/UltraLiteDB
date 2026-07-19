using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UltraLiteDB.Tests.Mapper
{
	#region Model

	public class WorldObjectDef
	{
		public int Id { get; set; }
		public double Density { get; set; }
	}

	public class NestedWorldDef
	{
		public int Id { get; set; }
		public WorldObjectDef Inner { get; set; } = new WorldObjectDef();
	}

	#endregion

	[TestClass]
	public class DeserializeMemberError_Tests
	{
		[TestMethod]
		public void DeserializeObject_WrapsMemberConversionError_WithContext()
		{
			var mapper = new BsonMapper();

			// Density is a double, but the document stores a non-numeric string
			var doc = new BsonDocument
			{
				["_id"] = 1,
				["Density"] = "heavy"
			};

			try
			{
				mapper.ToObject<WorldObjectDef>(doc);
				Assert.Fail("Expected a deserialization error");
			}
			catch (UltraLiteException ex)
			{
				Assert.AreEqual(UltraLiteException.DESERIALIZE_MEMBER, ex.ErrorCode);
				StringAssert.Contains(ex.Message, "Density");
				StringAssert.Contains(ex.Message, "WorldObjectDef");
				// the original conversion failure is preserved
				Assert.IsNotNull(ex.InnerException);
			}
		}

		[TestMethod]
		public void DeserializeObject_DoesNotDoubleWrapNestedMemberError()
		{
			var mapper = new BsonMapper();

			var doc = new BsonDocument
			{
				["_id"] = 1,
				["Inner"] = new BsonDocument
				{
					["_id"] = 2,
					["Density"] = "heavy"
				}
			};

			try
			{
				mapper.ToObject<NestedWorldDef>(doc);
				Assert.Fail("Expected a deserialization error");
			}
			catch (UltraLiteException ex)
			{
				// the innermost UltraLiteException already carries member context and is not
				// re-wrapped by the outer member, so the raw conversion failure surfaces once
				Assert.AreEqual(UltraLiteException.DESERIALIZE_MEMBER, ex.ErrorCode);
				StringAssert.Contains(ex.Message, "Density");
				StringAssert.Contains(ex.Message, "WorldObjectDef");
				Assert.IsNotNull(ex.InnerException);
				// inner is the raw conversion error, not another UltraLiteException wrapper
				Assert.IsNotInstanceOfType(ex.InnerException, typeof(UltraLiteException));
			}
		}

		[TestMethod]
		public void DeserializeObject_ValidDocument_StillDeserializes()
		{
			var mapper = new BsonMapper();

			var doc = new BsonDocument
			{
				["_id"] = 1,
				["Density"] = 2.5
			};

			var result = mapper.ToObject<WorldObjectDef>(doc);

			Assert.AreEqual(1, result.Id);
			Assert.AreEqual(2.5, result.Density);
		}
	}
}

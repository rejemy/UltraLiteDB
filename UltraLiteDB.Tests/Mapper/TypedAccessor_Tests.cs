using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UltraLiteDB.Tests.Mapper
{
	#region Model

	public interface ITypedAccessorNamed
	{
		string? Name { get; set; }
	}

	public class TypedAccessorBase
	{
		public int Id { get; set; }
		public string? BaseValue { get; set; }
	}

	public class TypedAccessorModel : TypedAccessorBase, ITypedAccessorNamed
	{
		private string? _upper;

		// interface implementation: a sealed virtual accessor
		public string? Name { get; set; }

		// virtual: stays on reflection
		public virtual int Virtual { get; set; }

		public long Long { get; set; }
		public double Double { get; set; }
		public float Float { get; set; }
		public FastPathColor Color { get; set; }
		public int? NullableInt { get; set; }
		public DateTime Date { get; set; }
		public Guid Guid { get; set; }
		public byte[]? Bytes { get; set; }
		public List<int>? List { get; set; }
		public Dictionary<string, int>? Dict { get; set; }
		public TypedAccessorBase? Nested { get; set; }
		public int InitOnly { get; init; }

		// setter with logic
		public string? Upper { get => _upper; set => _upper = value?.ToUpperInvariant(); }

		private int Private { get; set; }

		public int GetPrivate() => Private;
		public void SetPrivate(int value) => Private = value;
	}

	public struct TypedAccessorStruct
	{
		public int X { get; set; }
		public string? Name { get; set; }
	}

	public class TypedAccessorStructHolder
	{
		public int Id { get; set; }
		public TypedAccessorStruct Value { get; set; }
	}

	public class TypedAccessorThrowing
	{
		public int Id { get; set; }
		public int Checked { get => 1; set => throw new InvalidOperationException("rejected"); }
	}

	#endregion

	/// <summary>
	/// Property access through typed delegates (<c>TypedAccessor</c>) must behave exactly like the reflection
	/// fallback, which runtimes without runtime generic instantiation (NativeAOT, older IL2CPP) still use.
	/// Every behavioral test runs against both strategies.
	/// </summary>
	[TestClass]
	[DoNotParallelize]
	public class TypedAccessor_Tests
	{
		private static readonly Type _reflection = typeof(BsonMapper).Assembly.GetType("UltraLiteDB.Reflection")!;
		private static readonly FieldInfo _enabled = _reflection.GetField("TypedAccessorsEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;

		/// <summary>
		/// Runs <paramref name="test"/> with typed accessors on, then with the reflection fallback forced.
		/// Each run gets fresh mappers, since accessors are built when an entity mapper is.
		/// </summary>
		private static void BothStrategies(Action<string> test)
		{
			try
			{
				_enabled.SetValue(null, true);
				test("typed");

				_enabled.SetValue(null, false);
				test("reflection");
			}
			finally
			{
				_enabled.SetValue(null, true);
			}
		}

		private static bool IsTyped(Delegate? accessor) => accessor?.Target?.GetType().Name.StartsWith("TypedAccessor") == true;

		private static TypedAccessorModel CreateModel()
		{
			var model = new TypedAccessorModel
			{
				Id = 7,
				BaseValue = "base",
				Name = "named",
				Virtual = 3,
				Long = long.MaxValue,
				Double = Math.PI,
				Float = 1.25f,
				Color = FastPathColor.Blue,
				NullableInt = 42,
				Date = new DateTime(2026, 9, 21, 1, 2, 3, DateTimeKind.Utc),
				Guid = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
				Bytes = new byte[] { 1, 2, 3 },
				List = new List<int> { 1, 2, 3 },
				Dict = new Dictionary<string, int> { { "a", 1 } },
				Nested = new TypedAccessorBase { Id = 8, BaseValue = "nested" },
				InitOnly = 9,
				Upper = "shout",
			};

			model.SetPrivate(11);
			return model;
		}

		private static void AssertModel(TypedAccessorModel expected, TypedAccessorModel actual, string strategy)
		{
			Assert.AreEqual(expected.Id, actual.Id, strategy);
			Assert.AreEqual(expected.BaseValue, actual.BaseValue, strategy);
			Assert.AreEqual(expected.Name, actual.Name, strategy);
			Assert.AreEqual(expected.Virtual, actual.Virtual, strategy);
			Assert.AreEqual(expected.Long, actual.Long, strategy);
			Assert.AreEqual(expected.Double, actual.Double, strategy);
			Assert.AreEqual(expected.Float, actual.Float, strategy);
			Assert.AreEqual(expected.Color, actual.Color, strategy);
			Assert.AreEqual(expected.NullableInt, actual.NullableInt, strategy);
			Assert.AreEqual(expected.Date, actual.Date, strategy);
			Assert.AreEqual(expected.Guid, actual.Guid, strategy);
			CollectionAssert.AreEqual(expected.Bytes, actual.Bytes, strategy);
			CollectionAssert.AreEqual(expected.List, actual.List, strategy);
			CollectionAssert.AreEquivalent(expected.Dict!.ToList(), actual.Dict!.ToList(), strategy);
			Assert.AreEqual(expected.Nested!.BaseValue, actual.Nested!.BaseValue, strategy);
			Assert.AreEqual(expected.InitOnly, actual.InitOnly, strategy);
			Assert.AreEqual(expected.Upper, actual.Upper, strategy);
			Assert.AreEqual(expected.GetPrivate(), actual.GetPrivate(), strategy);
		}

		[TestMethod]
		public void TypedAccessors_AreSupportedOnThisRuntime()
		{
			// otherwise every "typed" run below would silently test the fallback
			var supported = (bool)_reflection.GetProperty("TypedAccessorsSupported", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
			Assert.IsTrue(supported);
		}

		[TestMethod]
		public void TypedAccessors_EligibilityRules()
		{
			var members = new BsonMapper { IncludeNonPublic = true }.GetEntityMapper(typeof(TypedAccessorModel)).Members;
			MemberMapper M(string name) => members.Single(m => m.MemberName == name);

			// class owners with non-virtual (or sealed interface) accessors get typed delegates, base-class and private ones included
			foreach (var name in new[] { "Id", "BaseValue", "Name", "Long", "Color", "NullableInt", "Bytes", "List", "Nested", "Upper", "Private" })
			{
				Assert.IsTrue(IsTyped(M(name).Getter), name + " getter");
			}

			Assert.IsTrue(IsTyped(M("Long").Setter), "Long setter");
			Assert.IsTrue(IsTyped(M("Upper").Setter), "Upper setter");

			// overridable accessors stay on reflection
			Assert.IsFalse(IsTyped(M("Virtual").Getter), "Virtual getter");
			Assert.IsFalse(IsTyped(M("Virtual").Setter), "Virtual setter");

			// struct owners stay on reflection (the boxed struct must be mutated in place)
			var structMembers = new BsonMapper().GetEntityMapper(typeof(TypedAccessorStruct)).Members;
			Assert.IsTrue(structMembers.All(m => !IsTyped(m.Getter) && !IsTyped(m.Setter)));
		}

		[TestMethod]
		public void RoundTrip_DirectPath()
		{
			BothStrategies(strategy =>
			{
				var mapper = new BsonMapper { IncludeNonPublic = true };
				var model = CreateModel();

				var bytes = mapper.SerializeToBytes(model);
				AssertModel(model, mapper.DeserializeFromBytes<TypedAccessorModel>(bytes), strategy);

				// and the same bytes as the document path
				CollectionAssert.AreEqual(BsonWriter.Serialize(mapper.ToDocument(model)), bytes, strategy);
			});
		}

		[TestMethod]
		public void RoundTrip_DocumentPath()
		{
			BothStrategies(strategy =>
			{
				var mapper = new BsonMapper { IncludeNonPublic = true };
				var model = CreateModel();

				AssertModel(model, mapper.ToObject<TypedAccessorModel>(mapper.ToDocument(model)), strategy);
			});
		}

		[TestMethod]
		public void RoundTrip_StructOwner()
		{
			BothStrategies(strategy =>
			{
				var mapper = new BsonMapper();
				var holder = new TypedAccessorStructHolder { Id = 1, Value = new TypedAccessorStruct { X = 5, Name = "s" } };

				var result = mapper.DeserializeFromBytes<TypedAccessorStructHolder>(mapper.SerializeToBytes(holder));

				Assert.AreEqual(5, result.Value.X, strategy);
				Assert.AreEqual("s", result.Value.Name, strategy);
			});
		}

		[TestMethod]
		public void Setter_WidensMismatchedNumericTypes()
		{
			// values stored as Int32 but read into long/double/float properties: reflection widens them, and the
			// typed setter must hand them to reflection rather than fail its exact-type fast path
			var doc = new BsonDocument
			{
				["_id"] = 1,
				["Long"] = 5,
				["Double"] = 6,
				["Float"] = 7,
			};

			BothStrategies(strategy =>
			{
				var mapper = new BsonMapper();

				foreach (var result in new[] { mapper.ToObject<TypedAccessorModel>(doc), mapper.DeserializeFromBytes<TypedAccessorModel>(BsonWriter.Serialize(doc)) })
				{
					Assert.AreEqual(5L, result.Long, strategy);
					Assert.AreEqual(6.0, result.Double, strategy);
					Assert.AreEqual(7f, result.Float, strategy);
				}
			});
		}

		[TestMethod]
		public void Setter_EnumFromUnderlyingInteger()
		{
			var doc = new BsonDocument { ["_id"] = 1, ["Color"] = 2 };

			BothStrategies(strategy =>
			{
				var result = new BsonMapper().DeserializeFromBytes<TypedAccessorModel>(BsonWriter.Serialize(doc));
				Assert.AreEqual(FastPathColor.Blue, result.Color, strategy);
			});
		}

		[TestMethod]
		public void Setter_NullAssignsDefault()
		{
			BothStrategies(strategy =>
			{
				// a custom member deserializer returning null: reflection assigns default(T), even to value types
				var mapper = new BsonMapper();
				mapper.ResolveMember = (type, memberInfo, member) =>
				{
					if (member.MemberName == "Long" || member.MemberName == "Name" || member.MemberName == "NullableInt")
					{
						member.Deserialize = (value, m) => null!;
					}
				};

				var model = CreateModel();
				var bytes = mapper.SerializeToBytes(model);

				foreach (var result in new[] { mapper.DeserializeFromBytes<TypedAccessorModel>(bytes), mapper.ToObject<TypedAccessorModel>(BsonReader.Deserialize(bytes)) })
				{
					Assert.AreEqual(0L, result.Long, strategy);
					Assert.IsNull(result.Name, strategy);
					Assert.IsNull(result.NullableInt, strategy);
					Assert.AreEqual(model.Id, result.Id, strategy);
				}
			});
		}

		[TestMethod]
		public void PrimitiveFastPath_HonorsCustomDeserializer()
		{
			BothStrategies(strategy =>
			{
				// writing ignores a custom serializer for a built-in type, but the direct reader applies a custom
				// deserializer registered for the member's type; the unboxed fast path must not skip it
				var mapper = new BsonMapper();
				mapper.RegisterType<DateTime>(d => new BsonValue(d), b => new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

				var model = CreateModel();
				var result = mapper.DeserializeFromBytes<TypedAccessorModel>(mapper.SerializeToBytes(model));

				Assert.AreEqual(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.Date, strategy);
				Assert.AreEqual(model.Long, result.Long, strategy);
			});
		}

		[TestMethod]
		public void PrimitiveFastPath_GuidWithOtherSubtype_UsesGeneralPath()
		{
			// a 16-byte generic-binary value (subtype 0) is not a Guid: the fast path must leave it untouched
			var doc = new BsonDocument { ["_id"] = 1, ["Guid"] = new BsonValue(Guid.NewGuid().ToByteArray()), ["Long"] = 3L };
			var bytes = BsonWriter.Serialize(doc);

			BothStrategies(strategy =>
			{
				var mapper = new BsonMapper();
				Exception? direct = null, document = null;

				try { mapper.DeserializeFromBytes<TypedAccessorModel>(bytes); } catch (Exception e) { direct = e; }
				try { mapper.ToObject<TypedAccessorModel>(doc); } catch (Exception e) { document = e; }

				// same outcome as the general path: a byte array can't be assigned to a Guid property
				Assert.IsNotNull(direct, strategy);
				Assert.IsNotNull(document, strategy);
			});
		}

		[TestMethod]
		public void Setter_ExceptionIsWrappedWithMemberContext()
		{
			var doc = new BsonDocument { ["_id"] = 1, ["Checked"] = 2 };

			BothStrategies(strategy =>
			{
				try
				{
					new BsonMapper().ToObject<TypedAccessorThrowing>(doc);
					Assert.Fail("Expected the setter's exception: " + strategy);
				}
				catch (UltraLiteException ex)
				{
					Assert.AreEqual(UltraLiteException.DESERIALIZE_MEMBER, ex.ErrorCode, strategy);
					StringAssert.Contains(ex.Message, "Checked");

					// typed delegates surface the setter's own exception; reflection wraps it in TargetInvocationException
					var inner = ex.InnerException is TargetInvocationException tie ? tie.InnerException : ex.InnerException;
					Assert.IsInstanceOfType(inner, typeof(InvalidOperationException), strategy);
				}
			});
		}
	}
}

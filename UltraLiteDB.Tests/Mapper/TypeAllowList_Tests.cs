using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UltraLiteDB.Tests.AllowList.Data;
using UltraLiteDB.Tests.AllowList.Gadgets;

namespace UltraLiteDB.Tests.AllowList.Data
{
	public class Animal
	{
		public int Id { get; set; }
		public string? Name { get; set; }
	}

	public class Dog : Animal
	{
		public bool GoodBoy { get; set; }
	}

	public class Box<T>
	{
		public int Id { get; set; }
		public T Value { get; set; } = default!;
	}

	public class Zoo
	{
		public int Id { get; set; }
		public Animal? Star { get; set; }
		public object? Extra { get; set; }
		public List<long>? Tickets { get; set; }
	}
}

namespace UltraLiteDB.Tests.AllowList.DataEvil
{
	public class Imposter : UltraLiteDB.Tests.AllowList.Data.Animal
	{
	}
}

namespace UltraLiteDB.Tests.AllowList.Gadgets
{
	/// <summary>
	/// Stands in for any type in the process whose constructor or setters have side effects.
	/// </summary>
	public class Gadget : UltraLiteDB.Tests.AllowList.Data.Animal
	{
		public static int Touched;

		public Gadget() { Touched++; }

		public string? Command { get => null; set { Touched++; } }
	}

	public class UnrelatedGadget
	{
		public UnrelatedGadget() { Gadget.Touched++; }

		public int Id { get; set; }
	}
}

namespace UltraLiteDB.Tests.Mapper
{
	[TestClass]
	public class TypeAllowList_Tests
	{
		private static string Name<T>() => typeof(T).FullName + ", " + typeof(T).Assembly.GetName().Name;

		/// <summary>
		/// Deserializes through both the BsonDocument path and the direct (bytes) path, which have separate
		/// discriminator handling, asserting they agree.
		/// </summary>
		private static void AssertBothPathsThrow<T>(BsonMapper mapper, BsonDocument doc, int errorCode)
		{
			var classic = Assert.ThrowsExactly<UltraLiteException>(() => mapper.ToObject<T>(doc));
			Assert.AreEqual(errorCode, classic.ErrorCode, classic.Message);

			var direct = Assert.ThrowsExactly<UltraLiteException>(() => mapper.DeserializeFromBytes<T>(BsonSerializer.Serialize(doc)));
			Assert.AreEqual(errorCode, direct.ErrorCode, direct.Message);
		}

		private static IEnumerable<T> BothPaths<T>(BsonMapper mapper, BsonDocument doc)
		{
			yield return mapper.ToObject<T>(doc);
			yield return mapper.DeserializeFromBytes<T>(BsonSerializer.Serialize(doc));
		}

		private static BsonDocument ZooWith(string member, BsonDocument value)
		{
			return new BsonDocument { ["_id"] = 1, [member] = value };
		}

		[TestInitialize]
		public void Reset()
		{
			Gadget.Touched = 0;
		}

		#region Rejected

		[TestMethod]
		public void Unallowed_Subtype_Is_Rejected_Before_Construction()
		{
			// Gadget really is an Animal, so only the allow-list stops it
			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<Gadget>(), ["Command"] = "rm -rf ~" });

			AssertBothPathsThrow<Zoo>(new BsonMapper(), doc, UltraLiteException.TYPE_NOT_ALLOWED);
			Assert.AreEqual(0, Gadget.Touched);
		}

		[TestMethod]
		public void Allowed_But_Unassignable_Type_Is_Rejected_Before_Construction()
		{
			var mapper = new BsonMapper().AllowTypes("UltraLiteDB.Tests.AllowList.*");
			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<UnrelatedGadget>(), ["Id"] = 5 });

			AssertBothPathsThrow<Zoo>(mapper, doc, UltraLiteException.TYPE_NOT_ASSIGNABLE);
			Assert.AreEqual(0, Gadget.Touched);
		}

		[TestMethod]
		public void Root_Document_Type_Is_Checked()
		{
			var doc = new BsonDocument { ["_type"] = Name<UnrelatedGadget>(), ["_id"] = 1 };

			AssertBothPathsThrow<Zoo>(new BsonMapper(), doc, UltraLiteException.TYPE_NOT_ALLOWED);
			AssertBothPathsThrow<Zoo>(new BsonMapper().AllowType<UnrelatedGadget>(), doc, UltraLiteException.TYPE_NOT_ASSIGNABLE);
			Assert.AreEqual(0, Gadget.Touched);
		}

		[TestMethod]
		public void Framework_Types_Are_Rejected_In_Object_Members()
		{
			var timer = new BsonDocument { ["_type"] = "System.Timers.Timer, System.ComponentModel.TypeConverter", ["Interval"] = 1, ["Enabled"] = true };
			var list = new BsonDocument { ["_type"] = Name<List<long>>(), ["Capacity"] = 250_000_000 };

			AssertBothPathsThrow<Zoo>(new BsonMapper(), ZooWith("Extra", timer), UltraLiteException.TYPE_NOT_ALLOWED);
			AssertBothPathsThrow<Zoo>(new BsonMapper(), ZooWith("Extra", list), UltraLiteException.TYPE_NOT_ALLOWED);
		}

		[TestMethod]
		public void Rejected_Names_Are_Not_Resolved()
		{
			const string assembly = "System.Net.Mail";
			bool Loaded() => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == assembly);

			if (Loaded()) Assert.Inconclusive(assembly + " was already loaded");

			var doc = ZooWith("Extra", new BsonDocument { ["_type"] = "System.Net.Mail.SmtpClient, " + assembly, ["Host"] = "example.com" });

			AssertBothPathsThrow<Zoo>(new BsonMapper(), doc, UltraLiteException.TYPE_NOT_ALLOWED);
			Assert.IsFalse(Loaded(), "a rejected _type must not load its assembly");
		}

		[TestMethod]
		public void Generic_Arguments_Must_Be_Allowed()
		{
			var mapper = new BsonMapper().AllowType(typeof(Box<>));

			// Box<T> is allowed, but T picks the declared type of Box.Value, so T must be allowed too
			var doc = ZooWith("Extra", new BsonDocument { ["_type"] = Name<Box<Gadget>>(), ["Value"] = new BsonDocument { ["Command"] = "x" } });

			AssertBothPathsThrow<Zoo>(mapper, doc, UltraLiteException.TYPE_NOT_ALLOWED);
			AssertBothPathsThrow<Zoo>(mapper.AllowType<Dog>(), ZooWith("Extra", new BsonDocument { ["_type"] = Name<Box<List<Gadget>>>() }), UltraLiteException.TYPE_NOT_ALLOWED);
			Assert.AreEqual(0, Gadget.Touched);
		}

		[TestMethod]
		public void Pattern_Does_Not_Match_Sibling_Namespace()
		{
			var mapper = new BsonMapper().AllowTypes("UltraLiteDB.Tests.AllowList.Data.*");

			Assert.IsTrue(mapper.IsTypeAllowed(typeof(Dog)));
			Assert.IsFalse(mapper.IsTypeAllowed(typeof(UltraLiteDB.Tests.AllowList.DataEvil.Imposter)));

			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<UltraLiteDB.Tests.AllowList.DataEvil.Imposter>() });

			AssertBothPathsThrow<Zoo>(mapper, doc, UltraLiteException.TYPE_NOT_ALLOWED);
		}

		[TestMethod]
		public void Document_Is_Not_Read_Into_Collection()
		{
			// no _type needed: a List<T> member given a document would otherwise have its Capacity set
			var doc = ZooWith("Tickets", new BsonDocument { ["Capacity"] = 250_000_000 });

			AssertBothPathsThrow<Zoo>(new BsonMapper(), doc, UltraLiteException.INVALID_DATA_TYPE);
			AssertBothPathsThrow<Zoo>(new BsonMapper(), ZooWith("Tickets", new BsonDocument()), UltraLiteException.INVALID_DATA_TYPE);
		}

		[TestMethod]
		public void Type_Id_Must_Be_Assignable()
		{
			var mapper = new BsonMapper();
			mapper.RegisterTypeId(typeof(UnrelatedGadget), "gadget");

			AssertBothPathsThrow<Zoo>(mapper, ZooWith("Star", new BsonDocument { ["_t"] = "gadget" }), UltraLiteException.TYPE_NOT_ASSIGNABLE);
			AssertBothPathsThrow<Zoo>(mapper, ZooWith("Star", new BsonDocument { ["_t"] = "nope" }), UltraLiteException.INVALID_TYPED_NAME);
			Assert.AreEqual(0, Gadget.Touched);
		}

		[TestMethod]
		public void Non_String_Type_Name_Is_Rejected()
		{
			AssertBothPathsThrow<Zoo>(new BsonMapper(), ZooWith("Star", new BsonDocument { ["_type"] = 42 }), UltraLiteException.INVALID_TYPED_NAME);
		}

		#endregion

		#region Allowed

		[TestMethod]
		public void AllowType_Reads_Derived_And_Object_Members()
		{
			var mapper = new BsonMapper().AllowType<Dog>();
			var zoo = new Zoo { Id = 1, Star = new Dog { Id = 2, Name = "Rex", GoodBoy = true }, Extra = new Dog { Id = 3 } };

			foreach (var result in BothPaths<Zoo>(mapper, mapper.ToDocument(zoo)))
			{
				Assert.IsInstanceOfType<Dog>(result.Star);
				Assert.IsTrue(((Dog)result.Star!).GoodBoy);
				Assert.IsInstanceOfType<Dog>(result.Extra);
			}

			var direct = mapper.DeserializeFromBytes<Zoo>(mapper.SerializeToBytes(zoo));
			Assert.IsInstanceOfType<Dog>(direct.Star);
		}

		[TestMethod]
		public void AllowTypes_Pattern_Reads_Derived_Types()
		{
			var mapper = new BsonMapper().AllowTypes("UltraLiteDB.Tests.AllowList.Data.*");

			using (var db = new UltraLiteDatabase(new MemoryStream(), mapper))
			{
				var col = db.GetCollection<Animal>("animals");
				col.Insert(new Dog { Id = 1, Name = "Rex", GoodBoy = true });

				var dog = col.FindById(1);
				Assert.IsInstanceOfType<Dog>(dog);
				Assert.IsTrue(((Dog)dog).GoodBoy);
			}
		}

		[TestMethod]
		public void Generic_Type_With_Allowed_Arguments_Is_Read()
		{
			var mapper = new BsonMapper().AllowTypes("UltraLiteDB.Tests.AllowList.Data.*");

			var ints = new Zoo { Id = 1, Extra = new Box<int> { Id = 2, Value = 7 } };
			var dogs = new Zoo { Id = 1, Extra = new Box<List<Dog>> { Id = 2, Value = new List<Dog> { new Dog { Name = "Rex" } } } };

			foreach (var result in BothPaths<Zoo>(mapper, mapper.ToDocument(ints)))
			{
				Assert.AreEqual(7, ((Box<int>)result.Extra!).Value);
			}

			foreach (var result in BothPaths<Zoo>(mapper, mapper.ToDocument(dogs)))
			{
				Assert.AreEqual("Rex", ((Box<List<Dog>>)result.Extra!).Value.Single().Name);
			}
		}

		[TestMethod]
		public void Registered_Type_Id_Is_Also_Allowed_By_Name()
		{
			var mapper = new BsonMapper();
			mapper.RegisterTypeId(typeof(Dog), "dog");

			// e.g. data written before the id was registered
			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<Dog>(), ["GoodBoy"] = true });

			foreach (var result in BothPaths<Zoo>(mapper, doc))
			{
				Assert.IsTrue(((Dog)result.Star!).GoodBoy);
			}
		}

		[TestMethod]
		public void AllowTypeFilter_Allows_And_Can_Be_Revoked()
		{
			var mapper = new BsonMapper();
			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<Dog>(), ["GoodBoy"] = true });

			mapper.AllowTypeFilter = t => t.Assembly == typeof(Dog).Assembly && t.Namespace == typeof(Dog).Namespace;

			foreach (var result in BothPaths<Zoo>(mapper, doc))
			{
				Assert.IsInstanceOfType<Dog>(result.Star);
			}

			// the resolved name was cached: replacing the filter must drop it
			mapper.AllowTypeFilter = null;

			AssertBothPathsThrow<Zoo>(mapper, doc, UltraLiteException.TYPE_NOT_ALLOWED);
		}

		[TestMethod]
		public void Naming_The_Declared_Type_Needs_No_Allowance()
		{
			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<Animal>(), ["Name"] = "Rex" });

			foreach (var result in BothPaths<Zoo>(new BsonMapper(), doc))
			{
				Assert.AreEqual(typeof(Animal), result.Star!.GetType());
				Assert.AreEqual("Rex", result.Star.Name);
			}
		}

		[TestMethod]
		public void Object_Member_Without_Type_Reads_As_Dictionary()
		{
			var doc = ZooWith("Extra", new BsonDocument { ["A"] = 1 });

			foreach (var result in BothPaths<Zoo>(new BsonMapper(), doc))
			{
				Assert.AreEqual(1, ((Dictionary<string, object>)result.Extra!)["A"]);
			}
		}

		#endregion

		#region Concurrency

		[TestMethod]
		public void Concurrent_Allow_Calls_Are_Not_Lost()
		{
			var mapper = new BsonMapper();

			// each type is allowed exactly once, so a lost update would leave one of them disallowed
			var boxes = new List<Type> { typeof(int) };
			for (var i = 0; i < 200; i++) boxes.Add(typeof(Box<>).MakeGenericType(boxes[boxes.Count - 1]));
			boxes.RemoveAt(0);

			var named = new[] { typeof(Animal), typeof(Dog), typeof(Zoo), typeof(Gadget), typeof(UnrelatedGadget), typeof(UltraLiteDB.Tests.AllowList.DataEvil.Imposter) };

			Parallel.For(0, boxes.Count + named.Length, i =>
			{
				if (i < boxes.Count) mapper.AllowType(boxes[i]);
				else mapper.AllowTypes(named[i - boxes.Count].FullName!);
			});

			foreach (var type in boxes.Concat(named))
			{
				Assert.IsTrue(mapper.IsTypeAllowed(type), type.FullName);
			}
		}

		[TestMethod]
		public void Allow_List_Changes_During_Deserialization_Never_Tear()
		{
			// Dog stays allowed throughout, so every read must succeed however the other changes interleave
			var mapper = new BsonMapper().AllowType<Dog>();
			var doc = ZooWith("Star", new BsonDocument { ["_type"] = Name<Dog>(), ["GoodBoy"] = true });
			var bytes = BsonSerializer.Serialize(doc);

			using var stop = new CancellationTokenSource();

			var writer = Task.Run(() =>
			{
				for (var i = 0; !stop.IsCancellationRequested; i++)
				{
					mapper.AllowTypeFilter = i % 2 == 0 ? t => t == typeof(Animal) : null;
					mapper.AllowTypes("Unrelated" + (i % 16) + ".*");
				}
			});

			Parallel.For(0, 4, _ =>
			{
				for (var i = 0; i < 2000; i++)
				{
					Assert.IsTrue(((Dog)mapper.DeserializeFromBytes<Zoo>(bytes).Star!).GoodBoy);
					Assert.IsTrue(((Dog)mapper.ToObject<Zoo>(doc).Star!).GoodBoy);
				}
			});

			stop.Cancel();
			writer.Wait();
		}

		#endregion
	}
}

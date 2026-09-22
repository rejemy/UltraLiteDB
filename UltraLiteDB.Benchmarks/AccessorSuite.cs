#nullable enable

using System;
using System.Reflection;

namespace UltraLiteDB.Benchmarks
{
	/// <summary>
	/// Per-member cost of the ways the mapper can read/write a property without runtime code generation
	/// (no Reflection.Emit / Expression.Compile, which Unity IL2CPP can't run). The "library" rows measure
	/// whatever Reflection.CreateGenericGetter/Setter currently build, so they track changes to the mapper.
	/// </summary>
	public static class AccessorSuite
	{
		public class Sample
		{
			public int Id { get; set; }
			public float Weight { get; set; }
			public bool Equipped { get; set; }
			public ItemKind Kind { get; set; }
		}

		/// <summary>
		/// Typed open-instance delegates over the property accessors, wrapped behind an object-based API: the same
		/// technique as the library's own TypedAccessor. Here the instantiations are written in code, so they're
		/// AOT-safe; the library builds them with MakeGenericType at runtime (see the MakeGenericType rows).
		/// </summary>
		public abstract class Accessor
		{
			public abstract object? Get(object target);
			public abstract void Set(object target, object? value);
		}

		public sealed class Accessor<TTarget, TValue> : Accessor where TTarget : class
		{
			private readonly Func<TTarget, TValue> _getter;
			private readonly Action<TTarget, TValue> _setter;

			public Accessor(PropertyInfo property)
			{
				_getter = (Func<TTarget, TValue>)Delegate.CreateDelegate(typeof(Func<TTarget, TValue>), property.GetGetMethod(true)!);
				_setter = (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), property.GetSetMethod(true)!);
			}

			public override object? Get(object target) => _getter((TTarget)target);

			public TValue GetTyped(object target) => _getter((TTarget)target);

			public override void Set(object target, object? value) => _setter((TTarget)target, (TValue)value!);
		}

		private const int MEMBERS = 3;
		private const int LOOPS = 100;

		public static void Run(double seconds, Func<string, bool> include)
		{
			var sample = new Sample { Id = 5, Weight = 1.5f, Equipped = true };
			var type = typeof(Sample);
			var properties = new[] { type.GetProperty("Id")!, type.GetProperty("Weight")!, type.GetProperty("Equipped")! };

			var getMethods = new MethodInfo[MEMBERS];
			var setMethods = new MethodInfo[MEMBERS];
			var backingFields = new FieldInfo[MEMBERS];

			for (var i = 0; i < MEMBERS; i++)
			{
				getMethods[i] = properties[i].GetGetMethod()!;
				setMethods[i] = properties[i].GetSetMethod()!;
				backingFields[i] = type.GetField("<" + properties[i].Name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
			}

			var idAccessor = new Accessor<Sample, int>(properties[0]);
			var accessors = new Accessor[] { idAccessor, new Accessor<Sample, float>(properties[1]), new Accessor<Sample, bool>(properties[2]) };

			// what the mapper actually uses today
			var members = new BsonMapper().GetEntityMapper(type).Members;
			var libraryGetters = new GenericGetter[MEMBERS];
			var librarySetters = new GenericSetter[MEMBERS];

			for (var i = 0; i < MEMBERS; i++)
			{
				var member = members.Find(m => m.MemberName == properties[i].Name)!;
				libraryGetters[i] = member.Getter!;
				librarySetters[i] = member.Setter!;
			}

			// The library builds its typed accessors with MakeGenericType at runtime. Accessor<Sample, ItemKind> appears
			// nowhere in code, so this works only where the runtime can create new generic instantiations over value
			// types: JIT yes, NativeAOT no, IL2CPP only with full generic sharing (Unity 2022.1+). These rows show it.
			var runtimeAccessor = TryCreateRuntimeAccessor(out var runtimeError);

			var boxedValues = new object[] { 5, 1.5f, true };
			object boxedKind = ItemKind.Potion;
			var reusedArgs = new object?[1];

			Console.WriteLine($"Per-member accessor cost ({MEMBERS} value-type auto-properties: int, float, bool):");

			Bench("get: library GenericGetter (current)", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) libraryGetters[i](sample); });
			Bench("get: MethodInfo.Invoke", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) getMethods[i].Invoke(sample, null); });
			Bench("get: backing FieldInfo.GetValue", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) backingFields[i].GetValue(sample); });
			Bench("get: typed delegate, boxed result", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) accessors[i].Get(sample); });
			if (runtimeAccessor != null)
			{
				Bench("get: typed delegate via MakeGenericType", () => { for (var n = 0; n < LOOPS * MEMBERS; n++) runtimeAccessor.Get(sample); });
			}
			else if (include("get: typed delegate via MakeGenericType"))
			{
				Console.WriteLine($"  get: typed delegate via MakeGenericType              unavailable: {runtimeError}");
			}
			Bench("get: typed delegate, unboxed (ceiling)", () => { var sum = 0; for (var n = 0; n < LOOPS * MEMBERS; n++) sum += idAccessor.GetTyped(sample); });

			Bench("set: library GenericSetter (current)", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) librarySetters[i](sample, boxedValues[i]); });
			Bench("set: MethodInfo.Invoke, new args array", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) setMethods[i].Invoke(sample, new[] { boxedValues[i] }); });
			Bench("set: MethodInfo.Invoke, reused args", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) { reusedArgs[0] = boxedValues[i]; setMethods[i].Invoke(sample, reusedArgs); } });
			Bench("set: backing FieldInfo.SetValue", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) backingFields[i].SetValue(sample, boxedValues[i]); });
			Bench("set: typed delegate from boxed", () => { for (var n = 0; n < LOOPS; n++) for (var i = 0; i < MEMBERS; i++) accessors[i].Set(sample, boxedValues[i]); });

			if (runtimeAccessor != null)
			{
				Bench("set: typed delegate via MakeGenericType", () => { for (var n = 0; n < LOOPS * MEMBERS; n++) runtimeAccessor.Set(sample, boxedKind); });
			}
			else if (include("set: typed delegate via MakeGenericType"))
			{
				Console.WriteLine($"  set: typed delegate via MakeGenericType              unavailable: {runtimeError}");
			}

			// Deserialize also creates every object through reflection (the mapper's TypeInstantiator uses
			// Activator.CreateInstance). These rows price that against a generic `new T()` factory.
			var createGeneric = CreateFactory(type);
			var sink = new object[1]; // keeps the allocation from being optimized away
			Console.WriteLine();
			Console.WriteLine("Per-object instantiation cost (the mapper creates each object, list and dictionary it deserializes):");
			Create("create: Activator.CreateInstance(Type) (current)", () => { for (var n = 0; n < LOOPS; n++) Activator.CreateInstance(type); });
			if (createGeneric != null)
			{
				Create("create: generic new T() via MakeGenericMethod", () => { for (var n = 0; n < LOOPS; n++) createGeneric(); });
			}
			Create("create: new Sample() (ceiling)", () => { for (var n = 0; n < LOOPS; n++) sink[0] = new Sample(); });

			void Bench(string name, Action action)
			{
				if (include(name)) Harness.Run(name, action, seconds, LOOPS * MEMBERS, "member");
			}

			void Create(string name, Action action)
			{
				if (include(name)) Harness.Run(name, action, seconds, LOOPS, "object");
			}
		}

		private static T New<T>() where T : new() => new T();

		/// <summary>
		/// A Func&lt;object&gt; over New&lt;T&gt;() built at runtime (reference-type T, so the return type is covariant).
		/// </summary>
		private static Func<object>? CreateFactory(Type type)
		{
			try
			{
				var method = typeof(AccessorSuite).GetMethod(nameof(New), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type);
				return (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), method);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Accessor? TryCreateRuntimeAccessor(out string? error)
		{
			try
			{
				var property = typeof(Sample).GetProperty("Kind")!;
				var accessorType = typeof(Accessor<,>).MakeGenericType(typeof(Sample), property.PropertyType);
				var accessor = (Accessor)Activator.CreateInstance(accessorType, property)!;

				var probe = new Sample();
				accessor.Set(probe, ItemKind.Junk);

				if (probe.Kind != ItemKind.Junk || !ItemKind.Junk.Equals(accessor.Get(probe)))
				{
					throw new InvalidOperationException("round trip returned the wrong value");
				}

				error = null;
				return accessor;
			}
			catch (Exception e)
			{
				var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
				error = inner.GetType().Name + ": " + inner.Message;
				return null;
			}
		}
	}
}

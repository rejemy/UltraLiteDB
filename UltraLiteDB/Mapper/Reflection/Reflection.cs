using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace UltraLiteDB
{
	#region Delegates

	/// <summary>Factory delegate for parameterless object creation.</summary>
	internal delegate object CreateObject();

	/// <summary>Delegate that sets a member value on a target object.</summary>
	public delegate void GenericSetter(object target, object? value);

	/// <summary>Delegate that gets a member value from a source object.</summary>
	public delegate object? GenericGetter(object obj);

	#endregion

	/// <summary>
	/// Reflection utilities for creating instances, building property getters/setters,
	/// and inspecting generic type information used by <see cref="BsonMapper"/>.
	/// </summary>
	internal class Reflection
	{
		private static Dictionary<Type, CreateObject> _cacheCtor = new Dictionary<Type, CreateObject>();

		#region CreateInstance

		/// <summary>
		/// Creates an instance of the given type using a cached parameterless constructor delegate.
		/// Handles classes, structs, and known generic interfaces (IList, IDictionary, IEnumerable).
		/// </summary>
		public static object CreateInstance(Type type)
		{
			try
			{
				if (_cacheCtor.TryGetValue(type, out CreateObject c))
				{
					return c();
				}
			}
			catch (Exception ex)
			{
				throw UltraLiteException.InvalidCtor(type, ex);
			}

			lock (_cacheCtor)
			{
				try
				{
					if (_cacheCtor.TryGetValue(type, out CreateObject c))
					{
						return c();
					}

					if (type.GetTypeInfo().IsClass)
					{
						_cacheCtor.Add(type, c = CreateClass(type));
					}
					else if (type.GetTypeInfo().IsInterface) // some know interfaces
					{
						if (type.GetTypeInfo().IsGenericType)
						{
							var typeDef = type.GetGenericTypeDefinition();

							if (typeDef == typeof(IList<>) ||
								typeDef == typeof(ICollection<>) ||
								typeDef == typeof(IEnumerable<>) ||
								typeDef == typeof(IReadOnlyList<>) ||
								typeDef == typeof(IReadOnlyCollection<>))
							{
								// List<T> implements all of the above read-only interfaces
								return CreateInstance(GetGenericListOfType(UnderlyingTypeOf(type)));
							}
							else if (typeDef == typeof(IDictionary<,>) ||
								typeDef == typeof(IReadOnlyDictionary<,>))
							{
								// Dictionary<K,V> implements IReadOnlyDictionary<K,V> too
								var k = type.GetTypeInfo().GetGenericArguments()[0];
								var v = type.GetTypeInfo().GetGenericArguments()[1];

								return CreateInstance(GetGenericDictionaryOfType(k, v));
							}
						}

						throw UltraLiteException.InvalidCtor(type, null);
					}
					else // structs
					{
						_cacheCtor.Add(type, c = CreateStruct(type));
					}

					return c();
				}
				catch (Exception ex)
				{
					throw UltraLiteException.InvalidCtor(type, ex);
				}
			}
		}

		#endregion

		#region Utils

		/// <summary>Returns true if the type is <see cref="Nullable{T}"/>.</summary>
		public static bool IsNullable(Type type)
		{
			if (!type.GetTypeInfo().IsGenericType) return false;
			var g = type.GetGenericTypeDefinition();
			return (g.Equals(typeof(Nullable<>)));
		}

		/// <summary>
		/// Gets the first generic type argument (e.g. <c>int</c> from <c>Nullable&lt;int&gt;</c>).
		/// Returns the type unchanged if it is not generic.
		/// </summary>
		public static Type UnderlyingTypeOf(Type type)
		{
			// works only for generics (if type is not generic, returns same type)
			var t = type.GetTypeInfo();

			if (!type.GetTypeInfo().IsGenericType) return type;

			return type.GetTypeInfo().GetGenericArguments()[0];
		}

		/// <summary>Constructs <c>List&lt;T&gt;</c> for the given element type.</summary>
		public static Type GetGenericListOfType(Type type)
		{
			var listType = typeof(List<>);
			return listType.MakeGenericType(type);
		}

		/// <summary>Constructs <c>Dictionary&lt;K,V&gt;</c> for the given key and value types.</summary>
		public static Type GetGenericDictionaryOfType(Type k, Type v)
		{
			var listType = typeof(Dictionary<,>);
			return listType.MakeGenericType(k, v);
		}

		/// <summary>
		/// Gets the element type from an array type or the <c>T</c> from <c>IEnumerable&lt;T&gt;</c>.
		/// Returns <c>typeof(object)</c> if no generic enumerable interface is found.
		/// </summary>
		public static Type GetListItemType(Type listType)
		{
			if (listType.IsArray) return listType.GetElementType();

			foreach (var i in listType.GetInterfaces())
			{
				if (i.GetTypeInfo().IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				{
					return i.GetTypeInfo().GetGenericArguments()[0];
				}
				// if interface is IEnumerable (non-generic), let's get from listType and not from interface
				// from #395
				else if (listType.GetTypeInfo().IsGenericType && i == typeof(IEnumerable))
				{
					return listType.GetTypeInfo().GetGenericArguments()[0];
				}
			}

			return typeof(object);
		}

		/// <summary>
		/// Returns true if the type implements <c>IEnumerable&lt;T&gt;</c> (excluding <c>string</c> and <c>BsonDocument</c>).
		/// </summary>
		public static bool IsList(Type type)
		{
			if (type.IsArray) return true;
			if (type == typeof(string) || type == typeof(BsonDocument)) return false; // do not define "String" as IEnumerable<char>

			foreach (var @interface in type.GetInterfaces())
			{
				if (@interface.GetTypeInfo().IsGenericType)
				{
					if (@interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
					{
						// if needed, you can also return the type used as generic argument
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Returns true for enumerable types the mapper stores as BSON arrays, so never reads from a document:
		/// everything enumerable except strings, BSON values and dictionaries (<see cref="IDictionary"/>,
		/// <c>IDictionary&lt;,&gt;</c>, <c>IReadOnlyDictionary&lt;,&gt;</c>).
		/// </summary>
		public static bool IsCollectionType(Type type)
		{
			if (!typeof(IEnumerable).IsAssignableFrom(type)) return false;
			if (type == typeof(string) || typeof(BsonValue).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type)) return false;

			if (IsGenericDictionaryInterface(type)) return false;

			foreach (var @interface in type.GetInterfaces())
			{
				if (IsGenericDictionaryInterface(@interface)) return false;
			}

			return true;
		}

		/// <summary>Returns true if the type is <c>IDictionary&lt;,&gt;</c> or <c>IReadOnlyDictionary&lt;,&gt;</c>.</summary>
		public static bool IsGenericDictionaryInterface(Type type)
		{
			if (!type.GetTypeInfo().IsGenericType) return false;

			var definition = type.GetGenericTypeDefinition();

			return definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>);
		}

		/// <summary>
		/// Returns the first member matching any of the predicates, evaluated in order of priority.
		/// Used to resolve ID members by convention (e.g. "Id", "TypeNameId", "_id").
		/// </summary>
		public static MemberInfo? SelectMember(IEnumerable<MemberInfo> members, params Func<MemberInfo, bool>[] predicates)
		{
			foreach (var predicate in predicates)
			{
				var member = members.FirstOrDefault(predicate);

				if (member != null)
				{
					return member;
				}
			}

			return null;
		}

		#endregion

		/// <summary>Creates a <see cref="CreateObject"/> factory for a class type using <see cref="Activator"/>.</summary>
		public static CreateObject CreateClass(Type type)
		{
			return () => Activator.CreateInstance(type);
		}

		/// <summary>Creates a <see cref="CreateObject"/> factory for a struct type using <see cref="Activator"/>.</summary>
		public static CreateObject CreateStruct(Type type)
		{
			return () => Activator.CreateInstance(type);
		}

		/// <summary>
		/// Creates a getter delegate for a field or property. Returns null if the property has no get accessor.
		/// </summary>
		public static GenericGetter? CreateGenericGetter(Type type, MemberInfo memberInfo)
		{
			// when member is a field, use simple Reflection
			if (memberInfo is FieldInfo)
			{
				var fieldInfo = (FieldInfo)memberInfo;

				return fieldInfo.GetValue;
			}

			var propertyInfo = (PropertyInfo)memberInfo;
			var getMethod = propertyInfo.GetGetMethod(true);

			if (getMethod == null) return null;

			// fastest: a typed delegate bound to the getter, when this runtime supports building one
			var typed = CreateTypedAccessor(type, propertyInfo, getMethod, null, null);

			if (typed != null) return typed.Get;

			// auto-property: read the backing field directly (identical result, much cheaper than MethodInfo.Invoke)
			var backingField = GetAutoPropertyBackingField(propertyInfo, getMethod);

			if (backingField != null) return backingField.GetValue;

			return target => getMethod.Invoke(target, null);
		}

		/// <summary>
		/// Creates a setter delegate for a field or property. Returns null if the property has no set accessor.
		/// Includes special handling for <c>byte[]</c> members that receive <c>ArraySegment&lt;byte&gt;</c> values.
		/// </summary>
		public static GenericSetter? CreateGenericSetter(Type type, MemberInfo memberInfo)
		{

			// when member is a field, use simple Reflection
			if (memberInfo is FieldInfo)
			{
				var fieldInfo = (FieldInfo)memberInfo;

				if (fieldInfo.FieldType == typeof(byte[]))
				{
					// Special setter for byte arrays
					return (target, value) => fieldInfo.SetValue(target, ((ArraySegment<byte>)value!).Array);
				}
				else
				{
					return fieldInfo.SetValue;
				}

			}

			var propertyInfo = (PropertyInfo)memberInfo;

			var setMethod = propertyInfo.GetSetMethod(true);

			if (setMethod == null) return null;

			// reflection setter: an auto-property writes its backing field directly (identical effect, no
			// MethodInfo.Invoke and no argument array); init-only backing fields are left to the accessor
			GenericSetter reflectionSetter;
			var backingField = GetAutoPropertyBackingField(propertyInfo, setMethod);

			if (backingField != null && !backingField.IsInitOnly)
			{
				reflectionSetter = backingField.SetValue;
			}
			else
			{
				reflectionSetter = (target, value) => InvokeSetter(setMethod, target, value);
			}

			// fastest: a typed delegate bound to the setter, when this runtime supports building one. Values that
			// aren't exactly the property type still go through the reflection setter, which converts them.
			var typed = CreateTypedAccessor(type, propertyInfo, null, setMethod, reflectionSetter);
			var setter = typed != null ? typed.Set : reflectionSetter;

			if (propertyInfo.PropertyType == typeof(byte[]))
			{
				// Special setter for byte arrays
				return (target, value) => setter(target, ((ArraySegment<byte>)value!).Array);
			}

			return setter;
		}

		#region Typed accessors

		/// <summary>
		/// Switch for the typed-accessor strategy, on by default. It exists so tests can exercise the reflection
		/// fallback that runtimes without runtime generic instantiation (NativeAOT, older IL2CPP) use. Only
		/// affects entity mappers built after it changes.
		/// </summary>
		internal static bool TypedAccessorsEnabled = true;

		/// <summary>
		/// Whether this runtime can build <see cref="TypedAccessor{TOwner, TValue}"/> instantiations with
		/// MakeGenericType and actually run them. Probed once, on first use, with a value type that appears in
		/// no generic instantiation in code, through a virtual (interface) accessor — the demanding cases — plus
		/// the unboxed primitive calls. An AOT compiler that precompiles statically visible instantiations (NativeAOT)
		/// can pass the probe while lacking others, so each accessor is also verified when it is created.
		/// </summary>
		internal static bool TypedAccessorsSupported => TypedAccessorSupport.Available;

		private static class TypedAccessorSupport
		{
			public static readonly bool Available = Probe();

			private static bool Probe()
			{
				try
				{
					var property = typeof(ProbeOwner).GetProperty(nameof(ProbeOwner.Value))!;
					var accessorType = typeof(TypedAccessor<,>).MakeGenericType(typeof(ProbeOwner), typeof(ProbeValue));
					var accessor = (TypedAccessor)Activator.CreateInstance(accessorType, property.GetGetMethod(true), property.GetSetMethod(true), null)!;

					var owner = new ProbeOwner();
					accessor.Set(owner, new ProbeValue { A = 0x1234567890L, B = 42 });
					var value = (ProbeValue)accessor.Get(owner)!;

					// and the unboxed primitive path the direct serializer uses
					var number = typeof(ProbeOwner).GetProperty(nameof(ProbeOwner.Number))!;
					var numberAccessor = (TypedAccessor)Activator.CreateInstance(
						typeof(TypedAccessor<,>).MakeGenericType(typeof(ProbeOwner), typeof(int)), number.GetGetMethod(true), number.GetSetMethod(true), null)!;
					numberAccessor.SetInt32(owner, 7);

					return value.A == 0x1234567890L && value.B == 42 && owner.Value.B == 42 &&
						numberAccessor.Primitive == DirectKind.Int32 && numberAccessor.GetInt32(owner) == 7;
				}
				catch (Exception)
				{
					// NotSupportedException (NativeAOT), ExecutionEngineException (IL2CPP without full generic
					// sharing), a stripped member, ...: use plain reflection everywhere
					return false;
				}
			}
		}

		[Preserve]
		private interface IProbeOwner
		{
			ProbeValue Value { get; set; }
		}

		[Preserve]
		private sealed class ProbeOwner : IProbeOwner
		{
			[Preserve]
			public ProbeValue Value { get; set; }

			[Preserve]
			public int Number { get; set; }
		}

		[Preserve]
		private struct ProbeValue
		{
			public long A;
			public int B;
		}

		/// <summary>
		/// Builds a typed accessor for a property getter or setter, or returns null when this member or runtime
		/// can't use one (the caller then uses reflection).
		/// </summary>
		private static TypedAccessor? CreateTypedAccessor(Type ownerType, PropertyInfo property, MethodInfo? getMethod, MethodInfo? setMethod, GenericSetter? convertingSetter)
		{
			if (!TypedAccessorsEnabled || !TypedAccessorsSupported) return null;

			var method = getMethod ?? setMethod!;
			var valueType = property.PropertyType;

			// Class owners only: a struct's accessors need a reference to the boxed value, which the object-based
			// setter shape can't express. Non-virtual (or sealed) accessors only, so dispatch is unchanged.
			if (ownerType.IsValueType || ownerType.ContainsGenericParameters || method.IsStatic) return null;
			if (method.IsVirtual && !method.IsFinal) return null;
			if (valueType.IsByRef || valueType.IsPointer || valueType.ContainsGenericParameters) return null;

			try
			{
				var accessorType = typeof(TypedAccessor<,>).MakeGenericType(ownerType, valueType);
				var accessor = (TypedAccessor)Activator.CreateInstance(accessorType, getMethod, setMethod, convertingSetter)!;

				// the probe proves the runtime can do this in general; an AOT compiler may still have precompiled some
				// instantiations and not others (NativeAOT does exactly that), so check this one before relying on it
				return accessor.Verify() ? accessor : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		#endregion

		/// <summary>
		/// Returns the compiler-generated backing field of an auto-property accessor, or null if the accessor
		/// has a user-written body (so it may contain logic) or the backing field can't be found.
		/// </summary>
		private static FieldInfo? GetAutoPropertyBackingField(PropertyInfo propertyInfo, MethodInfo accessor)
		{
			if (!accessor.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) return null;

			var field = propertyInfo.DeclaringType?.GetField("<" + propertyInfo.Name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

			return field != null && field.FieldType == propertyInfo.PropertyType ? field : null;
		}

		/// <summary>
		/// Reusable single-element argument array for <see cref="InvokeSetter"/>. Taken out of the slot while
		/// in use, so a setter that re-enters the mapper on the same thread just gets a fresh array.
		/// </summary>
		[ThreadStatic]
		private static object?[]? _setterArgs;

		private static void InvokeSetter(MethodInfo setMethod, object target, object? value)
		{
			var args = _setterArgs ?? new object?[1];
			_setterArgs = null;

			args[0] = value;
			setMethod.Invoke(target, args);
			args[0] = null;

			_setterArgs = args;
		}

	}
}

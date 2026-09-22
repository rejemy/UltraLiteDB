using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace UltraLiteDB
{
	/// <summary>
	/// Controls which CLR types a document's <c>_type</c> discriminator may name.
	/// </summary>
	/// <remarks>
	/// <c>_type</c> holds a type name the mapper instantiates and populates through its setters, so a document from
	/// an untrusted source (a save file, a synced database, JSON from the network) could otherwise name any type
	/// loaded in the process. Only types that are allowed here, registered with
	/// <see cref="RegisterTypeId"/>, or exactly the declared type are accepted, and every resolved type must be
	/// assignable to the member or root type it is read into. <c>_t</c> ids only ever resolve to registered types.
	/// </remarks>
	public partial class BsonMapper
	{
		/// <summary>Allowed type names are cached up to this many entries per allow-list version.</summary>
		private const int MAX_CACHED_TYPE_NAMES = 256;

		/// <summary>
		/// Immutable snapshot of the allow-list, plus the <c>_type</c> names already resolved under it. Every change
		/// publishes a new snapshot, so checks never lock and each sees one consistent version; a change made while
		/// another thread is deserializing takes effect for that thread at some point after it is published.
		/// </summary>
		private sealed class AllowList
		{
			public static readonly AllowList Empty = new AllowList(new HashSet<Type>(), new HashSet<string>(StringComparer.Ordinal), Array.Empty<string>(), null);

			/// <summary>Types allowed by <see cref="AllowType(Type)"/>.</summary>
			public readonly HashSet<Type> Types;

			/// <summary>
			/// Full names (generic definition names for generic types) of allowed and registered types, so a
			/// <c>_type</c> name can be rejected before it is resolved.
			/// </summary>
			public readonly HashSet<string> Names;

			/// <summary>Patterns added by <see cref="AllowTypes(string)"/>.</summary>
			public readonly string[] Patterns;

			public readonly Func<Type, bool>? Filter;

			/// <summary><c>_type</c> names already resolved to a type this allow-list allows.</summary>
			public readonly Dictionary<string, Type> Resolved;

			public AllowList(HashSet<Type> types, HashSet<string> names, string[] patterns, Func<Type, bool>? filter)
				: this(types, names, patterns, filter, new Dictionary<string, Type>(StringComparer.Ordinal))
			{
			}

			private AllowList(HashSet<Type> types, HashSet<string> names, string[] patterns, Func<Type, bool>? filter, Dictionary<string, Type> resolved)
			{
				this.Types = types;
				this.Names = names;
				this.Patterns = patterns;
				this.Filter = filter;
				this.Resolved = resolved;
			}

			public AllowList WithResolved(string name, Type type)
			{
				return new AllowList(this.Types, this.Names, this.Patterns, this.Filter, new Dictionary<string, Type>(this.Resolved, StringComparer.Ordinal) { [name] = type });
			}
		}

		/// <summary>If true, allow all types. Only use for trusted data. If true, bypasses allow list.</summary>
		public bool AllowAllTypes { get; set;}

		/// <summary>Current allow-list. Read with <see cref="Volatile.Read{T}(ref T)"/>; replaced, never mutated.</summary>
		private AllowList _allowList = AllowList.Empty;

		/// <summary>Serializes allow-list changes, so concurrent changes can't lose one another. Checks don't take it.</summary>
		private readonly object _allowLock = new object();

		/// <summary>
		/// Generic collection definitions allowed as type arguments of an allowed type when their own type arguments are
		/// allowed. A document can never populate a collection's own properties (collections are only read from BSON arrays).
		/// </summary>
		private static readonly HashSet<Type> _allowedContainerTypes = new HashSet<Type>
		{
			typeof(Nullable<>),
			typeof(List<>),
			typeof(HashSet<>),
			typeof(Dictionary<,>),
			typeof(IEnumerable<>),
			typeof(ICollection<>),
			typeof(IList<>),
			typeof(ISet<>),
			typeof(IReadOnlyCollection<>),
			typeof(IReadOnlyList<>),
			typeof(IDictionary<,>),
			typeof(IReadOnlyDictionary<,>),
		};


		/// <summary>
		/// Allows documents to name <typeparamref name="T"/> in their <c>_type</c> field, so it can be read into a
		/// member or collection of a base class, interface, or <c>object</c> type.
		/// </summary>
		public BsonMapper AllowType<T>()
		{
			return this.AllowType(typeof(T));
		}

		/// <summary>
		/// Allows documents to name <paramref name="type"/> in their <c>_type</c> field, so it can be read into a
		/// member or collection of a base class, interface, or <c>object</c> type. A generic type definition
		/// (e.g. <c>typeof(MyBox&lt;&gt;)</c>) allows every instantiation whose type arguments are also allowed.
		/// </summary>
		public BsonMapper AllowType(Type type)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));

			lock (_allowLock)
			{
				var current = _allowList;

				this.PublishAllowList(new AllowList(
					new HashSet<Type>(current.Types) { type },
					new HashSet<string>(current.Names, StringComparer.Ordinal) { GetDefinitionName(type) },
					current.Patterns,
					current.Filter));
			}

			return this;
		}

		/// <summary>
		/// Allows documents to name, in their <c>_type</c> field, any type whose full name matches
		/// <paramref name="pattern"/>, where <c>*</c> matches any run of characters: <c>"MyCompany.DataTypes.*"</c>
		/// allows every type in that namespace and the namespaces nested in it. Generic types match on their
		/// definition name (<c>"MyCompany.DataTypes.Box`1"</c>), and their type arguments must be allowed too.
		/// </summary>
		/// <remarks>
		/// Only allow namespaces that hold data types: every type they contain can be created, and its settable
		/// properties set, by whoever writes the document. <c>"*"</c> allows every type in the process and is unsafe
		/// for any data you did not write yourself.
		/// </remarks>
		public BsonMapper AllowTypes(string pattern)
		{
			if (string.IsNullOrEmpty(pattern)) throw new ArgumentNullException(nameof(pattern));

			lock (_allowLock)
			{
				var current = _allowList;
				var patterns = new string[current.Patterns.Length + 1];

				Array.Copy(current.Patterns, patterns, current.Patterns.Length);
				patterns[current.Patterns.Length] = pattern;

				this.PublishAllowList(new AllowList(current.Types, current.Names, patterns, current.Filter));
			}

			return this;
		}

		/// <summary>
		/// Optional predicate, consulted for types not otherwise allowed; return <c>true</c> to allow a type in
		/// <c>_type</c>. Like the other rules, it sees a generic type's definition and each of its type arguments
		/// separately, never a constructed generic type. It may be called concurrently by threads deserializing
		/// with this mapper. <c>t =&gt; true</c> allows every type and is unsafe for any data you did not write yourself.
		/// </summary>
		public Func<Type, bool>? AllowTypeFilter
		{
			get => Volatile.Read(ref _allowList).Filter;
			set
			{
				lock (_allowLock)
				{
					var current = _allowList;

					this.PublishAllowList(new AllowList(current.Types, current.Names, current.Patterns, value));
				}
			}
		}

		/// <summary>
		/// Returns true if <paramref name="type"/> may be named in a document's <c>_type</c> field.
		/// </summary>
		public bool IsTypeAllowed(Type type)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));

			return this.IsAllowed(Volatile.Read(ref _allowList), type);
		}

		/// <summary>
		/// Records a type registered with <see cref="RegisterTypeId"/>: registered types are allowed by name too.
		/// </summary>
		private void AllowRegisteredType(Type type)
		{
			lock (_allowLock)
			{
				var current = _allowList;

				this.PublishAllowList(new AllowList(
					current.Types,
					new HashSet<string>(current.Names, StringComparer.Ordinal) { GetDefinitionName(type) },
					current.Patterns,
					current.Filter));
			}
		}

		/// <summary>
		/// Publishes a changed allow-list, starting its resolved-name cache empty (the change may allow less than
		/// before). Call with <see cref="_allowLock"/> held.
		/// </summary>
		private void PublishAllowList(AllowList allowList)
		{
			Volatile.Write(ref _allowList, allowList);
		}

		/// <summary>
		/// Resolves a document's <c>_type</c> name to the type to read into <paramref name="declaredType"/>, throwing
		/// if the name doesn't resolve, the type isn't allowed, or it isn't assignable to <paramref name="declaredType"/>.
		/// </summary>
		internal Type ResolveTypeName(Type declaredType, BsonValue typeName)
		{
			if (!typeName.IsString) throw UltraLiteException.InvalidTypedName(typeName.ToString());

			var name = typeName.AsString;
			var allowList = Volatile.Read(ref _allowList);

			if (!allowList.Resolved.TryGetValue(name, out var type))
			{
				type = this.ResolveUncachedTypeName(allowList, declaredType, name);
			}

			if (!declaredType.IsAssignableFrom(type))
			{
				throw UltraLiteException.TypeNotAssignable(type, declaredType);
			}

			return type;
		}

		/// <summary>
		/// Resolves a document's <c>_t</c> id to its registered type, throwing if the id isn't registered or the type
		/// isn't assignable to <paramref name="declaredType"/>.
		/// </summary>
		internal Type ResolveTypeId(Type declaredType, BsonValue typeId)
		{
			if (!_customIdToType.TryGetValue(typeId, out var type))
			{
				throw UltraLiteException.InvalidTypedId(typeId);
			}

			if (!declaredType.IsAssignableFrom(type))
			{
				throw UltraLiteException.TypeNotAssignable(type, declaredType);
			}

			return type;
		}

		private Type ResolveUncachedTypeName(AllowList allowList, Type declaredType, string name)
		{
			// reject names that can't be allowed before resolving them: Type.GetType loads the named assembly and
			// builds any generic instantiation the name describes
			var definitionName = GetDefinitionName(name);
			var isDeclaredType = definitionName == GetDefinitionName(declaredType);

			if (!isDeclaredType && allowList.Filter == null && !allowList.Names.Contains(definitionName) && !MatchesAllowedPattern(allowList, definitionName))
			{
				throw UltraLiteException.TypeNotAllowed(name, declaredType);
			}

			Type? type;

			try
			{
				type = Type.GetType(name);
			}
			catch (Exception)
			{
				// malformed names, unloadable assemblies, invalid generic arguments, ...
				type = null;
			}

			if (type == null) throw UltraLiteException.InvalidTypedName(name);

			if (this.IsAllowed(allowList, type))
			{
				// cache it, unless the allow-list changed (or another name was cached) meanwhile: then a later read
				// just resolves it again
				if (allowList.Resolved.Count < MAX_CACHED_TYPE_NAMES)
				{
					Interlocked.CompareExchange(ref _allowList, allowList.WithResolved(name, type), allowList);
				}

				return type;
			}

			// naming exactly the declared type allows nothing more than omitting _type would
			if (type == declaredType) return type;

			throw UltraLiteException.TypeNotAllowed(name, declaredType);
		}

		/// <summary>
		/// Whether <paramref name="type"/> may be named in <c>_type</c> under <paramref name="allowList"/>.
		/// </summary>
		private bool IsAllowed(AllowList allowList, Type type)
		{
			// exactly this type was allowed or registered
			if (AllowAllTypes || allowList.Types.Contains(type) || _customTypeToId.ContainsKey(type)) return true;

			var typeInfo = type.GetTypeInfo();

			// arrays can't be instantiated from a document, nor can open generic types
			if (type.IsArray || typeInfo.IsGenericTypeDefinition) return false;

			if (typeInfo.IsGenericType)
			{
				return this.IsAllowedDefinition(allowList, type.GetGenericTypeDefinition()) && this.AreAllowedTypeArguments(allowList, typeInfo);
			}

			return this.IsAllowedDefinition(allowList, type);
		}

		/// <summary>
		/// A type argument decides the declared type of the members that use it, so each must be allowed too.
		/// </summary>
		private bool AreAllowedTypeArguments(AllowList allowList, TypeInfo genericType)
		{
			foreach (var argument in genericType.GetGenericArguments())
			{
				if (!this.IsAllowedTypeArgument(allowList, argument)) return false;
			}

			return true;
		}

		/// <summary>
		/// Whether a non-generic type or generic type definition is allowed by the mapper's rules.
		/// </summary>
		private bool IsAllowedDefinition(AllowList allowList, Type type)
		{
			return allowList.Types.Contains(type) ||
				_customTypeToId.ContainsKey(type) ||
				MatchesAllowedPattern(allowList, type.FullName) ||
				(allowList.Filter != null && allowList.Filter(type));
		}

		/// <summary>
		/// Whether a generic type argument or array element type is allowed. Besides allowed types, this accepts
		/// types a document can't populate through setters: built-in value types, enums, BSON types, types with a
		/// custom deserializer, <c>object</c> (whose values need an allowed <c>_type</c> of their own), and arrays
		/// and common collections of allowed types (collections are only ever read from BSON arrays).
		/// </summary>
		private bool IsAllowedTypeArgument(AllowList allowList, Type type)
		{
			var typeInfo = type.GetTypeInfo();

			if (type == typeof(object) ||
				typeInfo.IsPrimitive ||
				typeInfo.IsEnum ||
				_bsonTypes.Contains(type) ||
				_basicTypes.Contains(type) ||
				type == typeof(BsonValue) || type == typeof(BsonDocument) || type == typeof(BsonArray) ||
				_customDeserializer.ContainsKey(type))
			{
				return true;
			}

			if (type.IsArray) return this.IsAllowedTypeArgument(allowList, type.GetElementType()!);

			if (typeInfo.IsGenericType && !typeInfo.IsGenericTypeDefinition && _allowedContainerTypes.Contains(type.GetGenericTypeDefinition()))
			{
				return this.AreAllowedTypeArguments(allowList, typeInfo);
			}

			return this.IsAllowed(allowList, type);
		}

		private static bool MatchesAllowedPattern(AllowList allowList, string? name)
		{
			if (name == null) return false;

			foreach (var pattern in allowList.Patterns)
			{
				if (MatchesPattern(name, pattern)) return true;
			}

			return false;
		}

		/// <summary>
		/// Ordinal wildcard match where <c>*</c> matches any run of characters (including none).
		/// </summary>
		internal static bool MatchesPattern(string name, string pattern)
		{
			int n = 0, p = 0, starP = -1, starN = 0;

			while (n < name.Length)
			{
				if (p < pattern.Length && pattern[p] == '*')
				{
					starP = p++;
					starN = n;
				}
				else if (p < pattern.Length && pattern[p] == name[n])
				{
					p++;
					n++;
				}
				else if (starP >= 0)
				{
					// let the last * absorb one more character
					p = starP + 1;
					n = ++starN;
				}
				else
				{
					return false;
				}
			}

			while (p < pattern.Length && pattern[p] == '*') p++;

			return p == pattern.Length;
		}

		/// <summary>
		/// The full name of a type, or of its generic type definition for a constructed generic type.
		/// </summary>
		private static string GetDefinitionName(Type type)
		{
			var name = type.GetTypeInfo().IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName;

			return name ?? type.Name;
		}

		/// <summary>
		/// The type (or generic type definition) part of a <c>_type</c> name, which is
		/// <c>"FullName, Assembly"</c>, with generic arguments in <c>[[...]]</c> and arrays as <c>[]</c> after the name.
		/// </summary>
		private static string GetDefinitionName(string typeName)
		{
			var end = typeName.IndexOfAny(_typeNameDelimiters);

			return (end < 0 ? typeName : typeName.Substring(0, end)).Trim();
		}

		private static readonly char[] _typeNameDelimiters = { '[', ',' };
	}
}

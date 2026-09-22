using System;
using System.Reflection;

namespace UltraLiteDB
{
	/// <summary>
	/// Reads or writes one property through typed open-instance delegates bound to its accessor methods,
	/// exposed with the same object-based shape as <see cref="GenericGetter"/>/<see cref="GenericSetter"/>.
	/// A delegate call is far cheaper than <c>MethodInfo.Invoke</c> or <c>FieldInfo.GetValue</c> — especially
	/// under IL2CPP — and needs no runtime code generation.
	/// </summary>
	/// <remarks>
	/// Instances are created with <c>MakeGenericType</c> for each (owner, property type) pair, which works only
	/// where the runtime can create new generic instantiations over value types at runtime (CoreCLR, Mono,
	/// IL2CPP with full generic sharing — Unity 2022.1+; not NativeAOT or older IL2CPP). <see cref="Reflection"/>
	/// probes for that once and falls back to plain reflection when it is unavailable.
	/// </remarks>
	[Preserve]
	internal abstract class TypedAccessor
	{
		/// <summary>
		/// The property type when it is one of the primitives the direct serializer reads and writes unboxed
		/// through the typed Get/Set methods below; <see cref="DirectKind.Object"/> otherwise.
		/// </summary>
		public DirectKind Primitive { get; protected set; }

		public abstract object? Get(object target);

		public abstract void Set(object target, object? value);

		/// <summary>
		/// Runs code of this generic instantiation. Called once after construction: on an AOT runtime missing code
		/// for the instantiation it throws there (and the member falls back to reflection) rather than mid-use.
		/// </summary>
		public abstract bool Verify();

		// Unboxed access, valid only for the matching Primitive (and only on the side — get or set — that was bound)

		public virtual int GetInt32(object target) => throw new InvalidOperationException();
		public virtual long GetInt64(object target) => throw new InvalidOperationException();
		public virtual double GetDouble(object target) => throw new InvalidOperationException();
		public virtual float GetSingle(object target) => throw new InvalidOperationException();
		public virtual bool GetBoolean(object target) => throw new InvalidOperationException();
		public virtual DateTime GetDateTime(object target) => throw new InvalidOperationException();
		public virtual Guid GetGuid(object target) => throw new InvalidOperationException();

		public virtual void SetInt32(object target, int value) => throw new InvalidOperationException();
		public virtual void SetInt64(object target, long value) => throw new InvalidOperationException();
		public virtual void SetDouble(object target, double value) => throw new InvalidOperationException();
		public virtual void SetSingle(object target, float value) => throw new InvalidOperationException();
		public virtual void SetBoolean(object target, bool value) => throw new InvalidOperationException();
		public virtual void SetDateTime(object target, DateTime value) => throw new InvalidOperationException();
		public virtual void SetGuid(object target, Guid value) => throw new InvalidOperationException();
	}

	[Preserve]
	internal sealed class TypedAccessor<TOwner, TValue> : TypedAccessor
		where TOwner : class
	{
		private readonly Func<TOwner, TValue>? _getter;
		private readonly Action<TOwner, TValue>? _setter;
		private readonly GenericSetter? _convertingSetter;

		/// <param name="getMethod">Getter to bind, or null.</param>
		/// <param name="setMethod">Setter to bind, or null.</param>
		/// <param name="convertingSetter">Reflection setter used for values that aren't a <typeparamref name="TValue"/>,
		/// so they get exactly the conversions reflection applies (e.g. a boxed int widened into a long property).</param>
		[Preserve]
		public TypedAccessor(MethodInfo? getMethod, MethodInfo? setMethod, GenericSetter? convertingSetter)
		{
			if (getMethod != null)
			{
				_getter = (Func<TOwner, TValue>)Delegate.CreateDelegate(typeof(Func<TOwner, TValue>), getMethod);
			}

			if (setMethod != null)
			{
				_setter = (Action<TOwner, TValue>)Delegate.CreateDelegate(typeof(Action<TOwner, TValue>), setMethod);
			}

			_convertingSetter = convertingSetter;

			var type = typeof(TValue);

			if (type == typeof(int)) this.Primitive = DirectKind.Int32;
			else if (type == typeof(long)) this.Primitive = DirectKind.Int64;
			else if (type == typeof(double)) this.Primitive = DirectKind.Double;
			else if (type == typeof(float)) this.Primitive = DirectKind.Single;
			else if (type == typeof(bool)) this.Primitive = DirectKind.Boolean;
			else if (type == typeof(DateTime)) this.Primitive = DirectKind.DateTime;
			else if (type == typeof(Guid)) this.Primitive = DirectKind.Guid;
			else this.Primitive = DirectKind.Object;
		}

		public override object? Get(object target)
		{
			return _getter!((TOwner)target);
		}

		public override bool Verify()
		{
			return (_getter != null || _setter != null) && typeof(TValue) != null;
		}

		// When TValue is the primitive, the delegates' runtime types are exactly Func<TOwner, int> etc., so these
		// reference casts succeed and no value is ever boxed.

		public override int GetInt32(object target) => ((Func<TOwner, int>)(object)_getter!)((TOwner)target);
		public override long GetInt64(object target) => ((Func<TOwner, long>)(object)_getter!)((TOwner)target);
		public override double GetDouble(object target) => ((Func<TOwner, double>)(object)_getter!)((TOwner)target);
		public override float GetSingle(object target) => ((Func<TOwner, float>)(object)_getter!)((TOwner)target);
		public override bool GetBoolean(object target) => ((Func<TOwner, bool>)(object)_getter!)((TOwner)target);
		public override DateTime GetDateTime(object target) => ((Func<TOwner, DateTime>)(object)_getter!)((TOwner)target);
		public override Guid GetGuid(object target) => ((Func<TOwner, Guid>)(object)_getter!)((TOwner)target);

		public override void SetInt32(object target, int value) => ((Action<TOwner, int>)(object)_setter!)((TOwner)target, value);
		public override void SetInt64(object target, long value) => ((Action<TOwner, long>)(object)_setter!)((TOwner)target, value);
		public override void SetDouble(object target, double value) => ((Action<TOwner, double>)(object)_setter!)((TOwner)target, value);
		public override void SetSingle(object target, float value) => ((Action<TOwner, float>)(object)_setter!)((TOwner)target, value);
		public override void SetBoolean(object target, bool value) => ((Action<TOwner, bool>)(object)_setter!)((TOwner)target, value);
		public override void SetDateTime(object target, DateTime value) => ((Action<TOwner, DateTime>)(object)_setter!)((TOwner)target, value);
		public override void SetGuid(object target, Guid value) => ((Action<TOwner, Guid>)(object)_setter!)((TOwner)target, value);

		public override void Set(object target, object? value)
		{
			if (value is TValue typed)
			{
				_setter!((TOwner)target, typed);
			}
			else if (value == null)
			{
				// reflection assigns default(T) for null, including to value types
				_setter!((TOwner)target, default!);
			}
			else
			{
				_convertingSetter!(target, value);
			}
		}
	}

	/// <summary>
	/// Marks code reached only through reflection so Unity's managed code stripping keeps it. UnityLinker honors
	/// any attribute named <c>PreserveAttribute</c>, whatever its namespace, so this library doesn't need to
	/// reference UnityEngine.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor | AttributeTargets.Method |
		AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Interface, Inherited = false)]
	internal sealed class PreserveAttribute : Attribute
	{
	}
}

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
    public delegate void GenericSetter(object target, object value);

    /// <summary>Delegate that gets a member value from a source object.</summary>
    public delegate object GenericGetter(object obj);

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
                        if(type.GetTypeInfo().IsGenericType)
                        {
                            var typeDef = type.GetGenericTypeDefinition();

                            if (typeDef == typeof(IList<>) || 
                                typeDef == typeof(ICollection<>) ||
                                typeDef == typeof(IEnumerable<>))
                            {
                                return CreateInstance(GetGenericListOfType(UnderlyingTypeOf(type)));
                            }
                            else if (typeDef == typeof(IDictionary<,>))
                            {
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
                else if(listType.GetTypeInfo().IsGenericType && i == typeof(IEnumerable))
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
        /// Returns the first member matching any of the predicates, evaluated in order of priority.
        /// Used to resolve ID members by convention (e.g. "Id", "TypeNameId", "_id").
        /// </summary>
        public static MemberInfo SelectMember(IEnumerable<MemberInfo> members, params Func<MemberInfo, bool>[] predicates)
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
        public static GenericGetter CreateGenericGetter(Type type, MemberInfo memberInfo)
        {
            // when member is a field, use simple Reflection
            if (memberInfo is FieldInfo)
            {
                var fieldInfo = memberInfo as FieldInfo;

                return fieldInfo.GetValue;
            }

            // if is property, use Emit IL code
            var propertyInfo = memberInfo as PropertyInfo;
            var getMethod = propertyInfo.GetGetMethod(true);

            if (getMethod == null) return null;

            return target => getMethod.Invoke(target, null);
        }

        /// <summary>
        /// Creates a setter delegate for a field or property. Returns null if the property has no set accessor.
        /// Includes special handling for <c>byte[]</c> members that receive <c>ArraySegment&lt;byte&gt;</c> values.
        /// </summary>
        public static GenericSetter CreateGenericSetter(Type type, MemberInfo memberInfo)
        {
            
            // when member is a field, use simple Reflection
            if (memberInfo is FieldInfo)
            {
                var fieldInfo = memberInfo as FieldInfo;

                if(fieldInfo.FieldType == typeof(byte[]))
                {
                    // Special setter for byte arrays
                    return (target, value) => fieldInfo.SetValue(target, ((ArraySegment<byte>)value).Array);
                }
                else
                {
                    return fieldInfo.SetValue;
                }
                
            }

            // if is property, use Emit IL code
            var propertyInfo = memberInfo as PropertyInfo;

            var setMethod = propertyInfo.GetSetMethod(true);

            if (setMethod == null) return null;

            if(propertyInfo.PropertyType == typeof(byte[]))
            {
                // Special setter for byte arrays
                return (target, value) => setMethod.Invoke(target, new[] { ((ArraySegment<byte>)value).Array });
            }
            else
            {
                return (target, value) => setMethod.Invoke(target, new[] { value });
            }
        }
    
    }
}
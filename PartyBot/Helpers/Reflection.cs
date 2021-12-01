using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace PartyBot.Helpers
{
    internal static class Reflection
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static TResult Invoke<TObject, TResult>(this TObject obj, string name, object[] args, Type[] types = null)
        {
            var methodInfo = typeof(TObject).GetMethod(name, flags, null, types ?? args.ToTypeArray(), null);
            return (TResult)methodInfo.Invoke(obj, args);
        }

        public static TResult Invoke<TResult>(this object obj, Type objType, string name, object[] args, Type[] types = null)
        {
            var methodInfo = objType.GetMethod(name, flags, null, types ?? args.ToTypeArray(), null);
            return (TResult)methodInfo.Invoke(obj, args);
        }

        public static void Invoke<TObject>(this TObject obj, string name, object[] args, Type[] types = null)
        {
            var methodInfo = typeof(TObject).GetMethod(name, flags, null, types ?? args.ToTypeArray(), null);
            methodInfo.Invoke(obj, args);
        }

        public static TResult InvokeStatic<TResult>(this Type type, string name, object[] args, Type[] types = null)
        {
            var methodInfo = type.GetMethod(name, flags ^ BindingFlags.Instance, null, types ?? args.ToTypeArray(), null);
            return (TResult)methodInfo.Invoke(null, args);
        }

        public static void InvokeStatic(this Type type, string name, object[] args, Type[] types = null)
        {
            var methodInfo = type.GetMethod(name, flags ^ BindingFlags.Instance, null, types ?? args.ToTypeArray(), null);
            methodInfo.Invoke(null, args);
        }

        public static void InvokeVirtual(this object obj, string name, object[] args, Type[] types = null)
        {
            types = types ?? args.ToTypeArray();
            var type = obj.GetType();
            var methodInfo = type.GetMethod(name, flags, null, types, null);
            while (methodInfo == null)
            {
                type = type.BaseType;
                methodInfo = type.GetMethod(name, flags, null, types, null);
            }
            methodInfo.Invoke(obj, args);
        }

        public static TReturn InvokeVirtual<TReturn>(this object obj, string name, object[] args, Type[] types = null)
        {
            types = types ?? args.ToTypeArray();
            var type = obj.GetType();
            var methodInfo = type.GetMethod(name, flags, null, types, null);
            while (methodInfo == null)
            {
                type = type.BaseType;
                methodInfo = type.GetMethod(name, flags, null, types, null);
            }
            return (TReturn)methodInfo.Invoke(obj, args);
        }


        /// <summary>
        /// Wraps around the lengthy syntax of C# to get MethodInfo
        /// </summary>
        /// <typeparam name="TObject"></typeparam>
        /// <param name="obj">Owner object</param>
        /// <param name="name">Name of method</param>
        /// <returns></returns>
        public static MethodInfo GetMethod<TObject>(this TObject obj, string name)
        {
            return typeof(TObject).GetMethod(name, flags);
        }

        public static TObject Construct<TObject>(params object[] args)
        {
            var constructor = typeof(TObject).GetConstructor(
                flags ^ BindingFlags.Static,
                null,
                args.Select(arg => arg.GetType()).ToArray(),
                null);
            if (constructor != null)
            {
                return (TObject)constructor.Invoke(args);
            }
            // WASM.
            else if (typeof(TObject).GetConstructors(flags ^ BindingFlags.Static).Count() == 0)
            {
                System.Diagnostics.Debug.WriteLine("No constructors found. Attempting to create raw object.");
                return (TObject)FormatterServices.GetUninitializedObject(typeof(TObject));
            }

            throw new InvalidOperationException("No suitable constructors found.");
        }

        public static TValue GetValue<TObject, TValue>(this TObject obj, string name)
        {
            var fieldInfo = typeof(TObject).GetField(name, flags);
            if (fieldInfo != null)
            {
                return (TValue)fieldInfo.GetValue(obj);
            }
            var propertyInfo = typeof(TObject).GetProperty(name, flags);
            return (TValue)propertyInfo.GetValue(obj);
        }

        public static TValue GetValue<TValue>(this object obj, Type type, string name)
        {
            var fieldInfo = type.GetField(name, flags);
            if (fieldInfo != null)
            {
                return (TValue)fieldInfo.GetValue(obj);
            }
            var propertyInfo = type.GetProperty(name, flags);
            return (TValue)propertyInfo.GetValue(obj);

        }

        public static void SetProperty<TObject, TValue>(this TObject obj, string name, TValue value)
        {
            var propertyInfo = typeof(TObject).GetProperty(name, flags);
            var setter = propertyInfo.GetSetMethod(nonPublic: true);
            if (setter != null)
            {
                setter.Invoke(obj, new object[] { value });
            }
            else
            {
                var backingField = typeof(TObject).GetField($"<{propertyInfo.Name}>k__BackingField", flags);
                backingField.SetValue(obj, value);
            }
        }

        /// <summary>
        /// Looks in all loaded assemblies for the given type.
        /// </summary>
        /// <param name="fullName">
        /// The full name of the type.
        /// </param>
        /// <returns>
        /// The <see cref="Type"/> found; null if not found.
        /// </returns>
        public static Type FindType(string fullName)
        {
            return
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.FullName.Equals(fullName));
        }

        private static Type[] ToTypeArray(this object[] obj)
        {
            return obj.Select(o => o.GetType()).ToArray();
        }
    }
}

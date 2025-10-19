using System.Reflection;

namespace MiniORM
{
    internal static class ReflectionHelper
    {
        /// <summary>
        /// Replaces an auto-generated backing field with an object instance.
        /// Commonly used to set properties without a setter.
        /// </summary>
        public static void ReplaceBackingField(object objInstance, string propertyName, object value)
        {
            FieldInfo backingField = objInstance.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.SetField)
                .First(fi => fi.Name == $"<{propertyName}>k__BackingField");

            backingField.SetValue(objInstance, value);
        }

        /// <summary>
        /// Extension method for MemberInfo, which checks if a member contains an attribute.
        /// </summary>
        public static bool HasAttribute<T>(this MemberInfo mi)
            where T : Attribute
        {
            return mi.GetCustomAttribute<T>() != null;
        }
    }
}

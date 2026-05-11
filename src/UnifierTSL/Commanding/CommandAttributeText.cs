using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnifierTSL.Commanding
{
    public static class CommandAttributeText
    {
        private const BindingFlags StaticMemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private static readonly ConditionalWeakTable<Attribute, DeclaringTypeHolder> DeclaringTypes = new();

        private sealed class DeclaringTypeHolder(Type type)
        {
            public Type Type { get; } = type;
        }

        public static void Register(Attribute attribute, Type declaringType) {
            ArgumentNullException.ThrowIfNull(attribute);
            ArgumentNullException.ThrowIfNull(declaringType);

            DeclaringTypes.Remove(attribute);
            DeclaringTypes.Add(attribute, new DeclaringTypeHolder(declaringType));
        }

        public static string Resolve(Type declaringType, Type attributeType, string propertyName, string? memberName) {
            return Invoke(declaringType, attributeType, propertyName, memberName);
        }

        public static string Invoke(Attribute attribute, string propertyName, string? memberName, params object?[] args) {
            ArgumentNullException.ThrowIfNull(attribute);
            var declaringType = DeclaringTypes.TryGetValue(attribute, out var holder)
                ? holder.Type
                : attribute.GetType();
            return Invoke(declaringType, attribute.GetType(), propertyName, memberName, args);
        }

        public static string Invoke(Type declaringType, Type attributeType, string propertyName, string? memberName, params object?[] args) {
            ArgumentNullException.ThrowIfNull(declaringType);
            ArgumentNullException.ThrowIfNull(attributeType);

            var normalizedMemberName = memberName?.Trim() ?? string.Empty;
            if (normalizedMemberName.Length == 0) {
                return string.Empty;
            }

            if (TryInvoke(declaringType, normalizedMemberName, args, out var value)
                || TryInvoke(attributeType, normalizedMemberName, args, out value)) {
                return value.Trim();
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is declaring type name, {1} is attribute type name, {2} is attribute property name, {3} is member name",
                $"Type '{declaringType.FullName}' uses '{attributeType.FullName}.{propertyName}' member reference '{normalizedMemberName}', but no static string property or method with that name exists on the declaring type or attribute type."));
        }

        public static bool IsStaticStringMemberName(string? value) {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0 || !(char.IsLetter(normalized[0]) || normalized[0] == '_')) {
                return false;
            }

            for (var i = 1; i < normalized.Length; i++) {
                if (!(char.IsLetterOrDigit(normalized[i]) || normalized[i] == '_')) {
                    return false;
                }
            }

            return true;
        }

        private static bool TryInvoke(Type declaringType, string memberName, object?[] args, out string value) {
            var property = declaringType.GetProperty(memberName, StaticMemberFlags);
            if (property is not null) {
                if (property.PropertyType != typeof(string)
                    || property.GetMethod is null
                    || property.GetIndexParameters().Length != 0) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is declaring type name, {1} is member name",
                        $"Static member reference '{declaringType.FullName}.{memberName}' must resolve to a parameterless static string property or a static string method."));
                }

                value = (string?)property.GetValue(null) ?? string.Empty;
                return true;
            }

            var method = declaringType.GetMethod(memberName, StaticMemberFlags);
            if (method is null) {
                value = string.Empty;
                return false;
            }

            if (method.ReturnType != typeof(string)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is declaring type name, {1} is member name",
                    $"Static member reference '{declaringType.FullName}.{memberName}' must resolve to a parameterless static string property or a static string method."));
            }

            var parameters = method.GetParameters();
            object?[] invocationArgs;
            if (parameters.Length == 0) {
                invocationArgs = [];
            }
            else if (parameters.Length == 1
                && parameters[0].ParameterType == typeof(object[])
                && parameters[0].GetCustomAttribute<ParamArrayAttribute>(inherit: false) is not null) {
                invocationArgs = [args];
            }
            else {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is declaring type name, {1} is member name",
                    $"Static member reference '{declaringType.FullName}.{memberName}' must resolve to a parameterless string method or a string method with a params object?[] parameter."));
            }

            value = (string?)method.Invoke(null, invocationArgs) ?? string.Empty;
            return true;
        }
    }
}

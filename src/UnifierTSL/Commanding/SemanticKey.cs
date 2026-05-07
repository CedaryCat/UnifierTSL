using System.Reflection;

namespace UnifierTSL.Commanding
{
    public readonly struct SemanticKey : IEquatable<SemanticKey>
    {
        public readonly string Id { get; }

        public readonly string DisplayName { get; }

        public SemanticKey(string id, string displayName) {
            if (string.IsNullOrWhiteSpace(id)) {
                throw new ArgumentException(GetString("Semantic key id must not be empty."), nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName)) {
                throw new ArgumentException(GetString("Semantic key display name must not be empty."), nameof(displayName));
            }

            var normalizedId = id.Trim();
            if (!IsValidId(normalizedId)) {
                throw new ArgumentException(
                    GetString($"Semantic key id '{normalizedId}' is invalid. Expected lowercase dot-separated kebab-case segments."),
                    nameof(id));
            }

            Id = normalizedId;
            DisplayName = displayName.Trim();
        }

        public readonly bool Equals(SemanticKey other) {
            return string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public readonly override bool Equals(object? obj) {
            return obj is SemanticKey other && Equals(other);
        }

        public readonly override int GetHashCode() {
            return StringComparer.Ordinal.GetHashCode(Id);
        }

        public readonly override string ToString() {
            return Id;
        }

        public static bool operator ==(SemanticKey left, SemanticKey right) {
            return left.Equals(right);
        }

        public static bool operator !=(SemanticKey left, SemanticKey right) {
            return !left.Equals(right);
        }

        private static bool IsValidId(string value) {
            var segments = value.Split('.', StringSplitOptions.None);
            if (segments.Length < 2) {
                return false;
            }

            foreach (var segment in segments) {
                if (!IsValidSegment(segment)) {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidSegment(string segment) {
            if (segment.Length == 0 || segment[0] == '-' || segment[^1] == '-') {
                return false;
            }

            var previousDash = false;
            foreach (var c in segment) {
                if (c == '-') {
                    if (previousDash) {
                        return false;
                    }

                    previousDash = true;
                    continue;
                }

                if ((c < 'a' || c > 'z') && (c < '0' || c > '9')) {
                    return false;
                }

                previousDash = false;
            }

            return true;
        }
    }

    internal static class SemanticKeyCatalog
    {
        public static SemanticKey Resolve(Type catalogType, string memberName) {

            if (string.IsNullOrWhiteSpace(memberName)) {
                throw new ArgumentException(GetString("Semantic key member name must not be empty."), nameof(memberName));
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var normalizedMemberName = memberName.Trim();

            var property = catalogType.GetProperty(normalizedMemberName, Flags);
            if (property is not null) {
                if (property.PropertyType != typeof(SemanticKey) || property.GetMethod is null) {
                    throw new InvalidOperationException(
                        GetString($"Semantic key member '{catalogType.FullName}.{normalizedMemberName}' must be a readable public static {nameof(SemanticKey)} property."));
                }

                return (SemanticKey)(property.GetValue(null)
                    ?? throw new InvalidOperationException(
                        GetString($"Semantic key member '{catalogType.FullName}.{normalizedMemberName}' returned null.")));
            }

            var field = catalogType.GetField(normalizedMemberName, Flags);
            if (field is not null) {
                if (field.FieldType != typeof(SemanticKey)) {
                    throw new InvalidOperationException(
                        GetString($"Semantic key member '{catalogType.FullName}.{normalizedMemberName}' must be a public static {nameof(SemanticKey)} field."));
                }

                return (SemanticKey)(field.GetValue(null)
                    ?? throw new InvalidOperationException(
                        GetString($"Semantic key member '{catalogType.FullName}.{normalizedMemberName}' returned null.")));
            }

            throw new InvalidOperationException(
                GetString($"Semantic key member '{catalogType.FullName}.{normalizedMemberName}' was not found. Expected a public static {nameof(SemanticKey)} field or property."));
        }
    }
}

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

namespace UnifierTSL.Commanding.Bindings
{
    public sealed record CommandTokenEnumMember(
        string MemberName,
        object Value,
        string CanonicalToken,
        ImmutableArray<string> Tokens);

    public sealed record CommandTokenEnumSpec
    {
        public required Type EnumType { get; init; }

        public ImmutableArray<CommandTokenEnumMember> Members { get; init; } = [];

        public ImmutableArray<string> AllTokens { get; init; } = [];

        public required ImmutableDictionary<string, CommandTokenEnumMember> TokenLookup { get; init; }

        public bool TryResolve(string token, out object? value) {
            if (TokenLookup.TryGetValue(token, out var member)) {
                value = member.Value;
                return true;
            }

            value = null;
            return false;
        }
    }

    public static class CommandTokenEnumSupport
    {
        private static readonly ConcurrentDictionary<Type, CommandTokenEnumSpec> Cache = new();

        public static CommandTokenEnumSpec GetSpec(Type enumType) {
            ArgumentNullException.ThrowIfNull(enumType);
            return Cache.GetOrAdd(enumType, BuildSpec);
        }

        private static CommandTokenEnumSpec BuildSpec(Type enumType) {
            if (!enumType.IsEnum) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is enum type name",
                    $"Command token enum support requires an enum CLR type, but '{enumType.FullName}' was requested."));
            }

            ImmutableArray<FieldInfo> fields = [.. enumType
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .OrderBy(static field => field.MetadataToken)];
            List<CommandTokenEnumMember> members = [];
            List<string> allTokens = [];
            var tokenLookup =
                ImmutableDictionary.CreateBuilder<string, CommandTokenEnumMember>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fields) {
                var attribute = field.GetCustomAttribute<CommandTokenAttribute>(inherit: false);
                if (attribute is null) {
                    continue;
                }

                var tokens = NormalizeTokens(attribute.Tokens);
                if (tokens.Length == 0) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is enum field name",
                        $"Command token enum member '{enumType.FullName}.{field.Name}' does not declare any usable tokens."));
                }

                var value = field.GetValue(null)
                    ?? throw new InvalidOperationException(GetParticularString(
                        "{0} is enum field name",
                        $"Command token enum member '{enumType.FullName}.{field.Name}' resolved to null."));
                CommandTokenEnumMember member = new(
                    field.Name,
                    value,
                    tokens[0],
                    tokens);

                foreach (var token in tokens) {
                    if (tokenLookup.ContainsKey(token)) {
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is enum type name, {1} is command token",
                            $"Command token enum '{enumType.FullName}' declares duplicate token '{token}'."));
                    }

                    tokenLookup[token] = member;
                    allTokens.Add(token);
                }

                members.Add(member);
            }

            return new CommandTokenEnumSpec {
                EnumType = enumType,
                Members = [.. members],
                AllTokens = [.. allTokens],
                TokenLookup = tokenLookup.ToImmutable(),
            };
        }

        private static ImmutableArray<string> NormalizeTokens(IEnumerable<string>? tokens) {
            if (tokens is null) {
                return [];
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> normalized = [];
            foreach (var token in tokens) {
                var value = token?.Trim() ?? string.Empty;
                if (value.Length == 0 || !seen.Add(value)) {
                    continue;
                }

                normalized.Add(value);
            }

            return [.. normalized];
        }
    }
}

using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;

namespace UnifierTSL.Commanding.Builtin
{
    [CommandController("command", Summary = nameof(ControllerSummary))]
    [Aliases("commands", "cmd")]
    internal static class CommandInspectionCommand
    {
        private static string ControllerSummary => GetString("Inspects registered command roots, actions, and endpoint bindings.");
        private static string ExecuteSummary => GetString("Shows management-oriented help for command inspection.");
        private static string HelpSummary => GetString("Shows management-oriented help for command inspection.");
        private static string ListSummary => GetString("Lists registered command roots with source and endpoint metadata.");
        private static string SourcesSummary => GetString("Lists controller-group registration sources and their registered roots.");
        private static string InfoSummary => GetString("Shows detailed technical metadata for a command root or alias.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TerminalCommand]
        public static CommandOutcome Execute() {
            return BuildHelpOutcome();
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [TerminalCommand]
        public static CommandOutcome Help() {
            return BuildHelpOutcome();
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [TerminalCommand]
        public static CommandOutcome List([RemainingText] string filter = "") {
            var normalizedFilter = filter.Trim();
            var endpointCatalog = CommandSystem.GetEndpointCatalog();
            List<CommandRootDefinition> roots = [.. CommandSystem.GetCatalog().Roots
                .Where(root => MatchesFilter(root, normalizedFilter, endpointCatalog))
                .OrderBy(static root => root.RootName, StringComparer.OrdinalIgnoreCase)];
            if (roots.Count == 0) {
                return string.IsNullOrWhiteSpace(normalizedFilter)
                    ? CommandOutcome.Warning(GetString("No command roots are currently registered."))
                    : CommandOutcome.Warning(GetString("No command roots matched filter \"{0}\".", normalizedFilter));
            }

            var builder = CommandOutcome.InfoBuilder(GetString(
                "Registered command roots: {0}.",
                roots.Count));
            foreach (var root in roots) {
                builder.AddInfo(FormatRootSummary(root, endpointCatalog));
            }

            return builder.Build();
        }

        [CommandAction("sources", Summary = nameof(SourcesSummary))]
        [TerminalCommand]
        public static CommandOutcome Sources([RemainingText] string filter = "") {
            var normalizedFilter = filter.Trim();
            var endpointCatalog = CommandSystem.GetEndpointCatalog();
            var sources = CommandSystem.GetCatalog().Roots
                .GroupBy(static root => root.SourceName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new {
                    SourceName = group.Key,
                    Roots = group.OrderBy(static root => root.RootName, StringComparer.OrdinalIgnoreCase).ToList(),
                })
                .Where(group => string.IsNullOrWhiteSpace(normalizedFilter)
                    || group.SourceName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)
                    || group.Roots.Any(root => MatchesFilter(root, normalizedFilter, endpointCatalog)))
                .OrderBy(static group => group.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sources.Count == 0) {
                return string.IsNullOrWhiteSpace(normalizedFilter)
                    ? CommandOutcome.Warning(GetString("No command registration sources are currently registered."))
                    : CommandOutcome.Warning(GetString("No command registration sources matched filter \"{0}\".", normalizedFilter));
            }

            var builder = CommandOutcome.InfoBuilder(GetString(
                "Registered command sources: {0}.",
                sources.Count));
            foreach (var source in sources) {
                builder.AddInfo(GetString(
                    "source={0} roots={1}",
                    source.SourceName,
                    source.Roots.Count));
                builder.AddInfo(GetString(
                    "  roots: {0}",
                    string.Join(", ", source.Roots.Select(static root => root.RootName))));
            }

            return builder.Build();
        }

        [CommandAction("info", Summary = nameof(InfoSummary))]
        [TerminalCommand]
        public static CommandOutcome Info([RemainingText] string commandOrAlias = "") {
            if (string.IsNullOrWhiteSpace(commandOrAlias)) {
                return CommandOutcome.Usage(GetString("command info <root-or-alias>"));
            }

            var normalizedName = NormalizeCommandName(commandOrAlias);
            var endpointCatalog = CommandSystem.GetEndpointCatalog();
            var root = FindRoot(normalizedName);
            if (root is null) {
                return CommandOutcome.Error(GetString(
                    "No registered command root or alias matching \"{0}\" was found.",
                    commandOrAlias.Trim()));
            }

            var builder = CommandOutcome.InfoBuilder(GetString(
                "Command root {0}:",
                root.RootName));
            builder.AddInfo(GetString("root={0}", root.RootName));
            builder.AddInfo(GetString("aliases={0}", FormatList(root.Aliases)));
            builder.AddInfo(GetString("source={0}", root.SourceName));
            builder.AddInfo(GetString("controller={0}", FormatTypeName(root.ControllerType)));
            builder.AddInfo(GetString("summary={0}", FormatOptional(root.Summary)));
            builder.AddInfo(GetString("actions={0}", root.Actions.Length));
            builder.AddInfo(GetString("endpoints={0}", FormatEndpointIds(root, endpointCatalog)));

            if (root.MismatchHandler is null) {
                builder.AddInfo(GetString("mismatch handler=-"));
            }
            else {
                builder.AddInfo(GetString(
                    "mismatch handler={0}.{1} mode={2}",
                    FormatTypeName(root.MismatchHandler.Method.DeclaringType),
                    root.MismatchHandler.Method.Name,
                    root.MismatchHandler.HandlingMode));
            }

            for (var i = 0; i < root.Actions.Length; i++) {
                var action = root.Actions[i];
                var actionBindings = GetActionBindings(root, action, endpointCatalog);
                builder.AddInfo(GetString(
                    "action[{0}] path={1}",
                    i + 1,
                    FormatActionPath(root.RootName, action.PathSegments)));
                builder.AddInfo(GetString(
                    "  method={0}.{1}",
                    FormatTypeName(action.Method.DeclaringType),
                    action.Method.Name));
                builder.AddInfo(GetString("  signature={0}", FormatMethodSignature(action.Method)));
                builder.AddInfo(GetString("  summary={0}", FormatOptional(action.Summary)));
                builder.AddInfo(GetString("  availability={0}", FormatTargets(MergeAvailability(actionBindings))));
                builder.AddInfo(GetString("  endpoints={0}", FormatActionBindings(actionBindings)));

                if (action.Parameters.Length == 0) {
                    builder.AddInfo(GetString("  parameters=-"));
                }
                else {
                    builder.AddInfo(GetString("  parameters={0}", action.Parameters.Length));
                    foreach (var parameter in action.Parameters) {
                        builder.AddInfo(GetString(
                            "    {0}",
                            FormatParameter(parameter)));
                    }
                }
            }

            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return BuildHelpOutcome();
        }

        private static CommandOutcome BuildHelpOutcome() {
            return CommandOutcome.InfoBuilder(GetString("UTSL command inspector"))
                .AddInfo(GetString("command list [filter] - Lists registered command roots with source and endpoints."))
                .AddInfo(GetString("command sources [filter] - Lists command registration sources and their root counts."))
                .AddInfo(GetString("command info <root-or-alias> - Dumps technical metadata for one registered command root."))
                .AddInfo(GetString("command help - Shows this help page."))
                .Build();
        }

        private static CommandRootDefinition? FindRoot(string commandName) {
            return CommandSystem.GetCatalog().Roots.FirstOrDefault(root =>
                root.RootName.Equals(commandName, StringComparison.OrdinalIgnoreCase)
                || root.Aliases.Any(alias => alias.Equals(commandName, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesFilter(
            CommandRootDefinition root,
            string filter,
            CommandEndpointCatalog endpointCatalog) {
            if (string.IsNullOrWhiteSpace(filter)) {
                return true;
            }

            return root.RootName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || root.Aliases.Any(alias => alias.Contains(filter, StringComparison.OrdinalIgnoreCase))
                || root.SourceName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || FormatTypeName(root.ControllerType).Contains(filter, StringComparison.OrdinalIgnoreCase)
                || GetEndpointIds(root, endpointCatalog).Any(endpointId => endpointId.Contains(filter, StringComparison.OrdinalIgnoreCase))
                || root.Actions.Any(action =>
                    action.PathSegments.Any(segment => segment.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        private static string NormalizeCommandName(string commandName) {
            var normalized = commandName.Trim();
            while (normalized.Length > 0 && (normalized[0] == '/' || normalized[0] == '.')) {
                normalized = normalized[1..];
            }

            return normalized.Trim();
        }

        private static string FormatRootSummary(CommandRootDefinition root, CommandEndpointCatalog endpointCatalog) {
            return GetString(
                "{0} | source={1} | aliases={2} | actions={3} | endpoints={4}",
                root.RootName,
                root.SourceName,
                FormatList(root.Aliases),
                root.Actions.Length,
                FormatEndpointIds(root, endpointCatalog));
        }

        private static string FormatEndpointIds(CommandRootDefinition root, CommandEndpointCatalog endpointCatalog) {
            return FormatList(GetEndpointIds(root, endpointCatalog));
        }

        private static ImmutableArray<string> GetEndpointIds(
            CommandRootDefinition root,
            CommandEndpointCatalog endpointCatalog) {
            return [.. GetEndpointBindings(root, endpointCatalog)
                .Select(static binding => binding.EndpointId.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static endpointId => endpointId, StringComparer.Ordinal)];
        }

        private static ImmutableArray<CommandEndpointRootBinding> GetEndpointBindings(
            CommandRootDefinition root,
            CommandEndpointCatalog endpointCatalog) {
            return [.. endpointCatalog.Roots.Where(candidate => MatchesRoot(candidate.Root, root))];
        }

        private static ImmutableArray<CommandEndpointActionBinding> GetActionBindings(
            CommandRootDefinition root,
            CommandActionDefinition action,
            CommandEndpointCatalog endpointCatalog) {
            return [.. GetEndpointBindings(root, endpointCatalog)
                .SelectMany(static candidate => candidate.Actions)
                .Where(candidate => candidate.Action.Method == action.Method)
                .OrderBy(static candidate => candidate.EndpointId.Value, StringComparer.Ordinal)];
        }

        private static bool MatchesRoot(CommandRootDefinition left, CommandRootDefinition right) {
            return ReferenceEquals(left, right)
                || (left.RootName.Equals(right.RootName, StringComparison.OrdinalIgnoreCase)
                    && left.SourceName.Equals(right.SourceName, StringComparison.Ordinal)
                    && left.ControllerType == right.ControllerType);
        }

        private static string FormatActionPath(string rootName, ImmutableArray<string> pathSegments) {
            return pathSegments.Length == 0
                ? rootName
                : $"{rootName} {string.Join(' ', pathSegments)}";
        }

        private static string FormatMethodSignature(MethodInfo method) {
            var parameters = string.Join(", ", method.GetParameters().Select(static parameter =>
                $"{FormatTypeName(parameter.ParameterType)} {parameter.Name}"));
            return $"{FormatTypeName(method.ReturnType)} {method.Name}({parameters})";
        }

        private static string FormatParameter(CommandParamDefinition parameter) {
            List<string> parts = [
                $"{parameter.Name}:{FormatTypeName(parameter.ParameterType)}",
            ];

            if (parameter.Optional) {
                parts.Add($"optional={FormatValue(parameter.DefaultValue) ?? "true"}");
            }

            if (parameter.ConsumesRemainingTokens) {
                parts.Add("consumes-remaining=true");
            }

            if (parameter.Variadic) {
                parts.Add("variadic=true");
            }

            if (parameter.Modifiers != CommandParamModifiers.None) {
                parts.Add($"modifiers={parameter.Modifiers}");
            }

            if (parameter.SemanticKey is SemanticKey semanticKey) {
                parts.Add($"semantic={semanticKey.Id}");
            }

            if (!string.IsNullOrWhiteSpace(parameter.SuggestionKindId)) {
                parts.Add($"suggestion-kind={parameter.SuggestionKindId}");
            }

            if (parameter.EnumCandidates.Length > 0) {
                parts.Add($"enum=[{string.Join(", ", parameter.EnumCandidates)}]");
            }

            if (parameter.AcceptedSpecialTokens.Length > 0) {
                parts.Add($"special=[{string.Join(", ", parameter.AcceptedSpecialTokens)}]");
            }

            return string.Join(" | ", parts);
        }

        private static string FormatActionBindings(ImmutableArray<CommandEndpointActionBinding> bindings) {
            if (bindings.Length == 0) {
                return "-";
            }

            return string.Join(" ; ", bindings.Select(binding =>
                $"{binding.EndpointId.Value} targets={FormatTargets(binding.Availability)}"));
        }

        private static CommandAvailability MergeAvailability(ImmutableArray<CommandEndpointActionBinding> bindings) {
            return CommandAvailability.Union(bindings.Select(static binding => binding.Availability));
        }

        private static string FormatTargets(CommandAvailability availability) {
            return availability.IsEmpty
                ? "-"
                : string.Join(", ", availability.AllowedTargets.Select(static target => target.Value));
        }

        private static string FormatList(IEnumerable<string> values) {
            List<string> normalized = values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return normalized.Count == 0 ? "-" : string.Join(", ", normalized);
        }

        private static string FormatOptional(string? value) {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string FormatTypeName(Type? type) {
            if (type is null) {
                return "unknown";
            }

            if (!type.IsGenericType) {
                return type.FullName ?? type.Name;
            }

            var genericName = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var markerIndex = genericName.IndexOf('`');
            if (markerIndex >= 0) {
                genericName = genericName[..markerIndex];
            }

            return $"{genericName}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
        }

        private static string? FormatValue(object? value) {
            switch (value) {
                case null:
                    return null;
                case string text:
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                case bool boolean:
                    return boolean ? "true" : "false";
                case CommandAvailability availability:
                    return FormatTargets(availability);
                case Type type:
                    return FormatTypeName(type);
                case IEnumerable<string> strings:
                    return FormatList(strings);
                case IEnumerable sequence when value is not string:
                    List<string> items = [];
                    foreach (var item in sequence) {
                        var formatted = FormatValue(item);
                        if (formatted is not null) {
                            items.Add(formatted);
                        }
                    }

                    return items.Count == 0 ? null : string.Join(", ", items);
                default:
                    return value.ToString();
            }
        }
    }
}

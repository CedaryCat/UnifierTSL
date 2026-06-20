using System.Collections.Immutable;
using UnifierTSL.Commanding.Bindings;

namespace UnifierTSL.Commanding.Composition
{
    internal readonly record struct CommandRootPatchTarget
    {
        private CommandRootPatchTarget(Type? controllerType, Type? controllerGroupType) {
            ControllerType = controllerType;
            ControllerGroupType = controllerGroupType;
        }

        internal Type? ControllerType { get; }
        internal Type? ControllerGroupType { get; }

        internal static CommandRootPatchTarget ForController(Type controllerType) {
            ArgumentNullException.ThrowIfNull(controllerType);

            return new CommandRootPatchTarget(controllerType, controllerGroupType: null);
        }

        internal CommandRootPatchTarget WithControllerGroup(Type controllerGroupType) {
            ArgumentNullException.ThrowIfNull(controllerGroupType);

            return new CommandRootPatchTarget(ControllerType, controllerGroupType);
        }

        internal bool IsValid => ControllerType is not null;

        internal string Format() {
            List<string> parts = [];
            if (ControllerType is not null) {
                parts.Add($"controller '{ControllerType.FullName}'");
            }
            if (ControllerGroupType is not null) {
                parts.Add($"source '{CommandSystemDiscovery.GetSourceName(ControllerGroupType)}'");
            }

            return parts.Count == 0
                ? GetString("empty command root target")
                : string.Join(", ", parts);
        }
    }

    internal enum CommandCatalogPatchKind : byte
    {
        AddRootAlias,
        RemoveRootAlias,
        DisableRoot,
        ReplaceRoot,
        AddActions,
        AddActionAlias,
        RemoveActionAlias,
        DisableAction,
    }

    internal enum CommandCatalogPatchRootResolution : byte
    {
        Strict,
        SkipMissing,
    }

    internal sealed record CommandCatalogPatch
    {
        internal required CommandCatalogPatchKind Kind { get; init; }
        internal required CommandRootPatchTarget RootTarget { get; init; }
        internal ImmutableArray<string> ActionPath { get; init; } = [];
        internal ImmutableArray<string> AliasPath { get; init; } = [];
        internal string? Alias { get; init; }
        internal Type? ControllerType { get; init; }
        internal bool OptionalRoot { get; init; }
        internal bool OptionalAction { get; init; }
        internal bool OptionalMember { get; init; }
    }

    public sealed class CommandCatalogEditor
    {
        private readonly List<CommandCatalogPatch> patches = [];

        internal CommandCatalogEditor() { }

        public CommandRootEditor Root(Type controllerType) {
            return CreateRootEditor(CommandRootPatchTarget.ForController(controllerType), optionalRoot: false);
        }

        public CommandRootEditor Root(Type controllerType, Type controllerGroupType) {
            return CreateRootEditor(CommandRootPatchTarget.ForController(controllerType).WithControllerGroup(controllerGroupType), optionalRoot: false);
        }

        public CommandRootEditor IfRootExists(Type controllerType) {
            return CreateRootEditor(CommandRootPatchTarget.ForController(controllerType), optionalRoot: true);
        }

        public CommandRootEditor IfRootExists(Type controllerType, Type controllerGroupType) {
            return CreateRootEditor(CommandRootPatchTarget.ForController(controllerType).WithControllerGroup(controllerGroupType), optionalRoot: true);
        }

        internal ImmutableArray<CommandCatalogPatch> Build() {
            return [.. patches];
        }

        private CommandRootEditor CreateRootEditor(CommandRootPatchTarget target, bool optionalRoot) {
            if (!target.IsValid) {
                throw new ArgumentException(GetString("Command root targets must include a controller type."), nameof(target));
            }

            return new CommandRootEditor(patches, target, optionalRoot);
        }
    }

    public sealed class CommandRootEditor
    {
        private readonly List<CommandCatalogPatch> patches;
        private readonly CommandRootPatchTarget target;
        private readonly bool optionalRoot;

        internal CommandRootEditor(List<CommandCatalogPatch> patches, CommandRootPatchTarget target, bool optionalRoot) {
            ArgumentNullException.ThrowIfNull(patches);

            this.patches = patches;
            this.target = target;
            this.optionalRoot = optionalRoot;
        }

        public CommandRootEditor AddAlias(string alias) {
            patches.Add(Create(CommandCatalogPatchKind.AddRootAlias) with {
                Alias = NormalizeRequired(alias, nameof(alias)),
            });
            return this;
        }

        public CommandRootEditor RemoveAlias(string alias) {
            patches.Add(Create(CommandCatalogPatchKind.RemoveRootAlias) with {
                Alias = NormalizeRequired(alias, nameof(alias)),
            });
            return this;
        }

        public CommandRootEditor RemoveAliasIfPresent(string alias) {
            patches.Add(Create(CommandCatalogPatchKind.RemoveRootAlias) with {
                Alias = NormalizeRequired(alias, nameof(alias)),
                OptionalMember = true,
            });
            return this;
        }

        public CommandRootEditor Disable() {
            patches.Add(Create(CommandCatalogPatchKind.DisableRoot));
            return this;
        }

        public CommandRootEditor ReplaceWith(Type controllerType) {
            ArgumentNullException.ThrowIfNull(controllerType);

            patches.Add(Create(CommandCatalogPatchKind.ReplaceRoot) with {
                ControllerType = controllerType,
            });
            return this;
        }

        public CommandRootEditor AddActionsFrom(Type controllerType) {
            ArgumentNullException.ThrowIfNull(controllerType);

            patches.Add(Create(CommandCatalogPatchKind.AddActions) with {
                ControllerType = controllerType,
            });
            return this;
        }

        public CommandActionEditor Path(string path) {
            return new CommandActionEditor(patches, target, CommandSystemDiscovery.NormalizePath(path), optionalRoot, optionalAction: false);
        }

        public CommandActionEditor IfPathExists(string path) {
            return new CommandActionEditor(patches, target, CommandSystemDiscovery.NormalizePath(path), optionalRoot, optionalAction: true);
        }

        private CommandCatalogPatch Create(CommandCatalogPatchKind kind) {
            return new CommandCatalogPatch {
                Kind = kind,
                RootTarget = target,
                OptionalRoot = optionalRoot,
            };
        }

        private static string NormalizeRequired(string value, string paramName) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(GetString("Command patch values must not be empty."), paramName);
            }

            return value.Trim();
        }
    }

    public sealed class CommandActionEditor
    {
        private readonly List<CommandCatalogPatch> patches;
        private readonly CommandRootPatchTarget rootTarget;
        private readonly ImmutableArray<string> actionPath;
        private readonly bool optionalRoot;
        private readonly bool optionalAction;

        internal CommandActionEditor(
            List<CommandCatalogPatch> patches,
            CommandRootPatchTarget rootTarget,
            ImmutableArray<string> actionPath,
            bool optionalRoot,
            bool optionalAction) {
            ArgumentNullException.ThrowIfNull(patches);

            this.patches = patches;
            this.rootTarget = rootTarget;
            this.actionPath = actionPath.IsDefault ? [] : actionPath;
            this.optionalRoot = optionalRoot;
            this.optionalAction = optionalAction;
        }

        public CommandActionEditor AddAlias(string path) {
            var aliasPath = NormalizeAliasPath(path);
            patches.Add(Create(CommandCatalogPatchKind.AddActionAlias) with {
                AliasPath = aliasPath,
            });
            return this;
        }

        public CommandActionEditor RemoveAlias(string path) {
            var aliasPath = NormalizeAliasPath(path);
            patches.Add(Create(CommandCatalogPatchKind.RemoveActionAlias) with {
                AliasPath = aliasPath,
            });
            return this;
        }

        public CommandActionEditor RemoveAliasIfPresent(string path) {
            var aliasPath = NormalizeAliasPath(path);
            patches.Add(Create(CommandCatalogPatchKind.RemoveActionAlias) with {
                AliasPath = aliasPath,
                OptionalMember = true,
            });
            return this;
        }

        public CommandActionEditor Disable() {
            patches.Add(Create(CommandCatalogPatchKind.DisableAction));
            return this;
        }

        private CommandCatalogPatch Create(CommandCatalogPatchKind kind) {
            return new CommandCatalogPatch {
                Kind = kind,
                RootTarget = rootTarget,
                ActionPath = actionPath,
                OptionalRoot = optionalRoot,
                OptionalAction = optionalAction,
            };
        }

        private static ImmutableArray<string> NormalizeAliasPath(string path) {
            var segments = CommandSystemDiscovery.NormalizePath(path);
            if (segments.IsDefaultOrEmpty) {
                throw new ArgumentException(GetString("Command action alias paths must not be empty."), nameof(path));
            }

            return segments;
        }
    }

    internal static class CommandCatalogPatchApplier
    {
        public static CommandCatalog Apply(
            CommandCatalog catalog,
            ImmutableArray<CommandCatalogPatch> patches,
            CommandRegistrationOptions bindingOptions,
            CommandCatalogPatchRootResolution rootResolution) {
            foreach (var patch in patches) {
                catalog = ApplyPatch(catalog, patch, bindingOptions, rootResolution);
            }

            return catalog with {
                Roots = [.. catalog.Roots.OrderBy(static root => root.RootName, StringComparer.OrdinalIgnoreCase)],
            };
        }

        private static CommandCatalog ApplyPatch(
            CommandCatalog catalog,
            CommandCatalogPatch patch,
            CommandRegistrationOptions bindingOptions,
            CommandCatalogPatchRootResolution rootResolution) {
            var rootIndex = ResolveRootIndex(catalog, patch, rootResolution);
            if (rootIndex < 0) {
                return catalog;
            }

            var roots = catalog.Roots.ToBuilder();
            var root = roots[rootIndex];
            switch (patch.Kind) {
                case CommandCatalogPatchKind.AddRootAlias:
                    roots[rootIndex] = root with { Aliases = AddRootAlias(root, patch.Alias!) };
                    break;
                case CommandCatalogPatchKind.RemoveRootAlias:
                    roots[rootIndex] = root with { Aliases = RemoveRootAlias(root, patch) };
                    break;
                case CommandCatalogPatchKind.DisableRoot:
                    roots.RemoveAt(rootIndex);
                    break;
                case CommandCatalogPatchKind.ReplaceRoot:
                    roots[rootIndex] = BuildReplacementRoot(root, patch, bindingOptions);
                    break;
                case CommandCatalogPatchKind.AddActions:
                    roots[rootIndex] = AddActions(root, patch, bindingOptions);
                    break;
                case CommandCatalogPatchKind.AddActionAlias:
                    ValidateActionAlias(root, patch);
                    roots[rootIndex] = RewriteActions(root, patch, action =>
                        action with { PathAliases = AddPath(action.PathAliases, patch.AliasPath) });
                    break;
                case CommandCatalogPatchKind.RemoveActionAlias:
                    roots[rootIndex] = RemoveActionAlias(root, patch);
                    break;
                case CommandCatalogPatchKind.DisableAction:
                    roots[rootIndex] = DisableActions(root, patch);
                    break;
                default:
                    throw new InvalidOperationException(GetString("Unknown command catalog patch kind."));
            }

            return catalog with { Roots = roots.ToImmutable() };
        }

        private static int ResolveRootIndex(
            CommandCatalog catalog,
            CommandCatalogPatch patch,
            CommandCatalogPatchRootResolution rootResolution) {
            List<int> indexes = [];
            for (var i = 0; i < catalog.Roots.Length; i++) {
                if (MatchesRootTarget(catalog.Roots[i], patch.RootTarget)) {
                    indexes.Add(i);
                }
            }

            if (indexes.Count == 1) {
                return indexes[0];
            }

            if (indexes.Count == 0 && (patch.OptionalRoot || rootResolution == CommandCatalogPatchRootResolution.SkipMissing)) {
                return -1;
            }

            if (indexes.Count == 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root target",
                    $"Command catalog edit target {patch.RootTarget.Format()} was not found."));
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is command root target",
                $"Command catalog edit target {patch.RootTarget.Format()} matched multiple roots. Pass a controller group type to disambiguate the source."));
        }

        private static bool MatchesRootTarget(CommandRootDefinition root, CommandRootPatchTarget target) {
            if (target.ControllerGroupType is not null
                && !root.SourceName.Equals(CommandSystemDiscovery.GetSourceName(target.ControllerGroupType), StringComparison.Ordinal)) {
                return false;
            }

            if (target.ControllerType is not null && root.ControllerType != target.ControllerType) {
                return false;
            }

            return target.IsValid;
        }

        private static CommandRootDefinition BuildReplacementRoot(
            CommandRootDefinition target,
            CommandCatalogPatch patch,
            CommandRegistrationOptions bindingOptions) {
            var replacement = DiscoverPatchController(target, patch, bindingOptions);
            return replacement with {
                SourceName = target.SourceName,
            };
        }

        private static CommandRootDefinition AddActions(
            CommandRootDefinition target,
            CommandCatalogPatch patch,
            CommandRegistrationOptions bindingOptions) {
            var actionSource = DiscoverPatchController(target, patch, bindingOptions);
            ValidateActionSourceOnly(patch, actionSource);
            return target with {
                Actions = [.. target.Actions, .. actionSource.Actions],
            };
        }

        private static CommandRootDefinition DiscoverPatchController(
            CommandRootDefinition target,
            CommandCatalogPatch patch,
            CommandRegistrationOptions bindingOptions) {
            var controllerType = patch.ControllerType
                ?? throw new InvalidOperationException(GetString("Command catalog edit controller type is missing."));
            var discovered = CommandSystemDiscovery.DiscoverController(
                CommandSystemDiscovery.GetSourceName(controllerType),
                controllerType,
                bindingOptions);
            if (!discovered.RootName.Equals(target.RootName, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is patch controller type, {2} is patch controller root name",
                    $"Command catalog edit for root '{target.RootName}' cannot use controller '{controllerType.FullName}' because it declares root '{discovered.RootName}'."));
            }

            return discovered;
        }

        private static void ValidateActionSourceOnly(CommandCatalogPatch patch, CommandRootDefinition actionSource) {
            if (!string.IsNullOrWhiteSpace(actionSource.Summary)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is patch controller type",
                    $"Command catalog edit cannot add actions from controller '{patch.ControllerType?.FullName}' because it declares a root summary. AddActionsFrom imports actions only; use ReplaceWith for root metadata."));
            }

            if (actionSource.Aliases.Length != 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is patch controller type",
                    $"Command catalog edit cannot add actions from controller '{patch.ControllerType?.FullName}' because it declares root aliases. AddActionsFrom imports actions only; use AddAlias on the target root explicitly."));
            }

            if (actionSource.MismatchHandler is not null) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is patch controller type",
                    $"Command catalog edit cannot add actions from controller '{patch.ControllerType?.FullName}' because it declares a mismatch handler. AddActionsFrom imports actions only; use ReplaceWith to replace root-level behavior."));
            }
        }

        private static CommandRootDefinition RewriteActions(
            CommandRootDefinition root,
            CommandCatalogPatch patch,
            Func<CommandActionDefinition, CommandActionDefinition> rewrite) {
            var matched = false;
            var actions = ImmutableArray.CreateBuilder<CommandActionDefinition>(root.Actions.Length);
            foreach (var action in root.Actions) {
                if (MatchesPath(action.PathSegments, patch.ActionPath)) {
                    actions.Add(rewrite(action));
                    matched = true;
                }
                else {
                    actions.Add(action);
                }
            }

            if (!matched && !patch.OptionalAction) {
                throw MissingAction(root, patch);
            }

            return root with { Actions = actions.ToImmutable() };
        }

        private static ImmutableArray<string> AddRootAlias(CommandRootDefinition root, string alias) {
            if (root.RootName.Equals(alias, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is alias",
                    $"Command catalog edit cannot add alias '{alias}' to root '{root.RootName}' because it is the canonical root name."));
            }

            return AddToken(root.Aliases, alias);
        }

        private static ImmutableArray<string> RemoveRootAlias(CommandRootDefinition root, CommandCatalogPatch patch) {
            var aliases = RemoveToken(root.Aliases, patch.Alias!, out var removed);
            if (!removed && !patch.OptionalMember) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is alias",
                    $"Command catalog edit target root '{root.RootName}' does not define alias '{patch.Alias}'."));
            }

            return aliases;
        }

        private static void ValidateActionAlias(CommandRootDefinition root, CommandCatalogPatch patch) {
            if (MatchesPath(patch.ActionPath, patch.AliasPath)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is action path",
                    $"Command catalog edit cannot add alias '{root.RootName}{FormatActionPath(patch.AliasPath)}' because it is the canonical action path."));
            }

            var matched = false;
            foreach (var action in root.Actions) {
                if (MatchesPath(action.PathSegments, patch.ActionPath)) {
                    matched = true;
                    continue;
                }

                if (MatchesPath(action.PathSegments, patch.AliasPath)
                    || action.PathAliases.Any(alias => MatchesPath(alias, patch.AliasPath))) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command root name, {1} is action path, {2} is alias path",
                        $"Command catalog edit cannot add alias '{root.RootName}{FormatActionPath(patch.AliasPath)}' to action '{root.RootName}{FormatActionPath(patch.ActionPath)}' because another action in the same root already uses that path."));
                }
            }

            if (!matched && !patch.OptionalAction) {
                throw MissingAction(root, patch);
            }
        }

        private static CommandRootDefinition RemoveActionAlias(CommandRootDefinition root, CommandCatalogPatch patch) {
            var matched = false;
            var removed = false;
            var actions = ImmutableArray.CreateBuilder<CommandActionDefinition>(root.Actions.Length);
            foreach (var action in root.Actions) {
                if (!MatchesPath(action.PathSegments, patch.ActionPath)) {
                    actions.Add(action);
                    continue;
                }

                matched = true;
                var pathAliases = RemovePath(action.PathAliases, patch.AliasPath, out var actionRemoved);
                removed |= actionRemoved;
                actions.Add(action with { PathAliases = pathAliases });
            }

            if (!matched && !patch.OptionalAction) {
                throw MissingAction(root, patch);
            }

            if (matched && !removed && !patch.OptionalMember) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name, {1} is action path, {2} is alias path",
                    $"Command catalog edit target action '{root.RootName}{FormatActionPath(patch.ActionPath)}' does not define alias '{root.RootName}{FormatActionPath(patch.AliasPath)}'."));
            }

            return root with { Actions = actions.ToImmutable() };
        }

        private static CommandRootDefinition DisableActions(CommandRootDefinition root, CommandCatalogPatch patch) {
            var actions = ImmutableArray.CreateBuilder<CommandActionDefinition>(root.Actions.Length);
            var matched = false;
            foreach (var action in root.Actions) {
                if (MatchesPath(action.PathSegments, patch.ActionPath)) {
                    matched = true;
                    continue;
                }

                actions.Add(action);
            }

            if (!matched && !patch.OptionalAction) {
                throw MissingAction(root, patch);
            }

            if (actions.Count == 0) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is command root name",
                    $"Command catalog edit disabled every action for root '{root.RootName}'. Disable the root instead."));
            }

            return root with { Actions = actions.ToImmutable() };
        }

        private static InvalidOperationException MissingAction(CommandRootDefinition root, CommandCatalogPatch patch) {
            return new InvalidOperationException(GetParticularString(
                "{0} is command root name, {1} is action path",
                $"Command catalog edit target action '{root.RootName}{FormatActionPath(patch.ActionPath)}' was not found."));
        }

        private static ImmutableArray<string> AddToken(ImmutableArray<string> values, string value) {
            return values.Any(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase))
                ? values
                : [.. values, value];
        }

        private static ImmutableArray<string> RemoveToken(ImmutableArray<string> values, string value, out bool removed) {
            removed = false;
            List<string> result = [];
            foreach (var candidate in values) {
                if (candidate.Equals(value, StringComparison.OrdinalIgnoreCase)) {
                    removed = true;
                    continue;
                }

                result.Add(candidate);
            }

            return [.. result];
        }

        private static ImmutableArray<ImmutableArray<string>> AddPath(
            ImmutableArray<ImmutableArray<string>> values,
            ImmutableArray<string> value) {
            return values.Any(candidate => MatchesPath(candidate, value))
                ? values
                : [.. values, value];
        }

        private static ImmutableArray<ImmutableArray<string>> RemovePath(
            ImmutableArray<ImmutableArray<string>> values,
            ImmutableArray<string> value,
            out bool removed) {
            removed = false;
            List<ImmutableArray<string>> result = [];
            foreach (var candidate in values) {
                if (MatchesPath(candidate, value)) {
                    removed = true;
                    continue;
                }

                result.Add(candidate);
            }

            return [.. result];
        }

        private static bool MatchesPath(ImmutableArray<string> left, ImmutableArray<string> right) {
            left = left.IsDefault ? [] : left;
            right = right.IsDefault ? [] : right;
            if (left.Length != right.Length) {
                return false;
            }

            for (var i = 0; i < left.Length; i++) {
                if (!left[i].Equals(right[i], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        private static string FormatActionPath(ImmutableArray<string> path) {
            path = path.IsDefault ? [] : path;
            return path.Length == 0 ? string.Empty : $" {string.Join(' ', path)}";
        }
    }
}

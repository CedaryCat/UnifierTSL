using System.Collections.Immutable;

namespace TShockAPI.Commanding
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TSCommandRootAttribute : Attribute
    {
        public bool DoLog { get; set; } = true;

        /// <summary>
        /// Gets or sets the static string property name that provides the legacy help text.
        /// </summary>
        public string HelpText { get; set; } = string.Empty;

        public string[] HelpLines { get; set; } = [];
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TShockCommandAttribute(params string[] permissions) : Attribute
    {
        private readonly ImmutableArray<string> permissions = permissions is null ? [] : [.. permissions];
        private bool serverScope;
        private bool playerScope;

        public IReadOnlyList<string> Permissions => permissions;

        public Type? PermissionFieldSource { get; set; } = typeof(Permissions);

        public bool ServerScope {
            get => serverScope;
            set {
                serverScope = value;
                if (!serverScope) {
                    playerScope = false;
                }
            }
        }

        public bool PlayerScope {
            get => playerScope;
            set {
                playerScope = value;
                if (playerScope) {
                    serverScope = true;
                }
            }
        }

        public bool AutoExposeToTerminal { get; set; } = true;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class DisallowRestAttribute : Attribute { }
}

namespace UnifierTSL.Plugins.Bindings
{
    /// <summary>
    /// Reserved API surface for future scope-based binding management.
    /// This is intentionally a placeholder in V1 and is not wired into runtime flows yet.
    /// </summary>
    public interface IPluginBindingScope
    {
        IEventBindingScope Events { get; }
        IPacketBindingScope Packets { get; }
        IServerExtensionBindingScope Extensions { get; }
        IConfigBindingScope Configs { get; }
        IDetourBindingScope Detours { get; }
    }

    /// <summary>
    /// Reserved API surface for future use.
    /// </summary>
    public interface IEventBindingScope { }

    /// <summary>
    /// Reserved API surface for future use.
    /// </summary>
    public interface IPacketBindingScope { }

    /// <summary>
    /// Reserved API surface for future use.
    /// </summary>
    public interface IServerExtensionBindingScope { }

    /// <summary>
    /// Reserved API surface for future use.
    /// </summary>
    public interface IConfigBindingScope { }

    /// <summary>
    /// Reserved API surface for future use.
    /// </summary>
    public interface IDetourBindingScope { }
}

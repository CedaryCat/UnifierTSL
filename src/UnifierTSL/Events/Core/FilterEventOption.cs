namespace UnifierTSL.Events.Core
{
    public enum FilterEventOption : byte
    {
        Normal = 1,
        Handled = 2,
        All = Normal | Handled,
    }
}

namespace UnifierTSL.PluginHost
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class PluginHostAttribute(int majorApiVersion, int minorApiVersion) : Attribute
    {
        public Version ApiVersion { get; init; } = new Version(majorApiVersion, minorApiVersion);
    }
}

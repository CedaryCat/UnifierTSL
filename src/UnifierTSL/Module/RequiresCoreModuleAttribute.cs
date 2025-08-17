namespace UnifierTSL.Module
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public class RequiresCoreModuleAttribute(string coreModuleName) : Attribute {
        public string CoreModuleName { get; } = coreModuleName;
    }
}

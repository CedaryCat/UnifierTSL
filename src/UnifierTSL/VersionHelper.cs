using System.Reflection;

namespace UnifierTSL
{
    public class VersionHelper
    {
        public readonly string TerrariaVersion;
        public readonly string OTAPIVersion;

        public VersionHelper() {
            var otapi = typeof(Terraria.Main).Assembly;
            var fileVersionAttr = otapi.GetCustomAttribute<AssemblyFileVersionAttribute>();
            TerrariaVersion = fileVersionAttr!.Version;

            var informationalVersionAttr = otapi.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            OTAPIVersion = informationalVersionAttr!.InformationalVersion.Split('+').First();
        }
    }
}

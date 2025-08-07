using System.Reflection;

namespace UnifierTSL
{
    public class VersionHelper
    {
        public readonly string UnifierApiVersion;
        public readonly string TerrariaVersion;
        public readonly string OTAPIVersion;
        public readonly string USPVersion;

        public VersionHelper() {
            var utsl = typeof(Program).Assembly;
            var otapi = typeof(Terraria.Main).Assembly;

            UnifierApiVersion = utsl.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
            TerrariaVersion = otapi.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
            USPVersion = otapi.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == "USPBuildVersion")
                .Single().Value!;

            var informationalVersionAttr = otapi.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            OTAPIVersion = informationalVersionAttr!.InformationalVersion.Split('+').First();
        }
    }
}

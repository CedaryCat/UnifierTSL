using System.Reflection;

namespace UnifierTSL
{
    public class VersionHelper
    {
        public readonly string UnifierApiVersion;
        public readonly string TerrariaVersion;
        public readonly string OTAPIVersion;

        public VersionHelper() {
            var utsl = typeof(Program).Assembly;
            var otapi = typeof(Terraria.Main).Assembly;


            UnifierApiVersion = utsl.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
            TerrariaVersion = otapi.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;

            var informationalVersionAttr = otapi.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            OTAPIVersion = informationalVersionAttr!.InformationalVersion.Split('+').First();
        }
    }
}

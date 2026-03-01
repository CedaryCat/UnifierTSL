using System.Reflection;
using System.Runtime.Loader;

namespace UnifierTSL.Extensions
{
    public static class ALCExt
    {
        internal static Assembly LoadFromStream(this AssemblyLoadContext alc, string fileName) {
            using FileStream stream = File.OpenRead(fileName);
            if (File.Exists(Path.ChangeExtension(fileName, "pdb"))) {
                using FileStream pdbStream = File.OpenRead(Path.ChangeExtension(fileName, "pdb"));
                return alc.LoadFromStream(stream, pdbStream);
            }
            else {
                return alc.LoadFromStream(stream);
            }
        }
    }
}

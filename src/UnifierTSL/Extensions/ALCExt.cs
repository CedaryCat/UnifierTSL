using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Extensions
{
    public static class ALCExt
    {
        public static Assembly LoadFromStream(this AssemblyLoadContext alc, string fileName) {
            using var stream = File.OpenRead(fileName);
            if (File.Exists(Path.ChangeExtension(fileName, "pdb"))) {
                using var pdbStream = File.OpenRead(Path.ChangeExtension(fileName, "pdb"));
                return alc.LoadFromStream(stream, pdbStream);
            }
            else {
                return alc.LoadFromStream(stream);
            }
        }
    }
}

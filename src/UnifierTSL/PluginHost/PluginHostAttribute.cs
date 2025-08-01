using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.PluginHost
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class PluginHostAttribute(int majorApiVersion, int minorApiVersion) : Attribute
    {
        public Version ApiVersion { get; init; } = new Version(majorApiVersion, minorApiVersion);
    }
}

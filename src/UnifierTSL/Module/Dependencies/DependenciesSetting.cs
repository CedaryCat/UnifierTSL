using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Module.Dependencies
{
    public sealed class DependenciesSetting
    {
        /// <summary>
        /// Indicates whether to enable aggressive dependencies cleanup.
        /// When set to <c>true</c>, any file not listed in the curFormatter dependencies map will be deleted 
        /// from the dependencies directory, which results in a cleaner state but may unintentionally 
        /// remove manually added files.
        /// When set to <c>false</c>, the cleanup process will only remove files associated with 
        /// previously known dependencies that have been explicitly removed, preserving any 
        /// manually added or unknown files.
        /// </summary>
        public bool EnableAggressiveCleanUp { get; set; } = false;
        public required Dictionary<string, DependencyRecord> Dependencies { get; set; }
    }
}

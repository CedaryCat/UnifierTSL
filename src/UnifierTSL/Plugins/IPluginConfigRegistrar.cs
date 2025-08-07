using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Entry point for registering plugin configuration files.
    /// </summary>
    public interface IPluginConfigRegistrar
    {
        /// <summary>
        /// The directory in which to look for configuration files for this plugin.
        /// </summary>
        public string Directory { get; }
        /// <summary>
        /// Default options for each plugin, independent of one another.  
        /// When creating an <see cref="IConfigRegistrationBuilder"/>, these options are used as the initial configuration.  
        /// Afterward, the configuration can be customized further through methods on <see cref="IConfigRegistrationBuilder"/>.  
        /// A plugin can predefine this configuration during initialization and then call <c>CreateConfigRegistration</c>  
        /// to avoid repeating the same setup for every configuration file.
        /// </summary>
        public IConfigOption DefaultOption { get; }

        /// <summary>
        /// Begins registration of a configuration using one of the built-in formats.
        /// </summary>
        /// <typeparam name="TConfig">The configuration type.</typeparam>
        /// <param name="format">The built-in format to use.</param>
        /// <returns>A fluent builder for further specification.</returns>
        IConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TConfig>(string relativePath)
            where TConfig : class, new();

        /// <summary>
        /// Begins registration of a configuration using one of the built-in formats.
        /// </summary>
        /// <typeparam name="TConfig">The configuration type.</typeparam>
        /// <param name="relativePath">
        /// The configuration file path, relative to the configuration folder assigned to the current plugin.
        /// </param>
        /// <param name="format">The built-in format to use.</param>
        /// <returns>A fluent builder for further specification.</returns>
        IConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TConfig>(string relativePath, ConfigFormat format)
            where TConfig : class, new();

        /// <summary>
        /// Begins registration of a configuration using a custom format provider.
        /// </summary>
        /// <typeparam name="TFormatProvider">The custom format provider type.</typeparam>
        /// <typeparam name="TConfig">The configuration type.</typeparam>
        /// <param name="relativePath">
        /// The configuration file path, relative to the configuration folder assigned to the current plugin.
        /// </param>
        /// <returns>A fluent builder for further specification.</returns>
        IConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TFormatProvider, TConfig>(string relativePath)
            where TFormatProvider : IConfigFormatProvider, new()
            where TConfig : class, new();
    }
}

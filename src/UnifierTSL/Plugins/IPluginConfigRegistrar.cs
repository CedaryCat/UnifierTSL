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
        /// Begins registration of a configuration using one of the built-in formats.
        /// </summary>
        /// <typeparam name="TConfig">Configuration type.</typeparam>
        /// <param name="relativePath">Relative file path of the config.</param>
        /// <param name="format">Built-in format to use.</param>
        /// <returns>Fluent builder for further specification.</returns>
        IPluginConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TConfig>(string relativePath, ConfigFormat format)
            where TConfig : class, new();

        /// <summary>
        /// Begins registration of a configuration using a custom format provider.
        /// The provider is resolved externally (e.g., via dependency injection) by its type.
        /// </summary>
        /// <typeparam name="TFormatProvider">Custom format provider type.</typeparam>
        /// <typeparam name="TConfig">Configuration type.</typeparam>
        /// <param name="relativePath">Relative file path of the config.</param>
        /// <returns>Fluent builder for further specification.</returns>
        IPluginConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TFormatProvider, TConfig>(string relativePath)
            where TFormatProvider : IConfigFormatProvider, new()
            where TConfig : class, new();
    }
}

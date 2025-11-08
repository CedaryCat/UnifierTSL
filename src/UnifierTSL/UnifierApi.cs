using System.Globalization;
using Terraria.Localization;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;

namespace UnifierTSL
{
    public partial class UnifierApi
    {
        public static RoleLogger CreateLogger(ILoggerHost host, Logger? overrideLogger = null) {
            return new RoleLogger(host, overrideLogger ?? LogCore);
        }

        public static void UpdateTitle(bool empty = false) {
            Console.Title = $"UnifierTSL " +
                            $"- {(empty ? 0 : UnifiedServerCoordinator.GetActiveClientCount())}/{byte.MaxValue} " +
                            $"@ {UnifiedServerCoordinator.ListeningEndpoint} " +
                            $"USP for Terraria v{VersionHelper.TerrariaVersion}";
        }
        public static string LibraryDirectory => AppContext.BaseDirectory;
        public static string BaseDirectory => Directory.GetCurrentDirectory();
        public static string TranslationsDirectory => Path.Combine(BaseDirectory, "i18n");
        public static CultureInfo TranslationCultureInfo 
            => LanguageManager.Instance.ActiveCulture.RedirectedCultureInfo();
    }
}

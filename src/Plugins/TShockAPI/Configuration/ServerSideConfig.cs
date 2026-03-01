using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Plugins;

namespace TShockAPI.Configuration
{

    /// <summary>
    /// Settings used to configure server side characters
    /// </summary>
    public class SscSettings
    {
        /// <summary>
        /// Enable server side characters, causing client data to be saved on the server instead of the client.
        /// </summary>
        [Description("Enable server side characters, causing client data to be saved on the server instead of the client.")]
        public bool Enabled = false;

        /// <summary>
        /// How often SSC should save, in minutes.
        /// </summary>
        [Description("How often SSC should save, in minutes.")]
        public int ServerSideCharacterSave = 5;

        /// <summary>
        /// Time, in milliseconds, to disallow discarding items after logging in when ServerSideCharacters is ON.
        /// </summary>
        [Description("Time, in milliseconds, to disallow discarding items after logging in when ServerSideCharacters is ON.")]
        public int LogonDiscardThreshold = 250;

        /// <summary>
        /// The starting default health for new players when SSC is enabled.
        /// </summary>
        [Description("The starting default health for new players when SSC is enabled.")]
        public int StartingHealth = 100;

        /// <summary>
        /// The starting default mana for new players when SSC is enabled.
        /// </summary>
        [Description("The starting default mana for new players when SSC is enabled.")]
        public int StartingMana = 20;

        /// <summary>
        /// The starting default inventory for new players when SSC is enabled.
        /// </summary>
        [Description("The starting default inventory for new players when SSC is enabled.")]
        public List<NetItem> StartingInventory = new List<NetItem>();

        /// <summary>
        /// Warns players that they have the bypass SSC permission enabled. To disable warning, turn this off.
        /// </summary>
        [Description("Warns players and the console if a player has the tshock.ignore.ssc permission with data in the SSC table.")]
        public bool WarnPlayersAboutBypassPermission = true;
    }

    public class ServerSideConfig(IPluginConfigRegistrar configRegistrar, string fileNameWithoutExtension, Func<SscSettings> defaultSettingFactory) : ConfigFile<SscSettings>(configRegistrar, fileNameWithoutExtension, defaultSettingFactory)
    {

        /// <summary>
        /// Dumps all configuration options to a text file in Markdown format
        /// </summary>
        public static void DumpDescriptions() {
            var sb = new StringBuilder();
            var defaults = new SscSettings();

            foreach (var field in defaults.GetType().GetFields().OrderBy(f => f.Name)) {
                if (field.IsStatic)
                    continue;

                var name = field.Name;
                var type = field.FieldType.Name;

                var descattr =
                    field.GetCustomAttributes(false).FirstOrDefault(o => o is DescriptionAttribute) as DescriptionAttribute;
                var desc = descattr != null && !string.IsNullOrWhiteSpace(descattr.Description) ? descattr.Description : "None";

                var def = field.GetValue(defaults);

                sb.AppendLine($"## {name}  ");
                sb.AppendLine($"{desc}");
                sb.AppendLine(GetString("* **Field type**: `{0}`", type));
                sb.AppendLine(GetString("* **Default**: `{0}`", def));
                sb.AppendLine();
            }

            File.WriteAllText("docs/ssc-config.md", sb.ToString());
        }

        public static SscSettings Default() {
            return new SscSettings() {
                StartingInventory = [new(-15, 1, 0), new(-13, 1, 0), new(-16, 1, 0)]
            };
        }
    }
}

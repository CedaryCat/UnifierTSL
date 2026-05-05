using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Terraria;
using Terraria.Chat.Commands;
using Terraria.ID;
using Terraria.Localization;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.Localization.Terraria
{
    public static class EnglishLanguage
    {
        private static readonly LanguageManager Language;
        private static readonly IReadOnlyDictionary<int, string> ItemNames;
        private static readonly IReadOnlyDictionary<int, string> NpcNames;
        private static readonly IReadOnlyDictionary<int, string> BuffNames;
        private static readonly IReadOnlyDictionary<int, string> PrefixNames;
        private static readonly Dictionary<string, string> CommandID2EnglishText = [];
        static EnglishLanguage() {
            LanguageManager.Instance._localizedTexts[""] = LocalizedText.Empty;
            Language = new();
            Language.LoadFilesForCulture(GameCulture.FromCultureName(GameCulture.CultureName.English));
            Language.ProcessCopyCommandsInTexts();
            BuildEnglishNames(out ItemNames, out NpcNames, out BuffNames, out PrefixNames);

            using FileStream stream = File.OpenRead(typeof(Main).Assembly.Location);
            using PEReader peReader = MetadataBlobHelpers.GetPEReader(stream)!;
            MetadataReader metadataReader = peReader.GetMetadataReader();
            List<CustomAttribute> attributes = MetadataBlobHelpers.ExtractCustomAttributeOnSpecificTypes(metadataReader, "Terraria.Chat.Commands", nameof(ChatCommandAttribute));

            foreach (CustomAttribute attribute in attributes) {
                if (AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out ParsedCustomAttribute parsedAttr)) {
                    if (parsedAttr.ConstructorArguments[0] is not string value) {
                        continue;
                    }
                    string commandKey = "ChatCommand." + value;

                    if (Language.Exists(commandKey)) {
                        CommandID2EnglishText[value] = Language.GetTextValue(commandKey);
                    }
                    else {
                        commandKey += "_";
                        LocalizedText[] array = Language.FindAll((string key, LocalizedText text) => key.StartsWith(commandKey));
                        foreach (LocalizedText key2 in array) {
                            if (CommandID2EnglishText.TryAdd(value, key2.Value)) {
                                break;
                            }
                        }
                    }
                }
            }
        }

        public static void Load() { }

        public static string? GetItemNameById(int id) {
            if (ItemNames.TryGetValue(id, out string? value)) {
                return value;
            }

            return null;
        }
        public static string? GetNpcNameById(int id) {
            if (NpcNames.TryGetValue(id, out string? value)) {
                return value;
            }

            return null;
        }
        public static string? GetBuffNameById(int id) {
            if (BuffNames.TryGetValue(id, out string? value)) {
                return value;
            }

            return null;
        }
        public static string? GetPrefixById(int id) {
            if (PrefixNames.TryGetValue(id, out string? value)) {
                return value;
            }

            return null;
        }
        public static string GetCommandPrefixByName(string commandName) {
            if (CommandID2EnglishText.TryGetValue(commandName, out string? value)) {
                return value;
            }
            return "";
        }

        private static void BuildEnglishNames(
            out IReadOnlyDictionary<int, string> itemNames,
            out IReadOnlyDictionary<int, string> npcNames,
            out IReadOnlyDictionary<int, string> buffNames,
            out IReadOnlyDictionary<int, string> prefixNames) {
            GameCulture englishCulture = GameCulture.FromCultureName(GameCulture.CultureName.English);
            GameCulture originalCulture = global::Terraria.Localization.Language.ActiveCulture;
            bool restoreCulture = originalCulture != englishCulture;

            try {
                if (restoreCulture) {
                    LanguageManager.Instance.SetLanguage(englishCulture);
                }

                itemNames = BuildItemNames();
                npcNames = BuildNpcNames();
                buffNames = BuildBuffNames();
                prefixNames = BuildPrefixNames();
            }
            finally {
                if (restoreCulture) {
                    LanguageManager.Instance.SetLanguage(originalCulture);
                }
            }
        }

        private static Dictionary<int, string> BuildItemNames() {
            Dictionary<int, string> itemNames = [];

            for (int i = 1; i < ItemID.Count; i++) {
                string itemName = Lang.GetItemNameValue(i);
                if (string.IsNullOrWhiteSpace(itemName)) {
                    continue;
                }

                itemNames[i] = itemName.Trim();
            }

            return itemNames;
        }

        private static Dictionary<int, string> BuildNpcNames() {
            Dictionary<int, string> npcNames = [];

            for (int i = -17; i < NPCID.Count; i++) {
                string npcName = Lang.GetNPCNameValue(i);
                if (string.IsNullOrWhiteSpace(npcName)) {
                    continue;
                }

                npcNames[i] = npcName.Trim();
            }

            return npcNames;
        }

        private static Dictionary<int, string> BuildBuffNames() {
            Dictionary<int, string> buffNames = [];

            for (int i = 1; i < BuffID.Count; i++) {
                string buffName = Lang.GetBuffName(i);
                if (string.IsNullOrWhiteSpace(buffName)) {
                    continue;
                }

                buffNames[i] = buffName.Trim();
            }

            return buffNames;
        }

        private static Dictionary<int, string> BuildPrefixNames() {
            Dictionary<int, string> prefixNames = [];

            for (int i = 1; i < PrefixID.Count; i++) {
                string prefixName = Lang.prefix[i].Value?.Trim() ?? string.Empty;
                if (prefixName.Length == 0) {
                    continue;
                }

                prefixNames[i] = prefixName;
            }

            return prefixNames;
        }
    }
}

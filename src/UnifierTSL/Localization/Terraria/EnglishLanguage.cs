using ReLogic.Utilities;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Terraria;
using Terraria.Chat.Commands;
using Terraria.Localization;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.Localization.Terraria
{
    public static class EnglishLanguage
    {
        private static readonly LanguageManager Language;
        private static readonly Dictionary<string, string> CommandID2EnglishText = [];
        static EnglishLanguage() {
            Language = new();
            Language.LoadFilesForCulture(GameCulture.FromCultureName(GameCulture.CultureName.English));
            Language.ProcessCopyCommandsInTexts();

            var stream = File.OpenRead(typeof(Main).Assembly.Location);
            var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            var attributes = MetadataBlobHelpers.ExtractCustomAttributeOnSpecificTypes(peReader, "Terraria.Chat.Commands", nameof(ChatCommandAttribute));

            foreach (var attribute in attributes) {
                if (AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out var parsedAttr)) {
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

        public static void Load() {

        }
        public static string GetCommandPrefixByName(string commandName) {
            if (CommandID2EnglishText.TryGetValue(commandName, out string? value)) {
                return value;
            }
            return "";
        }
    }
}

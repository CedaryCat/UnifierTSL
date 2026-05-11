global using static Atelier.I18n;

using GetText;
using UnifierTSL;

namespace Atelier;

internal static class I18n
{
    public static Catalog C = new(
        nameof(Atelier),
        UnifierApi.TranslationsDirectory,
        UnifierApi.TranslationCultureInfo);

    public static string GetString(FormattableStringAdapter text) {
        return C.GetString(text);
    }

    public static string GetString(FormattableString text) {
        return C.GetString(text);
    }

    public static string GetString(FormattableStringAdapter text, params object?[] args) {
        return C.GetString(text, args);
    }
}

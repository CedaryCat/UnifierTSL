global using static UnifierTSL.I18n;
using GetText;

namespace UnifierTSL
{
    internal static class I18n
    {

        public static Catalog C = new(
            nameof(UnifierTSL),
            UnifierApi.TranslationsDirectory,
            UnifierApi.TranslationCultureInfo);

        #region ICatalog forwarding methods
        /// <summary>
        /// Returns <paramref name="text"/> translated into the selected language.
        /// Similar to <c>gettext</c> function.
        /// </summary>
        /// <param name="text">Text to translate.</param>
        /// <returns>Translated text.</returns>
        static string GetString(FormattableStringAdapter text) {
            return C.GetString(text);
        }

        /// <summary>
        /// Returns <paramref name="text"/> translated into the selected language.
        /// Similar to <c>gettext</c> function.
        /// </summary>
        /// <param name="text">Text to translate.</param>
        /// <returns>Translated text.</returns>
        public static string GetString(FormattableString text) {
            return C.GetString(text);
        }

        /// <summary>
        /// Returns <paramref name="text"/> translated into the selected language.
        /// Similar to <c>gettext</c> function.
        /// </summary>
        /// <param name="text">Text to translate.</param>
        /// <param name="args">Optional arguments for <see cref="string.Format(string, object?[])"/> method.</param>
        /// <returns>Translated text.</returns>
        public static string GetString(FormattableStringAdapter text, params object?[] args) {
            return C.GetString(text, args);
        }

        /// <summary>
        /// Returns the plural form for <paramref name="n"/> of the translation of <paramref name="text"/>.
        /// Similar to <c>gettext</c> function.
        /// </summary>
        /// <param name="text">Singular form of message to translate.</param>
        /// <param name="pluralText">Plural form of message to translate.</param>
        /// <param name="n">Value that determines the plural form.</param>
        /// <returns>Translated text.</returns>
        public static string GetPluralString(FormattableStringAdapter text, FormattableStringAdapter pluralText, long n) {
            return C.GetPluralString(text, pluralText, n);
        }

        /// <summary>
        /// Returns the plural form for <paramref name="n"/> of the translation of <paramref name="text"/>.
        /// Similar to <c>gettext</c> function.
        /// </summary>
        /// <param name="text">Singular form of message to translate.</param>
        /// <param name="pluralText">Plural form of message to translate.</param>
        /// <param name="n">Value that determines the plural form.</param>
        /// <returns>Translated text.</returns>
        public static string GetPluralString(FormattableString text, FormattableString pluralText, long n) {
            return C.GetPluralString(text, pluralText, n);
        }

        /// <summary>
        /// Returns the plural form for <paramref name="n"/> of the translation of <paramref name="text"/>.
        /// Similar to <c>gettext</c> function.
        /// </summary>
        /// <param name="text">Singular form of message to translate.</param>
        /// <param name="pluralText">Plural form of message to translate.</param>
        /// <param name="n">Value that determines the plural form.</param>
        /// <param name="args">Optional arguments for <see cref="string.Format(string, object[])"/> method.</param>
        /// <returns>Translated text.</returns>
        public static string GetPluralString(FormattableStringAdapter text, FormattableStringAdapter pluralText, long n, params object[] args) {
            return C.GetPluralString(text, pluralText, n, args);
        }

        /// <summary>
        /// Returns <paramref name="text"/> translated into the selected language using given <paramref name="context"/>.
        /// Similar to <c>pgettext</c> function.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">Text to translate.</param>
        /// <returns>Translated text.</returns>
        public static string GetParticularString(string context, FormattableStringAdapter text) {
            return C.GetParticularString(context, text);
        }

        /// <summary>
        /// Returns <paramref name="text"/> translated into the selected language using given <paramref name="context"/>.
        /// Similar to <c>pgettext</c> function.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">Text to translate.</param>
        /// <returns>Translated text.</returns>
        public static string GetParticularString(string context, FormattableString text) {
            return C.GetParticularString(context, text);
        }

        /// <summary>
        /// Returns <paramref name="text"/> translated into the selected language using given <paramref name="context"/>.
        /// Similar to <c>pgettext</c> function.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">Text to translate.</param>
        /// <param name="args">Optional arguments for <see cref="string.Format(string, object[])"/> method.</param>
        /// <returns>Translated text.</returns>
        public static string GetParticularString(string context, FormattableStringAdapter text, params object[] args) {
            return C.GetParticularString(context, text, args);
        }

        /// <summary>
        /// Returns the plural form for <paramref name="n"/> of the translation of <paramref name="text"/> using given <paramref name="context"/>.
        /// Similar to <c>npgettext</c> function.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">Singular form of message to translate.</param>
        /// <param name="pluralText">Plural form of message to translate.</param>
        /// <param name="n">Value that determines the plural form.</param>
        /// <returns>Translated text.</returns>
        public static string GetParticularPluralString(string context, FormattableStringAdapter text, FormattableStringAdapter pluralText, long n) {
            return C.GetParticularPluralString(context, text, pluralText, n);
        }

        /// <summary>
        /// Returns the plural form for <paramref name="n"/> of the translation of <paramref name="text"/> using given <paramref name="context"/>.
        /// Similar to <c>npgettext</c> function.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">Singular form of message to translate.</param>
        /// <param name="pluralText">Plural form of message to translate.</param>
        /// <param name="n">Value that determines the plural form.</param>
        /// <returns>Translated text.</returns>
        public static string GetParticularPluralString(string context, FormattableString text, FormattableString pluralText, long n) {
            return C.GetParticularPluralString(context, text, pluralText, n);
        }

        /// <summary>
        /// Returns the plural form for <paramref name="n"/> of the translation of <paramref name="text"/> using given <paramref name="context"/>.
        /// Similar to <c>npgettext</c> function.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">Singular form of message to translate.</param>
        /// <param name="pluralText">Plural form of message to translate.</param>
        /// <param name="n">Value that determines the plural form.</param>
        /// <param name="args">Optional arguments for <see cref="string.Format(string, object[])"/> method.</param>
        /// <returns>Translated text.</returns>
        public static string GetParticularPluralString(string context, FormattableStringAdapter text, FormattableStringAdapter pluralText, long n, params object[] args) {
            return C.GetParticularPluralString(context, text, pluralText, n, args);
        }
        #endregion
    }
}


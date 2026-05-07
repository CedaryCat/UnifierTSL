/*
TShock, a server mod for Terraria
Copyright (C) 2011-2019 Pryaxis & TShock Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

namespace TShockAPI.Localization
{
    /// <summary>
    /// Compatibility facade over the shared UnifierTSL English localization provider.
    /// </summary>
    public static class EnglishLanguage
    {
        internal static void Initialize() { }

        public static string? GetItemNameById(int id) => UnifierTSL.Localization.Terraria.EnglishLanguage.GetItemNameById(id);

        public static string? GetNpcNameById(int id) => UnifierTSL.Localization.Terraria.EnglishLanguage.GetNpcNameById(id);

        public static string? GetBuffNameById(int id) => UnifierTSL.Localization.Terraria.EnglishLanguage.GetBuffNameById(id);

        public static string? GetPrefixById(int id) => UnifierTSL.Localization.Terraria.EnglishLanguage.GetPrefixById(id);

        public static string GetCommandPrefixByName(string name) => UnifierTSL.Localization.Terraria.EnglishLanguage.GetCommandPrefixByName(name);
    }
}

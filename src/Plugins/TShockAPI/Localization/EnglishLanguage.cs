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

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Initializers;
using Terraria.Localization;
using Terraria.UI.Chat;

namespace TShockAPI.Localization
{
	/// <summary>
	/// Provides a series of methods that give English texts
	/// </summary>
	public static class EnglishLanguage
	{
		private static readonly Dictionary<int, string> ItemNames = new Dictionary<int, string>();

		private static readonly Dictionary<int, string> NpcNames = new Dictionary<int, string>();

		private static readonly Dictionary<int, string> Prefixs = new Dictionary<int, string>();

		private static readonly Dictionary<int, string> Buffs = new Dictionary<int, string>();

		private static readonly Dictionary<string,string> VanillaCommandsPrefixs = new Dictionary<string, string>();

		internal static void Initialize()
		{
			var culture = Language.ActiveCulture;

			var skip = culture == GameCulture.FromCultureName(GameCulture.CultureName.English);

			try
			{
				if (!skip)
				{
					LanguageManager.Instance.SetLanguage(GameCulture.FromCultureName(GameCulture.CultureName.English));
				}

				for (var i = -48; i < Terraria.ID.ItemID.Count; i++)
				{
					ItemNames.Add(i, Lang.GetItemNameValue(i));
				}

				for (var i = -17; i < Terraria.ID.NPCID.Count; i++)
				{
					NpcNames.Add(i, Lang.GetNPCNameValue(i));
				}

				for (var i = 0; i < Terraria.ID.BuffID.Count; i++)
				{
					Buffs.Add(i, Lang.GetBuffName(i));
				}

				foreach (var field in typeof(PrefixID)
							.GetFields().Where(f => !f.Name.Equals("Count", StringComparison.Ordinal)))
				{
					var i = (int)field.GetValue(null)!;
					Prefixs.Add(i, Lang.prefix[i].Value);
				}

				ChatInitializer.Load();
				foreach (var command in ChatManager.Commands._localizedCommands)
				{
					if (VanillaCommandsPrefixs.ContainsKey(command.Value._name))
						continue;
					VanillaCommandsPrefixs.Add(command.Value._name,command.Key.Value);
				}
				ChatManager.Commands._localizedCommands.Clear();
			}
			finally
			{
				if (!skip)
				{
					LanguageManager.Instance.SetLanguage(culture);
				}
			}
		}

		/// <summary>
		/// Get the english name of an item
		/// </summary>
		/// <param name="id">Id of the item</param>
		/// <returns>Item name in English</returns>
		public static string? GetItemNameById(int id)
		{
			if (ItemNames.TryGetValue(id, out var itemName))
				return itemName;

			return null;
		}

		/// <summary>
		/// Get the english name of a npc
		/// </summary>
		/// <param name="id">Id of the npc</param>
		/// <returns>Npc name in English</returns>
		public static string? GetNpcNameById(int id)
		{
			if (NpcNames.TryGetValue(id, out var npcName))
				return npcName;

			return null;
		}

		/// <summary>
		/// Get prefix in English
		/// </summary>
		/// <param name="id">Prefix Id</param>
		/// <returns>Prefix in English</returns>
		public static string? GetPrefixById(int id)
		{
			if (Prefixs.TryGetValue(id, out var prefix))
				return prefix;

			return null;
		}

		/// <summary>
		/// Get buff name in English
		/// </summary>
		/// <param name="id">Buff Id</param>
		/// <returns>Buff name in English</returns>
		public static string? GetBuffNameById(int id)
		{
			if (Buffs.TryGetValue(id, out var buff))
				return buff;

			return null;
		}

		/// <summary>
		/// Get vanilla command prefix in English
		/// </summary>
		/// <param name="name">vanilla command name</param>
		/// <returns>vanilla command prefix in English</returns>
		public static string? GetCommandPrefixByName(string name)
		{
			if (VanillaCommandsPrefixs.TryGetValue(name, out var commandText))
				return commandText;
			return null;
		}
	}
}

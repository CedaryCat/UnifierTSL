using TrProtocol.Models;
using TrProtocol.NetPackets.Modules;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Provides handling for the Creative Power net module. Checks permissions on all creative powers
	/// </summary>
	public class CreativePowerHandler : IPacketHandler<NetCreativePowersModule>
	{

		/// <summary>
		/// Determines if a player has permission to use a specific creative power
		/// </summary>
		/// <param name="powerType"></param>
		/// <param name="player"></param>
		/// <returns></returns>
		public static bool HasPermission(ServerContext server, CreativePowerTypes powerType, TSPlayer player)
		{
			if (!PowerToPermissionMap.TryGetValue(powerType, out string? permission))
			{
                server.Log.Debug(GetString("CreativePowerHandler received permission check request for unknown creative power"));
				return false;
			}

            //prevent being told about the spawnrate permission on join until relogic fixes
            if (!player.HasReceivedNPCPermissionError && powerType == CreativePowerTypes.SetSpawnRate)
			{
				player.HasReceivedNPCPermissionError = true;
				return false;
			}

			if (!player.HasPermission(permission))
			{
				player.SendErrorMessage(PermissionToDescriptionMap[permission]);
				return false;
			}

			return true;
		}

        public void OnReceive(ref RecievePacketEvent<NetCreativePowersModule> args) {
			if (!HasPermission(args.LocalReciever.Server, args.Packet.PowerType, args.GetTSPlayer())) {
				args.HandleMode = PacketHandleMode.Cancel;
			}
        }


        /// <summary>
        /// Maps creative powers to permission nodes
        /// </summary>
        public static Dictionary<CreativePowerTypes, string> PowerToPermissionMap = new Dictionary<CreativePowerTypes, string>
		{
			{ CreativePowerTypes.FreezeTime,              Permissions.journey_timefreeze		},
			{ CreativePowerTypes.SetDawn,                 Permissions.journey_timeset			},
			{ CreativePowerTypes.SetNoon,                 Permissions.journey_timeset			},
			{ CreativePowerTypes.SetDusk,                 Permissions.journey_timeset			},
			{ CreativePowerTypes.SetMidnight,             Permissions.journey_timeset			},
			{ CreativePowerTypes.Godmode,                 Permissions.journey_godmode			},
			{ CreativePowerTypes.WindStrength,            Permissions.journey_windstrength		},
			{ CreativePowerTypes.RainStrength,            Permissions.journey_rainstrength		},
			{ CreativePowerTypes.TimeSpeed,               Permissions.journey_timespeed			},
			{ CreativePowerTypes.RainFreeze,              Permissions.journey_rainfreeze		},
			{ CreativePowerTypes.WindFreeze,              Permissions.journey_windfreeze		},
			{ CreativePowerTypes.IncreasePlacementRange,  Permissions.journey_placementrange	},
			{ CreativePowerTypes.WorldDifficulty,         Permissions.journey_setdifficulty		},
			{ CreativePowerTypes.BiomeSpreadFreeze,       Permissions.journey_biomespreadfreeze },
			{ CreativePowerTypes.SetSpawnRate,            Permissions.journey_setspawnrate		},
		};

		/// <summary>
		/// Maps journey mode permission nodes to descriptions of what the permission allows
		/// </summary>
		public static Dictionary<string, string> PermissionToDescriptionMap = new Dictionary<string, string>
		{
			{ Permissions.journey_timefreeze,			GetString("You do not have permission to freeze the time of the server.")						},
			{ Permissions.journey_timeset,				GetString("You do not have permission to modify the time of the server.")						},
			{ Permissions.journey_godmode,				GetString("You do not have permission to toggle godmode.")										},
			{ Permissions.journey_windstrength,			GetString("You do not have permission to modify the wind strength of the server.")				},
			{ Permissions.journey_rainstrength,			GetString("You do not have permission to modify the rain strength of the server.")				},
			{ Permissions.journey_timespeed,			GetString("You do not have permission to modify the time speed of the server.")					},
			{ Permissions.journey_rainfreeze,			GetString("You do not have permission to freeze the rain strength of the server.")				},
			{ Permissions.journey_windfreeze,			GetString("You do not have permission to freeze the wind strength of the server.")				},
			{ Permissions.journey_placementrange,		GetString("You do not have permission to modify the tile placement range of your character.") 	},
			{ Permissions.journey_setdifficulty,		GetString("You do not have permission to modify the world difficulty of the server.")			},
			{ Permissions.journey_biomespreadfreeze,	GetString("You do not have permission to freeze the biome spread of the server.")				},
			{ Permissions.journey_setspawnrate,			GetString("You do not have permission to modify the NPC spawn rate of the server.")				},
		};
	}
}

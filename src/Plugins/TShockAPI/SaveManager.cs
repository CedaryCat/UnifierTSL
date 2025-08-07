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
using Microsoft.Xna.Framework;
using NuGet.Protocol.Plugins;
using System.Threading.Channels;
using UnifierTSL.Servers;

namespace TShockAPI
{

    public static class SaveManager
    {

        private static readonly Channel<SaveTask?> _channel;
        private static readonly Task _saveWorkerTask;

        static SaveManager() {
            _channel = Channel.CreateUnbounded<SaveTask?>();
            _saveWorkerTask = Task.Run(SaveWorkerAsync);

            On.Terraria.IO.WorldFileSystemContext.SaveWorld_bool_bool += OnSaveWorld;
            TShock.DisposingEvent += Dispose;
        }

        private static void OnSaveWorld(On.Terraria.IO.WorldFileSystemContext.orig_SaveWorld_bool_bool orig, 
            Terraria.IO.WorldFileSystemContext self, 
            bool useCloudSaving,
            bool resetTime) {

            if (self.root is ServerContext server) {
                OnSaveWorld(server);
            }
            orig(self, useCloudSaving, resetTime);
        }

        private static void Dispose() {
            _channel.Writer.TryWrite(null); // Signal exit
            _saveWorkerTask.Wait();

            On.Terraria.IO.WorldFileSystemContext.SaveWorld_bool_bool -= OnSaveWorld;
        }

        public static void OnSaveWorld(ServerContext server) {
            var setting = TShock.Config.GetServerSettings(server.Name);
            if (setting.AnnounceSave) {
                try {
                    Utils.Broadcast(server, GetString("Saving world..."), Color.Yellow);
                }
                catch (Exception ex) {
                    server.Log.Error("World saved notification failed");
                    server.Log.Error(ex.ToString());
                }
            }
        }

        public static void SaveWorld(ServerContext server, bool wait = true, bool resetTime = false, bool direct = false) {
            var task = new SaveTask(server, resetTime, direct);
            _channel.Writer.TryWrite(task);

            if (wait) {
                while (_channel.Reader.Count > 0) {
                    Task.Delay(50).Wait();
                }
            }
        }

        static async Task SaveWorkerAsync() {
            await foreach (var task in _channel.Reader.ReadAllAsync()) {
                if (task == null)
                    break;

                var server = task.Server;
                try {

                    if (task.Direct) {
                        OnSaveWorld(server);
                        server.WorldFile.SaveWorld(task.ResetTime);
                    }
                    else {
                        server.WorldFile.SaveWorld(task.ResetTime);
                    }

                    var setting = TShock.Config.GetServerSettings(server.Name);

                    if (setting.AnnounceSave)
                        Utils.Broadcast(server, GetString("World saved."), Color.Yellow);

                    server.Log.Info(GetString("World saved at ({0})", server.Main.worldPathName));
                }
                catch (Exception e) {
                    server.Log.Error("World saved failed");
                    server.Log.Error(e.ToString());
                }
            }
        }

        record SaveTask(ServerContext Server, bool ResetTime, bool Direct)
        {
            public override string ToString() {
                return GetString("ResetTime {0}, Direct {1}", ResetTime, Direct);
            }
        }
    }
}

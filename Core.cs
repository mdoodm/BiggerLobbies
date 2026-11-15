using System;
using System.Collections.Generic;
using BiggerLobbies.Integrations;
using BiggerLobbies.Utils;
using MelonLoader;

[assembly: MelonInfo(typeof(BiggerLobbies.Core), Constants.MOD_NAME, Constants.MOD_VERSION, Constants.MOD_AUTHOR)]
[assembly: MelonGame(Constants.Game.GAME_STUDIO, Constants.Game.GAME_NAME)]
[assembly: MelonIncompatibleAssemblies("ModManager&PhoneApp")]

namespace BiggerLobbies
{
    public class Core : MelonMod
    {
        public override void OnInitializeMelon()
        {
            InitFeatures();
        }

        // Composition root. The order matters: it is the order preferences are created in the config file.
        private static void InitFeatures()
        {
            var cat = MelonPreferences.CreateCategory("BiggerLobbies");
            Utils.Log.Init(cat);
            LobbySize.Init(cat);
            DebugDummies.Init(cat);
            LobbyPrivacy.Init(cat);
            BanList.Init(cat);
            Whitelist.Init(cat);
            Crews.Init(cat);
            ModHandshake.Init(cat);
            DiscordPresence.Init(cat);
            HostSettingsMenu.Init(cat);
            DepartedPlayers.InitLeftWith(cat);
            AutoSave.Init(cat);
            GrowSyncFix.Init(cat);
            LateJoinFix.Init(cat);
            AlwaysHostLobby.Init(cat);
        }

        private static readonly (string Name, Action Run)[] Updates =
        {
            ("AutoSave", AutoSave.Update),
            ("DebugDummies", DebugDummies.Update),
            ("DepartedPlayers", DepartedPlayers.Update),
            ("DepartedPlayers.LeftWith", DepartedPlayers.UpdateLeftWith),
            ("HostSettingsMenu", HostSettingsMenu.Update),
            ("DiscordPresence", DiscordPresence.Update),
        };
        private static readonly Dictionary<string, float> _lastError = new();

        // Each feature runs on its own so one failing doesn't stop the rest; errors are logged at most once per 10 s per feature.
        public override void OnUpdate()
        {
            foreach (var (name, run) in Updates)
            {
                try { run(); }
                catch (Exception e)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (_lastError.TryGetValue(name, out var last) && now - last < 10f) continue;
                    _lastError[name] = now;
                    MelonLogger.Error($"{name} update failed: {e}");
                }
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            DebugDummies.OnSceneLoaded(sceneName);
            HostSettingsMenu.OnSceneLoaded(sceneName);
        }

        public override void OnApplicationQuit()
        {
            DiscordPresence.Shutdown();
        }
    }
}

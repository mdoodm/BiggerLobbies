using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BiggerLobbies.Integrations;
using BiggerLobbies.Utils;
using MelonLoader;
using Constants = BiggerLobbies.Utils.Constants; // Steamworks has a Constants class too

[assembly: MelonInfo(typeof(BiggerLobbies.Core), Constants.MELON_NAME, Constants.MOD_VERSION, Constants.MOD_AUTHOR)]
[assembly: MelonGame(Constants.Game.GAME_STUDIO, Constants.Game.GAME_NAME)]
[assembly: MelonIncompatibleAssemblies("ModManager&PhoneApp")]
// Each build declares its backend so MelonLoader skips the other build's DLL before any of its code runs.
#if MONO
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.MONO)]
// These are named Il2Cpp* on the default branch. Without this, MelonLoader warns there that the
// Mono DLL is missing them (and says that warning will become a load error in later versions).
[assembly: MelonOptionalDependencies("FishNet.Runtime", "com.rlabrecque.steamworks.net", "ScheduleOne.Core")]
#else
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
#endif
// Patches are applied in OnInitializeMelon, after the backend check, instead of automatically on load.
[assembly: HarmonyDontPatchAll]

namespace BiggerLobbies
{
    public class Core : MelonMod
    {
#if MONO
        private const bool BuiltForIl2Cpp = false;
#else
        private const bool BuiltForIl2Cpp = true;
#endif
        // False when this DLL doesn't match the game's backend. Every callback checks it first.
        private static bool _active;

        // This method and the guard must only touch MelonLoader/BCL types: on the wrong backend,
        // anything referencing game types throws a type-load error when it is compiled.
        public override void OnInitializeMelon()
        {
            if (MelonUtils.IsGameIl2Cpp() != BuiltForIl2Cpp)
            {
                string want = BuiltForIl2Cpp ? "Il2Cpp (default)" : "Mono (alternate)";
                string have = MelonUtils.IsGameIl2Cpp() ? "Il2Cpp (default)" : "Mono (alternate)";
                MelonLogger.Warning($"This is the {want} build of BiggerLobbies, but the game is the {have} branch. It won't load. Use the other BiggerLobbies DLL.");
                return;
            }
            // Set before InitFeatures so updates keep running if one feature's init throws, as before.
            _active = true;
            HarmonyInstance.PatchAll(typeof(Core).Assembly);
            InitFeatures();
        }

        // Composition root. The order matters: it is the order preferences are created in the config file.
        [MethodImpl(MethodImplOptions.NoInlining)]
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
            CrossBranchTest.Init(cat);
            CrossBranchLobby.Init(cat);
            BuildUpdates();
        }

        // Built in InitFeatures, not as a static initializer: the delegates load feature types,
        // which would fail in Core's static constructor on the wrong backend, before the guard runs.
        private static (string Name, Action Run)[] Updates = Array.Empty<(string, Action)>();
        private static readonly Dictionary<string, float> _lastError = new();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BuildUpdates()
        {
            Updates = new (string Name, Action Run)[]
            {
                ("AutoSave", AutoSave.Update),
                ("CrossBranchTest", CrossBranchTest.Update),
                ("CrossBranchLobby", CrossBranchLobby.Update),
                ("DebugDummies", DebugDummies.Update),
                ("DepartedPlayers", DepartedPlayers.Update),
                ("DepartedPlayers.LeftWith", DepartedPlayers.UpdateLeftWith),
                ("HostSettingsMenu", HostSettingsMenu.Update),
                ("DiscordPresence", DiscordPresence.Update),
            };
        }

        // The callbacks below only check _active and call out, so they compile safely on the wrong backend.
        public override void OnUpdate()
        {
            if (_active) RunUpdates();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (_active) SceneLoaded(sceneName);
        }

        public override void OnApplicationQuit()
        {
            if (_active) Quit();
        }

        // Each feature runs on its own so one failing doesn't stop the rest; errors are logged at most once per 10 s per feature.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunUpdates()
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SceneLoaded(string sceneName)
        {
            DebugDummies.OnSceneLoaded(sceneName);
            HostSettingsMenu.OnSceneLoaded(sceneName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Quit()
        {
            DiscordPresence.Shutdown();
        }
    }
}

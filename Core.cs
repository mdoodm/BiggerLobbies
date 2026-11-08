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
        public static Core? Instance { get; private set; }

        public override void OnInitializeMelon()
        {
            Instance = this;
            HarmonyPatches.SetModInstance(this);
#if !MONO
            AutoSave.Init();
#endif
        }

#if !MONO
        public override void OnUpdate()
        {
            AutoSave.Update();
            DebugDummies.Update();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            DebugDummies.OnSceneLoaded(sceneName);
        }
#endif

        public override void OnApplicationQuit()
        {
            Instance = null;
        }
    }
}
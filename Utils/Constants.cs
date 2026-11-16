namespace BiggerLobbies.Utils
{
    public static class Constants
    {
        /// <summary>
        /// Mod information
        /// </summary>
        public const string MOD_NAME = "BiggerLobbies";
        /// <summary>The name MelonLoader shows. Each build needs its own: MelonLoader sorts mods by
        /// this name, and a second mod with the same name is dropped before its backend is checked,
        /// so with both DLLs in Mods whichever loaded first would win, even the wrong one.</summary>
#if MONO
        public const string MELON_NAME = MOD_NAME + " (Mono)";
#else
        public const string MELON_NAME = MOD_NAME;
#endif
        public const string MOD_VERSION = "2.2.0";
        public const string MOD_AUTHOR = "Bars";

        /// <summary>Shown in the bottom-left corner of the screen and of Host settings.</summary>
        public const string CREDIT = MOD_NAME + " V" + MOD_VERSION + " by Mdoodm";

        /// <summary>
        /// Game-related constants
        /// </summary>
        public static class Game
        {
            public const string GAME_STUDIO = "TVGS";
            public const string GAME_NAME = "Schedule I";
        }

        /// <summary>
        /// Default (and tested) lobby size. The Il2Cpp build reads the MaxPlayers setting instead (LobbySize.MaxPlayers).
        /// </summary>
        public const int MAX_PLAYERS = 20;

        /// <summary>
        /// The game's own lobby limit (Lobby.PlayerLimit is a constant in IL2CPP and must not be written)
        /// </summary>
        public const int VANILLA_LIMIT = 4;

    }
}

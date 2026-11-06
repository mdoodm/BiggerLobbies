namespace BiggerLobbies.Utils
{
    public static class Constants
    {
        /// <summary>
        /// Mod information
        /// </summary>
        public const string MOD_NAME = "BiggerLobbies";
        public const string MOD_VERSION = "1.0.3";
        public const string MOD_AUTHOR = "Bars";

        /// <summary>
        /// Game-related constants
        /// </summary>
        public static class Game
        {
            public const string GAME_STUDIO = "TVGS";
            public const string GAME_NAME = "Schedule I";
        }

        /// <summary>
        /// Maximum player limit for lobbies
        /// </summary>
        public const int MAX_PLAYERS = 20;

        /// <summary>
        /// The game's own lobby limit (Lobby.PlayerLimit is a constant in IL2CPP and must not be written)
        /// </summary>
        public const int VANILLA_LIMIT = 4;

    }
}

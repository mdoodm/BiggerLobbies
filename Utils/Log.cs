using MelonLoader;

namespace BiggerLobbies.Utils
{
    // The mod stays quiet in the MelonLoader log: warnings and errors always show,
    // everything else only when DiagnosticLog is on.
    public static class Log
    {
        private static MelonPreferences_Entry<bool>? _diag;

        public static void Init(MelonPreferences_Category cat)
        {
            _diag = cat.CreateEntry("DiagnosticLog", false, "Diagnostic log",
                "Writes detailed info lines to the MelonLoader log (lobby events, kicks, bans, saves). Leave off unless you're reporting a problem. Warnings and errors are always logged.");
        }

        public static bool Verbose => _diag?.Value ?? false;

        public static void Info(string s) { if (Verbose) MelonLogger.Msg(s); }
    }
}

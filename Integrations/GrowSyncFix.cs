#if !MONO
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Vanilla netcode gap: pot moisture and growth are simulated locally on every machine
// and the host only corrects clients on some events. Two server-only paths never tell
// clients: botanist watering and sleep/time-skip moisture drain. Clients then show a
// stale "needs water" icon / frozen growth %, or never show "needs water".
// Host-only fix: push the host's values to clients after those events, and resync
// every pot periodically. Clients don't need the mod.
public static class GrowSyncFix
{
    private static MelonPreferences_Entry<bool>? _enabled;
    private static MelonPreferences_Entry<int>? _minutes;

    public static void Init(MelonPreferences_Category cat)
    {
        _enabled = cat.CreateEntry("PlantSyncEnabled", true, "Plant sync enabled",
            "As host, send pot water level and growth progress to clients so everyone sees the same values.");
        _minutes = cat.CreateEntry("PlantSyncMinutes", 30, "Plant resync interval (in-game minutes)");
    }

    private static bool Server
    {
        get
        {
            try { return _enabled != null && _enabled.Value && InstanceFinder.IsServer; }
            catch { return false; }
        }
    }

    private static void PushMoisture(GrowContainer? c)
    {
        if (c == null) return;
        c.SetMoistureData_Client(null, c._currentMoistureAmount); // null conn = send to all observers
    }

    // Botanist watering sets moisture on the server only.
    [HarmonyPatch(typeof(WaterPotBehaviour), nameof(WaterPotBehaviour.OnActionSuccess))]
    private static class WaterPotPatch
    {
        private static void Postfix(WaterPotBehaviour __instance)
        {
            if (!Server) return;
            try { PushMoisture(__instance._growContainer); }
            catch (Exception e) { MelonLogger.Error($"Plant sync (botanist water) failed: {e}"); }
        }
    }

    // Sleep / time skip drains moisture on the server only.
    [HarmonyPatch(typeof(GrowContainer), nameof(GrowContainer.OnTimeSkipped))]
    private static class TimeSkipPatch
    {
        private static void Postfix(GrowContainer __instance)
        {
            if (!Server) return;
            try { PushMoisture(__instance); }
            catch (Exception e) { MelonLogger.Error($"Plant sync (time skip) failed: {e}"); }
        }
    }

    // Periodic resync of moisture + growth progress, staggered per pot.
    [HarmonyPatch(typeof(Pot), nameof(Pot.OnMinPass))]
    private static class MinPassPatch
    {
        private static readonly Dictionary<int, int> Ticks = new();

        private static void Postfix(Pot __instance)
        {
            if (!Server) return;
            int every = _minutes?.Value ?? 30;
            if (every <= 0) return;
            try
            {
                int id = __instance.GetInstanceID();
                Ticks.TryGetValue(id, out int n);
                if (++n < every) { Ticks[id] = n; return; }
                Ticks[id] = (id & 0x7fffffff) % 7; // stagger so pots don't all send on the same minute

                PushMoisture(__instance);
                var plant = __instance.Plant;
                if (plant != null) __instance.SetGrowthProgress_Client(plant.NormalizedGrowthProgress);
            }
            catch (Exception e) { MelonLogger.Error($"Plant sync (resync) failed: {e}"); }
        }
    }
}

#endif

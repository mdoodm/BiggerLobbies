#if MONO
// Original patches, kept for the Mono build only. Not updated or tested for game 0.4.7.
// The IL2CPP build uses Il2CppLobbyPatches.cs / LobbyDedupePatches.cs instead.
#if MONO
using ScheduleOne.Networking;
using ScheduleOne.UI.Multiplayer;
using ScheduleOne.DevUtilities;
using Steamworks;
#else
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.UI.Multiplayer;
using Il2CppScheduleOne.DevUtilities;
using Il2CppSteamworks;
using Il2Cpp;
#endif
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Constants = BiggerLobbies.Utils.Constants;

namespace BiggerLobbies.Integrations
{
    [HarmonyPatch]
    public static class HarmonyPatches
    {
        private static Core? _modInstance;

        /// <summary>
        /// Set the mod instance for patch callbacks
        /// </summary>
        public static void SetModInstance(Core modInstance)
        {
            _modInstance = modInstance;
        }

        #region Lobby Patches

        /// <summary>
        /// Patch Lobby.Start() to resize Players array to MAX_PLAYERS
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Lobby), "Start")]
        private static void Lobby_Start_Postfix(Lobby __instance)
        {
            try
            {
                if (__instance.Players.Length != Constants.MAX_PLAYERS)
                {
                    CSteamID[] newPlayers = new CSteamID[Constants.MAX_PLAYERS];
                    int copyLength = Math.Min(__instance.Players.Length, Constants.MAX_PLAYERS);
                    Array.Copy(__instance.Players, newPlayers, copyLength);
                    __instance.Players = newPlayers;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to resize Lobby.Players array: {ex}");
            }
        }

        /// <summary>
        /// Patch Lobby.CreateLobby() to use MAX_PLAYERS instead of hardcoded 4
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Lobby), "CreateLobby")]
        private static bool Lobby_CreateLobby_Prefix(Lobby __instance)
        {
            try
            {
                SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Constants.MAX_PLAYERS);
                return false; // Skip original method
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to patch CreateLobby: {ex}");
                return true; // Run original method on error
            }
        }

        /// <summary>
        /// Patch Lobby.TryOpenInviteInterface() to check MAX_PLAYERS capacity
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Lobby), "TryOpenInviteInterface")]
        private static bool Lobby_TryOpenInviteInterface_Prefix(Lobby __instance)
        {
            try
            {
                if (!__instance.IsInLobby)
                {
                    Utils.Log.Info("Not currently in a lobby, creating one...");
                    SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Constants.MAX_PLAYERS);
                }
                
                int currentMembers = SteamMatchmaking.GetNumLobbyMembers(__instance.LobbySteamID);
                if (currentMembers >= Constants.MAX_PLAYERS)
                {
                    MelonLogger.Warning("Lobby already at max capacity!");
                    return false; // Skip original method
                }
                else
                {
                    SteamFriends.ActivateGameOverlayInviteDialog(__instance.LobbySteamID);
                    return false; // Skip original method
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to patch TryOpenInviteInterface: {ex}");
                return true; // Run original method on error
            }
        }

        /// <summary>
        /// Patch Lobby.OnLobbyCreated() to set max_players and num_slots lobby data
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Lobby), "OnLobbyCreated")]
        private static void Lobby_OnLobbyCreated_Postfix(Lobby __instance, LobbyCreated_t result)
        {
            try
            {
                if (result.m_eResult == EResult.k_EResultOK)
                {
                    CSteamID lobbySteamID = (CSteamID)result.m_ulSteamIDLobby;
                    string maxPlayersStr = Constants.MAX_PLAYERS.ToString();
                    
                    // Set max_players and num_slots to MAX_PLAYERS
                    SteamMatchmaking.SetLobbyData(lobbySteamID, "max_players", maxPlayersStr);
                    SteamMatchmaking.SetLobbyData(lobbySteamID, "num_slots", maxPlayersStr);
                    
                    Utils.Log.Info($"Set lobby max_players and num_slots to {Constants.MAX_PLAYERS}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to set lobby max_players/num_slots: {ex}");
            }
        }

        #endregion

        #region LobbyInterface Patches

        /// <summary>
        /// Patch LobbyInterface.Awake() to start coroutine for dynamic UI slot creation
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LobbyInterface), "Awake")]
        private static void LobbyInterface_Awake_Postfix(LobbyInterface __instance)
        {
            try
            {
                if (_modInstance != null)
                {
                    MelonCoroutines.Start(WaitForLobby(__instance));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to start WaitForLobby coroutine: {ex}");
            }
        }

        /// <summary>
        /// Coroutine to wait for Lobby singleton and dynamically create UI slots
        /// </summary>
        private static IEnumerator WaitForLobby(LobbyInterface lobbyInterface)
        {
            // Wait for Lobby singleton to exist
            while (Singleton<Lobby>.Instance == null)
            {
                yield return null;
            }

            Lobby lobby = Singleton<Lobby>.Instance;
            
            // Resize Players array if needed
            if (lobby.Players.Length != Constants.MAX_PLAYERS)
            {
                CSteamID[] newPlayers = new CSteamID[Constants.MAX_PLAYERS];
                int copyLength = Math.Min(lobby.Players.Length, Constants.MAX_PLAYERS);
                Array.Copy(lobby.Players, newPlayers, copyLength);
                lobby.Players = newPlayers;
            }

            // Check Steam initialization
            if (!SteamManager.Initialized)
            {
                lobbyInterface.gameObject.SetActive(false);
                yield break;
            }

            // Find GridLayoutGroup in LobbyInterface
            GridLayoutGroup gridGroup = lobbyInterface.GetComponentInChildren<GridLayoutGroup>();
            if (gridGroup == null)
            {
                MelonLogger.Error("Could not find GridLayoutGroup in LobbyInterface");
                yield break;
            }

            Transform entries = gridGroup.transform;

            // Clone template entries if needed
            if (entries.childCount > 1)
            {
                Transform template = entries.GetChild(1);
                int slotsToAdd = Constants.MAX_PLAYERS - 4; // We need 16 more slots (20 - 4)

                for (int i = 0; i < slotsToAdd; i++)
                {
                    GameObject newEntry = UnityEngine.Object.Instantiate(template.gameObject, entries);
                    int newIndex = entries.childCount - 1;
                    newEntry.name = template.gameObject.name + " (" + newIndex.ToString() + ")";
                }

                // Move template to end
                template.SetSiblingIndex(Constants.MAX_PLAYERS);
            }

            // Update PlayerSlots array
            int slotCount = entries.childCount - 1; // Exclude template
            RectTransform[] newSlots = new RectTransform[slotCount];

            for (int j = 1; j < entries.childCount; j++)
            {
                newSlots[j - 1] = entries.GetChild(j).GetComponent<RectTransform>();
            }

            lobbyInterface.PlayerSlots = newSlots;

            // Update lobby title
            lobbyInterface.LobbyTitle.text = "Lobby (" + lobby.PlayerCount + "/" + Constants.MAX_PLAYERS + ")";

            lobbyInterface.enabled = true;
        }

        /// <summary>
        /// Patch LobbyInterface delegate in Awake() to use MAX_PLAYERS in title
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LobbyInterface), "Awake")]
        private static void LobbyInterface_Awake_Postfix_UpdateTitle(LobbyInterface __instance)
        {
            try
            {
                // Patch the onLobbyChange delegate to use MAX_PLAYERS
                if (__instance.Lobby != null)
                {
#if MONO
                    Action originalDelegate = __instance.Lobby.onLobbyChange;
                    __instance.Lobby.onLobbyChange = null;
                    
                    __instance.Lobby.onLobbyChange = () =>
                    {
                        // Call original delegate if it exists
                        if (originalDelegate != null)
                            originalDelegate();
                        else
                        {
                            // Fallback: call UpdateButtons and UpdatePlayers if delegate was null
                            __instance.UpdateButtons();
                            __instance.UpdatePlayers();
                        }
                        
                        // Update title with MAX_PLAYERS
                        __instance.LobbyTitle.text = "Lobby (" + __instance.Lobby.PlayerCount + "/" + Constants.MAX_PLAYERS + ")";
                    };
#else
                    Il2CppSystem.Action originalDelegate = __instance.Lobby.onLobbyChange;
                    __instance.Lobby.onLobbyChange = null;
                    
                    // Wrap in System.Action for implicit cast to Il2CppSystem.Action
                    __instance.Lobby.onLobbyChange = new System.Action(() =>
                    {
                        // Call original delegate if it exists
                        if (originalDelegate != null)
                            originalDelegate.Invoke();
                        else
                        {
                            // Fallback: call UpdateButtons and UpdatePlayers if delegate was null
                            __instance.UpdateButtons();
                            __instance.UpdatePlayers();
                        }
                        
                        // Update title with MAX_PLAYERS
                        __instance.LobbyTitle.text = "Lobby (" + __instance.Lobby.PlayerCount + "/" + Constants.MAX_PLAYERS + ")";
                    });
#endif
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to patch onLobbyChange delegate: {ex}");
            }
        }

        /// <summary>
        /// Patch LobbyInterface.UpdateButtons() to use MAX_PLAYERS
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LobbyInterface), "UpdateButtons")]
        private static void LobbyInterface_UpdateButtons_Postfix(LobbyInterface __instance)
        {
            try
            {
                __instance.InviteButton.gameObject.SetActive(__instance.Lobby.IsHost && __instance.Lobby.PlayerCount < Constants.MAX_PLAYERS);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to patch UpdateButtons: {ex}");
            }
        }

        #endregion
    }
}

#endif

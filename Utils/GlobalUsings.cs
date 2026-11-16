// Game and library namespaces for the whole project. The Il2Cpp game prefixes them with "Il2Cpp";
// the Mono game doesn't. Source files don't import game namespaces themselves.
global using BiggerLobbies.Utils; // Shim helpers (Shim.Bytes, .As<T>(), ...)
#if MONO
global using ScheduleOne.DevUtilities;
global using ScheduleOne.GameTime;
global using ScheduleOne.Growing;
global using ScheduleOne.ItemFramework;
global using ScheduleOne.Networking;
global using ScheduleOne.NPCs.Behaviour;
global using ScheduleOne.ObjectScripts;
global using ScheduleOne.Persistence;
global using ScheduleOne.Platform;
global using ScheduleOne.PlayerScripts;
global using ScheduleOne.UI;
global using ScheduleOne.UI.MainMenu;
global using ScheduleOne.UI.Multiplayer;
global using FishNet;
global using FishNet.Connection;
global using FishNet.Managing.Server;
global using FishNet.Transporting;
global using FishNet.Transporting.Multipass;
global using Steamworks;
global using TMPro;
// Namespace roots, for the few places that need a fully qualified game type.
global using S1 = ScheduleOne;
#else
global using Il2Cpp;
global using Il2CppScheduleOne.DevUtilities;
global using Il2CppScheduleOne.GameTime;
global using Il2CppScheduleOne.Growing;
global using Il2CppScheduleOne.ItemFramework;
global using Il2CppScheduleOne.Networking;
global using Il2CppScheduleOne.NPCs.Behaviour;
global using Il2CppScheduleOne.ObjectScripts;
global using Il2CppScheduleOne.Persistence;
global using Il2CppScheduleOne.Platform;
global using Il2CppScheduleOne.PlayerScripts;
global using Il2CppScheduleOne.UI;
global using Il2CppScheduleOne.UI.MainMenu;
global using Il2CppScheduleOne.UI.Multiplayer;
global using Il2CppFishNet;
global using Il2CppFishNet.Connection;
global using Il2CppFishNet.Managing.Server;
global using Il2CppFishNet.Transporting;
global using Il2CppFishNet.Transporting.Multipass;
global using Il2CppSteamworks;
global using Il2CppTMPro;
global using S1 = Il2CppScheduleOne;
#endif

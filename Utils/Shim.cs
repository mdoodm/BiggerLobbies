// The only place that knows which backend the build is for. Everything else uses these
// aliases and helpers, so the same source builds for both the Il2Cpp and the Mono game.
#if MONO
global using ByteArray = byte[];
global using UIntArray = uint[];
global using SteamIdArray = Steamworks.CSteamID[];
global using RectArray = UnityEngine.RectTransform[];
global using ClientMap = System.Collections.Generic.Dictionary<int, FishNet.Connection.NetworkConnection>;
global using SteamTransport = FishySteamworks.FishySteamworks;
global using ServerSocket = FishySteamworks.Server.ServerSocket;
#else
global using ByteArray = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>;
global using UIntArray = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint>;
global using SteamIdArray = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Il2CppSteamworks.CSteamID>;
global using RectArray = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.RectTransform>;
global using ClientMap = Il2CppSystem.Collections.Generic.Dictionary<int, Il2CppFishNet.Connection.NetworkConnection>;
global using SteamTransport = Il2CppFishySteamworks.FishySteamworks;
global using ServerSocket = Il2CppFishySteamworks.Server.ServerSocket;
#endif

namespace BiggerLobbies.Utils
{
    internal static class Shim
    {
        /// <summary>Null check that also compiles for types that are classes on one backend and structs on the other.</summary>
        public static bool IsNull<T>(T v) => v is null;

        /// <summary>Moves src over dest, replacing dest if it exists.</summary>
        public static void MoveOverwrite(string src, string dest)
        {
#if MONO
            // netstandard2.1 has no File.Move overwrite overload; File.Replace swaps in place.
            if (System.IO.File.Exists(dest)) System.IO.File.Replace(src, dest, null);
            else System.IO.File.Move(src, dest);
#else
            System.IO.File.Move(src, dest, overwrite: true);
#endif
        }

#if MONO
        // Il2Cpp wrappers have WasCollected (the native object is gone). On Mono, Unity's
        // overloaded == null already covers destroyed objects, so this keeps call sites shared.
        extension(UnityEngine.Object? o)
        {
            public bool WasCollected => o == null;
        }

        public static ByteArray Bytes(int n) => new byte[n];
        public static UIntArray UInts(int n) => new uint[n];
        public static SteamIdArray SteamIds(int n) => new Steamworks.CSteamID[n];
        public static RectArray Rects(int n) => new UnityEngine.RectTransform[n];

        /// <summary>Checked downcast: null when the object isn't a T.</summary>
        public static T? As<T>(this object? o) where T : class => o as T;

        /// <summary>Namespace and name of the object's runtime type.</summary>
        public static (string Namespace, string Name) TypeOf(object o)
        {
            var t = o.GetType();
            return (t.Namespace ?? "", t.Name);
        }

        /// <summary>Short identity for logs: stays the same for the same object.</summary>
        public static string Id(object? o) =>
            o == null ? "null" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o).ToString("X");
#else
        public static ByteArray Bytes(int n) => new(n);
        public static UIntArray UInts(int n) => new(n);
        public static SteamIdArray SteamIds(int n) => new(n);
        public static RectArray Rects(int n) => new(n);

        /// <summary>Checked downcast: null when the object isn't a T.</summary>
        public static T? As<T>(this Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? o)
            where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase => o?.TryCast<T>();

        /// <summary>Namespace and name of the object's runtime (Il2Cpp) type.</summary>
        public static (string Namespace, string Name) TypeOf(Il2CppSystem.Object o)
        {
            var t = o.GetIl2CppType();
            return (t.Namespace ?? "", t.Name);
        }

        /// <summary>Short identity for logs: the native object pointer.</summary>
        public static string Id(object? o) => o switch
        {
            null => "null",
            Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b => b.Pointer.ToString("X"),
            _ => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o).ToString("X"),
        };
#endif
    }
}

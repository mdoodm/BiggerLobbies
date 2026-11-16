using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BiggerLobbies.Integrations;

// Steam profile pictures for the Host settings lists. Steam may not have an avatar
// downloaded yet; those images show a grey square and fill in once it arrives.
public static class Avatars
{
    private static readonly Dictionary<ulong, Texture2D> _cache = new();
    private static readonly HashSet<ulong> _requested = new();
    private static readonly List<(RawImage img, ulong id, int tries)> _pending = new();
    private static float _nextTry;
    private const int MaxTries = 60;   // polls 0.5 s apart: stop waiting on an avatar Steam never delivers

    /// <summary>Shows the avatar for this Steam ID on the image now, or as soon as Steam has it.</summary>
    public static void Show(RawImage img, ulong id)
    {
        img.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        img.texture = null;
        if (id == 0) return;
        if (Showcase.TryFace(id, out var face))
        {
            _pending.RemoveAll(p => p.img == img);
            ShowSprite(img, face);
            return;
        }
        var tex = Get(id);
        if (tex != null) Apply(img, tex);
        else
        {
            // An image that is shown again replaces its earlier wait instead of stacking up.
            _pending.RemoveAll(p => p.img == img);
            _pending.Add((img, id, 0));
        }
    }

    /// <summary>Shows a sprite (an NPC portrait, possibly from an atlas) on a RawImage.</summary>
    public static void ShowSprite(RawImage img, Sprite face)
    {
        var tex = face.texture;
        var r = face.textureRect;
        img.texture = tex;
        img.color = Color.white;
        img.uvRect = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
    }

    private static void Apply(RawImage img, Texture2D tex)
    {
        img.texture = tex;
        img.color = Color.white;
        img.uvRect = new Rect(0, 1, 1, -1);   // Steam images are stored top row first
    }

    /// <summary>Call every frame; retries avatars Steam hadn't downloaded yet.</summary>
    public static void Update()
    {
        if (_pending.Count == 0 || Time.unscaledTime < _nextTry) return;
        _nextTry = Time.unscaledTime + 0.5f;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (img, id, tries) = _pending[i];
            if (img == null || img.WasCollected) { _pending.RemoveAt(i); continue; }
            var tex = Get(id);
            if (tex == null)
            {
                if (tries + 1 >= MaxTries) _pending.RemoveAt(i);
                else _pending[i] = (img, id, tries + 1);
                continue;
            }
            Apply(img, tex);
            _pending.RemoveAt(i);
        }
    }

    private static Texture2D? Get(ulong id)
    {
        if (_cache.TryGetValue(id, out var t) && t != null && !t.WasCollected) return t;
        try
        {
            var sid = new CSteamID(id);
            int handle = SteamFriends.GetMediumFriendAvatar(sid);
            if (handle <= 0)
            {
                // 0: no avatar known yet; -1: downloading. Ask Steam once to fetch it.
                if (_requested.Add(id)) SteamFriends.RequestUserInformation(sid, false);
                return null;
            }
            if (!SteamUtils.GetImageSize(handle, out uint w, out uint h) || w == 0 || h == 0) return null;
            int size = (int)(w * h * 4);
            var buf = Shim.Bytes(size);
            if (!SteamUtils.GetImageRGBA(handle, buf, size)) return null;
            var tex = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(buf);
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _cache[id] = tex;
            return tex;
        }
        catch { return null; }
    }
}

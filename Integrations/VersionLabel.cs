using System;
using MelonLoader;
using UnityEngine;

namespace BiggerLobbies.Integrations;

// "BiggerLobbies Vx.x.x by Mdoodm" in the bottom-left corner of the screen, at all times.
// Its own overlay canvas, drawn above the game's UI, with no raycaster so it never takes a
// click. The font is borrowed from the game's first TextMeshPro text, once there is one.
public static class VersionLabel
{
    private static GameObject? _root;
    private static float _nextTry;
    private static bool _failed;

    public static void Update()
    {
        if (_failed) return;
        if (_root != null && !_root.WasCollected) return;
        if (Time.unscaledTime < _nextTry) return;
        _nextTry = Time.unscaledTime + 1f;
        try { Build(); }
        catch (Exception e)
        {
            _failed = true;
            MelonLogger.Warning($"Couldn't show the version label: {e.Message}");
        }
    }

    private static void Build()
    {
        var source = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        if (source == null || source.font == null) return;   // no UI yet; try again next second

        var root = new GameObject("BiggerLobbies VersionLabel");
        UnityEngine.Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = root.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = source.font;
        text.text = Utils.Constants.CREDIT;
        text.fontSize = 16;
        text.color = new Color(1f, 1f, 1f, 0.55f);
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.enableWordWrapping = false;
        text.richText = false;
        text.raycastTarget = false;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(10, 8);
        rt.sizeDelta = new Vector2(600, 24);

        _root = root;
        Utils.Log.Info("Version label shown in the bottom-left corner");
    }
}

#if !MONO
using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BiggerLobbies.Integrations;

// "Host settings": a button under Settings in the main menu and the pause menu, opening a
// screen made from the game's own Settings screen. It has lobby type, lobby size, the
// whitelist switch, connected players (ping, kick, blacklist), the whitelist and the
// blacklist, plus a friends picker for adding to either list.
//
// Everything is cloned from the game's UI so it uses the game's fonts, colours, sounds
// and Escape/back behaviour. Game scripts on the cloned widgets are stripped so nothing
// changes a real game setting; our own listeners do the work.
public static class HostSettingsMenu
{
    private static readonly Color Accent = new(0.281f, 0.527f, 0.755f, 1f);   // the game's blue "Apply" button
    private static readonly Color Grey = new(0.439f, 0.439f, 0.439f, 1f);     // the game's small-button grey
    private static readonly Color Muted = new(0.70f, 0.70f, 0.70f, 1f);

    private static MelonPreferences_Entry<string>? _keyEntry;
    private static Key _key = Key.F6;

    private sealed class Ui
    {
        public bool InGame;
        public MenuScreen Screen = null!;
        public MenuScreen Picker = null!;
        public RectTransform Layout = null!, PickerLayout = null!;
        public TextMeshProUGUI PickerTitle = null!;
        public GameObject LabelT = null!, TextT = null!, ButtonT = null!, SliderT = null!;
        public float RowW;
        public GameObject? MenuButton;
        public RectTransform? Panel;
        public float PanelH0, PanelY0, ButtonStep;
        public bool ButtonShown = true;

        public readonly List<(Button b, LobbyPrivacy.Mode m)> PrivacyButtons = new();
        public Button WlOff = null!, WlOn = null!;
        public Slider Size = null!;
        public TextMeshProUGUI SizeValue = null!, SizeHint = null!, Status = null!;
        public TextMeshProUGUI? PickerStatus, PrivacyHint;
        public TextMeshProUGUI PlayersHeading = null!, WhiteHeading = null!, BlackHeading = null!;
        public RectTransform Players = null!, White = null!, Black = null!;
        public bool PickForWhitelist;
        public string LastPlayersKey = "", LastWhiteKey = "", LastBlackKey = "";
        public ulong ConfirmBlacklist;
        public readonly Dictionary<int, TextMeshProUGUI> PingTexts = new();
        public readonly List<(GameObject row, string name)> PickerRows = new();
        public TMP_InputField? Search;
        public TextMeshProUGUI? PickerCount, NoMatch;
        public int FriendCount, OnlineCount;
    }

    private static Ui? _menu, _pause;
    private static int _pendingSize = -1;
    private static float _applySizeAt;
    private static float _nextRefresh;

    public static void Init(MelonPreferences_Category cat)
    {
        _keyEntry = cat.CreateEntry("HostPanelKey", "F6", "Host settings key",
            "Key that opens Host settings while you host a save. The same screen is in the pause menu and the main menu.");
        if (!Enum.TryParse(_keyEntry.Value?.Trim(), true, out _key) || _key == Key.None || !Enum.IsDefined(typeof(Key), _key))
        {
            MelonLogger.Warning($"HostPanelKey '{_keyEntry.Value}' isn't a key name; using F6");
            _key = Key.F6;
        }
    }

    // --- Injection ---------------------------------------------------------------------------

    public static void OnSceneLoaded(string scene)
    {
        if (scene == "Menu") { _menu = null; MelonCoroutines.Start(InjectWhenReady(false)); }
        else if (scene == "Main") { _pause = null; Whitelist.WarnIfEmpty(); MelonCoroutines.Start(InjectWhenReady(true)); }
    }

    private static IEnumerator InjectWhenReady(bool inGame)
    {
        for (int i = 0; i < 120; i++)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Ui? ui = null;
            try { ui = inGame ? InjectPause() : InjectMainMenu(); }
            catch (Exception e)
            {
                MelonLogger.Error($"Couldn't add Host settings to the {(inGame ? "pause" : "main")} menu: {e}");
                yield break;
            }
            if (ui == null) continue;
            if (inGame) _pause = ui; else _menu = ui;
            MelonLogger.Msg($"Host settings added to the {(inGame ? "pause" : "main")} menu");
            yield break;
        }
    }

    private static Ui? InjectMainMenu()
    {
        MenuScreen? home = null; GameObject? settings = null;
        foreach (var s in UnityEngine.Object.FindObjectsOfType<MenuScreen>(true))
        {
            if (s.name == "Home" && s.transform.Find("Bank/Panel") != null) home = s;
            if (s.name == "Settings" && s.GetComponent<SettingsScreen>() != null) settings = s.gameObject;
        }
        if (home == null || settings == null) return null;
        return Build(home, settings, home.transform.Find("Bank/Panel"), false);
    }

    private static Ui? InjectPause()
    {
        var pm = Singleton<PauseMenu>.Instance;
        if (pm == null || pm.Container == null) return null;
        var home = pm.Container.Find("Menu")?.GetComponent<MenuScreen>();
        var settings = pm.Container.Find("SettingsScreen_Ingame")?.gameObject;
        var panel = pm.Container.Find("Menu/Bank/Panel");
        if (home == null || settings == null || panel == null) return null;
        return Build(home, settings, panel, true);
    }

    private static Ui Build(MenuScreen home, GameObject settings, Transform panel, bool inGame)
    {
        var ui = new Ui { InGame = inGame };
        ui.Screen = CloneScreen(settings, home, "Host settings", ui, true);
        ui.Picker = CloneScreen(settings, ui.Screen, "Add from friends", ui, false);
        ui.PickerTitle = ui.Picker.transform.Find("Title").GetComponent<TextMeshProUGUI>();
        BuildMain(ui);
        Header(ui, ui.Screen, () => ui.Screen.Close(), true);
        Header(ui, ui.Picker, () => ui.Picker.Close(), false);
        ActivateOnce(ui.Screen);
        ActivateOnce(ui.Picker);
        AddMenuButton(ui, panel);
        return ui;
    }

    // A MenuScreen hides itself in Awake, and Awake only runs once its parents are active.
    // The pause menu is inactive until the game is paused, so a screen opened there for the
    // first time would hide itself straight away. Run Awake first, whenever we can.
    private static readonly HashSet<IntPtr> _awake = new();

    private static void ActivateOnce(MenuScreen s)
    {
        if (_awake.Contains(s.Pointer)) return;
        var parent = s.transform.parent;
        if (parent != null && !parent.gameObject.activeInHierarchy) return;
        s.gameObject.SetActive(true);
        if (!s.IsOpen) s.gameObject.SetActive(false);
        _awake.Add(s.Pointer);
    }

    // --- Cloning the game's Settings screen ------------------------------------------------

    private static readonly HashSet<string> KeepScripts = new() { "MenuScreen", "MonoState", "ButtonSound", "AudioSourceController", "CanvasGroupFader" };

    private static MenuScreen CloneScreen(GameObject settings, MenuScreen previous, string title, Ui ui, bool keepTemplates)
    {
        bool was = settings.activeSelf;
        settings.SetActive(false);
        var go = UnityEngine.Object.Instantiate(settings, settings.transform.parent);
        settings.SetActive(was);
        go.name = "BiggerLobbies " + title;

        var old = go.GetComponent<SettingsScreen>();
        int prio = old != null ? old.ExitInputPriority : 0;
        if (old != null) UnityEngine.Object.DestroyImmediate(old);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(760, 580);

        // Keep the background, title and one content panel (Controls has the section-label style).
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var c = go.transform.GetChild(i);
            if (c.name != "Background" && c.name != "Title" && c.name != "Content") UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        go.transform.Find("Title").GetComponent<TextMeshProUGUI>().text = title;

        var content = go.transform.Find("Content");
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var c = content.GetChild(i);
            if (c.name != "Controls") UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        var controls = content.Find("Controls");
        controls.gameObject.SetActive(true);
        var layout = controls.Find("ControlsPanel/Scroll View/Viewport/Vertical Layout").GetComponent<RectTransform>();
        var lvlg = layout.GetComponent<VerticalLayoutGroup>();
        if (lvlg != null) { lvlg.spacing = 4; lvlg.padding = new RectOffset(20, 20, 16, 40); lvlg.childAlignment = TextAnchor.UpperLeft; }

        if (keepTemplates)
        {
            var holder = new GameObject("Templates");
            holder.SetActive(false);
            holder.transform.SetParent(go.transform, false);
            ui.LabelT = Keep(layout.Find("SettingsLabel"), holder);
            ui.TextT = Keep(layout.Find("SettingsSlider/Label"), holder);
            ui.ButtonT = Keep(layout.Find("BindingLabel/RestoreDefault"), holder);
            ui.SliderT = Keep(layout.Find("SettingsSlider"), holder);
        }
        for (int i = layout.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(layout.GetChild(i).gameObject);

        Strip(go);

        var ms = go.AddComponent<MenuScreen>();
        ms.Group = go.GetComponent<CanvasGroup>();
        ms.State = go.GetComponent<Il2CppScheduleOne.State.MonoState>();
        ms.PreviousScreen = previous;
        ms.ExitInputPriority = prio;

        if (keepTemplates) ui.Layout = layout; else ui.PickerLayout = layout;
        float vw = layout.parent.GetComponent<RectTransform>().rect.width - 40f;
        ui.RowW = float.IsNaN(vw) || vw < 400f ? 680f : vw;
        return ms;
    }

    private static GameObject Keep(Transform? t, GameObject holder)
    {
        if (t == null) throw new Exception("settings screen layout changed; a template is missing");
        var copy = UnityEngine.Object.Instantiate(t.gameObject, holder.transform);
        copy.SetActive(true);
        return copy;
    }

    // Remove the game's scripts from the clone (settings logic, gamepad navigation), keeping
    // only the screen plumbing, sounds and fades.
    private static void Strip(GameObject go)
    {
        var all = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            var c = all[i];
            if (c == null) continue;
            var type = c.GetIl2CppType();
            string ns = type.Namespace ?? "";
            if (!ns.StartsWith("ScheduleOne") && !ns.StartsWith("Il2CppScheduleOne")) continue;
            if (KeepScripts.Contains(type.Name)) continue;
            UnityEngine.Object.DestroyImmediate(c);
        }
        // Gamepad selection frames: the scripts that toggled them are gone, so hide them.
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Selected Frame" || t.name == "SelectedFrame") t.gameObject.SetActive(false);
        foreach (var b in go.GetComponentsInChildren<Button>(true)) b.onClick = new Button.ButtonClickedEvent();
        foreach (var s in go.GetComponentsInChildren<Slider>(true)) s.onValueChanged = new Slider.SliderEvent();
    }

    // --- Menu button ---------------------------------------------------------------------------

    private static void AddMenuButton(Ui ui, Transform panel)
    {
        var settingsBtn = panel.Find("Settings");
        if (settingsBtn == null) { MelonLogger.Warning("No Settings button to copy; Host settings is only on the F6 key"); return; }
        var go = UnityEngine.Object.Instantiate(settingsBtn.gameObject, panel);
        go.name = "HostSettings";
        go.transform.SetSiblingIndex(settingsBtn.GetSiblingIndex() + 1);
        foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (t.text == "Settings") t.text = "Host settings";
        var btn = go.GetComponent<Button>();
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener((UnityAction)(() => Open(ui)));
        var trig = go.GetComponent<EventTrigger>();
        if (trig != null)
            for (int i = trig.triggers.Count - 1; i >= 0; i--)
            {
                var id = trig.triggers[i].eventID;
                if (id == EventTriggerType.PointerClick || id == EventTriggerType.Submit) trig.triggers.RemoveAt(i);
            }

        // The panel is a fixed-height vertical layout; grow it by one button, keeping the top in place.
        ui.Panel = panel.GetComponent<RectTransform>();
        var srt = settingsBtn.GetComponent<RectTransform>();
        var vlg = panel.GetComponent<VerticalLayoutGroup>();
        ui.ButtonStep = srt.rect.height + (vlg != null ? vlg.spacing : 2f);
        ui.PanelH0 = ui.Panel.sizeDelta.y;
        ui.PanelY0 = ui.Panel.anchoredPosition.y;
        ui.MenuButton = go;
        ui.ButtonShown = false;
        ShowMenuButton(ui, true);
    }

    private static void ShowMenuButton(Ui ui, bool show)
    {
        if (ui.MenuButton == null || ui.Panel == null || ui.ButtonShown == show) return;
        ui.ButtonShown = show;
        ui.MenuButton.SetActive(show);
        float extra = show ? ui.ButtonStep : 0f;
        ui.Panel.sizeDelta = new Vector2(ui.Panel.sizeDelta.x, ui.PanelH0 + extra);
        ui.Panel.anchoredPosition = new Vector2(ui.Panel.anchoredPosition.x, ui.PanelY0 - (1f - ui.Panel.pivot.y) * extra);
    }

    // --- Building blocks -----------------------------------------------------------------------

    private static RectTransform Row(Ui ui, Transform parent, float h)
    {
        var go = new GameObject("Row");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(ui.RowW, h);
        return rt;
    }

    private static RectTransform Box(Ui ui, Transform parent)
    {
        var go = new GameObject("Box");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(ui.RowW, 0);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4; v.childControlWidth = false; v.childControlHeight = false;
        v.childForceExpandWidth = false; v.childForceExpandHeight = false;
        var f = go.AddComponent<ContentSizeFitter>();
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    private static void Place(RectTransform rt, float x, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static TextMeshProUGUI Text(Ui ui, Transform row, string s, float x, float w, Color? color = null, float size = 14, bool plain = false)
    {
        var go = UnityEngine.Object.Instantiate(ui.TextT, row);
        go.name = "Text";
        var t = go.GetComponent<TextMeshProUGUI>();
        Place(go.GetComponent<RectTransform>(), x, w, 30);
        t.richText = !plain;
        t.text = s; t.fontSize = size;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (color.HasValue) t.color = color.Value;
        return t;
    }

    // --- Layout grid ------------------------------------------------------------------------------
    // One label column and one control column; rows 36 tall, 4 apart; 24 above a section, 8 below.
    private const float LabelX = 12, ControlX = 200, RowH = 36, AvatarS = 28, NameX = LabelX + AvatarS + 12;
    private static readonly Color Playing = new(0.44f, 0.84f, 0.38f, 1f);   // the game's own green (version label)
    private static readonly Color OnlineDot = new(0.44f, 0.84f, 0.38f, 1f);   // online is green too; playing Schedule I adds a green name and label
    private static readonly Color Blocked = new(0.86f, 0.36f, 0.32f, 1f);
    private static Sprite? _circle, _fade;
    private static bool _spritesSearched;

    private static void FindSprites()
    {
        if (_spritesSearched && (_circle == null || !_circle.WasCollected)) return;
        _spritesSearched = true;
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (s == null) continue;
            if (_circle == null && s.name == "Circle") _circle = s;
            if (_fade == null && s.name == "Vertical gradient") _fade = s;
        }
    }

    private static TextMeshProUGUI Section(Ui ui, Transform parent, string s, out RectTransform row, bool first = false)
    {
        if (!first) Spacer(ui, parent, 16);   // + 4 + 4 layout spacing = 24 above
        row = Row(ui, parent, 28);
        row.name = "Section " + s;
        var go = UnityEngine.Object.Instantiate(ui.LabelT, row);
        go.name = "Heading";
        Place(go.GetComponent<RectTransform>(), LabelX - 2, 360, 28);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = s;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        return t;
    }

    private static string Heading(string name, int count) => count > 0 ? $"{name}  <color=#8C8C8C>{count}</color>" : name;

    private static Button Btn(Ui ui, Transform row, string label, float x, float w, Action onClick)
    {
        var go = UnityEngine.Object.Instantiate(ui.ButtonT, row);
        go.name = "Button " + label;
        Place(go.GetComponent<RectTransform>(), x, w, 28);
        var t = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (t != null) { t.text = label; t.enableWordWrapping = false; t.fontSize = 14; }
        var b = go.GetComponent<Button>();
        b.onClick = new Button.ButtonClickedEvent();
        b.onClick.AddListener((UnityAction)(() =>
        {
            try { onClick(); } catch (Exception e) { MelonLogger.Error($"Host settings button '{label}' failed: {e}"); }
        }));
        return b;
    }

    private static Button RightBtn(Ui ui, Transform row, string label, float fromRight, float w, Action onClick)
        => Btn(ui, row, label, ui.RowW - fromRight - w, w, onClick);

    private static void SetLabel(Button b, string s)
    {
        var t = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (t != null) t.text = s;
    }

    private static void Highlight(Button b, bool on)
    {
        var c = b.colors;
        c.normalColor = on ? Accent : Grey;
        c.selectedColor = c.normalColor;
        b.colors = c;
    }

    // An "Added" state: blue and not clickable.
    private static void Done(Button b, string label)
    {
        SetLabel(b, label);
        var c = b.colors;
        c.disabledColor = Accent;
        b.colors = c;
        b.interactable = false;
    }

    private static void Spacer(Ui ui, Transform parent, float h) => Row(ui, parent, h).name = "Spacer";

    private static TextMeshProUGUI Hint(Ui ui, Transform parent, string s, float x = ControlX)
    {
        var r = Row(ui, parent, 20);
        r.name = "Hint";
        return Text(ui, r, s, x, ui.RowW - x, Muted, 12);
    }

    // Steam profile picture with the presence dot on its corner (the screen's signature).
    private static void Avatar(Transform row, ulong steamId, Color? dot)
    {
        var go = new GameObject("Avatar");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(row, false);
        Place(rt, LabelX, AvatarS, AvatarS);
        var img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        Avatars.Show(img, steamId);
        if (dot == null || _circle == null) return;

        // Ring in the panel colour, then the dot, so it reads on any avatar.
        foreach (var (size, col) in new[] { (16f, new Color(0.10f, 0.10f, 0.10f, 1f)), (11f, dot.Value) })
        {
            var d = new GameObject("Dot");
            var drt = d.AddComponent<RectTransform>();
            drt.SetParent(rt, false);
            drt.anchorMin = drt.anchorMax = new Vector2(1, 0);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(-3, 3);
            drt.sizeDelta = new Vector2(size, size);
            var di = d.AddComponent<Image>();
            di.sprite = _circle;
            di.color = col;
            di.raycastTarget = false;
        }
    }

    private static Color? DotFor(ulong id)
    {
        if (BanList.IsBanned(id)) return Blocked;
        var s = Friends.StateOf(id);
        return s == Friends.Presence.Playing ? Playing : s == Friends.Presence.Online ? OnlineDot : null;
    }

    // Title bar: Back on the left, the title with a status line under it, a close cross on the right.
    private static void Header(Ui ui, MenuScreen screen, Action back, bool main)
    {
        var bb = Btn(ui, screen.transform, "Back", 0, 88, back);
        var rt = bb.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(20, -32);
        rt.sizeDelta = new Vector2(88, 28);

        var cb = Btn(ui, screen.transform, "×", 0, 36, () => CloseAll(ui));
        var crt = cb.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(1, 0.5f);
        crt.anchoredPosition = new Vector2(-20, -32);
        crt.sizeDelta = new Vector2(36, 28);
        var ct = cb.GetComponentInChildren<TextMeshProUGUI>(true);
        if (ct != null) { ct.fontSize = 22; ct.fontStyle = FontStyles.Normal; }

        var title = screen.transform.Find("Title").GetComponent<RectTransform>();
        title.anchoredPosition = new Vector2(title.anchoredPosition.x, -28);

        var sgo = UnityEngine.Object.Instantiate(ui.TextT, screen.transform);
        sgo.name = "Status";
        var srt = sgo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(0, -56);
        srt.sizeDelta = new Vector2(-280, 18);
        var st = sgo.GetComponent<TextMeshProUGUI>();
        st.text = ""; st.fontSize = 12; st.color = Muted; st.richText = false;
        st.alignment = TextAlignmentOptions.Center;
        st.enableWordWrapping = false; st.overflowMode = TextOverflowModes.Ellipsis;
        if (main) ui.Status = st; else ui.PickerStatus = st;
    }

    // A soft fade at the bottom of the scroll area, so it's clear the page goes on.
    private static void BottomFade(Transform layout)
    {
        if (_fade == null) return;
        var scroll = layout.parent?.parent;   // Viewport -> Scroll View
        if (scroll == null) return;
        var go = new GameObject("Fade");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(scroll, false);
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(-6, 0);
        rt.sizeDelta = new Vector2(-12, 40);
        var img = go.AddComponent<Image>();
        img.sprite = _fade;
        img.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        img.raycastTarget = false;
    }

    private static void CloseAll(Ui ui)
    {
        try { if (ui.Picker.IsOpen) ui.Picker.Close(); } catch { }
        try { if (ui.Screen.IsOpen) ui.Screen.Close(); } catch { }
        if (ui.InGame)
        {
            try { var pm = Singleton<PauseMenu>.Instance; if (pm != null && pm.IsPaused) pm.Resume(); } catch { }
        }
    }

    // --- Main screen ---------------------------------------------------------------------------

    private static string PrivacyHint(LobbyPrivacy.Mode m) => m switch
    {
        LobbyPrivacy.Mode.Private => "Only people you invite can join",
        LobbyPrivacy.Mode.Open => "Listed in Steam's lobby search for anyone to find",
        _ => "Steam friends can use Join Game, and anyone you invite can join",
    };

    private static void BuildMain(Ui ui)
    {
        FindSprites();
        var L = ui.Layout;

        // Lobby
        Section(ui, L, "Lobby", out _, first: true);
        Spacer(ui, L, 0);
        var r = Row(ui, L, RowH);
        Text(ui, r, "Who can join", LabelX, ControlX - LabelX - 8);
        float x = ControlX;
        foreach (LobbyPrivacy.Mode m in Enum.GetValues(typeof(LobbyPrivacy.Mode)))
        {
            var mode = m;
            var b = Btn(ui, r, LobbyPrivacy.Label(m), x, 120, () => { SetStatus(ui, LobbyPrivacy.Set(mode)); RefreshStatic(ui); });
            ui.PrivacyButtons.Add((b, m));
            x += 128;
        }
        ui.PrivacyHint = Hint(ui, L, "");

        // Lobby size: the game's slider, re-laid on the control column.
        var sliderGo = UnityEngine.Object.Instantiate(ui.SliderT, L);
        sliderGo.name = "LobbySize";
        var srt = sliderGo.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = new Vector2(0, 1); srt.pivot = new Vector2(0, 1);
        srt.sizeDelta = new Vector2(ui.RowW, RowH);
        var label = sliderGo.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        label.text = "Lobby size";
        Place(label.GetComponent<RectTransform>(), LabelX, ControlX - LabelX - 8, 30);
        ui.Size = sliderGo.GetComponentInChildren<Slider>(true);
        var slrt = ui.Size.GetComponent<RectTransform>();
        Place(slrt, ControlX, 376, 20);
        ui.SizeValue = ui.Size.transform.Find("Value").GetComponent<TextMeshProUGUI>();
        var vrt = ui.SizeValue.GetComponent<RectTransform>();
        vrt.anchorMin = vrt.anchorMax = new Vector2(1, 0.5f);
        vrt.pivot = new Vector2(0, 0.5f);
        vrt.anchoredPosition = new Vector2(16, 0);
        vrt.sizeDelta = new Vector2(48, 30);
        ui.SizeValue.alignment = TextAlignmentOptions.MidlineLeft;
        ui.Size.onValueChanged = new Slider.SliderEvent();
        ui.Size.wholeNumbers = true;
        ui.Size.minValue = LobbySize.Min;
        ui.Size.maxValue = LobbySize.Max;
        ui.Size.SetValueWithoutNotify(LobbySize.MaxPlayers);
        ui.Size.onValueChanged.AddListener((UnityAction<float>)(v =>
        {
            _pendingSize = Mathf.RoundToInt(v);
            _applySizeAt = Time.unscaledTime + 0.6f;
            ui.SizeValue.text = _pendingSize.ToString();
        }));
        ui.SizeHint = Hint(ui, L, "");

        // Whitelist
        ui.WhiteHeading = Section(ui, L, "Whitelist", out var wrow);
        RightBtn(ui, wrow, "Add friends", 0, 140, () => OpenPicker(ui, true));
        Spacer(ui, L, 0);
        var w = Row(ui, L, RowH);
        Text(ui, w, "Only listed players", LabelX, ControlX - LabelX - 8);
        ui.WlOff = Btn(ui, w, "Off", ControlX, 120, () => { Whitelist.SetEnabled(false); SetStatus(ui, "Whitelist off"); RefreshStatic(ui); _nextRefresh = 0; });
        ui.WlOn = Btn(ui, w, "On", ControlX + 128, 120, () => { Whitelist.SetEnabled(true); SetStatus(ui, "Whitelist on. Players already here stay"); RefreshStatic(ui); _nextRefresh = 0; });
        Hint(ui, L, "Starts empty each game. The blacklist always wins");
        Spacer(ui, L, 4);
        ui.White = Box(ui, L);

        // Players
        ui.PlayersHeading = Section(ui, L, "Players", out _);
        Spacer(ui, L, 0);
        ui.Players = Box(ui, L);

        // Blacklist
        ui.BlackHeading = Section(ui, L, "Blacklist", out var brow);
        RightBtn(ui, brow, "Add friends", 0, 140, () => OpenPicker(ui, false));
        Hint(ui, L, "Never allowed in. Saved between games", LabelX);
        Spacer(ui, L, 4);
        ui.Black = Box(ui, L);

        BottomFade(L);
        RefreshStatic(ui);
    }

    private static void SetStatus(Ui ui, string s)
    {
        if (ui.Status != null) ui.Status.text = s ?? "";
    }

    private static void RefreshStatic(Ui ui)
    {
        foreach (var (b, m) in ui.PrivacyButtons) Highlight(b, LobbyPrivacy.Current == m);
        if (ui.PrivacyHint != null) ui.PrivacyHint.text = PrivacyHint(LobbyPrivacy.Current);
        Highlight(ui.WlOff, !Whitelist.Enabled);
        Highlight(ui.WlOn, Whitelist.Enabled);
        if (_pendingSize < 0)
        {
            ui.Size.SetValueWithoutNotify(LobbySize.MaxPlayers);
            ui.SizeValue.text = LobbySize.MaxPlayers.ToString();
        }
        int n = HostLobby.MemberCount();
        ui.SizeHint.text = HostLobby.TryGet(out _)
            ? $"Applies now. Lowest is {LobbySize.LiveMin} with {n} in the lobby"
            : "Applies to the next lobby you host";
    }

    // --- Lists -----------------------------------------------------------------------------------

    private static void Clear(Transform box)
    {
        for (int i = box.childCount - 1; i >= 0; i--)
        {
            var g = box.GetChild(i).gameObject;
            g.SetActive(false);
            UnityEngine.Object.Destroy(g);
        }
    }

    private static void Empty(Ui ui, Transform box, string s)
    {
        var r = Row(ui, box, 28);
        Text(ui, r, s, LabelX, ui.RowW - LabelX, Muted, 13);
    }

    private static void RefreshLists(Ui ui, bool force)
    {
        // Players
        var players = HostPlayers.List();
        ui.PlayersHeading.text = Heading("Players", players.Count);
        var pk = string.Join("|", players.ConvertAll(p => $"{p.ClientId}:{p.SteamId}:{p.Name}")) + "|" + ui.ConfirmBlacklist + InstanceFinder.IsServer;
        if (force || pk != ui.LastPlayersKey)
        {
            ui.LastPlayersKey = pk;
            Clear(ui.Players);
            ui.PingTexts.Clear();
            if (players.Count == 0)
                Empty(ui, ui.Players, InstanceFinder.IsServer ? "Nobody else is here yet" : "Players appear here while you host");
            foreach (var p in players)
            {
                var row = p;
                var r = Row(ui, ui.Players, RowH);
                Avatar(r, row.SteamId, Playing);
                Text(ui, r, row.Name, NameX, ui.RowW - NameX - 324, plain: true);
                var ping = Text(ui, r, "", ui.RowW - 308, 72, Muted, 13);
                ping.alignment = TextAlignmentOptions.MidlineRight;
                ui.PingTexts[row.ClientId] = ping;
                RightBtn(ui, r, "Kick", 128, 100, () => { SetStatus(ui, HostPlayers.Kick(row, false)); _nextRefresh = 0; });
                bool confirming = ui.ConfirmBlacklist != 0 && ui.ConfirmBlacklist == row.SteamId;
                var bb = RightBtn(ui, r, confirming ? "Confirm" : "Blacklist", 0, 120, () =>
                {
                    if (ui.ConfirmBlacklist == row.SteamId) { ui.ConfirmBlacklist = 0; SetStatus(ui, HostPlayers.Kick(row, true)); }
                    else { ui.ConfirmBlacklist = row.SteamId; SetStatus(ui, $"Click Confirm to blacklist {row.Name}"); }
                    _nextRefresh = 0;
                });
                if (confirming) Highlight(bb, true);
                bb.interactable = row.SteamId != 0;
            }
        }
        foreach (var p in players)
            if (ui.PingTexts.TryGetValue(p.ClientId, out var pt) && pt != null)
                pt.text = p.Ping >= 0 ? $"{p.Ping} ms" : "";

        // Whitelist
        ui.WhiteHeading.text = Heading("Whitelist", Whitelist.Players.Count);
        var wk = string.Join("|", ListKey(Whitelist.Players)) + Whitelist.Enabled + "|" + string.Join(",", PresenceKey(Whitelist.Players));
        if (force || wk != ui.LastWhiteKey)
        {
            ui.LastWhiteKey = wk;
            Clear(ui.White);
            if (Whitelist.Players.Count == 0)
                Empty(ui, ui.White, Whitelist.Enabled ? "Nobody else can join until you add someone" : "Nobody added");
            foreach (var e in Whitelist.Players)
            {
                var id = e.SteamId; var name = e.Name.Length > 0 ? e.Name : e.SteamId.ToString();
                var r = Row(ui, ui.White, RowH);
                Avatar(r, id, DotFor(id));
                var nt = Text(ui, r, name, NameX, ui.RowW - NameX - 120, plain: true);
                var st = Friends.StateOf(id);
                if (st == Friends.Presence.Playing) nt.color = Playing;
                else if (st == Friends.Presence.Offline) nt.color = Muted;
                RightBtn(ui, r, "Remove", 0, 100, () => { Whitelist.Remove(id); SetStatus(ui, $"Removed {name} from the whitelist"); _nextRefresh = 0; });
            }
        }

        // Blacklist
        ui.BlackHeading.text = Heading("Blacklist", BanList.Bans.Count);
        var bk = string.Join("|", ListKey(BanList.Bans));
        if (force || bk != ui.LastBlackKey)
        {
            ui.LastBlackKey = bk;
            Clear(ui.Black);
            if (BanList.Bans.Count == 0) Empty(ui, ui.Black, "Nobody blacklisted");
            foreach (var e in BanList.Bans)
            {
                var id = e.SteamId; var name = e.Name.Length > 0 ? e.Name : e.SteamId.ToString();
                var r = Row(ui, ui.Black, RowH);
                Avatar(r, id, Blocked);
                Text(ui, r, name, NameX, 260, plain: true);
                Text(ui, r, id.ToString(), NameX + 268, 200, Muted, 12);
                RightBtn(ui, r, "Remove", 0, 100, () => { BanList.Unban(id); SetStatus(ui, $"Removed {name} from the blacklist"); _nextRefresh = 0; });
            }
        }
    }

    private static IEnumerable<string> PresenceKey(IReadOnlyList<BanList.Entry> list)
    {
        foreach (var e in list) yield return ((int)Friends.StateOf(e.SteamId)).ToString();
    }

    private static IEnumerable<string> ListKey(IReadOnlyList<BanList.Entry> list)
    {
        foreach (var e in list) yield return e.SteamId.ToString();
    }

    // --- Friends picker ----------------------------------------------------------------------------

    private static void OpenPicker(Ui ui, bool whitelist)
    {
        ui.PickForWhitelist = whitelist;
        ui.PickerTitle.text = whitelist ? "Add to whitelist" : "Add to blacklist";
        FillPicker(ui);
        FilterPicker(ui, "");
        ActivateOnce(ui.Picker);
        ui.Picker.Open();
        try { ui.Search?.ActivateInputField(); } catch { }
    }

    private static void FillPicker(Ui ui)
    {
        FindSprites();
        var L = ui.PickerLayout;
        Clear(L);
        var friends = Friends.List();
        int online = friends.FindAll(f => f.Online).Count;
        if (ui.PickerStatus != null)
            ui.PickerStatus.text = ui.PickForWhitelist ? "Who can join this session" : "Who can never join. Saved between games";
        ui.PickerRows.Clear();
        ui.FriendCount = friends.Count; ui.OnlineCount = online;

        var sr = Row(ui, L, 40);
        sr.name = "Search";
        ui.Search = SearchBox(ui, sr);
        var hint = Row(ui, L, 24);
        ui.PickerCount = Text(ui, hint, "", LabelX, ui.RowW - LabelX, Muted, 12);
        var nm = Row(ui, L, 28);
        ui.NoMatch = Text(ui, nm, "", LabelX, ui.RowW - LabelX, Muted, 13);
        nm.gameObject.SetActive(false);

        foreach (var f in friends)
        {
            var fr = f;
            var r = Row(ui, L, RowH);
            ui.PickerRows.Add((r.gameObject, fr.Name ?? ""));
            bool inWhite = Whitelist.Contains(fr.SteamId), inBlack = BanList.IsBanned(fr.SteamId);
            Avatar(r, fr.SteamId, inBlack ? Blocked : fr.InThisGame ? Playing : fr.Online ? OnlineDot : (Color?)null);
            var name = Text(ui, r, fr.Name, NameX, 300, plain: true);
            if (fr.InThisGame) name.color = Playing;
            else if (!fr.Online && !inBlack) name.color = Muted;
            if (fr.InThisGame) Text(ui, r, "Playing Schedule I", NameX + 308, 180, Playing, 12);

            Button? b = null;
            b = RightBtn(ui, r, "Add", 0, 100, () =>
            {
                bool ok = ui.PickForWhitelist ? Whitelist.Add(fr.SteamId, fr.Name) : AddBlack(fr);
                if (ok) Done(b!, "Added");
                _nextRefresh = 0;
            });
            if (ui.PickForWhitelist)
            {
                if (inBlack) { SetLabel(b, "Blacklisted"); b.interactable = false; }
                else if (inWhite) Done(b, "Added");
            }
            else if (inBlack) Done(b, "Added");
        }
    }

    private static void FilterPicker(Ui ui, string q)
    {
        q = (q ?? "").Trim();
        int shown = 0;
        foreach (var (row, name) in ui.PickerRows)
        {
            bool match = q.Length == 0 || name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            if (row != null && row.activeSelf != match) row.SetActive(match);
            if (match) shown++;
        }
        if (ui.PickerCount != null)
            ui.PickerCount.text = ui.FriendCount == 0 ? "No Steam friends found"
                : q.Length == 0 ? $"{ui.OnlineCount} of {ui.FriendCount} friends online"
                : shown == 0 ? "" : $"{shown} of {ui.FriendCount} friends match";
        if (ui.NoMatch != null)
        {
            ui.NoMatch.text = $"No friends called \"{q}\"";
            ui.NoMatch.transform.parent.gameObject.SetActive(q.Length > 0 && shown == 0);
        }
    }

    // A search field in the game's style: rounded dark box, the game's font, a muted placeholder.
    private static TMP_InputField SearchBox(Ui ui, Transform row)
    {
        var box = new GameObject("SearchBox");
        var brt = box.AddComponent<RectTransform>();
        brt.SetParent(row, false);
        Place(brt, LabelX, ui.RowW - LabelX, 32);
        var bg = box.AddComponent<Image>();
        var btnImg = ui.ButtonT.GetComponent<Image>();
        if (btnImg != null) { bg.sprite = btnImg.sprite; bg.type = btnImg.type; bg.pixelsPerUnitMultiplier = btnImg.pixelsPerUnitMultiplier; }
        bg.color = new Color(0.07f, 0.07f, 0.07f, 0.85f);

        var area = new GameObject("Text Area");
        var art = area.AddComponent<RectTransform>();
        art.SetParent(brt, false);
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(12, 4); art.offsetMax = new Vector2(-12, -4);
        area.AddComponent<RectMask2D>();

        TextMeshProUGUI MakeText(string name, string text, Color col)
        {
            var go = UnityEngine.Object.Instantiate(ui.TextT, art);
            go.name = name;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = 14; t.color = col; t.richText = false;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        var placeholder = MakeText("Placeholder", "Search friends", Muted);
        var text = MakeText("Text", "", Color.white);

        var input = box.AddComponent<TMP_InputField>();
        input.textViewport = art;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = text.font;
        input.pointSize = 14;
        input.caretColor = Color.white;
        input.customCaretColor = true;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.45f);
        input.characterLimit = 32;
        input.richText = false;
        input.onValueChanged.AddListener((UnityAction<string>)(q => FilterPicker(ui, q)));
        return input;
    }

    private static bool AddBlack(Friends.Friend f)
    {
        BanList.Ban(f.SteamId, f.Name);
        return BanList.IsBanned(f.SteamId);
    }

    // --- Open / update -------------------------------------------------------------------------------

    private static void Open(Ui ui)
    {
        ui.ConfirmBlacklist = 0;
        RefreshStatic(ui);
        RefreshLists(ui, true);
        SetStatus(ui, ui.InGame || HostLobby.TryGet(out _) ? "" : "These apply when you host a save.");
        ActivateOnce(ui.Screen);
        ActivateOnce(ui.Picker);
        ui.Screen.Open();
    }

    public static void Update()
    {
        Avatars.Update();
        // F6: open from the pause menu while hosting.
        try
        {
            var kb = Keyboard.current;
            if (kb != null && kb[_key].wasPressedThisFrame && _pause != null && InstanceFinder.IsServer && Visible() == null)
            {
                var pm = Singleton<PauseMenu>.Instance;
                if (pm != null && !pm.IsPaused) pm.Pause();
                Open(_pause);
            }
        }
        catch { }

        if (_pause != null) ShowMenuButton(_pause, SafeIsServer());
        if (_menu != null) ShowMenuButton(_menu, !InOthersLobby());

        var ui = Visible();
        bool dragging = false;
        try { dragging = ui != null && Mouse.current != null && Mouse.current.leftButton.isPressed; } catch { }
        if (_pendingSize >= 0 && (ui == null || (!dragging && Time.unscaledTime >= _applySizeAt)))
        {
            string msg = LobbySize.SetLive(_pendingSize);
            _pendingSize = -1;
            foreach (var u in new[] { _pause, _menu })
                if (u != null) { try { SetStatus(u, msg); RefreshStatic(u); } catch { } }
        }
        if (ui == null) return;
        if (_pendingSize >= 0) ui.SizeValue.text = _pendingSize.ToString();

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 1f;
            if (ui.Screen.IsOpen) { RefreshStatic(ui); RefreshLists(ui, false); }
        }
    }

    private static Ui? Visible()
    {
        foreach (var ui in new[] { _pause, _menu })
        {
            if (ui == null) continue;
            try { if (ui.Screen != null && (ui.Screen.IsOpen || ui.Picker.IsOpen)) return ui; }
            catch { }
        }
        return null;
    }

    private static bool InOthersLobby()
    {
        try
        {
            var svc = HostLobby.Service();
            return svc != null && svc.IsInLobby && !HostLobby.TryGet(out _);
        }
        catch { return false; }
    }

    private static bool SafeIsServer()
    {
        try { return InstanceFinder.IsServer; } catch { return false; }
    }
}

#endif

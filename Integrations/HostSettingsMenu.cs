#if !MONO
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
// whitelist switch, connected players (ping, kick, ban), the whitelist and banned
// players with a ban length, plus a friends picker for adding to either list.
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
        public readonly List<(Button b, TimeSpan? len)> BanLenButtons = new();
        public TextMeshProUGUI CrewsHeading = null!;
        public TextMeshProUGUI DepartedHeading = null!;
        public RectTransform Departed = null!;
        public string LastDepartedKey = "", LastTakenKey = "";
        public TextMeshProUGUI TakenHeading = null!;
        public RectTransform TakenList = null!;
        public readonly List<Toggle> Tabs = new();
        public readonly List<TextMeshProUGUI> TabLabels = new();
        public readonly List<GameObject> Pages = new();
        public int Tab;
        public RectTransform CrewList = null!;
        public TMP_InputField? CrewName, BanReason;
        public string LastCrewKey = "", ConfirmDeleteCrew = "";
        public Button WorldOff = null!, WorldOn = null!, PlayersOff = null!, PlayersOn = null!;
        public readonly List<(Button off, Button on, Func<bool> get)> DiscordToggles = new();
        public TextMeshProUGUI? DiscordHint;
        public Slider WorldEvery = null!, PlayersEvery = null!;
        public TextMeshProUGUI WorldValue = null!, PlayersValue = null!, LastSave = null!;
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
        // The game can report the same scene more than once; run one injection per menu at a time.
        if (scene == "Menu" && !_injecting[0]) { _menu = null; _injecting[0] = true; MelonCoroutines.Start(InjectWhenReady(false)); }
        else if (scene == "Main" && !_injecting[1]) { _pause = null; Whitelist.WarnIfEmpty(); _injecting[1] = true; MelonCoroutines.Start(InjectWhenReady(true)); }
    }

    private static readonly bool[] _injecting = new bool[2];

    private static IEnumerator InjectWhenReady(bool inGame)
    {
        try { foreach (var x in InjectLoop(inGame)) yield return x; }
        finally { _injecting[inGame ? 1 : 0] = false; }
    }

    private static IEnumerable<object?> InjectLoop(bool inGame)
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
            Utils.Log.Info($"Host settings added to the {(inGame ? "pause" : "main")} menu");
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
        RemoveOld(settings.transform.parent, panel);
        var ui = new Ui { InGame = inGame };
        ui.Screen = CloneScreen(settings, home, "Host settings", ui, true);
        ui.Picker = CloneScreen(settings, ui.Screen, "Add from friends", ui, false);
        ui.PickerTitle = ui.Picker.transform.Find("Title").GetComponent<TextMeshProUGUI>();
        BuildMain(ui);
        SetupTabs(ui);
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

        // The game's category tabs are toggles; keep their bar on the main screen.
        var tabToggles = new List<Toggle>();
        if (keepTemplates && old != null && old.Categories != null)
            for (int i = 0; i < old.Categories.Length; i++)
            {
                var t = old.Categories[i]?.Toggle;
                if (t != null) tabToggles.Add(t);
            }
        Transform? tabBar = tabToggles.Count >= 4 ? tabToggles[0].transform.parent : null;
        if (old != null) UnityEngine.Object.DestroyImmediate(old);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(760, 580);

        // Keep the background, title and one content panel (Controls has the section-label style).
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var c = go.transform.GetChild(i);
            if (c.name == "Background" || c.name == "Title" || c.name == "Content") continue;
            if (tabBar != null && tabBar.IsChildOf(c)) continue;   // the tab bar or its holder
            UnityEngine.Object.DestroyImmediate(c.gameObject);
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

        if (tabBar != null)
        {
            for (int i = tabToggles.Count - 1; i >= 4; i--) UnityEngine.Object.DestroyImmediate(tabToggles[i].gameObject);
            for (int i = 0; i < 4; i++) ui.Tabs.Add(tabToggles[i]);
        }

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

    // The main menu survives a trip into the game and back, so a second injection would add a
    // second button and screen. Remove anything left from before and give the panel its height back.
    private static void RemoveOld(Transform screens, Transform panel)
    {
        int removed = 0;
        float step = 0;
        var settingsBtn = panel.Find("Settings");
        if (settingsBtn != null)
        {
            var vlg = panel.GetComponent<VerticalLayoutGroup>();
            step = settingsBtn.GetComponent<RectTransform>().rect.height + (vlg != null ? vlg.spacing : 2f);
        }
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            var c = panel.GetChild(i);
            if (c.name != "HostSettings") continue;
            if (c.gameObject.activeSelf) removed++;
            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        for (int i = screens.childCount - 1; i >= 0; i--)
        {
            var c = screens.GetChild(i);
            if (c.name.StartsWith("BiggerLobbies ")) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        if (removed > 0 && step > 0)
        {
            var rt = panel.GetComponent<RectTransform>();
            float extra = step * removed;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y - extra);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y + (1f - rt.pivot.y) * extra);
            Utils.Log.Info($"Removed {removed} leftover Host settings button(s) from an earlier visit to this menu");
        }
    }

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

    private static RectTransform Box(Ui ui, Transform parent, bool card = false)
    {
        var go = new GameObject(card ? "Card" : "Box");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(ui.RowW, 0);
        if (card)
        {
            // The panel lives on its own child that the layout ignores; an Image on the card
            // itself would report the sprite's size as the card's preferred height.
            var bgo = new GameObject("Bg");
            var brt = bgo.AddComponent<RectTransform>();
            brt.SetParent(rt, false);
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            bgo.AddComponent<LayoutElement>().ignoreLayout = true;
            var bg = bgo.AddComponent<Image>();
            var src = ui.ButtonT.GetComponent<Image>();
            if (src != null) { bg.sprite = src.sprite; bg.type = src.type; bg.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier; }
            bg.color = new Color(1f, 1f, 1f, 0.035f);
            bg.raycastTarget = false;
        }
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = card ? 0 : 4; v.childControlWidth = false; v.childControlHeight = false;
        if (card) v.padding = new RectOffset(0, 0, 4, 4);
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
    private static readonly Color Danger = new(0.70f, 0.22f, 0.20f, 1f);     // button red: white text stays readable
    private static TimeSpan? _banLength;                                      // null = permanent
    private static bool _prefsDirty;                                          // autosave sliders: write the file once dragging stops
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

    private static readonly string[] TabNames = { "Players", "Access", "Lobby", "Saving" };

    private static void SetupTabs(Ui ui)
    {
        if (ui.Tabs.Count < 4)
        {
            // No tab bar found: show every page, one under the other, as before.
            foreach (var pg in ui.Pages) pg.SetActive(true);
            MelonLogger.Warning("Host settings: the game's tab bar wasn't found; showing one long page");
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var t = ui.Tabs[i];
            t.gameObject.name = "Tab " + TabNames[i];
            var lab = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lab != null) { lab.text = TabNames[i]; lab.enableWordWrapping = false; lab.overflowMode = TextOverflowModes.Overflow; }
            ui.TabLabels.Add(lab!);
            t.onValueChanged = new Toggle.ToggleEvent();
            t.onValueChanged.AddListener((UnityAction<bool>)(on => { if (on) ShowTab(ui, idx); }));
        }
        ShowTab(ui, 0);
    }

    private static void ShowTab(Ui ui, int i)
    {
        ui.Tab = i;
        for (int j = 0; j < ui.Pages.Count; j++) ui.Pages[j].SetActive(ui.Tabs.Count < 4 || j == i);
        for (int j = 0; j < ui.Tabs.Count; j++) ui.Tabs[j].SetIsOnWithoutNotify(j == i);
        RelayoutPage(ui, i);
        MelonCoroutines.Start(RelayoutNextFrame(ui, i));
        var sr = ui.Layout.GetComponentInParent<ScrollRect>();
        if (sr != null) sr.verticalNormalizedPosition = 1f;
    }

    // A page that was inactive hasn't been laid out since its lists last changed, and nested
    // layout groups only settle one level per pass, so rebuild it bottom-up when it's shown
    // (and once more next frame, after the ContentSizeFitters have run).
    private static void RelayoutPage(Ui ui, int i)
    {
        try
        {
            if (i < 0 || i >= ui.Pages.Count) return;
            var page = ui.Pages[i].GetComponent<RectTransform>();
            var groups = page.GetComponentsInChildren<LayoutGroup>(true);
            for (int g = groups.Length - 1; g >= 0; g--)
            {
                var rt = groups[g].GetComponent<RectTransform>();
                if (rt != null && rt != page) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(page);
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.Layout);
        }
        catch { }
    }

    private static IEnumerator RelayoutNextFrame(Ui ui, int i)
    {
        yield return null;
        if (ui.Tab == i) RelayoutPage(ui, i);
    }

    private static void RefreshTabLabels(Ui ui)
    {
        if (ui.TabLabels.Count < 4) return;
        int players = HostPlayers.List().Count;
        if (ui.TabLabels[0] != null) ui.TabLabels[0].text = players > 0 ? $"Players {players}" : "Players";
    }

    // Small overlapping avatar for a crew's member preview.
    private static void MiniAvatar(Transform row, ulong steamId, float x)
    {
        var go = new GameObject("Mini");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(row, false);
        Place(rt, x, 24, 24);
        var img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        Avatars.Show(img, steamId);
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
        if (main && ui.Tabs.Count >= 4)
        {
            // Main screen: the tab bar sits under the title, so the status goes at the right end of that row.
            srt.anchorMin = srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(1, 0.5f);
            srt.anchoredPosition = new Vector2(-24, -66);
            srt.sizeDelta = new Vector2(320, 20);
        }
        else
        {
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0, -56);
            srt.sizeDelta = new Vector2(-280, 18);
        }
        var st = sgo.GetComponent<TextMeshProUGUI>();
        st.text = ""; st.fontSize = 12; st.color = Muted; st.richText = false;
        st.alignment = main && ui.Tabs.Count >= 4 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center;
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
        var root = ui.Layout;
        // One page per tab, in tab order; only the selected one is active.
        for (int i = 0; i < 4; i++) { var pg = Box(ui, root); pg.name = "Page " + TabNames[i]; ui.Pages.Add(pg.gameObject); }
        Transform L = ui.Pages[2].transform;

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

        // Discord
        Section(ui, L, "Discord", out _);
        Hint(ui, L, "Shown under 'Playing Schedule I' on your Discord profile while the game runs", LabelX);
        Spacer(ui, L, 4);
        DiscordRow(ui, L, "Rich presence", () => DiscordPresence.Enabled, v => DiscordPresence.Enabled = v);
        DiscordRow(ui, L, "Player count", () => DiscordPresence.ShowPlayers, v => DiscordPresence.ShowPlayers = v);
        DiscordRow(ui, L, "Lobby type", () => DiscordPresence.ShowLobbyType, v => DiscordPresence.ShowLobbyType = v);
        DiscordRow(ui, L, "Save name", () => DiscordPresence.ShowSaveName, v => DiscordPresence.ShowSaveName = v);
        ui.DiscordHint = Hint(ui, L, "");

        // Whitelist
        L = ui.Pages[1].transform;
        ui.WhiteHeading = Section(ui, L, "Whitelist", out var wrow, first: true);
        RightBtn(ui, wrow, "Add friends", 0, 140, () => OpenPicker(ui, true));
        Spacer(ui, L, 0);
        var w = Row(ui, L, RowH);
        Text(ui, w, "Only listed players", LabelX, ControlX - LabelX - 8);
        ui.WlOff = Btn(ui, w, "Off", ControlX, 120, () => { Whitelist.SetEnabled(false); SetStatus(ui, "Whitelist off"); RefreshStatic(ui); _nextRefresh = 0; });
        ui.WlOn = Btn(ui, w, "On", ControlX + 128, 120, () => { Whitelist.SetEnabled(true); SetStatus(ui, "Whitelist on. Players already here stay"); RefreshStatic(ui); _nextRefresh = 0; });
        Hint(ui, L, "Starts empty each game. Bans always win");
        Spacer(ui, L, 4);
        ui.White = Box(ui, L, card: true);

        // Saved crews
        ui.CrewsHeading = Section(ui, L, "Saved crews", out _);
        Spacer(ui, L, 0);
        var cr = Row(ui, L, RowH);
        Text(ui, cr, "Save whitelist as", LabelX, ControlX - LabelX - 8);
        ui.CrewName = InputBox(ui, cr, ControlX, ui.RowW - ControlX - 152, "Crew name", 24);
        RightBtn(ui, cr, "Save crew", 0, 140, () =>
        {
            SetStatus(ui, Crews.SaveCurrent(ui.CrewName?.text ?? ""));
            if (ui.CrewName != null) ui.CrewName.text = "";
            _nextRefresh = 0;
        });
        Hint(ui, L, "Loading a crew replaces the whitelist with it and turns the whitelist on");
        Spacer(ui, L, 4);
        ui.CrewList = Box(ui, L, card: true);

        // Players
        L = ui.Pages[0].transform;
        ui.PlayersHeading = Section(ui, L, "In the game", out _, first: true);
        Hint(ui, L, "Kick lets them rejoin. Ban keeps them out for the ban length set on Access. Players with BiggerLobbies are told why", LabelX);
        Spacer(ui, L, 4);
        ui.Players = Box(ui, L, card: true);

        ui.DepartedHeading = Section(ui, L, "Left the game", out _);
        Hint(ui, L, "Saved players who aren't here and still carry items. Take items moves their items and cash to you", LabelX);
        Spacer(ui, L, 4);
        ui.Departed = Box(ui, L, card: true);

        ui.TakenHeading = Section(ui, L, "Taken items", out _);
        Hint(ui, L, "What you took in this save. Give back returns what you still carry to their empty slots. Originals are backed up in UserData/BiggerLobbies/Taken", LabelX);
        Spacer(ui, L, 4);
        ui.TakenList = Box(ui, L, card: true);

        // Banned players
        L = ui.Pages[1].transform;
        ui.BlackHeading = Section(ui, L, "Banned", out var brow);
        RightBtn(ui, brow, "Ban friends", 0, 140, () => OpenPicker(ui, false));
        Spacer(ui, L, 0);
        var lenRow = Row(ui, L, RowH);
        Text(ui, lenRow, "Ban length", LabelX, ControlX - LabelX - 8);
        float bx = ControlX;
        foreach (var (lenLabel, len) in BanLength.Options)
        {
            var l = len; var lab = lenLabel;
            var b = Btn(ui, lenRow, lenLabel, bx, 100, () => { _banLength = l; SetStatus(ui, $"New bans: {lab.ToLowerInvariant()}"); RefreshStatic(ui); });
            ui.BanLenButtons.Add((b, len));
            bx += 108;
        }
        var reasonRow = Row(ui, L, RowH);
        Text(ui, reasonRow, "Ban reason", LabelX, ControlX - LabelX - 8);
        ui.BanReason = InputBox(ui, reasonRow, ControlX, ui.RowW - ControlX, "Reason (optional)", 40);
        Hint(ui, L, "Used for the next ban, then the reason clears. Players with BiggerLobbies see it. Bans are saved between games and end on their own");
        Spacer(ui, L, 4);
        ui.Black = Box(ui, L, card: true);

        // Autosave
        L = ui.Pages[3].transform;
        Section(ui, L, "Autosave", out var arow, first: true);
        RightBtn(ui, arow, "Save now", 0, 140, () => { SetStatus(ui, AutoSave.SaveNow()); _nextRefresh = 0; });
        Spacer(ui, L, 0);
        var wr = Row(ui, L, RowH);
        Text(ui, wr, "World saves", LabelX, ControlX - LabelX - 8);
        ui.WorldOff = Btn(ui, wr, "Off", ControlX, 120, () => { AutoSave.WorldEnabled = false; MelonPreferences.Save(); SetStatus(ui, "World autosave off"); RefreshStatic(ui); });
        ui.WorldOn = Btn(ui, wr, "On", ControlX + 128, 120, () => { AutoSave.WorldEnabled = true; MelonPreferences.Save(); SetStatus(ui, "World autosave on"); RefreshStatic(ui); });
        ui.WorldEvery = SliderRow(ui, L, "Every", 1, 60, AutoSave.WorldMinutes, out ui.WorldValue, v => { AutoSave.WorldMinutes = v; ui.WorldValue.text = $"{v} min"; _prefsDirty = true; });
        var pr = Row(ui, L, RowH);
        Text(ui, pr, "Player saves", LabelX, ControlX - LabelX - 8);
        ui.PlayersOff = Btn(ui, pr, "Off", ControlX, 120, () => { AutoSave.PlayersEnabled = false; MelonPreferences.Save(); SetStatus(ui, "Player saves off"); RefreshStatic(ui); });
        ui.PlayersOn = Btn(ui, pr, "On", ControlX + 128, 120, () => { AutoSave.PlayersEnabled = true; MelonPreferences.Save(); SetStatus(ui, "Player saves on"); RefreshStatic(ui); });
        ui.PlayersEvery = SliderRow(ui, L, "Every", 10, 300, AutoSave.PlayerSeconds, out ui.PlayersValue, v => { AutoSave.PlayerSeconds = v; ui.PlayersValue.text = $"{v} s"; _prefsDirty = true; });
        ui.LastSave = Hint(ui, L, "");

        BottomFade(root);
        RefreshStatic(ui);
    }

    // An Off/On pair like the autosave rows, bound to one Discord setting.
    private static void DiscordRow(Ui ui, Transform parent, string label, Func<bool> get, Action<bool> set)
    {
        var r = Row(ui, parent, RowH);
        Text(ui, r, label, LabelX, ControlX - LabelX - 8);
        var off = Btn(ui, r, "Off", ControlX, 120, () => { set(false); SetStatus(ui, $"Discord: {label.ToLowerInvariant()} off"); RefreshStatic(ui); });
        var on = Btn(ui, r, "On", ControlX + 128, 120, () => { set(true); SetStatus(ui, $"Discord: {label.ToLowerInvariant()} on"); RefreshStatic(ui); });
        ui.DiscordToggles.Add((off, on, get));
    }

    private static void SetStatus(Ui ui, string s)
    {
        if (ui.Status != null) ui.Status.text = s ?? "";
    }

    private static void RefreshStatic(Ui ui)
    {
        foreach (var (b, m) in ui.PrivacyButtons) Highlight(b, LobbyPrivacy.Current == m);
        foreach (var (b, len) in ui.BanLenButtons)
        {
            bool on = len == _banLength;
            var c = b.colors;
            c.normalColor = on ? (len == null ? Danger : Accent) : Grey;   // Permanent selected shows red
            c.selectedColor = c.normalColor;
            b.colors = c;
        }
        if (ui.PrivacyHint != null) ui.PrivacyHint.text = PrivacyHint(LobbyPrivacy.Current);
        Highlight(ui.WlOff, !Whitelist.Enabled);
        Highlight(ui.WlOn, Whitelist.Enabled);
        Highlight(ui.WorldOff, !AutoSave.WorldEnabled);
        Highlight(ui.WorldOn, AutoSave.WorldEnabled);
        foreach (var (off, on, get) in ui.DiscordToggles)
        {
            bool v = get();
            Highlight(off, !v); Highlight(on, v);
            bool usable = DiscordPresence.Enabled || ReferenceEquals(get, ui.DiscordToggles[0].get);
            off.interactable = usable; on.interactable = usable;
        }
        if (ui.DiscordHint != null)
            ui.DiscordHint.text = !DiscordPresence.Enabled ? "Nothing is sent to Discord"
                : DiscordPresence.Status == "Connected" ? (DiscordPresence.ShowSaveName ? "Connected. Your organisation's name is visible to anyone who can see your profile" : "Connected")
                : "Discord isn't running on this PC. It connects when it starts";
        Highlight(ui.PlayersOff, !AutoSave.PlayersEnabled);
        Highlight(ui.PlayersOn, AutoSave.PlayersEnabled);
        if (!_prefsDirty)
        {
            ui.WorldEvery.SetValueWithoutNotify(AutoSave.WorldMinutes);
            ui.PlayersEvery.SetValueWithoutNotify(AutoSave.PlayerSeconds);
        }
        ui.WorldValue.text = $"{AutoSave.WorldMinutes} min";
        ui.PlayersValue.text = $"{AutoSave.PlayerSeconds} s";
        var since = AutoSave.SecondsSinceLastSave();
        ui.LastSave.text = since == null ? "Saves run while you host a save"
            : since.Value < 60 ? "Last save under a minute ago"
            : $"Last save {Mathf.FloorToInt(since.Value / 60f)} min ago";
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
            if (g.name == "Bg") continue;
            g.SetActive(false);
            UnityEngine.Object.Destroy(g);
        }
    }

    private static void Empty(Ui ui, Transform box, string s)
    {
        var r = Row(ui, box, 36);
        Text(ui, r, s, LabelX, ui.RowW - LabelX, Muted, 13);
    }

    // Nested size-to-content layouts settle one level per pass, so a list that changes while
    // the screen is open would keep its old height and overlap the section below. Rebuild
    // the list, then its page, then the scroll content, in that order.
    private static void Relayout(Ui ui, RectTransform box)
    {
        try
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(box);
            if (box.parent is RectTransform page) LayoutRebuilder.ForceRebuildLayoutImmediate(page);
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.Layout);
        }
        catch { }
    }

    // Every other row in a card gets a faint stripe, so long lists stay easy to follow.
    private static void Stripe(Transform box)
    {
        int n = 0;
        for (int i = 0; i < box.childCount; i++)
        {
            var c = box.GetChild(i);
            if (!c.gameObject.activeSelf || c.name == "Bg") continue;
            if (n++ % 2 == 0) continue;
            if (c.GetComponent<Image>() != null) continue;
            var img = c.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.025f);
            img.raycastTarget = false;
        }
    }

    private static string TakenAgo(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return "Just now";
        if (d.TotalMinutes < 60) return $"{Math.Floor(d.TotalMinutes)} min ago";
        if (d.TotalHours < 48) return $"{Math.Floor(d.TotalHours)} h ago";
        return $"{Math.Floor(d.TotalDays)} days ago";
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return "Left just now";
        if (d.TotalMinutes < 60) return $"Left {Math.Floor(d.TotalMinutes)} min ago";
        if (d.TotalHours < 48) return $"Left {Math.Floor(d.TotalHours)} h ago";
        return $"Left {Math.Floor(d.TotalDays)} days ago";
    }

    // An item icon with its count in the corner, like the game's own inventory slots.
    private static void ItemChip(Ui ui, Transform row, DepartedPlayers.Slot slot, float x)
    {
        var go = new GameObject("Item");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(row, false);
        Place(rt, x, 30, 30);
        var bg = go.AddComponent<Image>();
        var src = ui.ButtonT.GetComponent<Image>();
        if (src != null) { bg.sprite = src.sprite; bg.type = src.type; bg.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier; }
        bg.color = new Color(0f, 0f, 0f, 0.35f);
        bg.raycastTarget = false;

        var icon = DepartedPlayers.Icon(slot.Item);
        if (icon != null)
        {
            var ig = new GameObject("Icon");
            var irt = ig.AddComponent<RectTransform>();
            irt.SetParent(rt, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(3, 3); irt.offsetMax = new Vector2(-3, -3);
            var img = ig.AddComponent<Image>();
            img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
        }
        var qgo = UnityEngine.Object.Instantiate(ui.TextT, rt);
        qgo.name = "Qty";
        var qrt = qgo.GetComponent<RectTransform>();
        qrt.anchorMin = qrt.anchorMax = new Vector2(1, 0);
        qrt.pivot = new Vector2(1, 0);
        qrt.anchoredPosition = new Vector2(-1, -1);
        qrt.sizeDelta = new Vector2(30, 14);
        var q = qgo.GetComponent<TextMeshProUGUI>();
        q.text = icon == null ? DepartedPlayers.SafeName(slot.Item, slot.Id) : (slot.Quantity > 1 ? slot.Quantity.ToString() : "");
        q.fontSize = icon == null ? 8 : 11; q.richText = false;
        q.alignment = icon == null ? TextAlignmentOptions.Center : TextAlignmentOptions.BottomRight;
        q.enableWordWrapping = false;
        q.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void RefreshLists(Ui ui, bool force)
    {
        // Saved crews
        ui.CrewsHeading.text = Heading("Saved crews", Crews.All.Count);
        var ck = string.Join("|", Crews.All.Select(c => c.Name + ":" + c.Members.Count)) + "|" + ui.ConfirmDeleteCrew;
        if (force || ck != ui.LastCrewKey)
        {
            ui.LastCrewKey = ck;
            Clear(ui.CrewList);
            if (Crews.All.Count == 0) Empty(ui, ui.CrewList, "No crews yet. Fill the whitelist, then save it here");
            foreach (var c in Crews.All)
            {
                var crew = c;
                var r = Row(ui, ui.CrewList, RowH);
                Text(ui, r, crew.Name, LabelX, ControlX - LabelX - 8, plain: true);
                int shown = Math.Min(4, crew.Members.Count);
                for (int i = 0; i < shown; i++) MiniAvatar(r, crew.Members[i].SteamId, ControlX + i * 20);
                string names = string.Join(", ", crew.Members.Take(2).Select(m => m.Name.Length > 0 ? m.Name : m.SteamId.ToString()));
                if (crew.Members.Count > 2) names += $" +{crew.Members.Count - 2}";
                float tx = ControlX + shown * 20 + 12;
                Text(ui, r, names, tx, ui.RowW - tx - 220, Muted, 13, plain: true);
                RightBtn(ui, r, "Load", 108, 100, () => { SetStatus(ui, Crews.Load(crew.Name)); ui.ConfirmDeleteCrew = ""; RefreshStatic(ui); _nextRefresh = 0; });
                bool confirming = ui.ConfirmDeleteCrew == crew.Name;
                var del = RightBtn(ui, r, confirming ? "Confirm" : "Delete", 0, 100, () =>
                {
                    if (ui.ConfirmDeleteCrew == crew.Name) { Crews.Delete(crew.Name); ui.ConfirmDeleteCrew = ""; SetStatus(ui, $"Deleted {crew.Name}"); }
                    else { ui.ConfirmDeleteCrew = crew.Name; SetStatus(ui, $"Click Confirm to delete {crew.Name}"); }
                    _nextRefresh = 0;
                });
                if (confirming) { var cc = del.colors; cc.normalColor = Danger; cc.selectedColor = Danger; del.colors = cc; }
            }
            Stripe(ui.CrewList); Relayout(ui, ui.CrewList);
        }

        // Players
        var players = HostPlayers.List();
        ui.PlayersHeading.text = Heading("In the game", players.Count);
        var pk = string.Join("|", players.ConvertAll(p => $"{p.ClientId}:{p.SteamId}:{p.Name}:{HelloKey(p.SteamId)}")) + "|" + ui.ConfirmBlacklist + InstanceFinder.IsServer;
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
                // Players with BiggerLobbies said hello: their version, and any mods that differ from yours.
                ModHandshake.Hello? hello = null;
                if (row.SteamId != 0 && ModHandshake.TryGet(row.SteamId, out var h)) hello = h;
                string diff = hello == null ? "" : ModsLine(hello);
                var r = Row(ui, ui.Players, diff.Length > 0 ? 48 : RowH);
                Avatar(r, row.SteamId, Playing);
                var nm = Text(ui, r, row.Name, NameX, ui.RowW - NameX - 324 - (hello != null ? 72 : 0), plain: true);
                if (diff.Length > 0)
                {
                    nm.GetComponent<RectTransform>().anchoredPosition = new Vector2(NameX, 8);
                    var dl = Text(ui, r, diff, NameX, ui.RowW - NameX - 324, Muted, 11, plain: true);
                    dl.GetComponent<RectTransform>().anchoredPosition = new Vector2(NameX, -10);
                }
                if (hello != null)
                {
                    bool older = ModHandshake.CompareToMine(hello.Version) < 0;
                    var vt = Text(ui, r, "v" + hello.Version, ui.RowW - 308 - 72, 64, older ? Blocked : Muted, 12, plain: true);
                    vt.alignment = TextAlignmentOptions.MidlineRight;
                }
                var ping = Text(ui, r, "", ui.RowW - 308, 72, Muted, 13);
                ping.alignment = TextAlignmentOptions.MidlineRight;
                ui.PingTexts[row.ClientId] = ping;
                RightBtn(ui, r, "Kick", 128, 100, () => { SetStatus(ui, HostPlayers.Kick(row, false)); _nextRefresh = 0; });
                bool confirming = ui.ConfirmBlacklist != 0 && ui.ConfirmBlacklist == row.SteamId;
                var bb = RightBtn(ui, r, confirming ? "Confirm" : "Ban", 0, 120, () =>
                {
                    if (ui.ConfirmBlacklist == row.SteamId) { ui.ConfirmBlacklist = 0; SetStatus(ui, HostPlayers.Kick(row, true, _banLength, TakeReason(ui))); }
                    else { ui.ConfirmBlacklist = row.SteamId; SetStatus(ui, $"Click Confirm to ban {row.Name} ({BanLength.Label(_banLength).ToLowerInvariant()}{ReasonNote(ui)})"); }
                    _nextRefresh = 0;
                });
                if (confirming) { var cc = bb.colors; cc.normalColor = Danger; cc.selectedColor = Danger; bb.colors = cc; }
                bb.interactable = row.SteamId != 0;
            }
            Stripe(ui.Players); Relayout(ui, ui.Players);
        }

        // Left the game
        var gone = DepartedPlayers.List();
        ui.DepartedHeading.text = Heading("Left the game", gone.Count);
        var dk = string.Join("|", gone.Select(g => $"{g.SteamId}:{g.Cash:0}:{string.Join(",", g.Items.Select(i => i.Id + i.Quantity))}:{Ago(g.SavedUtc)}:{(g.Compare == null ? "" : DepartedPlayers.CompareLine(g.Compare))}")) + InstanceFinder.IsServer;
        if (force || dk != ui.LastDepartedKey)
        {
            ui.LastDepartedKey = dk;
            Clear(ui.Departed);
            if (gone.Count == 0)
                Empty(ui, ui.Departed, InstanceFinder.IsServer ? "Nobody has left with anything, and every save matches what they left with" : "Players who leave with items appear here while you host");
            foreach (var g in gone)
            {
                var who = g;
                // With a record of what they left with, a third line compares it with their save.
                bool cmp = who.Compare != null;
                var r = Row(ui, ui.Departed, cmp ? 70 : 48);
                Avatar(r, who.SteamId, null);
                var nm = Text(ui, r, who.Name, NameX, 150, plain: true);
                nm.GetComponent<RectTransform>().anchoredPosition = new Vector2(NameX, cmp ? 16 : 8);
                var meta = Text(ui, r, Ago(who.SavedUtc), NameX, 150, Muted, 11);
                meta.GetComponent<RectTransform>().anchoredPosition = new Vector2(NameX, cmp ? -2 : -10);
                if (cmp)
                {
                    var cl = Text(ui, r, DepartedPlayers.CompareLine(who.Compare!), NameX, ui.RowW - NameX - 12, who.Compare!.Matches ? Muted : Blocked, 11, plain: true);
                    cl.GetComponent<RectTransform>().anchoredPosition = new Vector2(NameX, -24);
                }
                float x = NameX + 158;
                int shown = Math.Min(6, who.Items.Count);
                for (int i = 0; i < shown; i++) { ItemChip(ui, r, who.Items[i], x); x += 34; }
                if (who.Items.Count > shown) { Text(ui, r, $"+{who.Items.Count - shown}", x + 2, 36, Muted, 12); x += 40; }
                if (who.Cash >= 0.01f) Text(ui, r, $"${who.Cash:N0}", x + 4, ui.RowW - x - 150, Playing, 13);
                RightBtn(ui, r, "Take items", 0, 130, () => { SetStatus(ui, DepartedPlayers.Take(who.SteamId)); DepartedPlayers.Invalidate(); _nextRefresh = 0; });
            }
            Stripe(ui.Departed); Relayout(ui, ui.Departed);
        }

        // Taken items
        var log = DepartedPlayers.History();
        ui.TakenHeading.text = Heading("Taken items", log.Count);
        var tk = string.Join("|", log.Select(t => $"{t.Utc.Ticks}:{TakenAgo(t.Utc)}:{t.Record?.AllReturned}"));
        if (force || tk != ui.LastTakenKey)
        {
            ui.LastTakenKey = tk;
            Clear(ui.TakenList);
            if (log.Count == 0) Empty(ui, ui.TakenList, "Nothing taken in this save");
            foreach (var t in log)
            {
                var entry = t;
                var r = Row(ui, ui.TakenList, RowH);
                MiniAvatar(r, t.SteamId, LabelX);
                string who = t.Name.Length > 0 && !t.Name.All(char.IsDigit) ? t.Name : DepartedPlayers.NameOf(t.SteamId);
                Text(ui, r, who, LabelX + 28, 150, plain: true);
                // Takes with a record get Give back; give backs and older takes are just listed.
                float btn = t.Record != null ? 120 : 0;
                Text(ui, r, t.Summary, LabelX + 186, ui.RowW - LabelX - 186 - 112 - btn, Muted, 13, plain: true);
                var when = Text(ui, r, TakenAgo(t.Utc), ui.RowW - 104 - btn, 96, Muted, 12);
                when.alignment = TextAlignmentOptions.MidlineRight;
                if (t.Record == null) continue;
                var gb = RightBtn(ui, r, "Give back", 0, 112, () =>
                {
                    SetStatus(ui, DepartedPlayers.GiveBack(entry, msg => { SetStatus(ui, msg); DepartedPlayers.Invalidate(); _nextRefresh = 0; }));
                    _nextRefresh = 0;
                });
                if (t.Record.AllReturned) { SetLabel(gb, "Returned"); gb.interactable = false; }
            }
            Stripe(ui.TakenList); Relayout(ui, ui.TakenList);
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
            Stripe(ui.White); Relayout(ui, ui.White);
        }

        // Banned players (the key holds the time left, so the countdown updates)
        ui.BlackHeading.text = Heading("Banned", BanList.Bans.Count);
        var bk = string.Join("|", BanKey(BanList.Bans));
        if (force || bk != ui.LastBlackKey)
        {
            ui.LastBlackKey = bk;
            Clear(ui.Black);
            if (BanList.Bans.Count == 0) Empty(ui, ui.Black, "Nobody banned");
            foreach (var e in BanList.Bans)
            {
                var id = e.SteamId; var name = e.Name.Length > 0 ? e.Name : DepartedPlayers.NameOf(e.SteamId);
                var r = Row(ui, ui.Black, RowH);
                Avatar(r, id, Blocked);
                bool hasReason = e.Reason.Length > 0;
                Text(ui, r, name, NameX, hasReason ? 170 : 312, plain: true);
                if (hasReason) Text(ui, r, e.Reason, NameX + 176, 190, Muted, 12, plain: true);
                Text(ui, r, BanList.Remaining(e), NameX + 372, 150, e.Permanent ? Blocked : Muted, 13);
                RightBtn(ui, r, "Unban", 0, 100, () => { BanList.Unban(id); SetStatus(ui, $"Unbanned {name}"); _nextRefresh = 0; });
            }
            Stripe(ui.Black); Relayout(ui, ui.Black);
        }
    }

    private static IEnumerable<string> PresenceKey(IReadOnlyList<BanList.Entry> list)
    {
        foreach (var e in list) yield return ((int)Friends.StateOf(e.SteamId)).ToString();
    }

    private static IEnumerable<string> BanKey(IReadOnlyList<BanList.Entry> list)
    {
        foreach (var e in list) yield return e.SteamId + ":" + BanList.Remaining(e) + ":" + e.Reason;
    }

    private static string HelloKey(ulong id) =>
        id != 0 && ModHandshake.TryGet(id, out var h) ? $"{h.Version}/{string.Join(",", h.Missing)}/{string.Join(",", h.Extra)}" : "";

    private static string ModsLine(ModHandshake.Hello h)
    {
        var parts = new List<string>();
        if (h.Missing.Count > 0) parts.Add("missing: " + string.Join(", ", h.Missing));
        if (h.Extra.Count > 0) parts.Add("extra: " + string.Join(", ", h.Extra));
        return string.Join("; ", parts);
    }

    // The reason typed on the Access tab, for the ban being made now. The box clears for the next one.
    private static string TakeReason(Ui ui)
    {
        string r = BanList.CleanReason(ui.BanReason?.text);
        if (ui.BanReason != null) ui.BanReason.text = "";
        return r;
    }

    private static string ReasonNote(Ui ui)
    {
        string r = BanList.CleanReason(ui.BanReason?.text);
        return r.Length > 0 ? ": " + r : "";
    }

    private static IEnumerable<string> ListKey(IReadOnlyList<BanList.Entry> list)
    {
        foreach (var e in list) yield return e.SteamId.ToString();
    }

    // --- Friends picker ----------------------------------------------------------------------------

    private static void OpenPicker(Ui ui, bool whitelist)
    {
        ui.PickForWhitelist = whitelist;
        ui.PickerTitle.text = whitelist ? "Add to whitelist" : "Ban friends";
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
            ui.PickerStatus.text = ui.PickForWhitelist ? "Who can join this session" : $"Ban length: {BanLength.Label(_banLength).ToLowerInvariant()}{(ReasonNote(ui).Length > 0 ? ". Reason" + ReasonNote(ui) : "")}";
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
            b = RightBtn(ui, r, ui.PickForWhitelist ? "Add" : "Ban", 0, 100, () =>
            {
                bool ok = ui.PickForWhitelist ? Whitelist.Add(fr.SteamId, fr.Name) : AddBlack(ui, fr);
                if (ok) Done(b!, ui.PickForWhitelist ? "Added" : "Banned");
                _nextRefresh = 0;
            });
            if (ui.PickForWhitelist)
            {
                if (inBlack) { SetLabel(b, "Banned"); b.interactable = false; }
                else if (inWhite) Done(b, "Added");
            }
            else if (inBlack) Done(b, "Banned");
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

    // The game's slider row, laid on the control column. The value label sits after the bar.
    private static Slider SliderRow(Ui ui, Transform parent, string label, int min, int max, int value, out TextMeshProUGUI valueText, Action<int> onChange)
    {
        var go = UnityEngine.Object.Instantiate(ui.SliderT, parent);
        go.name = "Slider " + label;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(ui.RowW, RowH);
        var lab = go.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        lab.text = label;
        Place(lab.GetComponent<RectTransform>(), LabelX, ControlX - LabelX - 8, 30);
        var slider = go.GetComponentInChildren<Slider>(true);
        Place(slider.GetComponent<RectTransform>(), ControlX, 376, 20);
        var vt = slider.transform.Find("Value").GetComponent<TextMeshProUGUI>();
        var vrt = vt.GetComponent<RectTransform>();
        vrt.anchorMin = vrt.anchorMax = new Vector2(1, 0.5f);
        vrt.pivot = new Vector2(0, 0.5f);
        vrt.anchoredPosition = new Vector2(16, 0);
        vrt.sizeDelta = new Vector2(72, 30);
        vt.alignment = TextAlignmentOptions.MidlineLeft;
        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = true;
        slider.minValue = min; slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener((UnityAction<float>)(v => { try { onChange(Mathf.RoundToInt(v)); } catch { } }));
        valueText = vt;
        return slider;
    }

    // A search field in the game's style: rounded dark box, the game's font, a muted placeholder.
    private static TMP_InputField SearchBox(Ui ui, Transform row)
    {
        var input = InputBox(ui, row, LabelX, ui.RowW - LabelX, "Search friends", 32);
        input.onValueChanged.AddListener((UnityAction<string>)(q => FilterPicker(ui, q)));
        return input;
    }

    private static TMP_InputField InputBox(Ui ui, Transform row, float x, float w, string hint, int limit)
    {
        var box = new GameObject("InputBox");
        var brt = box.AddComponent<RectTransform>();
        brt.SetParent(row, false);
        Place(brt, x, w, 32);
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

        var placeholder = MakeText("Placeholder", hint, Muted);
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
        input.characterLimit = limit;
        input.richText = false;
        return input;
    }

    private static bool AddBlack(Ui ui, Friends.Friend f)
    {
        BanList.Ban(f.SteamId, f.Name, _banLength, TakeReason(ui));
        return BanList.IsBanned(f.SteamId);
    }

    // --- Open / update -------------------------------------------------------------------------------

    private static void Open(Ui ui)
    {
        ui.ConfirmBlacklist = 0;
        ui.ConfirmDeleteCrew = "";
        ShowTab(ui, ui.InGame ? 0 : 2);
        RefreshStatic(ui);
        RefreshLists(ui, true);
        SetStatus(ui, ui.InGame || HostLobby.TryGet(out _) ? "" : "These apply when you host a save");
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
        HideBigLobbyGrid(ui != null);
        bool dragging = false;
        try { dragging = ui != null && Mouse.current != null && Mouse.current.leftButton.isPressed; } catch { }
        if (_pendingSize >= 0 && (ui == null || (!dragging && Time.unscaledTime >= _applySizeAt)))
        {
            string msg = LobbySize.SetLive(_pendingSize);
            _pendingSize = -1;
            foreach (var u in new[] { _pause, _menu })
                if (u != null) { try { SetStatus(u, msg); RefreshStatic(u); } catch { } }
        }
        if (_prefsDirty && !dragging) { _prefsDirty = false; MelonPreferences.Save(); }
        if (ui == null) return;
        if (_pendingSize >= 0) ui.SizeValue.text = _pendingSize.ToString();

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 1f;
            if (ui.Screen.IsOpen) { RefreshStatic(ui); RefreshLists(ui, false); RefreshTabLabels(ui); }
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

    // The game's lobby avatar grid (top right) draws over Host settings once it's more than two
    // rows tall (40+ players). Hide it while the screen is open; the Players tab lists everyone.
    private static GameObject? _hiddenGrid;

    private static void HideBigLobbyGrid(bool screenOpen)
    {
        try
        {
            if (!screenOpen)
            {
                if (_hiddenGrid != null) { if (!_hiddenGrid.WasCollected) _hiddenGrid.SetActive(true); _hiddenGrid = null; }
                return;
            }
            if (_hiddenGrid != null) return;
            var grid = LobbySize.Grid;
            if (grid == null || grid.WasCollected || !grid.gameObject.activeInHierarchy) return;
            int shown = 0;
            var t = grid.transform;
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.activeSelf) shown++;
            if (shown <= 41) return;   // invite button + two rows of 20
            _hiddenGrid = grid.gameObject;
            _hiddenGrid.SetActive(false);
        }
        catch { }
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

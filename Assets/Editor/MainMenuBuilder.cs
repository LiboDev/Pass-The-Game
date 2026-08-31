using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Authors Assets/Scenes/MainMenu.unity once, as real serialized GameObjects. Nothing here runs
/// at play time - this is a one-shot generator so the menu did not have to be clicked together
/// by hand, and the scene it writes is the artifact. Edit the scene from then on; re-running
/// this throws those edits away, which is why it asks first.
///
/// Writes nothing but the scene. The build scene list is a ProjectSettings file and belongs to the
/// user, so adding MainMenu to it is a checklist step rather than something this quietly does.
/// </summary>
public static class MainMenuBuilder
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string MixerPath = "Assets/Audio/GameAudioMixer.mixer";

    private static readonly Color Ink = new Color(0.031f, 0.031f, 0.039f, 1f);
    private static readonly Color PanelFill = new Color(0.059f, 0.055f, 0.067f, 0.97f);
    private static readonly Color Bone = new Color(0.851f, 0.835f, 0.804f, 1f);
    private static readonly Color BoneDim = new Color(0.851f, 0.835f, 0.804f, 0.45f);
    private static readonly Color Blood = new Color(0.639f, 0.18f, 0.133f, 1f);
    private static readonly Color Track = new Color(1f, 1f, 1f, 0.1f);
    private static readonly Color PortraitFill = new Color(0.11f, 0.1f, 0.122f, 1f);

    private static Font font;

    // Seeds the credits list. Duplicating one of these rows in the hierarchy is how a dev adds
    // themselves - the layout group and size fitter grow the list on their own.
    private static readonly (string Name, string Role)[] PlaceholderCredits =
    {
        ("Developer One", "Programming"),
        ("Developer Two", "Art"),
        ("Developer Three", "Design"),
        ("Developer Four", "Audio"),
        ("Developer Five", "Writing"),
        ("Developer Six", "Level Design"),
        ("Developer Seven", "Production"),
    };

    [MenuItem("Tools/Build Main Menu Scene")]
    public static void Build()
    {
        if (!Application.isBatchMode
            && System.IO.File.Exists(ScenePath)
            && !EditorUtility.DisplayDialog(
                "Rebuild Main Menu",
                ScenePath + " already exists. Rebuilding replaces it and discards any edits "
                + "made to it in the editor.",
                "Replace it",
                "Cancel"))
        {
            return;
        }

        BuildScene();
    }

    /// <summary>
    /// Built into an additively loaded scene and closed again, so whatever the editor already has
    /// open is neither saved nor unloaded on the way past.
    /// </summary>
    private static void BuildScene()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Additive leaves whatever the editor has open alone, but Unity refuses it while an
        // untitled scene is loaded - which is the state a batch run starts in. There is nothing
        // to preserve in that case, so it falls back to replacing it.
        Scene previous = SceneManager.GetActiveScene();
        bool additive = !string.IsNullOrEmpty(previous.path);

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);

        if (additive)
        {
            SceneManager.SetActiveScene(scene);
        }

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Ink;
        cameraObject.tag = "MainCamera";

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        AudioController audio = new GameObject("Audio Controller").AddComponent<AudioController>();
        SetReference(audio, "mixer", AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath));

        Transform canvas = BuildCanvas();
        MainMenuNavigation nav = canvas.gameObject.AddComponent<MainMenuNavigation>();

        GameObject mainPage = BuildMainPage(canvas, nav);
        GameObject settingsPage = BuildSettingsPage(canvas, nav);
        GameObject creditsPage = BuildCreditsPage(canvas, nav);

        SetReference(nav, "mainPage", mainPage);
        SetReference(nav, "settingsPage", settingsPage);
        SetReference(nav, "creditsPage", creditsPage);

        // Only the first page is left on; the other two are switched by ShowPage at runtime.
        settingsPage.SetActive(false);
        creditsPage.SetActive(false);

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        if (additive)
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }

        AssetDatabase.SaveAssets();

        Debug.Log("Built " + ScenePath + ". Add it to the build scene list yourself: "
                  + "File > Build Profiles > Scene List.");
    }

    private static Transform BuildCanvas()
    {
        GameObject canvasObject = new GameObject(
            "Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject backdrop = Panel("Backdrop", canvasObject.transform, Ink);
        Stretch(backdrop.GetComponent<RectTransform>());

        return canvasObject.transform;
    }

    private static GameObject BuildMainPage(Transform canvas, MainMenuNavigation nav)
    {
        GameObject page = NewRect("Main Page", canvas);
        Stretch(page.GetComponent<RectTransform>());

        Text title = Label("Title", page.transform, "PASS THE GAME", 120, TextAnchor.MiddleCenter);
        title.color = Bone;
        title.fontStyle = FontStyle.Bold;
        Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(1400f, 160f));

        Text subtitle = Label(
            "Subtitle", page.transform, "A  H O R R O R  A N T H O L O G Y", 30, TextAnchor.MiddleCenter);
        subtitle.color = BoneDim;
        Anchor(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(1400f, 44f));

        GameObject rule = Panel("Rule", page.transform, Blood);
        Anchor(rule.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -410f), new Vector2(420f, 3f));

        GameObject buttons = NewRect("Buttons", page.transform);
        Anchor(buttons.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(460f, 400f));

        VerticalLayoutGroup layout = buttons.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        MenuButton(buttons.transform, "Play", nav.Play);
        MenuButton(buttons.transform, "Settings", nav.ShowSettings);
        MenuButton(buttons.transform, "Credits", nav.ShowCredits);
        MenuButton(buttons.transform, "Quit", nav.Quit);

        return page;
    }

    private static GameObject BuildSettingsPage(Transform canvas, MainMenuNavigation nav)
    {
        GameObject page = NewRect("Settings Page", canvas);
        Stretch(page.GetComponent<RectTransform>());

        Transform panel = BuildPanel(page.transform, "AUDIO", new Vector2(760f, 620f)).transform;

        SliderRow(panel, "Master", nav.SetMasterVolume, out Slider master, out Text masterValue);
        SliderRow(panel, "Music", nav.SetMusicVolume, out Slider music, out Text musicValue);
        SliderRow(panel, "Sound Effects", nav.SetSfxVolume, out Slider sfx, out Text sfxValue);

        SetReference(nav, "masterSlider", master);
        SetReference(nav, "musicSlider", music);
        SetReference(nav, "sfxSlider", sfx);
        SetReference(nav, "masterValueLabel", masterValue);
        SetReference(nav, "musicValueLabel", musicValue);
        SetReference(nav, "sfxValueLabel", sfxValue);

        Spacer(panel);
        BackButton(panel, nav);
        return page;
    }

    private static GameObject BuildCreditsPage(Transform canvas, MainMenuNavigation nav)
    {
        GameObject page = NewRect("Credits Page", canvas);
        Stretch(page.GetComponent<RectTransform>());

        Transform panel = BuildPanel(page.transform, "CREDITS", new Vector2(940f, 820f)).transform;

        GameObject scrollView = NewRect("Scroll View", panel);
        scrollView.AddComponent<LayoutElement>().flexibleHeight = 1f;
        ScrollRect scroll = scrollView.AddComponent<ScrollRect>();

        GameObject viewport = NewRect("Viewport", scrollView.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.pivot = new Vector2(0f, 1f);
        viewportRect.offsetMax = new Vector2(-22f, 0f);
        viewport.AddComponent<RectMask2D>();

        GameObject content = NewRect("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        // The two pieces that make the list dynamic: the layout group stacks whatever entries are
        // under Content, and the fitter grows Content to whatever that stack needs. Adding a dev
        // is duplicating an entry - no code involved, and no count to keep in sync.
        VerticalLayoutGroup list = content.AddComponent<VerticalLayoutGroup>();
        list.padding = new RectOffset(4, 4, 0, 0);
        list.spacing = 10f;
        list.childAlignment = TextAnchor.UpperLeft;
        list.childControlWidth = true;
        list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (int i = 0; i < PlaceholderCredits.Length; i++)
        {
            CreditEntry(content.transform, i + 1, PlaceholderCredits[i].Name, PlaceholderCredits[i].Role);
        }

        Scrollbar scrollbar = BuildScrollbar(scrollView.transform);

        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        BackButton(panel, nav);
        return page;
    }

    /// <summary>One credits row: a portrait slot on the left, a name and a role on the right.</summary>
    private static void CreditEntry(Transform parent, int index, string name, string role)
    {
        GameObject entry = NewRect("Credit " + index + " - " + name, parent);
        entry.AddComponent<LayoutElement>().preferredHeight = 108f;

        HorizontalLayoutGroup layout = entry.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 22f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        // Sprite left empty on purpose: drop one on this Image and the portrait is done. The flat
        // fill is what an unfilled slot looks like, rather than a hole in the row.
        GameObject portrait = Panel("Portrait", entry.transform, PortraitFill);
        LayoutElement portraitSize = portrait.AddComponent<LayoutElement>();
        portraitSize.preferredWidth = 88f;
        portraitSize.preferredHeight = 88f;
        portraitSize.flexibleWidth = 0f;

        GameObject info = NewRect("Info", entry.transform);
        info.AddComponent<LayoutElement>().flexibleWidth = 1f;

        VerticalLayoutGroup infoLayout = info.AddComponent<VerticalLayoutGroup>();
        infoLayout.spacing = 2f;
        infoLayout.childAlignment = TextAnchor.MiddleLeft;
        infoLayout.childControlWidth = true;
        infoLayout.childControlHeight = true;
        infoLayout.childForceExpandHeight = false;

        Text nameLabel = Label("Name", info.transform, name, 32, TextAnchor.LowerLeft);
        nameLabel.color = Bone;
        nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        Text roleLabel = Label("Role", info.transform, role, 22, TextAnchor.UpperLeft);
        roleLabel.color = BoneDim;
        roleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
    }

    private static Scrollbar BuildScrollbar(Transform parent)
    {
        GameObject bar = Panel("Scrollbar", parent, Track);
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = Vector2.one;
        barRect.pivot = new Vector2(1f, 1f);
        barRect.offsetMin = new Vector2(-10f, 0f);
        barRect.offsetMax = Vector2.zero;

        GameObject slidingArea = NewRect("Sliding Area", bar.transform);
        Stretch(slidingArea.GetComponent<RectTransform>());

        GameObject handle = Panel("Handle", slidingArea.transform, Blood);

        Scrollbar scrollbar = bar.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        return scrollbar;
    }

    /// <summary>The framed box both the settings and the credits page sit inside.</summary>
    private static GameObject BuildPanel(Transform parent, string heading, Vector2 size)
    {
        GameObject panel = Panel("Panel", parent, PanelFill);
        Anchor(panel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, size);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(44, 44, 36, 36);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        Text title = Label("Heading", panel.transform, heading, 52, TextAnchor.MiddleCenter);
        title.color = Bone;
        title.fontStyle = FontStyle.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;

        GameObject rule = Panel("Rule", panel.transform, Blood);
        rule.AddComponent<LayoutElement>().preferredHeight = 3f;

        return panel;
    }

    private static void SliderRow(
        Transform parent,
        string label,
        UnityAction<float> onChanged,
        out Slider slider,
        out Text valueLabel)
    {
        GameObject row = NewRect(label + " Row", parent);
        LayoutElement rowSize = row.AddComponent<LayoutElement>();
        rowSize.preferredHeight = 64f;
        rowSize.flexibleHeight = 0f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        Text rowLabel = Label("Label", row.transform, label, 28, TextAnchor.MiddleLeft);
        rowLabel.color = Bone;
        rowLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 250f;

        slider = BuildSlider(row.transform);
        slider.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        UnityEventTools.AddPersistentListener(slider.onValueChanged, onChanged);

        valueLabel = Label("Value", row.transform, "100%", 26, TextAnchor.MiddleRight);
        valueLabel.color = BoneDim;
        valueLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;
    }

    /// <summary>
    /// A slider drawn out of plain rectangles: a thin track, an accent fill and a square handle.
    /// The invisible graphic on the root is what takes the clicks - the track is six pixels tall,
    /// so without it a drag anywhere else in the row missed the slider and the volume never moved.
    /// </summary>
    private static Slider BuildSlider(Transform parent)
    {
        const float handleSize = 16f;
        const float trackHeight = 6f;

        GameObject sliderObject = NewRect("Slider", parent);
        sliderObject.AddComponent<Image>().color = Color.clear;
        Slider slider = sliderObject.AddComponent<Slider>();

        GameObject background = Panel("Background", sliderObject.transform, Track);
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(1f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.sizeDelta = new Vector2(0f, trackHeight);

        // Inset by half a handle at each end, so the handle stays inside the track at the
        // extremes rather than hanging off the ends of the row.
        GameObject fillArea = NewRect("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.anchoredPosition = Vector2.zero;
        fillAreaRect.sizeDelta = new Vector2(-handleSize, trackHeight);

        GameObject fill = Panel("Fill", fillArea.transform, Blood);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        GameObject handleArea = NewRect("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0f, 0.5f);
        handleAreaRect.anchorMax = new Vector2(1f, 0.5f);
        handleAreaRect.anchoredPosition = Vector2.zero;
        handleAreaRect.sizeDelta = new Vector2(-handleSize, handleSize);

        GameObject handle = Panel("Handle", handleArea.transform, Bone);
        handle.GetComponent<RectTransform>().sizeDelta = new Vector2(handleSize, 0f);

        slider.fillRect = fillRect;
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(1f);
        return slider;
    }

    private static void BackButton(Transform parent, MainMenuNavigation nav)
    {
        Button back = MenuButton(parent, "Back", nav.ShowMain);
        LayoutElement size = back.GetComponent<LayoutElement>();
        size.preferredHeight = 66f;
        size.flexibleHeight = 0f;
    }

    private static void Spacer(Transform parent)
    {
        NewRect("Spacer", parent).AddComponent<LayoutElement>().flexibleHeight = 1f;
    }

    /// <summary>
    /// No background of its own: the image behind the label is near-transparent until the pointer
    /// is over it, so the type carries the button and the accent only shows on hover and press.
    /// </summary>
    private static Button MenuButton(Transform parent, string text, UnityAction onClick)
    {
        GameObject buttonObject = NewRect(text + " Button", parent);

        Image background = buttonObject.AddComponent<Image>();
        background.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;

        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(1f, 1f, 1f, 0.04f);
        colors.highlightedColor = new Color(Blood.r, Blood.g, Blood.b, 0.55f);
        colors.pressedColor = new Color(Blood.r, Blood.g, Blood.b, 0.85f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0.04f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.02f);
        button.colors = colors;

        UnityEventTools.AddPersistentListener(button.onClick, onClick);

        LayoutElement size = buttonObject.AddComponent<LayoutElement>();
        size.preferredHeight = 72f;
        size.flexibleHeight = 0f;

        Text label = Label("Label", buttonObject.transform, text, 34, TextAnchor.MiddleCenter);
        label.color = Bone;
        Stretch(label.rectTransform);

        return button;
    }

    private static GameObject NewRect(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static GameObject Panel(string name, Transform parent, Color color)
    {
        GameObject panel = NewRect(name, parent);
        panel.AddComponent<Image>().color = color;
        return panel;
    }

    private static Text Label(string name, Transform parent, string text, int size, TextAnchor alignment)
    {
        GameObject textObject = NewRect(name, parent);
        Text label = textObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.text = text;
        label.alignment = alignment;
        label.color = Bone;
        label.raycastTarget = false;
        // Truncate is the default and it drops a line whole rather than clipping it, so a label
        // one pixel taller than the box the layout hands it renders as nothing at all.
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetReference(Object target, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

}

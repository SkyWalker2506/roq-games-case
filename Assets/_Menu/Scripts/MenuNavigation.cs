using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Shared.Sequencing;

/// <summary>
/// Single navigation component used by every scene in the project.
///
/// It decides what to build from the scene it wakes up in, so the exact same component (and therefore the
/// exact same serialised data) can sit in the menu scene and in all four case scenes:
///   * in <see cref="MenuSceneName"/> it builds the case picker (title, one-line subtitle, four buttons);
///   * anywhere else it builds a single top-left HOME button that returns to the menu.
///
/// No serialised fields on purpose: lesson #4 of this project is that scene-stored field values silently
/// win over C# initialisers, so behaviour is derived at runtime instead of being baked into the scene.
/// The generated UI lives on its own screen-space canvas, exactly like <c>ReplayButton</c>, which keeps its
/// top-right corner; HOME sits in the top-left corner so the two never overlap. Every piece of chrome -
/// picker buttons, HOME and REPLAY - is built from <see cref="UIStyle"/>, so the menu and the in-case HUD
/// are the same design language rather than two different default-grey rectangles.
/// </summary>
[DisallowMultipleComponent]
public sealed class MenuNavigation : MonoBehaviour
{
    /// <summary>Scene name of the case picker. Must be build index 0.</summary>
    public const string MenuSceneName = "MainMenu";

    /// <summary>Name of the root object this component is expected to live on (used by the editor setup).</summary>
    public const string RootName = "MenuNavigation";

    /// <summary>Headline shown at the top of the menu.</summary>
    public const string MenuTitle = "ROQ Games — Game Developer Case";

    /// <summary>One line under the headline explaining how the cases are played.</summary>
    public const string MenuSubtitle = "Each case is one sequence — use the input shown.";

    /// <summary>Scene names of the four cases, in menu order.</summary>
    public static readonly string[] CaseScenes =
    {
        "FitTheShape",
        "BlockHole",
        "Stickerdom",
        "Buca",
    };

    /// <summary>Button captions, index-matched to <see cref="CaseScenes"/>.</summary>
    public static readonly string[] CaseLabels =
    {
        "Case 1 — Fit the Shape",
        "Case 2 — Block Hole",
        "Case 3 — Stickerdom",
        "Case 4 — Buca",
    };

    /// <summary>How each case is played, index-matched to <see cref="CaseScenes"/>. Shown on the button.</summary>
    public static readonly string[] CaseHints =
    {
        "tap",
        "drag & drop",
        "tap",
        "pull & release",
    };

    readonly List<Button> _caseButtons = new List<Button>(4);
    Button _homeButton;

    /// <summary>The four case buttons, in menu order. Empty outside the menu scene.</summary>
    public IList<Button> CaseButtons { get { return _caseButtons; } }

    /// <summary>The HOME button. Null in the menu scene.</summary>
    public Button HomeButton { get { return _homeButton; } }

    /// <summary>True when this instance is running inside the menu scene.</summary>
    public bool IsMenuScene { get { return SceneManager.GetActiveScene().name == MenuSceneName; } }

    void Start()
    {
        EnsureEventSystem();

        if (IsMenuScene) BuildMenu();
        else BuildHome();
    }

    // ------------------------------------------------------------------ navigation

    /// <summary>Loads the case scene at <paramref name="index"/> (0..3).</summary>
    public void LoadCase(int index)
    {
        if (index < 0 || index >= CaseScenes.Length)
        {
            Debug.LogError("[MenuNavigation] case index out of range: " + index);
            return;
        }

        string scene = CaseScenes[index];
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogError("[MenuNavigation] NAV_MISSING_SCENE " + scene + " is not in Build Settings.");
            return;
        }

        Debug.Log("[MenuNavigation] NAV_LOAD case=" + index + " scene=" + scene);
        SceneManager.LoadScene(scene);
    }

    /// <summary>Returns to the menu scene.</summary>
    public void GoHome()
    {
        if (!Application.CanStreamedLevelBeLoaded(MenuSceneName))
        {
            Debug.LogError("[MenuNavigation] NAV_MISSING_SCENE " + MenuSceneName + " is not in Build Settings.");
            return;
        }

        Debug.Log("[MenuNavigation] NAV_HOME from=" + SceneManager.GetActiveScene().name);
        SceneManager.LoadScene(MenuSceneName);
    }

    // ------------------------------------------------------------------ menu ui

    void BuildMenu()
    {
        Canvas canvas = BuildCanvas("MenuCanvas", 500);

        // Full-screen backdrop: a vertical gradient rather than one flat dark rectangle, so the picker has
        // some depth without needing a single imported texture.
        GameObject bgGo = new GameObject("Background", typeof(Image));
        bgGo.transform.SetParent(canvas.transform, false);
        UIStyle.Stretch(bgGo.GetComponent<RectTransform>());
        Image bgImage = bgGo.GetComponent<Image>();
        bgImage.sprite = UIStyle.Backdrop;
        bgImage.type = Image.Type.Simple;
        bgImage.color = Color.white;
        bgImage.raycastTarget = false;

        BuildHeader(canvas.transform);

        // Four case buttons, stacked and centred with an even vertical rhythm.
        const float buttonWidth = 840f;
        const float buttonHeight = 156f;
        const float gap = 38f;
        const float blockCentreY = -150f;                // below centre, so the header and the bottom margin balance
        float block = CaseScenes.Length * buttonHeight + (CaseScenes.Length - 1) * gap;
        float top = blockCentreY + block * 0.5f;

        _caseButtons.Clear();
        for (int i = 0; i < CaseScenes.Length; i++)
        {
            float centreY = top - buttonHeight * 0.5f - i * (buttonHeight + gap);
            Button b = BuildCaseButton(canvas.transform, i, new Vector2(0f, centreY), new Vector2(buttonWidth, buttonHeight));

            int index = i;                       // captured per iteration, not by reference
            b.onClick.AddListener(() => LoadCase(index));
            _caseButtons.Add(b);

            Debug.Log("[MenuNavigation] menu entry " + i + " -> " + CaseScenes[i] +
                      " loadable=" + Application.CanStreamedLevelBeLoaded(CaseScenes[i]));
        }

        Debug.Log("[MenuNavigation] MENU_READY buttons=" + _caseButtons.Count);
    }

    /// <summary>Title, a short accent rule and the one-line subtitle, anchored to the top of the canvas.</summary>
    static void BuildHeader(Transform parent)
    {
        GameObject headerGo = new GameObject("Header", typeof(RectTransform));
        headerGo.transform.SetParent(parent, false);
        RectTransform header = headerGo.GetComponent<RectTransform>();
        header.anchorMin = new Vector2(0.5f, 1f);
        header.anchorMax = new Vector2(0.5f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -180f);
        header.sizeDelta = new Vector2(1000f, 260f);

        // Sizes are ~0.9x the uGUI numbers they replace: TMP renders roughly ten percent wider at the
        // same point size, and at the old 54 the headline ran into both screen edges. Same hierarchy,
        // same footprint on screen - only the rasteriser changed.
        Row(header, "Title", MenuTitle, 0f, 96f, 48f, FontStyles.Bold, UIStyle.TextPrimary);

        // Short accent rule: the cheapest way to separate the headline from the subtitle.
        GameObject ruleGo = new GameObject("Rule", typeof(Image));
        ruleGo.transform.SetParent(header, false);
        RectTransform rule = ruleGo.GetComponent<RectTransform>();
        rule.anchorMin = new Vector2(0.5f, 1f);
        rule.anchorMax = new Vector2(0.5f, 1f);
        rule.pivot = new Vector2(0.5f, 1f);
        rule.anchoredPosition = new Vector2(0f, -112f);
        rule.sizeDelta = new Vector2(180f, 6f);
        Image ruleImage = ruleGo.GetComponent<Image>();
        ruleImage.sprite = UIStyle.RoundedFill;
        ruleImage.type = Image.Type.Sliced;
        ruleImage.pixelsPerUnitMultiplier = 6f;
        ruleImage.color = UIStyle.Accent;
        ruleImage.raycastTarget = false;

        Row(header, "Subtitle", MenuSubtitle, -142f, 56f, 32f, FontStyles.Normal, UIStyle.TextMuted);
    }

    /// <summary>One centred line of header text at <paramref name="offsetY"/> below the header's top edge.</summary>
    static void Row(RectTransform parent, string name, string content, float offsetY, float height,
                    float fontSize, FontStyles style, Color color)
    {
        GameObject go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(0f, offsetY);
        rect.sizeDelta = new Vector2(0f, height);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = UIStyle.FontAsset;
        if (font != null) text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.margin = Vector4.zero;
        // Never wrap: the header is a headline plus ONE line of subtitle, and a wrapped subtitle is the
        // difference between a designed header and a paragraph that happened to land there.
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    /// <summary>One picker row: accent bar, case name on the left, the input it wants on the right.</summary>
    static Button BuildCaseButton(Transform parent, int index, Vector2 anchoredPosition, Vector2 size)
    {
        Button btn = UIStyle.Chrome(parent, "CaseButton_" + (index + 1), new Vector2(0.5f, 0.5f),
                                    anchoredPosition, size, UIStyle.RadiusLarge,
                                    UIStyle.ButtonNormal, UIStyle.ButtonHover, UIStyle.ButtonPressed);

        // Accent bar hugging the left edge: gives the row a reading direction and a spot of colour.
        GameObject barGo = new GameObject("AccentBar", typeof(Image));
        barGo.transform.SetParent(btn.transform, false);
        RectTransform bar = barGo.GetComponent<RectTransform>();
        bar.anchorMin = new Vector2(0f, 0.5f);
        bar.anchorMax = new Vector2(0f, 0.5f);
        bar.pivot = new Vector2(0f, 0.5f);
        bar.anchoredPosition = new Vector2(26f, 0f);
        bar.sizeDelta = new Vector2(8f, 76f);
        Image barImage = barGo.GetComponent<Image>();
        barImage.sprite = UIStyle.RoundedFill;
        barImage.type = Image.Type.Sliced;
        barImage.pixelsPerUnitMultiplier = 5f;
        barImage.color = UIStyle.Accent;
        barImage.raycastTarget = false;

        UIStyle.Label(btn.transform, "Label", CaseLabels[index], 40f, FontStyles.Bold,
                      UIStyle.TextPrimary, TextAlignmentOptions.Left, 62f);
        UIStyle.Label(btn.transform, "Hint", CaseHints[index], 30f, FontStyles.Normal,
                      UIStyle.TextMuted, TextAlignmentOptions.Right, 40f);

        return btn;
    }

    // ------------------------------------------------------------------ home ui

    void BuildHome()
    {
        // Above ReplayButton's canvas (sortingOrder 1000) so nothing a case scene adds can cover it.
        Canvas canvas = BuildCanvas("HomeCanvas", 1001);

        // Top-LEFT, mirroring ReplayButton's top-right corner: same builder, so same size, same margin,
        // same rounded shape, outline and hover/pressed states.
        _homeButton = ReplayButton.BuildCornerButton(canvas.transform, "HomeButton", "HOME",
            new Vector2(0f, 1f),
            new Vector2(ReplayButton.EdgeMargin, -ReplayButton.EdgeMargin));
        _homeButton.onClick.AddListener(GoHome);

        Debug.Log("[MenuNavigation] HOME_READY scene=" + SceneManager.GetActiveScene().name +
                  " corner=top-left size=" + ReplayButton.ButtonSize.x + "x" +
                  ReplayButton.ButtonSize.y +
                  " margin=" + ReplayButton.EdgeMargin +
                  " menuLoadable=" + Application.CanStreamedLevelBeLoaded(MenuSceneName));
    }

    // ------------------------------------------------------------------ ui helpers

    Canvas BuildCanvas(string name, int sortingOrder)
    {
        GameObject canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1728f);   // same framing as ReplayButton
        scaler.matchWidthOrHeight = 1f;

        return canvas;
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }
}

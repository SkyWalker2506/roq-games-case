using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Shared.Sequencing
{
    /// <summary>
    /// The one place the project's runtime UI look is defined: palette, procedurally generated rounded-rect
    /// sprites and the button/label builders. It lives in Shared.Runtime (rather than next to the menu)
    /// because the menu assembly can reference this one but not the other way round, and HOME / REPLAY /
    /// the case picker have to come out of the same mould or the whole thing reads as programmer UI.
    ///
    /// Everything is generated in code: the project ships no UI sprite atlas, and a signed-distance rounded
    /// rect stays crisp at any button size once it is 9-sliced.
    /// </summary>
    public static class UIStyle
    {
        // ---- palette ----------------------------------------------------
        public static readonly Color BackdropTop = new Color(0.145f, 0.169f, 0.286f, 1f);
        public static readonly Color BackdropBottom = new Color(0.043f, 0.055f, 0.098f, 1f);

        public static readonly Color ButtonNormal = new Color(0.161f, 0.196f, 0.322f, 1f);
        public static readonly Color ButtonHover = new Color(0.243f, 0.298f, 0.463f, 1f);
        public static readonly Color ButtonPressed = new Color(0.341f, 0.427f, 0.655f, 1f);

        public static readonly Color ChipNormal = new Color(0.129f, 0.153f, 0.251f, 0.88f);
        public static readonly Color ChipHover = new Color(0.216f, 0.259f, 0.400f, 0.96f);
        public static readonly Color ChipPressed = new Color(0.341f, 0.427f, 0.655f, 1f);

        public static readonly Color Accent = new Color(0.443f, 0.678f, 1f, 1f);
        public static readonly Color Outline = new Color(0.560f, 0.660f, 1f, 0.34f);
        public static readonly Color TextPrimary = new Color(0.953f, 0.969f, 1f, 1f);
        public static readonly Color TextMuted = new Color(0.714f, 0.776f, 0.902f, 1f);

        /// <summary>Corner-radius multiplier for a large panel-sized button.</summary>
        public const float RadiusLarge = 0.70f;
        /// <summary>Corner-radius multiplier for a small chrome button (HOME / REPLAY).</summary>
        public const float RadiusSmall = 1.55f;

        // ---- generated sprites ------------------------------------------
        const int TexSize = 64;
        const float CornerRadius = 18f;
        const float OutlineWidth = 3f;

        static Sprite _fill;
        static Sprite _outline;
        static Sprite _backdrop;

        /// <summary>Solid rounded rectangle, 9-sliced so the corner radius never stretches.</summary>
        public static Sprite RoundedFill
        {
            get { if (_fill == null) _fill = BuildRounded(false); return _fill; }
        }

        /// <summary>Rounded-rectangle outline only, same geometry as <see cref="RoundedFill"/>.</summary>
        public static Sprite RoundedOutline
        {
            get { if (_outline == null) _outline = BuildRounded(true); return _outline; }
        }

        /// <summary>Vertical backdrop gradient, dark at the bottom and a shade lighter at the top.</summary>
        public static Sprite Backdrop
        {
            get { if (_backdrop == null) _backdrop = BuildBackdrop(); return _backdrop; }
        }

        static TMP_FontAsset _fontAsset;
        static bool _fontLogged;

        /// <summary>
        /// The signed-distance-field font every piece of chrome writes with. The built-in dynamic font
        /// rasterises at whatever size the atlas happens to hold and goes soft the moment the CanvasScaler
        /// scales it; the SDF asset stays crisp at any scale, which is the whole point of moving the text
        /// stack over. Shared.Runtime now references the TMP assembly, so HOME / REPLAY and the menu can
        /// share this one font instead of splitting into two text stacks.
        /// </summary>
        public static TMP_FontAsset FontAsset
        {
            get
            {
                if (_fontAsset != null) return _fontAsset;

                _fontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (_fontAsset == null) _fontAsset = TMP_Settings.defaultFontAsset;

                if (!_fontLogged)
                {
                    _fontLogged = true;
                    if (_fontAsset == null)
                        Debug.LogError("[UIStyle] TMP_FONT_MISSING no TMP font asset found - import TMP essentials.");
                    else
                        Debug.Log("[UIStyle] TMP_FONT " + _fontAsset.name);
                }

                return _fontAsset;
            }
        }

        /// <summary>
        /// Drops the generated sprites and the one-shot font log latch. With Enter Play Mode Options
        /// disabling the domain reload these statics would otherwise survive between plays: the sprites
        /// would keep leaking a HideAndDontSave Texture2D per Play, and the latch would swallow the
        /// TMP_FONT / TMP_FONT_MISSING line so a missing-font regression could never be seen again.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DestroySprite(ref _fill);
            DestroySprite(ref _outline);
            DestroySprite(ref _backdrop);
            _fontAsset = null;
            _fontLogged = false;
        }

        static void DestroySprite(ref Sprite sprite)
        {
            if (sprite == null) { sprite = null; return; }
            if (sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
            sprite = null;
        }

        static Sprite BuildRounded(bool ringOnly)
        {
            Texture2D tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.name = ringOnly ? "UIStyle_RoundedOutline" : "UIStyle_RoundedFill";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;

            float half = TexSize * 0.5f;
            float inner = half - CornerRadius;
            Color32[] px = new Color32[TexSize * TexSize];

            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - CornerRadius;   // < 0 inside the shape

                    float a = ringOnly
                        ? Mathf.Clamp01(Mathf.Min(-d, d + OutlineWidth) + 0.5f)
                        : Mathf.Clamp01(0.5f - d);

                    px[y * TexSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            float b = CornerRadius + 2f;
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, TexSize, TexSize), new Vector2(0.5f, 0.5f),
                                     100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = tex.name;
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        static Sprite BuildBackdrop()
        {
            const int h = 256;
            Texture2D tex = new Texture2D(4, h, TextureFormat.RGBA32, false);
            tex.name = "UIStyle_Backdrop";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;

            Color32[] px = new Color32[4 * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);                       // 0 = bottom of the screen
                Color c = Color.Lerp(BackdropBottom, BackdropTop, Mathf.SmoothStep(0f, 1f, Mathf.Pow(t, 0.85f)));
                Color32 c32 = c;
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c32;
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, 4f, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = tex.name;
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        // ---- builders ----------------------------------------------------

        /// <summary>Stretches a RectTransform over its whole parent.</summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// A rounded, outlined button with real hover / pressed states. The Image is left white and the
        /// state colours live in the ColorBlock, so highlighted and pressed are actually visible instead of
        /// being three shades of the same tint.
        /// </summary>
        public static Button Chrome(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition,
                                    Vector2 size, float radiusScale, Color normal, Color hover, Color pressed)
        {
            GameObject go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image bg = go.GetComponent<Image>();
            bg.sprite = RoundedFill;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = radiusScale;
            bg.color = Color.white;                 // the tint comes from the ColorBlock below

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = btn.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.selectedColor = hover;
            colors.pressedColor = pressed;
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.4f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            GameObject edgeGo = new GameObject("Outline", typeof(Image));
            edgeGo.transform.SetParent(go.transform, false);
            Stretch(edgeGo.GetComponent<RectTransform>());
            Image edge = edgeGo.GetComponent<Image>();
            edge.sprite = RoundedOutline;
            edge.type = Image.Type.Sliced;
            edge.pixelsPerUnitMultiplier = radiusScale;
            edge.color = Outline;
            edge.raycastTarget = false;

            return btn;
        }

        /// <summary>
        /// One TextMesh Pro label inside <paramref name="parent"/>, inset by <paramref name="padding"/> on
        /// each side. Auto-sizing is off on purpose: every caller passes the size the design asks for, and
        /// an auto-sized label would quietly renegotiate the type hierarchy behind the design's back.
        /// </summary>
        public static TextMeshProUGUI Label(Transform parent, string name, string content, float fontSize,
                                            FontStyles style, Color color, TextAlignmentOptions align, float padding)
        {
            GameObject go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, 0f);
            rect.offsetMax = new Vector2(-padding, 0f);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = FontAsset;
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = align;
            text.enableAutoSizing = false;
            text.margin = Vector4.zero;
            // A one-line caption must never wrap or be clipped: wrapping turns a button label into a
            // paragraph, clipping turns it into a lie about what the button does.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }

    /// <summary>
    /// Small top-right REPLAY button. It does NOT re-run the sequence: it reloads the active scene, so the
    /// pieces, the shifted deck and the peeled sticker are genuinely back to their starting state and the
    /// scene waits for the player's first input again (nothing auto-plays).
    /// Drop this component on any object in a case scene; it builds its own screen-space Canvas at runtime,
    /// so one component per scene is enough. HOME (built by MenuNavigation) sits in the top-left corner,
    /// this one in the top-right, so the two never overlap and neither covers the play area. Both are built
    /// out of <see cref="UIStyle"/>, so the two corners are visually the same button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReplayButton : MonoBehaviour
    {
        /// <summary>Button footprint in canvas reference units (reference resolution 1080x1728).</summary>
        public static readonly Vector2 ButtonSize = new Vector2(110f, 54f);

        /// <summary>Distance from the screen edges, same value for HOME so the two line up.</summary>
        public const float EdgeMargin = 24f;

        /// <summary>Label font size that fits <see cref="ButtonSize"/>.</summary>
        public const float LabelFontSize = 22f;

        [Tooltip("Existing uGUI button to hook up. Left empty, a Canvas and Button are created at runtime.")]
        [SerializeField] Button button;

        [Tooltip("Label shown on the generated button.")]
        [SerializeField] string label = "REPLAY";

        /// <summary>The generated (or assigned) button, so a test can click it without a real pointer.</summary>
        public Button Button { get { return button; } }

        void Start()
        {
            if (button == null) button = BuildRuntimeButton();
            if (button != null) button.onClick.AddListener(OnClick);
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnClick);
        }

        /// <summary>Reloads the active scene: a hard reset, not a replay of the animation.</summary>
        public void OnClick()
        {
            Scene active = SceneManager.GetActiveScene();
            Debug.Log("[ReplayButton] REPLAY_RELOAD scene=" + active.name + " buildIndex=" + active.buildIndex);
            SceneManager.LoadScene(active.buildIndex);
        }

        /// <summary>
        /// The shared HUD corner button: same rounded shape, outline, palette and hover/pressed behaviour
        /// for REPLAY (top-right) and HOME (top-left). Position and footprint are the caller's business.
        /// </summary>
        public static Button BuildCornerButton(Transform parent, string name, string label,
                                               Vector2 anchor, Vector2 anchoredPosition)
        {
            Button btn = UIStyle.Chrome(parent, name, anchor, anchoredPosition, ButtonSize, UIStyle.RadiusSmall,
                                        UIStyle.ChipNormal, UIStyle.ChipHover, UIStyle.ChipPressed);
            UIStyle.Label(btn.transform, "Label", label, LabelFontSize, FontStyles.Bold,
                          UIStyle.TextPrimary, TextAlignmentOptions.Center, 6f);
            return btn;
        }

        Button BuildRuntimeButton()
        {
            GameObject canvasGo = new GameObject("ReplayCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // stays on top of anything a case scene adds

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1728f); // matches the reference video framing
            scaler.matchWidthOrHeight = 1f;

            EnsureEventSystem();

            Button btn = BuildCornerButton(canvasGo.transform, "ReplayButton", label,
                                           new Vector2(1f, 1f),                       // top-right corner
                                           new Vector2(-EdgeMargin, -EdgeMargin));

            Debug.Log("[ReplayButton] REPLAY_UI corner=top-right size=" + ButtonSize.x + "x" + ButtonSize.y +
                      " margin=" + EdgeMargin);
            return btn;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }
    }
}

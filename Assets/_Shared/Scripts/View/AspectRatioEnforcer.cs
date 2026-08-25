using UnityEngine;
using UnityEngine.Rendering;

namespace Shared.View
{
    /// <summary>
    /// Locks a camera's viewport to the reference aspect (1080x1728 = 0.625) whatever the real screen
    /// is, and paints the leftover strips with a neutral dark colour.
    ///
    /// Why: the reference videos are a narrow 0.625 mobile frame. Without this the scene simply stretched
    /// to whatever aspect the Game View or the device happened to be, so at a 0.811 window every case
    /// rendered ~30% wider than intended: the interaction area shrank in the frame and the composition
    /// read as empty. The vertical field of view is what Unity keeps constant, so narrowing the viewport
    /// restores exactly the framing the reference has.
    ///
    /// The frame-strip capture already renders into a 540x864 (0.625) render texture, so this component
    /// deliberately stands down for off-screen renders and in batchmode: cropping those a second time
    /// would letterbox the capture instead of matching it.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Shared/Aspect Ratio Enforcer")]
    public sealed class AspectRatioEnforcer : MonoBehaviour
    {
        public const float ReferenceWidth = 1080f;
        public const float ReferenceHeight = 1728f;

        /// <summary>0.625 - the reference videos' width / height.</summary>
        public const float TargetAspect = ReferenceWidth / ReferenceHeight;

        /// <summary>Below this the viewport is treated as already matching and left untouched.</summary>
        const float Epsilon = 0.0005f;

        [Tooltip("Colour of the letterbox / pillarbox bars.")]
        public Color barColor = new Color(0.043f, 0.043f, 0.055f, 1f);

        Camera _cam;
        Camera _bars;

        // ------------------------------------------------------------------ pure viewport maths

        /// <summary>
        /// The normalised viewport rect that turns a <paramref name="width"/> x <paramref name="height"/>
        /// surface into a centred <see cref="TargetAspect"/> frame. Static and side effect free so a gate
        /// can check it for screen sizes no test machine actually has.
        /// </summary>
        public static Rect ComputeRect(int width, int height)
        {
            if (width <= 0 || height <= 0) return new Rect(0f, 0f, 1f, 1f);

            float screenAspect = (float)width / height;
            if (screenAspect > TargetAspect)
            {
                // Too wide: pillarbox, keep full height.
                float w = TargetAspect / screenAspect;
                if (w > 1f - Epsilon) return new Rect(0f, 0f, 1f, 1f);
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }

            // Too tall: letterbox, keep full width.
            float h = screenAspect / TargetAspect;
            if (h > 1f - Epsilon) return new Rect(0f, 0f, 1f, 1f);
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        /// <summary>The aspect the camera actually renders at once <see cref="ComputeRect"/> is applied.</summary>
        public static float ResultingAspect(int width, int height)
        {
            Rect r = ComputeRect(width, height);
            return (width * r.width) / (height * r.height);
        }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Apply();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (_cam != null) _cam.rect = new Rect(0f, 0f, 1f, 1f);
            DestroyBars();
        }

        void LateUpdate() { Apply(); }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _cam) Apply();
        }

        // ------------------------------------------------------------------ application

        public void Apply()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;

            // Stand down for the capture harness (off-screen render targets are authored at 0.625
            // already) and for batchmode, where "Screen" is a headless stub that means nothing.
            if (_cam.targetTexture != null || Application.isBatchMode)
            {
                Release();
                return;
            }

            Rect rect = ComputeRect(Screen.width, Screen.height);
            if (rect.width >= 1f && rect.height >= 1f)
            {
                Release();
                return;
            }

            if (_cam.rect != rect) _cam.rect = rect;
            EnsureBars();
        }

        void Release()
        {
            if (_cam.rect != new Rect(0f, 0f, 1f, 1f)) _cam.rect = new Rect(0f, 0f, 1f, 1f);
            DestroyBars();
        }

        /// <summary>
        /// A depth-sorted-behind camera that clears the whole surface to <see cref="barColor"/> and draws
        /// nothing, so the strips outside the letterboxed viewport are a defined neutral colour rather
        /// than whatever was left in the back buffer.
        ///
        /// Created only in play mode and marked HideAndDontSave: the editor scene setup scripts look the
        /// scene's camera up with FindFirstObjectByType, and a second serialised camera would poison them.
        /// </summary>
        void EnsureBars()
        {
            if (!Application.isPlaying) return;
            if (_bars != null)
            {
                _bars.backgroundColor = barColor;
                _bars.depth = _cam.depth - 100f;
                return;
            }

            GameObject go = new GameObject("AspectLetterboxBackdrop");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false);

            _bars = go.AddComponent<Camera>();
            _bars.clearFlags = CameraClearFlags.SolidColor;
            _bars.backgroundColor = barColor;
            _bars.cullingMask = 0;
            _bars.depth = _cam.depth - 100f;
            _bars.rect = new Rect(0f, 0f, 1f, 1f);
            _bars.orthographic = true;
            _bars.orthographicSize = 1f;
            _bars.nearClipPlane = 0.01f;
            _bars.farClipPlane = 1f;
            _bars.useOcclusionCulling = false;
            _bars.allowHDR = false;
            _bars.allowMSAA = false;
        }

        void DestroyBars()
        {
            if (_bars == null) return;
            GameObject go = _bars.gameObject;
            _bars = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}

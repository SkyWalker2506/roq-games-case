using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Case3.EditorTools
{
    /// <summary>
    /// Gives every PageObj_* a die-cut white rim and a drop shadow, the way the reference's
    /// page items are printed.
    ///
    /// WHY THIS EXISTS. Measured on the reference's idle frame against ours: thin white band
    /// pixels inside the page were 54721 (reference) against 19927 (ours), and the pixels the
    /// metric selects show why - in the reference every item, bright or dimmed, is cut out with
    /// a white border, while on our page only the three playable stickers carry one. The border
    /// is not in our art either: all 14 obj_*.png have exactly 0 near-white pixels inside their
    /// opaque area, so it has to be generated at render time.
    ///
    /// WHAT IT DOES NOT DO. It does not brighten anything. Owner requirement from commit
    /// 2515a89 - "3 tane resim renkli olsun digerleri karanlik olsun" - keeps the page objects
    /// dark, and the reference agrees: its dimmed items keep their white rim while staying dark
    /// (sampled on the reference's RAMEN can, rim RGB 254/253/253 over art whose darkest
    /// decile is 66/33/21). Rim and object luminance are separate reads, and only the rim moves.
    ///
    /// SORTING. Each PageObj_* needs two orders below itself and the authored orders are packed
    /// (100..121 with only eight gaps), so the band is renumbered rank-preserving to 100 + 4*rank.
    /// Relative draw order between page objects is therefore unchanged by construction; the
    /// numbers move, the ordering does not. Nothing else in the scene uses 51..199.
    /// </summary>
    public static class Case3PageRim
    {
        const string RimShader = "Case3/PageObjectRim";
        const string RimMatPath = "Assets/Case3_Stickerdom/Materials/Case3_PageObjectRim.mat";
        const string DropMatPath = "Assets/Case3_Stickerdom/Materials/Case3_PageObjectDrop.mat";

        const string RimChild = "Rim";
        const string DropChild = "Drop";

        // Reference rims measure 8-14 render-target px on a 1080-wide frame (white-run lengths
        // across four scanlines through its page). 10 is the middle of that band. At the page
        // objects' scale of 1.4 this is ~7.1 texels, inside the >=9 px padding every obj_*.png has.
        const float RimPixels = 10f;

        // The scene's existing per-sticker shadow convention, reused verbatim so page objects and
        // stickers cast the same shadow: Shadow_* are tinted (0.22,0.14,0.08,0.42) and offset
        // (0.10,-0.14) in WORLD units.
        static readonly Color DropColor = new Color(0.22f, 0.14f, 0.08f, 0.42f);
        static readonly Vector2 DropWorldOffset = new Vector2(0.10f, -0.14f);

        const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";

        /// <summary>
        /// Zero-argument entry point, so this is reproducible on a clean clone with
        ///   tools/unity-run.sh -batchmode -nographics -quit \
        ///     -executeMethod Case3.EditorTools.Case3PageRim.ApplyToStickerdom
        /// It is deliberately NOT called from Case3SceneSetup.Build: that method has asserted
        /// "exactly three authored stickers" since commit 2515a89 left five Sticker_* in the
        /// scene, so it throws before reaching any of its own work. Hanging this pass off it
        /// would have made the rims unrunnable.
        /// </summary>
        public static void ApplyToStickerdom()
        {
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Apply(scene);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Idempotent. Safe to run on an already-rimmed scene; it rewrites, never stacks.</summary>
        public static void Apply(Scene scene)
        {
            Material rimMat = EnsureMaterial(RimMatPath, Color.white);
            Material dropMat = EnsureMaterial(DropMatPath, DropColor);
            if (rimMat == null || dropMat == null)
            {
                Debug.LogError("[Case3PageRim] RIM_FAILED could not create the rim materials");
                return;
            }

            List<SpriteRenderer> objects = new List<SpriteRenderer>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) CollectPageObjects(roots[i].transform, objects);

            if (objects.Count == 0)
            {
                Debug.LogError("[Case3PageRim] RIM_FAILED no PageObj_* found in " + scene.name);
                return;
            }

            // Rank-preserving renumber, so the rim and drop each get their own order and no two
            // renderers in the scene end up sharing one (Case3StripPass reports a clash if they do).
            objects.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            StringBuilder sb = new StringBuilder();
            for (int rank = 0; rank < objects.Count; rank++)
            {
                SpriteRenderer obj = objects[rank];
                int order = 100 + 4 * rank;
                obj.sortingOrder = order;
                EditorUtility.SetDirty(obj);

                Vector3 scale = obj.transform.lossyScale;
                Vector3 dropLocal = new Vector3(
                    Mathf.Approximately(scale.x, 0f) ? 0f : DropWorldOffset.x / scale.x,
                    Mathf.Approximately(scale.y, 0f) ? 0f : DropWorldOffset.y / scale.y,
                    0f);

                EnsureLayer(obj, RimChild, rimMat, order - 1, Vector3.zero, Color.white);
                EnsureLayer(obj, DropChild, dropMat, order - 2, dropLocal, DropColor);

                sb.Append(obj.name).Append("=").Append(order).Append(' ');
            }

            Debug.Log(string.Format(
                "[Case3PageRim] RIM_OK {0} page objects rimmed, rim={1:0.0} px white, drop={2} at world {3}; orders: {4}",
                objects.Count, RimPixels, DropColor, DropWorldOffset, sb.ToString().Trim()));
        }

        static void CollectPageObjects(Transform t, List<SpriteRenderer> into)
        {
            if (t.name.StartsWith("PageObj_", StringComparison.Ordinal))
            {
                SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) into.Add(sr);
            }
            for (int i = 0; i < t.childCount; i++) CollectPageObjects(t.GetChild(i), into);
        }

        static void EnsureLayer(SpriteRenderer owner, string childName, Material material,
                                int sortingOrder, Vector3 localPosition, Color color)
        {
            Transform existing = owner.transform.Find(childName);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(childName);
                go.transform.SetParent(owner.transform, false);
            }

            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = owner.sprite;
            sr.flipX = owner.flipX;
            sr.flipY = owner.flipY;
            sr.sharedMaterial = material;
            sr.color = color;
            sr.sortingLayerID = owner.sortingLayerID;
            sr.sortingOrder = sortingOrder;
            EditorUtility.SetDirty(sr);
            EditorUtility.SetDirty(go);
        }

        /// <summary>
        /// Code is the single owner of these two materials' values. They are rewritten on every
        /// run rather than only on creation, because a serialised .mat silently outlives any
        /// change to the numbers above and the next measurement would start from a stale value.
        /// </summary>
        static Material EnsureMaterial(string path, Color color)
        {
            Shader shader = Shader.Find(RimShader);
            if (shader == null)
            {
                Debug.LogError("[Case3PageRim] RIM_FAILED shader not found: " + RimShader);
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_Color", color);
            mat.SetFloat("_RimPixels", RimPixels);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}

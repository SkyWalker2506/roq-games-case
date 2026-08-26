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

        /// <summary>
        /// Removes any authored contact-shadow child, however many previous runs left behind and
        /// whichever of the two names they ended up under.
        /// </summary>
        static void PurgeShadowChildren(Component obj)
        {
            if (obj == null) return;
            for (int i = obj.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = obj.transform.GetChild(i);
                if (c.name != DropChild &&
                    !c.name.StartsWith(StickerPeel.PaperShadowPrefix, System.StringComparison.Ordinal))
                    continue;
                Undo.DestroyObjectImmediate(c.gameObject);
            }
        }

        // MEASURED, not chosen. tools/case3_rim_metrics.py casts rays off the f=0.5 level set of
        // the sticker/paper boundary and reads the white band's width along the normal. On the
        // reference frame the three bright stickers pool at W = 8.50 render px on a 1080-wide
        // frame (grey cat 7.50, spaghetti 8.50, candy cane 8.50). The instrument reads 0.5 px low
        // on its own positive control - an authored 10.00 px hard rim comes back as 9.50 - so
        // authoring 9.0 here is what lands on the reference's 8.5.
        const float RimPixels = 9f;

        // The reference's rim edge measures 2.00 px from opaque to bare paper (median over those
        // three stickers), against 1.25 px measured on a synthetic 1.0 px edge. So the reference's
        // own edge is about 1.5 px of real antialiasing, and that is what is authored.
        const float RimEdgeAA = 1.5f;

        // The drop is the same piece of paper, so it keeps the same WIDTH; only its edge is soft.
        // A hard-edged dark ring would read as a second outline rather than as a shadow.
        const float DropEdgeAA = 6f;

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

        /// <summary>
        /// Rim, then re-wire. Both, in that order, in one batchmode run.
        ///
        /// The order matters and is not cosmetic: Case3PageEntries.MakeEntry hands each item's
        /// "Rim" child to its StickerPeel as a COMPANION, so the die cut travels with the sticker
        /// when it peels off the page instead of staying behind as a white silhouette of something
        /// that has already flown away. A Rim created after that wiring would be an orphan.
        /// </summary>
        public static void ApplyAndWire()
        {
            ApplyToStickerdom();
            Case3PageEntries.Build();
            // Both halves are synchronous, so nothing is left to drive: exit rather than sit in
            // batchmode holding tools/unity-run.sh's lock against the next run.
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Idempotent. Safe to run on an already-rimmed scene; it rewrites, never stacks.</summary>
        public static void Apply(Scene scene)
        {
            Material rimMat = EnsureMaterial(RimMatPath, Color.white, RimEdgeAA);
            Material dropMat = EnsureMaterial(DropMatPath, DropColor, DropEdgeAA);
            if (rimMat == null || dropMat == null)
            {
                Debug.LogError("[Case3PageRim] RIM_FAILED could not create the rim materials");
                return;
            }

            List<SpriteRenderer> objects = new List<SpriteRenderer>();
            List<SpriteRenderer> stickers = new List<SpriteRenderer>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Collect(roots[i].transform, "PageObj_", objects);
                Collect(roots[i].transform, "Sticker_", stickers);
            }

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
                // NO drop-shadow layer. It used to be authored here as a "Drop" child, and
                // Case3PageEntries then RENAMED it to "Shadow_<name>" so StickerPeel could find it by
                // prefix. Which meant the next run of this pass looked for "Drop", did not find it,
                // and made another one. Six setup cycles, six shadow sprites per sheet, 59 in the
                // scene - and StickerPeel only ever hid the first. The shading is the curl shader's
                // job now, so there is nothing here to leave behind.
                PurgeShadowChildren(obj);

                sb.Append(obj.name).Append("=").Append(order).Append(' ');
            }

            // ---- the five strip sheets, on the same rim, for the same reason.
            //
            // Their border used to be PAINTED INTO the PNG by the art generator, as
            // threshold(gaussian_blur(alpha)) - which is not an outset of the silhouette but a
            // function of local shape, so it rounded corners off and dropped thin features
            // entirely (the chopsticks and the steam on sticker_noodle_blue had no border at
            // all while the bowl beside them had a fat one). tools/case3_strip_die_cut.py takes
            // that baked ring back off - recovering the art's own alpha out of the generator, so
            // it is exact rather than reconstructed - and the rim is drawn here instead, by the
            // same shader, at the same measured width as every other sticker on the page.
            //
            // These have their own Shadow_* child already, authored with the strip, so only a
            // Rim is added. Their orders are packed (501..506), so the band is renumbered
            // rank-preserving to 500 + 4*rank, exactly as the page objects' band is: the numbers
            // move, the relative draw order does not, and the coverage rule - which only ever
            // compares one order with another - cannot see the difference.
            stickers.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
            for (int rank = 0; rank < stickers.Count; rank++)
            {
                SpriteRenderer st = stickers[rank];
                int order = 500 + 4 * rank;
                st.sortingOrder = order;
                EditorUtility.SetDirty(st);
                EnsureLayer(st, RimChild, rimMat, order - 1, Vector3.zero, Color.white);
                sb.Append(st.name).Append("=").Append(order).Append(' ');
            }

            Debug.Log(string.Format(
                "[Case3PageRim] RIM_OK {0} page objects + {1} strip stickers rimmed, rim={2:0.0} px " +
                "white (edge {3:0.0} px), drop={4} at world {5}; orders: {6}",
                objects.Count, stickers.Count, RimPixels, RimEdgeAA, DropColor, DropWorldOffset,
                sb.ToString().Trim()));
        }

        static void Collect(Transform t, string prefix, List<SpriteRenderer> into)
        {
            if (t.name.StartsWith(prefix, StringComparison.Ordinal))
            {
                SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) into.Add(sr);
            }
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), prefix, into);
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
        static Material EnsureMaterial(string path, Color color, float edgeAA)
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

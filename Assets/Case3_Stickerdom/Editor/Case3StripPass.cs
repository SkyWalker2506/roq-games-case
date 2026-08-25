using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Case3.EditorTools
{
    /// <summary>
    /// PENDING - scene surgery for owner requirements (2) "ayni resimlerden tekrar etmesin" and
    /// (3) "3 tane resim renkli olsun digerleri karanlik olsun".
    /// MUST NOT run before the landing-burst capture is taken and committed: it changes page
    /// composition, and the burst's trail control (frame_150 = 1615 px) is measured over this page.
    ///
    /// Measured starting state (all figures from the session report):
    ///   - 8 strip stickers drawn from 3 distinct drawings. sticker_cat/sticker_cat_grey are one
    ///     drawing recoloured (RMS 0.155); sticker_noodle/sticker_noodle_blue likewise (0.135).
    ///     Cross-subject pairs sit at 0.38-0.50, so those two are a separate population.
    ///   - Only 3 are playable: Case3Director.entries = Cat/Noodle/Sweets, and PickSticker iterates
    ///     entries only. "Only those three fly" is ALREADY true.
    ///   - AFFORDANCE IS INVERTED: playable meanL 157.1, decoy meanL 165.9 (gap -8.8), and the
    ///     brightest decoy (Sticker_Sweets2, 179.8) outshines the dimmest playable sticker
    ///     (Sticker_Sweets, 146.6) by +33.1. The page currently advertises the wrong stickers.
    ///
    /// Option C is only partly executable. It needs 5 distinct unused drawings; the project has 2.
    /// All 14 obj_* are already on the page, 11 of the 14 unused sprites are the CANCELLED HUD
    /// chrome (knob_*, *_pill, tool_bar, done_ring, recycle, quad_free, sheet_ring_*), and
    /// StickerBackground is a blank plate. So: re-sprite the 2 that can be re-sprited, delete the 3
    /// that cannot. The 2 chosen carry the recolour variants, so re-spriting them removes both
    /// recolour repeats; the 3 deleted are the exact-duplicate repeats.
    /// </summary>
    static class Case3StripPass
    {
        // Never touched - the three playable stickers from Case3Director.entries.
        static readonly string[] Keep = { "Sticker_Cat", "Sticker_Noodle", "Sticker_Sweets" };

        // Decoy -> replacement drawing. Both replacements are unused, non-HUD, non-reference art.
        static readonly Dictionary<string, string> Respite = new Dictionary<string, string>
        {
            { "Sticker_Noodle3", "Assets/Case3_Stickerdom/Sprites/teacup.png" },        // was sticker_noodle_blue
            { "Sticker_Cat3",    "Assets/Case3_Stickerdom/Sprites/PurplePackage.png" }, // was sticker_cat_grey
        };
        static readonly Dictionary<string, string> RespriteShadows = new Dictionary<string, string>
        {
            { "Shadow_Noodle3", "Assets/Case3_Stickerdom/Sprites/teacup.png" },
            { "Shadow_Cat3",    "Assets/Case3_Stickerdom/Sprites/PurplePackage.png" },
        };

        // Exact-duplicate repeats with no distinct art left to give them.
        // Only the parents are listed: Shadow_* are CHILDREN of their Sticker_*
        // (verified in Stickerdom.unity), so destroying the parent takes the shadow
        // with it. Listing the shadows here as well made the count gate unsatisfiable.
        static readonly string[] Delete =
        {
            "Sticker_Cat2", "Sticker_Noodle2", "Sticker_Sweets2",
        };

        // Must not survive the parent deletions - checked, not assumed.
        static readonly string[] MustBeGone =
        {
            "Shadow_Cat2", "Shadow_Noodle2", "Shadow_Sweets2",
        };

        // One dim material, not eight. The page used to carry eight PageObjectDim variants whose
        // saturation ran 0.40-1.00 and whose linear value ran 0.238-0.935 - measurably eight
        // different answers to a question the reference answers once. They are collapsed into
        // Case3_PageObjectDim, which carries the reference's own transform (saturation 1.00,
        // flat linear multiply 0.238). tools/case3_gate.py dim is the check.
        const string DimMaterial = "Assets/Case3_Stickerdom/Materials/Case3_PageObjectDim.mat";

        static void Report()
        {
            SpriteRenderer[] all = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            Dictionary<string, List<string>> bySprite = new Dictionary<string, List<string>>();
            HashSet<int> orders = new HashSet<int>();
            bool clash = false;
            foreach (SpriteRenderer r in all)
            {
                if (!orders.Add(r.sortingOrder)) clash = true;
                if (r.sprite == null) continue;
                if (!bySprite.ContainsKey(r.sprite.name)) bySprite[r.sprite.name] = new List<string>();
                bySprite[r.sprite.name].Add(r.gameObject.name);
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Case3StripPass] " + all.Length + " SpriteRenderers, " + orders.Count +
                          " distinct sorting orders, clash=" + clash);
            foreach (KeyValuePair<string, List<string>> kv in bySprite)
                if (kv.Value.Count > 1)
                    sb.AppendLine("  REPEATED '" + kv.Key + "' x" + kv.Value.Count + " -> " +
                                  string.Join(", ", kv.Value.ToArray()));
            Debug.Log(sb.ToString());
        }

        static void OptionC()
        {
            if (!EditorUtility.DisplayDialog("Case3 strip pass - Option C",
                    "Re-sprites Sticker_Noodle3 -> teacup and Sticker_Cat3 -> PurplePackage (and their " +
                    "shadows), deletes Sticker_Cat2/Noodle2/Sweets2 and their shadows, and dims the two " +
                    "survivors to the reference's background level.\n\n" +
                    "Destructive. Do NOT run before the landing-burst capture is committed.",
                    "Run", "Cancel")) return;

            Material dim = AssetDatabase.LoadAssetAtPath<Material>(DimMaterial);
            if (dim == null) { Debug.LogError("[Case3StripPass] missing " + DimMaterial); return; }

            int resprited = 0, deleted = 0;
            foreach (KeyValuePair<string, string> kv in Respite) resprited += Respite1(kv.Key, kv.Value, dim);
            foreach (KeyValuePair<string, string> kv in RespriteShadows) resprited += Respite1(kv.Key, kv.Value, null);
            foreach (string n in Delete) deleted += Destroy(n);

            Debug.Log("[Case3StripPass] Option C: re-sprited+dimmed " + resprited + ", destroyed " +
                      deleted + ". Kept playable: " + string.Join(", ", Keep) +
                      ". Save the scene to keep it, then re-run REPORT to confirm zero repeats.");
        }

        static GameObject Find(string name)
        {
            foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (r.gameObject.name == name) return r.gameObject;
            return null;
        }

        static int Respite1(string objectName, string spritePath, Material dim)
        {
            GameObject go = Find(objectName);
            if (go == null) { Debug.LogWarning("[Case3StripPass] not found: " + objectName); return 0; }
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (s == null) { Debug.LogError("[Case3StripPass] not a Sprite (check its importer): " + spritePath); return 0; }
            SpriteRenderer r = go.GetComponent<SpriteRenderer>();
            if (r == null) return 0;
            Undo.RecordObject(r, "Case3 re-sprite + dim decoy");
            r.sprite = s;
            // dim == null for shadows: they already carry their own dark tint
            // (0.22,0.14,0.08,0.42) on Sprites-Default, and pushing them through
            // PageObjectDim would desaturate and re-grade a shape that is meant to
            // stay a flat shadow.
            if (dim != null) r.sharedMaterial = dim;
            EditorUtility.SetDirty(r);
            return 1;
        }

        static int Destroy(string name)
        {
            GameObject go = Find(name);
            if (go == null) { Debug.LogWarning("[Case3StripPass] not found: " + name); return 0; }
            Undo.DestroyObjectImmediate(go);
            return 1;
        }
    
        /// <summary>
        /// Zero-argument batch entry point so the pass can run without a human clicking the menu:
        ///   tools/unity-run.sh -batchmode -executeMethod Case3.EditorTools.Case3StripPass.RunOptionCBatch
        /// Opens Stickerdom, applies Option C, saves, and exits non-zero if anything was not found.
        /// No -quit: this method finishes its own work synchronously and calls Exit itself.
        /// </summary>
        public static void RunOptionCBatch()
        {
            const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

            Material dim = AssetDatabase.LoadAssetAtPath<Material>(DimMaterial);
            if (dim == null) { Fail("missing " + DimMaterial); return; }

            int want = Respite.Count + RespriteShadows.Count + Delete.Length;
            int done = 0;
            foreach (KeyValuePair<string, string> kv in Respite)         done += Respite1(kv.Key, kv.Value, dim);
            foreach (KeyValuePair<string, string> kv in RespriteShadows) done += Respite1(kv.Key, kv.Value, null);
            foreach (string n in Delete)                                 done += Destroy(n);

            if (done != want) { Fail("applied " + done + "/" + want + " - scene NOT saved"); return; }

            foreach (string n in MustBeGone)
                if (Find(n) != null) { Fail("survived parent deletion: " + n + " - scene NOT saved"); return; }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("[Case3StripPass] OPTION_C_OK applied=" + done + "/" + want + " scene saved");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Fail(string msg)
        {
            Debug.LogError("[Case3StripPass] OPTION_C_FAIL " + msg);
            if (Application.isBatchMode) EditorApplication.Exit(7);
        }
}
}

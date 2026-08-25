using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case3;
using Shared.EditorTools;
using Shared.Sequencing;

/// <summary>
/// Validates, reconstructs and wires the layered Case 3 scene. Visual layout belongs to Stickerdom.unity
/// built from individual layered scene objects (background, sheet, rings, card decks, ghost slots, stickers,
/// rewards, tools) with authored sprites.
/// </summary>
public static class Case3SceneSetup
{
    const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";
    const string CurlMaterialPath = "Assets/Case3_Stickerdom/Materials/Case3_StickerCurl.mat";
    // The one dim material the page uses. A covered sticker wears it until it is uncovered.
    const string DimMaterialPath = "Assets/Case3_Stickerdom/Materials/Case3_PageObjectDim.mat";
    const string SparklePath = "Assets/Case3_Stickerdom/VFX/SparklePop.prefab";
    const string AttachBurstPath = "Assets/Case3_Stickerdom/VFX/AttachBurst.prefab";
    const string RootName = "Case3_Sequence";

    static readonly string[] Keys = { "Cat", "Noodle", "Sweets" };

    public static void BuildMenu()
    {
        Build();
    }

    public static void ReconstructLayeredSceneMenu()
    {
        ReconstructLayeredScene();
    }

    /// <summary>
    /// Reconstructs the entire Case 3 screen out of real, separate scene objects with authored sprites.
    /// Eliminates any full-screen backdrop texture or video crop.
    /// </summary>
    /// <remarks>
    /// DESTRUCTIVE - this method destroys every root GameObject first, and it does NOT
    /// rebuild everything the authored scene contains. Stickerdom.unity has drifted from
    /// this builder and the scene is the source of truth for the captured frame:
    ///   - 14 PageObj_* under-art objects live ONLY in the scene. Nothing here recreates
    ///     them, so running this menu item wipes the entire under-art layer.
    ///   - Ghost_* SpriteRenderers are authored at alpha 0 (invisible); this builder
    ///     writes alpha 0.40, so running it makes three dark sticker silhouettes appear
    ///     inside the empty slots.
    /// Diff the rebuilt scene against the authored one before saving, or export the
    /// scene-only objects into this builder first.
    /// </remarks>
    public static void ReconstructLayeredScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Remove all old objects in scene
        GameObject[] rootGos = scene.GetRootGameObjects();
        for (int i = 0; i < rootGos.Length; i++)
        {
            UnityEngine.Object.DestroyImmediate(rootGos[i]);
        }

        // 1. Main Camera
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 8.64f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.08f, 0.22f, 1f);
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        // 2. Directional Light
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.98f, 0.95f, 1f);
        light.intensity = 1.0f;
        lightGo.transform.position = new Vector3(0f, 0f, -5f);
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 3. Environment / PageBackground
        GameObject envGo = new GameObject("Environment");
        GameObject bgGo = new GameObject("PageBackground");
        bgGo.transform.SetParent(envGo.transform);
        SpriteRenderer bgSr = bgGo.AddComponent<SpriteRenderer>();
        bgSr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Background/PageBackground.png");
        bgSr.sortingOrder = 0;
        bgGo.transform.position = new Vector3(0f, 0f, 1f);
        bgGo.transform.localScale = new Vector3(1.06f, 1.69f, 1f);

        // 4. Page / Sheet & Rings
        GameObject pageGo = new GameObject("Page");

        // 4a. Album Cast Shadow (Shadow Class 1: casts on wooden table behind sheet)
        GameObject sheetShadow = new GameObject("PageSheetShadow");
        sheetShadow.transform.SetParent(pageGo.transform);
        SpriteRenderer shSr = sheetShadow.AddComponent<SpriteRenderer>();
        shSr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/StickerSheetBackground.png");
        shSr.color = new Color(0.20f, 0.12f, 0.06f, 0.45f);
        shSr.sortingOrder = 4;
        sheetShadow.transform.position = new Vector3(0.08f, -1.92f, 0f);
        sheetShadow.transform.localScale = new Vector3(1.98f, 1.28f, 1f);

        GameObject sheetGo = new GameObject("PageSheet");
        sheetGo.transform.SetParent(pageGo.transform);
        SpriteRenderer sheetSr = sheetGo.AddComponent<SpriteRenderer>();
        sheetSr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/StickerSheetBackground.png");
        sheetSr.sortingOrder = 5;
        sheetGo.transform.position = new Vector3(0f, -1.80f, 0f);
        sheetGo.transform.localScale = new Vector3(1.95f, 1.25f, 1f);

        // State-driven under-art darkening (owner rule: dark reads come from a
        // material, never a baked-dark texture).
        Material underArtMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Case3_Stickerdom/Materials/Case3_PageUnderArt.mat");
        if (underArtMat != null) sheetSr.sharedMaterial = underArtMat;

        // Rings and side stubs removed: the reference page has no visible binder rings.

        // 5. EmptyCards / Top Slots
        GameObject emptyCardsGo = new GameObject("EmptyCards");
        Sprite deckBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/DeckBackground.png");

        float[] slotX = { -3.40f, 0f, 3.40f };
        for (int i = 0; i < Keys.Length; i++)
        {
            GameObject slotGroup = new GameObject("Slot_" + Keys[i]);
            slotGroup.transform.SetParent(emptyCardsGo.transform);
            slotGroup.transform.position = new Vector3(slotX[i], 5.50f, 0f);

            GameObject cardBase = new GameObject("Empty_" + Keys[i]);
            cardBase.transform.SetParent(slotGroup.transform);
            cardBase.transform.localPosition = Vector3.zero;
            cardBase.transform.localScale = new Vector3(1.05f, 1.05f, 1f);
            SpriteRenderer baseSr = cardBase.AddComponent<SpriteRenderer>();
            baseSr.sprite = deckBgSprite;
            baseSr.sortingOrder = 10;

            // Inner slot panel + its inner shadow removed: the pair drew a 170x223
            // StickerBackground inside the 290x374 slot, producing a visible inset
            // rounded-rect (measured step -17.9 gray against the surrounding panel)
            // that the reference does not have. Removing it also lifts the slot
            // interior from 174.8 to ~182.0 mean gray, against the reference's 182.6.
            // The remaining slot busy-ness is DeckBackground.png's own grid and
            // vertical gradient, not this pair.

            // Badge pill removed: the reference boxes have no counter pill.
        }

        // 6. GhostSlots
        GameObject ghostSlotsGo = new GameObject("GhostSlots");
        Sprite[] stickerSprites = {
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Stickers/sticker_cat.png"),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Stickers/sticker_noodle.png"),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Stickers/sticker_sweets.png")
        };
        // Slot-fit scales: cell interior is 2.39 x 3.23 world units; each sticker
        // is scaled so its sprite fits 88% of the cell (consistent landing size).
        float[] ghostScales = { 0.64f, 0.59f, 0.73f };
        for (int i = 0; i < Keys.Length; i++)
        {
            GameObject ghost = new GameObject("Ghost_" + Keys[i]);
            ghost.transform.SetParent(ghostSlotsGo.transform);
            ghost.transform.position = new Vector3(slotX[i], 5.35f, 0f);
            ghost.transform.localScale = new Vector3(ghostScales[i], ghostScales[i], 1f);
            SpriteRenderer gSr = ghost.AddComponent<SpriteRenderer>();
            gSr.sprite = stickerSprites[i];
            gSr.color = new Color(0.18f, 0.18f, 0.25f, 0.40f);
            gSr.sortingOrder = 20;
        }

        // 7. CardRewards
        GameObject cardRewardsGo = new GameObject("CardRewards");
        Sprite[] rewardSprites = {
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Cards/card_filled_cat.png"),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Cards/card_filled_noodle.png"),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Case3_Stickerdom/Sprites/Cards/card_filled_sweets.png")
        };
        for (int i = 0; i < Keys.Length; i++)
        {
            GameObject reward = new GameObject("Reward_" + Keys[i]);
            reward.transform.SetParent(cardRewardsGo.transform);
            reward.transform.position = new Vector3(slotX[i], 5.50f, 0f);
            reward.transform.localScale = new Vector3(1.05f, 1.05f, 1f);
            SpriteRenderer rSr = reward.AddComponent<SpriteRenderer>();
            rSr.sprite = rewardSprites[i];
            rSr.color = new Color(1f, 1f, 1f, 0f);
            rSr.enabled = false;
            rSr.sortingOrder = 50;
        }

        // 8. WaitingStickers
        GameObject waitingGo = new GameObject("WaitingStickers");
        Vector3[] stickerPos = {
            new Vector3(2.10f, -1.20f, 0f),
            new Vector3(-1.80f, -3.80f, 0f),
            new Vector3(0.60f, -2.40f, 0f)
        };
        // On-page home size. Deliberately LARGER than the slot-fit landing size
        // (0.64 / 0.59 / 0.73, see ghostScales): in the reference the playable
        // stickers read at 275-322 px on their long axis while ours read at
        // 217 / 159 / 176 px, so every family is scaled by 1.45x. The sticker
        // still lands at the slot's own scale, so the card fit is unchanged.
        float[] stickerScales = { 0.93f, 0.86f, 1.06f };
        Material curlMat = AssetDatabase.LoadAssetAtPath<Material>(CurlMaterialPath);

        for (int i = 0; i < Keys.Length; i++)
        {
            GameObject stGo = new GameObject("Sticker_" + Keys[i]);
            stGo.transform.SetParent(waitingGo.transform);
            stGo.transform.position = stickerPos[i];
            stGo.transform.localScale = new Vector3(stickerScales[i], stickerScales[i], 1f);

            // Per-Sticker Drop Shadow (Shadow Class 2: generated from sprite alpha at runtime)
            GameObject stShadow = new GameObject("Shadow_" + Keys[i]);
            stShadow.transform.SetParent(stGo.transform);
            stShadow.transform.localPosition = new Vector3(0.10f, -0.14f, 0f);
            stShadow.transform.localScale = Vector3.one;
            SpriteRenderer stShSr = stShadow.AddComponent<SpriteRenderer>();
            stShSr.sprite = stickerSprites[i];
            stShSr.color = new Color(0.22f, 0.14f, 0.08f, 0.42f);
            stShSr.sortingOrder = 28;

            SpriteRenderer sSr = stGo.AddComponent<SpriteRenderer>();
            sSr.sprite = stickerSprites[i];
            sSr.sortingOrder = 30;

            StickerPeel peel = stGo.AddComponent<StickerPeel>();
            peel.sticker = sSr;
            peel.curlMaterial = curlMat;
        }

        // 9. Gameplay decor is cancelled on this case: no tool band, no label pills, and no
        // teacup (our sprite is a three-quarter-view mug in a strictly top-down scene; the
        // reference does have a top-down cup+saucer clipped by the bottom-left corner, but
        // restoring it needs new art).

        // Bottom tool band removed: UI is cancelled on this case.

        // Label pills removed: the reference has no label text under the tool band.

        // 10. Case3_Sequence Root
        GameObject rootGo = new GameObject(RootName);
        Case3Director director = rootGo.AddComponent<Case3Director>();
        StickerFlight flight = rootGo.AddComponent<StickerFlight>();
        rootGo.AddComponent<ReplayButton>();

        // Wire Director
        GameObject sparklePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SparklePath);
        GameObject attachBurstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AttachBurstPath);

        Material dimMaterial = AssetDatabase.LoadAssetAtPath<Material>(DimMaterialPath);

        flight.sparklePrefab = sparklePrefab;
        flight.sparkleColor = new Color(0.93f, 1f, 0.16f, 1f);
        flight.sparkleInterval = 0.045f;
        flight.sparkleScale = 0.42f;
        flight.sparkleScatter = 0.08f;

        List<Case3Director.Entry> entries = new List<Case3Director.Entry>();
        for (int i = 0; i < Keys.Length; i++)
        {
            Transform stTf = waitingGo.transform.Find("Sticker_" + Keys[i]);
            Transform ghTf = ghostSlotsGo.transform.Find("Ghost_" + Keys[i]);
            Transform rwTf = cardRewardsGo.transform.Find("Reward_" + Keys[i]);

            entries.Add(new Case3Director.Entry
            {
                key = Keys[i],
                peel = stTf.GetComponent<StickerPeel>(),
                sticker = stTf.GetComponent<SpriteRenderer>(),
                targetSlot = ghTf.GetComponent<SpriteRenderer>(),
                reward = rwTf.GetComponent<SpriteRenderer>()
            });
        }

        director.entries = entries.ToArray();
        director.flight = flight;
        director.sceneCamera = cam;
        director.attachBurstPrefab = attachBurstPrefab;
        director.dimMaterial = dimMaterial;
        director.tapDuration = 0.05f;
        director.peelDuration = 0.30f;
        director.flightDuration = 0.35f;
        director.flipDuration = 0.12f;
        director.popDuration = 0.11f;
        director.settleDuration = 0.07f;
        director.peelEnd = 0.96f;
        director.flightShrink = 0.88f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Case3Setup] Layered scene successfully reconstructed and saved!");
    }

    /// <summary>
    /// Batchmode entry point. All authored objects/assets are validated before the first mutation, then
    /// stable runtime references and source-authoritative timing/VFX values are saved.
    /// </summary>
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform waiting = FindRoot(scene, "WaitingStickers");
        Transform ghosts = FindRoot(scene, "GhostSlots");
        Transform rewards = FindRoot(scene, "CardRewards");
        Transform rootTf = FindRoot(scene, RootName);
        Camera cam = UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        Material curlMaterial = AssetDatabase.LoadAssetAtPath<Material>(CurlMaterialPath);
        Material dimMaterial = AssetDatabase.LoadAssetAtPath<Material>(DimMaterialPath);
        GameObject sparklePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SparklePath);
        GameObject attachBurstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AttachBurstPath);

        if (waiting == null || ghosts == null || rewards == null || rootTf == null || cam == null ||
            curlMaterial == null || dimMaterial == null || sparklePrefab == null || attachBurstPrefab == null)
        {
            Fail("authored scene/assets are incomplete in Stickerdom.unity");
            return;
        }

        // Ghost slots and reward cards are 1:1 with Keys and must stay that way - an extra
        // ghost would be a destination with no sticker. Sticker_ is a LOWER bound: the page
        // also carries dim decoy sheets (Sticker_Cat3, Sticker_Noodle3) that are deliberately
        // not entries, so they are never tappable. Requiring exactly three here made Build()
        // throw on the authored scene from 2515a89 onwards; the per-key Find below is what
        // actually validates that each of the three playable stickers is present.
        if (CountNamedChildren(waiting, "Sticker_") < Keys.Length ||
            CountNamedChildren(ghosts, "Ghost_") != Keys.Length ||
            CountNamedChildren(rewards, "Reward_") != Keys.Length)
        {
            Fail("expected three ghost targets and reward cards, and at least three authored stickers");
            return;
        }

        SpriteRenderer[] stickers = new SpriteRenderer[Keys.Length];
        SpriteRenderer[] slots = new SpriteRenderer[Keys.Length];
        SpriteRenderer[] rewardCards = new SpriteRenderer[Keys.Length];

        for (int i = 0; i < Keys.Length; i++)
        {
            Transform stickerTf = waiting.Find("Sticker_" + Keys[i]);
            Transform slotTf = ghosts.Find("Ghost_" + Keys[i]);
            Transform rewardTf = rewards.Find("Reward_" + Keys[i]);
            if (stickerTf == null || slotTf == null || rewardTf == null)
            {
                Fail("missing authored object for key " + Keys[i]);
                return;
            }

            stickers[i] = stickerTf.GetComponent<SpriteRenderer>();
            slots[i] = slotTf.GetComponent<SpriteRenderer>();
            rewardCards[i] = rewardTf.GetComponent<SpriteRenderer>();
            if (stickers[i] == null || stickers[i].sprite == null || slots[i] == null ||
                rewardCards[i] == null || rewardCards[i].sprite == null)
            {
                Fail("missing SpriteRenderer/sprite for key " + Keys[i]);
                return;
            }
        }

        GameObject root = rootTf.gameObject;
        Case3Director director = EnsureSingle<Case3Director>(root);
        StickerFlight flight = EnsureSingle<StickerFlight>(root);
        EnsureSingle<ReplayButton>(root);

        List<Case3Director.Entry> entries = new List<Case3Director.Entry>(Keys.Length);
        for (int i = 0; i < Keys.Length; i++)
        {
            StickerPeel peel = EnsureSingle<StickerPeel>(stickers[i].gameObject);
            peel.sticker = stickers[i];
            peel.curlMaterial = curlMaterial;
            EditorUtility.SetDirty(peel);

            entries.Add(new Case3Director.Entry
            {
                key = Keys[i],
                peel = peel,
                sticker = stickers[i],
                targetSlot = slots[i],
                reward = rewardCards[i]
            });

            Color rewardColor = rewardCards[i].color;
            rewardColor.a = 0f;
            rewardCards[i].color = rewardColor;
            rewardCards[i].enabled = false;
            EditorUtility.SetDirty(rewardCards[i]);
        }

        flight.sparklePrefab = sparklePrefab;
        flight.sparkleColor = new Color(0.93f, 1f, 0.16f, 1f);
        flight.sparkleInterval = 0.045f;
        flight.sparkleScale = 0.42f;
        flight.sparkleScatter = 0.08f;

        director.entries = entries.ToArray();
        director.flight = flight;
        director.sceneCamera = cam;
        director.attachBurstPrefab = attachBurstPrefab;
        director.dimMaterial = dimMaterial;
        director.tapDuration = 0.05f;
        director.peelDuration = 0.30f;
        director.flightDuration = 0.35f;
        director.flipDuration = 0.12f;
        director.popDuration = 0.11f;
        director.settleDuration = 0.07f;
        director.peelEnd = 0.96f;
        director.flightShrink = 0.88f;

        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(flight);
        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Dump(cam, entries, director);

        StringBuilder pairs = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0) pairs.Append(" | ");
            pairs.Append(entries[i].sticker.name).Append(" -> ").Append(entries[i].targetSlot.name);
        }
        Debug.Log("[Case3Setup] SETUP_OK authored scene preserved; pairs: " + pairs);
    }

    /// <summary>Zero-argument entry point for the frame-strip capture gate.</summary>
    public static void CaptureStickerdom()
    {
        FrameStripCapture.Capture("Stickerdom");
    }

    /// <summary>Capture-only entry point for Case 3.</summary>
    public static void BuildAndCapture()
    {
        CaptureStickerdom();
    }

    static int CountNamedChildren(Transform parent, string prefix)
    {
        int count = 0;
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name.StartsWith(prefix, StringComparison.Ordinal)) count++;
        return count;
    }

    static T EnsureSingle<T>(GameObject go) where T : Component
    {
        T[] existing = go.GetComponents<T>();
        if (existing.Length > 1)
            throw new InvalidOperationException("[Case3Setup] duplicate " + typeof(T).Name + " on " + go.name);
        return existing.Length == 1 ? existing[0] : go.AddComponent<T>();
    }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform hit = FindDeep(roots[i].transform, name);
            if (hit != null) return hit;
        }
        return null;
    }

    static Transform FindDeep(Transform current, string name)
    {
        if (current.name == name) return current;
        for (int i = 0; i < current.childCount; i++)
        {
            Transform hit = FindDeep(current.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    static void Dump(Camera cam, List<Case3Director.Entry> entries, Case3Director director)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Case3Setup] ---- authored scene discovery ----");
        sb.AppendLine(string.Format("camera pos={0} ortho={1} size={2:0.000}",
            cam.transform.position, cam.orthographic, cam.orthographicSize));

        for (int i = 0; i < entries.Count; i++)
        {
            Case3Director.Entry entry = entries[i];
            sb.AppendLine("pair " + i + " key=" + entry.key);
            DumpSprite(sb, "  sticker", entry.sticker);
            DumpSprite(sb, "  slot", entry.targetSlot);
            DumpSprite(sb, "  reward", entry.reward);
            sb.AppendLine(string.Format("  curl material={0} shader={1}",
                entry.peel != null && entry.peel.curlMaterial != null ? entry.peel.curlMaterial.name : "<null>",
                entry.peel != null && entry.peel.curlMaterial != null ? entry.peel.curlMaterial.shader.name : "-"));
        }

        sb.AppendLine(string.Format("vfx sparkle={0} attachBurst={1}",
            director.flight != null && director.flight.sparklePrefab != null,
            director.attachBurstPrefab != null));
        sb.AppendLine(string.Format(
            "timeline tap={0:0.00} peel={1:0.00} flight={2:0.00} flip={3:0.00} pop={4:0.00} settle={5:0.00} total={6:0.00}",
            director.tapDuration, director.peelDuration, director.flightDuration,
            director.flipDuration, director.popDuration, director.settleDuration,
            director.tapDuration + director.peelDuration + director.flightDuration +
            director.flipDuration + director.popDuration + director.settleDuration));
        Debug.Log(sb.ToString());
    }

    static void DumpSprite(StringBuilder sb, string label, SpriteRenderer renderer)
    {
        if (renderer == null) { sb.AppendLine(label + ": <null>"); return; }
        sb.AppendLine(string.Format("{0}: {1} pos={2} scale={3} sprite={4} order={5} bounds e={6}",
            label, renderer.name, renderer.transform.position, renderer.transform.localScale,
            renderer.sprite != null ? renderer.sprite.name : "-", renderer.sortingOrder, renderer.bounds.extents));
    }

    static void Fail(string message)
    {
        string full = "[Case3Setup] SETUP_FAILED " + message;
        Debug.LogError(full);
        if (Application.isBatchMode) throw new InvalidOperationException(full);
    }
}

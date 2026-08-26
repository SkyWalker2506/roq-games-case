using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case3;

/// <summary>
/// Turns the page's drawn objects into real, named, stackable collectibles, and gives every reward
/// card the reference's "n/5" counter.
///
/// WHY THIS EXISTS. The page carried nineteen sprites and the director knew about three of them. The
/// other sixteen were decor: permanently dim, never tappable, never named. That is what the owner
/// saw as "it only works for three objects" - the mechanism was right and the population was almost
/// empty. Registering the page items is what makes the coverage rule mean anything, because coverage
/// can only promote an item the director has heard of.
///
/// THE STACK. The reference draws 1/5 on the Cat card AND on the Noodle card, while its page carries
/// a different number of cats than of noodles, and it separately draws 1/2 in the page's bottom-right
/// corner. So 5 is a fixed per-card requirement collected across the level, and 1/2 is the page
/// counter - two different numbers with two different meanings. Cards here therefore ask for 5 and
/// several page items share a card: four ramen and the strip's two noodle sheets all collect onto
/// "Noodle", so a second ramen turns 1/5 into 2/5 instead of opening a second card.
///
/// THE COUNTER'S PLACE. The reference has no bottom band on its card and prints 1/5 over the bottom
/// right of the art, its glyphs 16.4% of the panel's height with the right edge at 96.7% of its
/// width, in a dark red measured at RGB(140, 38, 28) with a white outline. Our card art has an empty
/// inset band across its bottom - noted in docs/verify/case3_deviation.md as ours and not the
/// reference's - so the counter is set INTO that band, right-aligned, in the reference's red. Same
/// information, same corner, drawn into the card in world space rather than as screen HUD.
///
/// THE TWO DECOYS. Sticker_Cat3 and Sticker_Noodle3 were named for the cat and the noodle but were
/// drawing PurplePackage and teacup - art belonging to neither, wearing the dim material, with
/// nothing above them to justify being dim. sticker_cat_grey and sticker_noodle_blue were already in
/// the project and unused. They are pointed at their own art and joined to their cards, which is
/// both what their names always claimed and what stops them violating the coverage rule.
/// </summary>
public static class Case3PageEntries
{
    const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";
    const string DimMaterialPath = "Assets/Case3_Stickerdom/Materials/Case3_PageObjectDim.mat";
    const string LitMaterialPath = "Assets/Case3_Stickerdom/Materials/Case3_PageObjectLit.mat";
    const string CurlMaterialPath = "Assets/Case3_Stickerdom/Materials/Case3_StickerCurl.mat";

    /// <summary>objectName, card key, the item's own name.</summary>
    static readonly string[,] PageItems =
    {
        { "PageObj_navy_ramen",   "Noodle", "Ramen Bowl"   },
        { "PageObj_ramen_cup",    "Noodle", "Cup Noodle"   },
        { "PageObj_ramen_small",  "Noodle", "Ramen Bowl"   },
        { "PageObj_ramen_tin",    "Noodle", "Ramen Tin"    },
        { "PageObj_choc",         "Sweets", "Chocolate"    },
        { "PageObj_marshmallows", "Sweets", "Marshmallows" },
        { "PageObj_croissant",    "Sweets", "Croissant"    },
        { "PageObj_bunplate",     "Sweets", "Buns"         },
        { "PageObj_pie",          "Sweets", "Pie"          },
    };

    /// <summary>The three reward cards the scene authors, and the only stack keys there are.</summary>
    static readonly string[] CardKeys = { "Cat", "Noodle", "Sweets" };

    /// <summary>
    /// TAKEN OFF THE PAGE. Fewer stickers, deliberately stacked, on the owner's instruction:
    /// "sprite sayisini azalt ve sadece ustunde baska sprite olmayanlar basilabilir aydinlik olsun".
    ///
    /// These five - a teapot, a jam jar, a gingham cloth, a pair of overalls and a teddy - belong to
    /// none of the three cards, and an item with no card can only ever be permanent decor. Permanent
    /// decor cannot satisfy "dim if and only if something is on top of it" for the whole session, and
    /// the gate proved it rather than my arguing it: with the five kept as decor the opening board was
    /// clean, then one lift turned it red, because PageObj_teapot's only cover was Sticker_Noodle and
    /// collecting the noodle left the teapot uncovered and dim. Burying decor under decor cannot save
    /// it either - whichever decor item is drawn highest has nothing above it by construction.
    ///
    /// A fourth "Home" card was built and then thrown away in favour of this: fewer, clearly stacked
    /// stickers read the rule at a glance, which is what the owner asked for, and adding a card would
    /// have made the page denser rather than clearer.
    /// </summary>
    static readonly string[] Retired =
    {
        "PageObj_teapot", "PageObj_jar", "PageObj_gingham", "PageObj_overalls", "PageObj_teddy",
    };

    /// <summary>
    /// Page PRINT: drawn on the page but never collectible. DELIBERATELY EMPTY, and it has to be.
    ///
    /// The first attempt kept five items as decor - teapot, jar, gingham, overalls, teddy - all
    /// genuinely buried on the opening board, so the page looked right. Case3CoverageGate then took
    /// one sticker off and went red: PageObj_teapot's only cover was Sticker_Noodle, and the instant
    /// Noodle was collected the teapot sat uncovered and dim. That is the owner's complaint reproduced
    /// two seconds later in the same session.
    ///
    /// It is not fixable by burying decor under decor, either. Whatever is drawn highest among the
    /// permanently-dim items has nothing above it by construction, so at least one always violates
    /// the rule. Under "dim if and only if something is on top of it", a page with permanent decor on
    /// it is simply not reachable. Every drawn item is therefore collectible, which is what the fourth
    /// card exists to make possible.
    /// </summary>
    public static readonly string[] PagePrint = new string[0];

    /// <summary>objectName, card key, name, the sprite it should have been drawing all along.</summary>
    static readonly string[,] Decoys =
    {
        { "Sticker_Cat3",    "Cat",    "Grey Cat",    "Assets/Case3_Stickerdom/Sprites/Stickers/sticker_cat_grey.png"    },
        { "Sticker_Noodle3", "Noodle", "Blue Noodle", "Assets/Case3_Stickerdom/Sprites/Stickers/sticker_noodle_blue.png" },
    };

    static readonly string[,] StripItems =
    {
        { "Sticker_Cat",    "Cat",    "Cat"        },
        { "Sticker_Noodle", "Noodle", "Noodle"     },
        { "Sticker_Sweets", "Sweets", "Candy Cane" },
    };

    // ---- counter geometry, in the card sprite's own pixels (276 x 356 at 100 px/unit).
    //
    // MOVED. It used to sit in an empty inset band across the card's bottom - a band that was
    // OURS and not the reference's, recorded as a deviation and left as a decision. The owner
    // has since asked for the reference's proportions on these cards, so the band is gone
    // (tools/generate_case3_consistent_art.py) and the counter is back where the reference
    // prints it: over the bottom right of the art itself.
    //
    // The reference's numbers, off the card_filled_* crops: glyphs 16.4% of the PANEL's
    // height, right edge at 96.7% of the panel's width. Our panel - the frame's opening,
    // below the name tab - is x 24..252, y 64..326, so 228 x 262 sprite pixels. That puts
    // the glyphs at 0.164 * 262 = 43 px tall with their right edge at 24 + 0.967 * 228 = 244.
    const float CardPixelsPerUnit = 100f;
    const float CardW = 276f, CardH = 356f;
    const float PanelX0 = 24f, PanelY0 = 64f, PanelX1 = 252f, PanelY1 = 326f;
    const float GlyphPx = 43f;                               // 16.4% of the panel's height
    const float BandW = 104f, BandH = 52f;
    const float BandCentreX = 244.5f - BandW * 0.5f;         // right edge at 96.7% of the panel
    const float BandCentreY = PanelY1 - 10f - BandH * 0.5f;  // sitting on the panel's bottom
    /// <summary>Reference glyph fill, sampled off Stickerdom.mp4's Cat card at RGB(140, 38, 28).</summary>
    static readonly Color CounterInk = new Color(140f / 255f, 38f / 255f, 28f / 255f, 1f);

    /// <summary>Zero-argument entry point for -executeMethod and for the pipeline CLI.</summary>
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StringBuilder log = new StringBuilder();

        Case3Director director = Object.FindFirstObjectByType<Case3Director>(FindObjectsInactive.Include);
        if (director == null) { Debug.LogError("[Case3PageEntries] no Case3Director in " + scene.name); return; }

        Material dim = AssetDatabase.LoadAssetAtPath<Material>(DimMaterialPath);
        Material curl = AssetDatabase.LoadAssetAtPath<Material>(CurlMaterialPath);
        Material pageLit = EnsurePageLitMaterial(dim);
        if (dim == null || curl == null || pageLit == null)
        {
            Debug.LogError("[Case3PageEntries] Case 3 materials are missing"); return;
        }

        // The strip sheets already carry the right lit material; reuse it verbatim so the decoys read
        // identically to the sheets they sit next to instead of getting a second, drifting copy.
        SpriteRenderer catSheet = Find(scene, "Sticker_Cat");
        Material stickerLit = catSheet != null ? catSheet.sharedMaterial : null;
        if (stickerLit == null || stickerLit == dim)
        {
            Debug.LogError("[Case3PageEntries] Sticker_Cat has no lit material to copy"); return;
        }

        List<Case3Director.Entry> entries = new List<Case3Director.Entry>();

        // ---- the three strip sheets keep their existing wiring; only their names are new.
        for (int i = 0; i < StripItems.GetLength(0); i++)
        {
            string objName = StripItems[i, 0], key = StripItems[i, 1], name = StripItems[i, 2];
            SpriteRenderer sr = Find(scene, objName);
            if (sr == null) { Debug.LogError("[Case3PageEntries] missing " + objName); return; }
            entries.Add(MakeEntry(scene, sr, key, name, stickerLit, curl, log));
        }

        // ---- the two mis-drawn decoys become the second cat and the second noodle.
        for (int i = 0; i < Decoys.GetLength(0); i++)
        {
            string objName = Decoys[i, 0], key = Decoys[i, 1], name = Decoys[i, 2], spritePath = Decoys[i, 3];
            SpriteRenderer sr = Find(scene, objName);
            Sprite art = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sr == null || art == null) { Debug.LogError("[Case3PageEntries] missing " + objName + " or " + spritePath); return; }

            log.AppendLine("  RETARGET " + objName + ": " + (sr.sprite != null ? sr.sprite.name : "<none>") + " -> " + art.name);
            sr.sprite = art;
            sr.sharedMaterial = stickerLit;      // coverage decides dim from here on, not the authoring
            EditorUtility.SetDirty(sr);
            foreach (Transform child in sr.transform)
            {
                SpriteRenderer cs = child.GetComponent<SpriteRenderer>();
                if (cs != null) { cs.sprite = art; EditorUtility.SetDirty(cs); }   // its contact shadow
            }
            entries.Add(MakeEntry(scene, sr, key, name, stickerLit, curl, log));
        }

        // ---- the page's own drawn objects.
        for (int i = 0; i < PageItems.GetLength(0); i++)
        {
            string objName = PageItems[i, 0], key = PageItems[i, 1], name = PageItems[i, 2];
            SpriteRenderer sr = Find(scene, objName);
            if (sr == null) { Debug.LogError("[Case3PageEntries] missing " + objName); return; }
            entries.Add(MakeEntry(scene, sr, key, name, pageLit, curl, log));
        }

        foreach (string name in Retired)
        {
            SpriteRenderer sr = Find(scene, name);
            if (sr == null || !sr.gameObject.activeSelf) continue;
            sr.gameObject.SetActive(false);
            EditorUtility.SetDirty(sr.gameObject);
            log.AppendLine("  RETIRED " + name + " (no card to collect it onto, so it could only ever be dim)");
        }

        SeparateChoc(scene, log);

        director.entries = entries.ToArray();
        director.dimMaterial = dim;
        director.stacks = BuildStacks(scene, log);
        EditorUtility.SetDirty(director);

        AssertStacksCanHoldThePage(director, log);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        log.Insert(0, "[Case3PageEntries] " + entries.Count + " collectible item(s) across " +
                      director.stacks.Length + " card(s); " + PagePrint.Length + " page-print item(s) left as decor\n");
        Debug.Log(log.ToString());

        // Close the Editor when this was driven from the command line.
        //
        // Without it the batch instance finishes its work and then sits idle forever holding
        // tools/unity-run.sh's mkdir lock, and every later run - captures, gates, other agents -
        // queues behind it in silence. That cost 30 minutes today: a capture appeared to hang when it
        // had simply never been allowed to start.
        //
        // `-quit` is NOT the alternative here. This project bans it because a method that drives
        // EditorApplication.update returns rc=0 having done nothing, which reads as a pass. Build does
        // not drive the update loop, so it can and must exit itself - the same thing Case3PageRim,
        // Case3StripPass and the gates already do.
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// <summary>
    /// A card cannot be asked for fewer than the page can deliver.
    ///
    /// WHY THIS EXISTS. The owner's screenshot shows a counter reading 6/5 - a stack over its own
    /// maximum. It is not a display bug: Case3Director.PushStack increments Collected with no
    /// ceiling, and this table hands the Noodle card six items (Sticker_Noodle, Sticker_Noodle3 and
    /// four PageObj ramen) and the Sweets card six (Sticker_Sweets plus choc, marshmallows,
    /// croissant, bunplate, pie) against a stackRequirement of 5. Collect them all and the card
    /// reads 6/5. Cat is the only card that cannot: it has two.
    ///
    /// The population is authored HERE, so the mismatch is detectable here. The fix is not: a card
    /// that asks for a different number than its neighbours needs a per-card requirement on
    /// Case3Director.StackCard, which this pass does not own. So this reports the exact violation
    /// with the exact numbers rather than clamping the count, because clamping would print 5/5 while
    /// six sit on the card - a wrong number instead of an impossible one.
    /// </summary>
    static void AssertStacksCanHoldThePage(Case3Director director, StringBuilder log)
    {
        Dictionary<string, int> population = new Dictionary<string, int>();
        for (int i = 0; i < director.entries.Length; i++)
        {
            string k = director.entries[i].key;
            population[k] = (population.TryGetValue(k, out int n) ? n : 0) + 1;
        }
        foreach (KeyValuePair<string, int> kv in population)
        {
            log.AppendLine(string.Format("  POPULATION {0}: {1} collectible(s) against a global requirement of {2}",
                kv.Key, kv.Value, director.stackRequirement));
        }

        // Give every card a requirement its own page can actually reach.
        //
        // The counter read 6/5 because the page carries SIX Noodle and SIX Sweets items against a
        // global requirement of five, and PushStack has no ceiling. Two ways to close that: trim the
        // page, or let the card ask for what the page holds. Trimming loses collectibles the owner
        // deliberately left tappable - every item with nothing on top must be collectible - so the
        // card follows the page.
        //
        // DEVIATION, recorded rather than hidden: the reference prints /5 on all three cards. Ours
        // will print /6 where the page holds six. A denominator that differs per card is a smaller
        // departure than a counter that exceeds its own maximum, which is what the owner saw.
        for (int i = 0; i < director.stacks.Length; i++)
        {
            Case3Director.StackCard c = director.stacks[i];
            if (c == null || string.IsNullOrEmpty(c.key)) continue;
            int have = population.TryGetValue(c.key, out int n) ? n : 0;
            // The denominator is what the PAGE holds, full stop - not the global requirement.
            // Max(global, have) printed "2/5" on the Cat card when only two cats exist to collect,
            // so the counter promised three that were never there. The owner: "iki tane kedi
            // olduguna gore ikide iki olmasi lazim".
            c.requirement = Mathf.Max(1, have);
            log.AppendLine(string.Format("  REQUIREMENT {0}: {1} (page holds {2})", c.key, c.requirement, have));
            if (have == 0)
                Debug.LogError(string.Format(
                    "[Case3PageEntries] the '{0}' card has no collectible on the page at all, so its " +
                    "counter can never move off 0/{1}.", c.key, c.requirement));
        }
    }

    /// <summary>
    /// Wires one item: a peel driver, the rim that has to travel with it, the contact shadow the peel
    /// looks for by prefix, and the card it collects onto.
    /// </summary>
    static Case3Director.Entry MakeEntry(Scene scene, SpriteRenderer sr, string key, string name,
                                         Material lit, Material curl, StringBuilder log)
    {
        StickerPeel peel = sr.GetComponent<StickerPeel>();
        if (peel == null) peel = Undo.AddComponent<StickerPeel>(sr.gameObject);
        peel.sticker = sr;
        peel.curlMaterial = curl;

        // The peel finds the contact shadow by the "Shadow_" prefix. The page items call theirs
        // "Drop"; renaming is what lets one peel driver serve both the strip and the page. The white
        // die-cut rim is the same piece of paper as the art, so it is handed to the peel as a
        // companion and vanishes with it - otherwise it stays lying on the page as a white
        // silhouette of a sticker that has already flown away.
        List<SpriteRenderer> companions = new List<SpriteRenderer>();
        foreach (Transform child in sr.transform)
        {
            SpriteRenderer cs = child.GetComponent<SpriteRenderer>();
            if (cs == null) continue;
            if (child.name == "Drop")
            {
                child.name = StickerPeel.PaperShadowPrefix + sr.name;
                log.AppendLine("  SHADOW " + sr.name + ": Drop -> " + child.name);
            }
            else if (child.name == "Rim") companions.Add(cs);
        }
        peel.companions = companions.ToArray();
        EditorUtility.SetDirty(peel);

        SpriteRenderer ghost = Find(scene, "Ghost_" + key);
        SpriteRenderer reward = Find(scene, "Reward_" + key);
        if (ghost == null || reward == null)
            Debug.LogError("[Case3PageEntries] no Ghost_/Reward_ pair for key " + key);

        log.AppendLine(string.Format("  ENTRY {0,-22} name=\"{1}\" card={2} order={3}", sr.name, name, key, sr.sortingOrder));

        return new Case3Director.Entry
        {
            key = key,
            displayName = name,
            peel = peel,
            sticker = sr,
            targetSlot = ghost,
            reward = reward,
            litMaterial = lit,
        };
    }

    /// <summary>
    /// The one ambiguous pair left on the board, made unambiguous.
    ///
    /// With the five retired items gone, thirteen of the fourteen remaining stickers were either
    /// plainly buried (13.72% .. 66.28%) or plainly clear (0.00%). PageObj_choc sat at 2.78%: the
    /// marshmallows just clipped its top edge. That is under the 5% threshold, so the chocolate is
    /// lit and tappable and the code is right - but a person looking at the page sees two stickers
    /// touching and one of them lit, which is exactly the "is this rule real?" reading the owner is
    /// objecting to. A rule has to be legible, not just true.
    ///
    /// Measured, lifting the marshmallows away from it:
    ///     dy 0.00 -> choc 2.78%   marshmallows 49.46%
    ///     dy 0.20 -> choc 0.86%   marshmallows 55.49%   &lt;- chosen
    ///     dy 0.35 -> choc 0.86%   marshmallows 60.77%
    /// 0.20 is the smallest move that separates them; past it the chocolate does not get any clearer
    /// and the marshmallows only sink further. It also widens the population gap the threshold lives
    /// in from 2.78..13.72 to 0.86..13.72, so 5% now has 5.8x clearance below and 2.7x above.
    /// </summary>
    static void SeparateChoc(Scene scene, StringBuilder log)
    {
        SpriteRenderer mar = Find(scene, "PageObj_marshmallows");
        if (mar == null) return;
        Vector3 authored = new Vector3(1.38f, -3.16f, 0f);
        Vector3 target = authored + new Vector3(0f, 0.20f, 0f);
        if ((mar.transform.position - target).sqrMagnitude < 0.0001f) return;
        log.AppendLine("  SEPARATE PageObj_marshmallows " + mar.transform.position + " -> " + target +
                       " (lifts PageObj_choc clear: 2.78% -> 0.86%)");
        mar.transform.position = target;
        EditorUtility.SetDirty(mar);
    }

    /// <summary>One card per key, each with an "n/5" label set into the card's bottom band.</summary>
    static Case3Director.StackCard[] BuildStacks(Scene scene, StringBuilder log)
    {
        string[] keys = CardKeys;
        List<Case3Director.StackCard> cards = new List<Case3Director.StackCard>();
        foreach (string key in keys)
        {
            SpriteRenderer reward = Find(scene, "Reward_" + key);
            if (reward == null) { Debug.LogError("[Case3PageEntries] no Reward_" + key); continue; }
            cards.Add(new Case3Director.StackCard { key = key, card = reward, counter = EnsureCounter(reward, log) });
        }
        return cards.ToArray();
    }

    static TextMeshPro EnsureCounter(SpriteRenderer card, StringBuilder log)
    {
        Transform existing = card.transform.Find("Counter");
        GameObject go = existing != null ? existing.gameObject : new GameObject("Counter");
        if (existing == null)
        {
            go.transform.SetParent(card.transform, false);
            go.AddComponent<TextMeshPro>();
        }
        TextMeshPro tmp = go.GetComponent<TextMeshPro>();

        // Sprite pixels -> the card's own local units. The sprite's pivot is its centre, y runs up.
        RectTransform rt = tmp.rectTransform;
        rt.sizeDelta = new Vector2(BandW / CardPixelsPerUnit, BandH / CardPixelsPerUnit);
        rt.localPosition = new Vector3((BandCentreX - CardW * 0.5f) / CardPixelsPerUnit,
                                       (CardH * 0.5f - BandCentreY) / CardPixelsPerUnit,
                                       -0.05f);
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        tmp.font = Shared.Sequencing.UIStyle.FontAsset;
        tmp.text = "0/5";
        // MEASURED off captured frames, twice, and the first reading was wrong in a way worth
        // recording: the ink mask for the glyphs also caught the cat's brown tail behind them,
        // so "1/5" measured 53 render px when the digits were 42.5. Isolating the rightmost
        // digit clear of the art gives 39 px at fontSize 5.48, against a panel 262 * 1.05 =
        // 275 render px tall, so 14.2% where the reference is 16.4%. 5.48 * 45.1/39 = 6.34.
        // RE-MEASURED after the panel changed under this number.
        //
        // 6.34 was solved against the OLD card, whose panel still carried the empty band at the
        // bottom. Removing that band moved the denominator, and the same fontSize then rendered the
        // digits at 25.7% of the panel instead of the reference's 16.4% - tall enough to overflow
        // their own 0.520-unit rect, which is what put "1/5" over the card's edge.
        //
        // Measured live through TMP's own textBounds rather than off pixels: at 6.34 the glyphs are
        // 0.708 world units against a 2.751-unit panel. 6.34 * (0.164 * 2.751) / 0.708 = 4.04.
        tmp.fontSize = 4.04f;
        tmp.enableAutoSizing = false;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = CounterInk;
        tmp.alignment = TextAlignmentOptions.Right;
        tmp.margin = Vector4.zero;
        // The reference's counter is a dark red with a white outline, because it is printed
        // over the art rather than into an empty band and has to stay legible on top of it.
        tmp.outlineColor = Color.white;
        tmp.outlineWidth = 0.22f;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.enabled = false;                     // nothing collected yet

        MeshRenderer mr = tmp.GetComponent<MeshRenderer>();
        mr.sortingLayerID = card.sortingLayerID;
        mr.sortingOrder = card.sortingOrder + 10;

        EditorUtility.SetDirty(tmp);
        log.AppendLine(string.Format("  COUNTER {0}: local {1} size {2} order {3}",
            card.name, rt.localPosition, rt.sizeDelta, mr.sortingOrder));
        return tmp;
    }

    /// <summary>
    /// The lit counterpart of the page's dim material. The dim shader's own note says lifting an
    /// object to foreground is _Value = 1, so this is that material and nothing else: same shader,
    /// same thresholds, the value multiply released. Anything more would make lit and dim differ in
    /// ways the reference's measured tone change does not.
    /// </summary>
    static Material EnsurePageLitMaterial(Material dim)
    {
        Material lit = AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath);
        if (lit == null)
        {
            lit = new Material(dim);
            lit.name = "Case3_PageObjectLit";
            AssetDatabase.CreateAsset(lit, LitMaterialPath);
        }
        lit.shader = dim.shader;
        lit.CopyPropertiesFromMaterial(dim);
        lit.SetFloat("_Value", 1f);
        EditorUtility.SetDirty(lit);
        AssetDatabase.SaveAssets();
        return lit;
    }

    static SpriteRenderer Find(Scene scene, string name)
    {
        foreach (SpriteRenderer sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (sr.name == name) return sr;
        return null;
    }
}

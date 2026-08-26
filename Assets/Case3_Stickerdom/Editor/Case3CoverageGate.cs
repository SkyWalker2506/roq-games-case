using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Case3;

/// <summary>
/// Proves the page tells the truth about what can be collected.
///
/// The reference's rule, read off the frames rather than assumed: a dim item that gets UNCOVERED
/// becomes a fully collectible sticker. The jar is uncovered at t=4.11 and is tapped and collected
/// at t=7.12; the change lands at PEEL COMPLETION, about 0.72 s before the sheet above it finishes
/// landing, in one native frame with no fade, and nothing ever reverts. Batching proves the trigger
/// is occlusion rather than a queue: the Cat lift promoted the two items under it, the Noodle lift
/// the two under it, and the Sweets lift promoted none.
///
/// The converse is FALSE and this gate does not assert it: the reference's page print is uncovered
/// and stays dim forever. So the invariant is one-directional, and it is the whole of what this gate
/// checks:
///
///     At any instant, an item covered by a drawn item above it must be dim and must not be
///     tappable; and any tappable item must be lit.
///
/// The gate is a black-box observer. It rasterises the overlap itself, from the sprite alpha and the
/// sorting orders, and then asks the director the only two questions that matter - PickSticker and
/// the renderer's material. It never asks the director what it thinks is covered, so a coverage bug
/// cannot cancel itself out.
///
/// RED BEFORE THE FIX, and this is not hypothetical: rasterised at 200 px/unit over all ten
/// above/below pairs on the authored page, Sticker_Cat (order 506) covers 13.7% of Sticker_Sweets
/// (order 505) and every other pair overlaps by 0.00%. Sticker_Sweets shipped lit and tappable while
/// covered. An AABB test had claimed nine pairs overlapped; alpha says one.
///
/// THAT "every other pair overlaps by 0.00%" WAS FALSE, and this gate could not see it. Fourteen of
/// the nineteen page sprites had Read/Write off; the alpha sampler returns -1 for an unreadable
/// texture, -1 fails the alpha test at every sample, and a sprite with no samples measures as having
/// no drawn area and therefore 0% coverage. The gate's CONTROL only proved that SOME pair overlapped,
/// which the one readable pair supplied, so the outage passed as a fact about the page. With
/// Read/Write on, seven page items are genuinely buried between 15.9% and 86.0%.
///
/// Two consequences, both carried here: the threshold moved to 5% (the middle of the real gap,
/// 1.92% .. 13.72%, instead of 4% clear of the highest non-overlap), and the gate now asserts the
/// director measured NOTHING blind - a coverage outage is no longer allowed to look like a
/// measurement of zero.
/// </summary>
[InitializeOnLoad]
public static class Case3CoverageGate
{
    const string KeyActive = "Case3CoverageGate.Active";
    const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";
    const double ReadyTimeout = 30.0;
    const double RunTimeout = 25.0;

    /// <summary>The gate's own threshold. Deliberately a separate constant from the director's.</summary>
    const float CoverThreshold = 0.05f;

    /// <summary>Gate-side sampling grid; a different resolution from the director's on purpose.</summary>
    const int Samples = 96;

    static bool _hooked;
    static bool _init;
    static int _phase;
    static int _failed;
    static int _checks;
    static double _stageStart;

    static int _tapIndex = -1;
    static int _victim = -1;
    static float _victimCoverAtRest;
    static bool _promoted;

    static Case3CoverageGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Zero-argument entry point for -executeMethod.</summary>
    public static void CoverageGate()
    {
        EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        SessionState.SetInt(KeyActive, 1);
        _init = false;
        Hook();
        Debug.Log("[Case3Coverage] GATE_START entering play mode");
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EditorApplication.update += Drive;
    }

    static void Drive()
    {
        if (SessionState.GetInt(KeyActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        Case3Director director = Object.FindFirstObjectByType<Case3Director>(FindObjectsInactive.Include);
        if (director == null || director.Count == 0)
        {
            Finish("no Case3Director (or no wired stickers) in the play-mode scene", 2);
            return;
        }

        if (!_init)
        {
            _init = true;
            _phase = 0;
            _failed = 0;
            _checks = 0;
            _promoted = false;
            _stageStart = EditorApplication.timeSinceStartup;
            director.AllowPlayWithoutInput();
        }

        double now = EditorApplication.timeSinceStartup;

        switch (_phase)
        {
            case 0:
            {
                if (!director.Ready && now - _stageStart < ReadyTimeout) break;

                // ---- the page at rest, every sheet still on it
                Report(director, "AT_REST");
                CheckInvariant(director, "AT_REST");

                // ---- the instrument must not be blind. A sprite whose texture cannot be read
                // measures 0% coverage no matter how deeply it is buried, and 0% reads as "light it
                // up and let the player tap it". That outage is what produced the previous, wrong
                // picture of this page, so it is a hard failure now rather than a silent zero.
                if (director.CoverageBlindCount > 0)
                {
                    Fail("INSTRUMENT", director.CoverageBlindCount + " entr(y/ies) have an unreadable sprite " +
                         "texture, so their coverage is not measured but reported as 0%");
                    Finish(null, 1);
                    return;
                }

                // ---- control: the instrument must actually find the overlap it exists to police.
                // If every pair measured 0, "covered items are dim" would pass on a page with no
                // covered items and prove nothing at all.
                _victim = -1;
                _victimCoverAtRest = 0f;
                for (int i = 0; i < director.Count; i++)
                {
                    float c = GateCoverage(director, i);
                    if (c > _victimCoverAtRest) { _victimCoverAtRest = c; _victim = i; }
                }
                if (_victim < 0 || _victimCoverAtRest < CoverThreshold)
                {
                    Fail("CONTROL", "no sticker on this page is covered by another (worst overlap " +
                         (_victimCoverAtRest * 100f).ToString("0.00") + "%), so every covered-item " +
                         "assertion in this gate is vacuous");
                    Finish(null, 1);
                    return;
                }
                Debug.Log(string.Format(
                    "[Case3Coverage] CONTROL ok: {0} is covered {1:0.00}% by a sheet above it, so the " +
                    "covered-item assertions have something to bite on",
                    director.entries[_victim].sticker.name, _victimCoverAtRest * 100f));

                // ---- second control: the gate's overlap measurement must not be an artefact of
                // bounding boxes. The pair it found has to survive an alpha test, and the pairs it
                // found nothing for have to stay at zero.
                Debug.Log("[Case3Coverage] CONTROL alpha-vs-box: " + BoxVsAlpha(director));

                // ---- pick the sheet that is DRAWN OVER the victim and lift it
                _tapIndex = CoveringOf(director, _victim);
                if (_tapIndex < 0)
                {
                    Fail("CONTROL", "the covered sticker has no covering sticker; the measurement is inconsistent");
                    Finish(null, 1);
                    return;
                }

                if (!director.PlaySelected(_tapIndex))
                {
                    Fail(director.entries[_tapIndex].sticker.name, "PlaySelected refused to start");
                    Finish(null, 1);
                    return;
                }
                Debug.Log("[Case3Coverage] lifting " + director.entries[_tapIndex].sticker.name +
                          ", which covers " + director.entries[_victim].sticker.name);
                _stageStart = now;
                _phase = 1;
                break;
            }

            case 1:
            {
                // Every frame of the run: the invariant must hold throughout, not just at the ends.
                CheckInvariant(director, "DURING");

                if (!_promoted && GateCoverage(director, _victim) < CoverThreshold &&
                    !IsDim(director, _victim) && director.Playable(_victim))
                {
                    _promoted = true;
                    Debug.Log(string.Format(
                        "[Case3Coverage] PROMOTED {0} at t+{1:0.000} s of the run: uncovered, lit and tappable",
                        director.entries[_victim].sticker.name, now - _stageStart));
                }

                if (director.IsPlaying && now - _stageStart < RunTimeout) break;
                if (director.IsPlaying)
                {
                    Fail(director.entries[_tapIndex].sticker.name, "sequence timed out");
                    Finish(null, 1);
                    return;
                }

                Report(director, "AFTER_LIFT");
                CheckInvariant(director, "AFTER_LIFT");

                // ---- the promotion itself: the sheet came off, so what was under it must now be
                // uncovered, lit and collectible. Nothing reverts.
                float after = GateCoverage(director, _victim);
                if (after >= CoverThreshold)
                    Fail(director.entries[_victim].sticker.name,
                         "still covered " + (after * 100f).ToString("0.00") + "% after the sheet above it was lifted");
                else if (!_promoted)
                    Fail(director.entries[_victim].sticker.name,
                         "was uncovered by the lift but never became a lit, tappable sticker");

                Finish(null, _failed == 0 ? 0 : 1);
                return;
            }
        }
    }

    // ------------------------------------------------------------------ the invariant

    /// <summary>
    /// One sentence, checked in both directions it actually claims:
    ///   covered  -> dim AND not tappable
    ///   tappable -> lit
    /// The converse (uncovered -> lit) is NOT asserted; the reference's page print refutes it.
    /// </summary>
    static void CheckInvariant(Case3Director director, string where)
    {
        Camera cam = director.sceneCamera != null ? director.sceneCamera : Camera.main;
        if (cam == null) { Fail(where, "no camera"); return; }

        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry e = director.entries[i];
            if (e == null || e.sticker == null || e.Consumed) continue;
            // A sheet in mesh mode is not drawn as a sprite at all; it is neither covering nor covered.
            if (!e.sticker.enabled) continue;

            _checks++;
            float cover = GateCoverage(director, i);
            bool dim = IsDim(director, i);

            if (cover >= CoverThreshold)
            {
                if (!dim)
                    Fail(e.sticker.name, where + ": covered " + (cover * 100f).ToString("0.00") +
                         "% by a sheet above it but drawn lit");

                Vector3 art = GateArtPoint(director, i);
                int picked = director.PickSticker(cam.WorldToScreenPoint(art));
                if (picked == i)
                    Fail(e.sticker.name, where + ": covered " + (cover * 100f).ToString("0.00") +
                         "% but a tap at " + art + " still peels it");
            }
            else
            {
                // tappable -> lit. Asked of the director, answered by the renderer.
                if (director.Playable(i) && dim)
                    Fail(e.sticker.name, where + ": tappable but drawn dim");
            }
        }
    }

    // ------------------------------------------------------------------ the gate's own measurements

    /// <summary>
    /// Fraction of sticker i's own drawn area that a sticker with a HIGHER sorting order also draws
    /// opaquely. Computed here, from the sprites, at a different grid resolution from the director's.
    /// </summary>
    static float GateCoverage(Case3Director director, int index)
    {
        if (index < 0 || index >= director.Count) return 0f;
        Case3Director.Entry e = director.entries[index];
        if (e == null || e.sticker == null || e.Consumed || !e.sticker.enabled) return 0f;

        Bounds b = e.sticker.bounds;
        int drawn = 0, hidden = 0;
        for (int yi = 0; yi < Samples; yi++)
        {
            float fy = (yi + 0.5f) / Samples;
            for (int xi = 0; xi < Samples; xi++)
            {
                float fx = (xi + 0.5f) / Samples;
                Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx),
                                        Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                if (Alpha(e.sticker, p) < Case3Director.TapAlphaThreshold) continue;
                drawn++;
                if (TopSpriteAt(director, p) != index) hidden++;
            }
        }
        return drawn == 0 ? 0f : (float)hidden / drawn;
    }

    /// <summary>Index of the sticker drawn on top at a world point, or -1. The gate's own answer.</summary>
    static int TopSpriteAt(Case3Director director, Vector3 world)
    {
        int hit = -1;
        int best = int.MinValue;
        for (int j = 0; j < director.Count; j++)
        {
            Case3Director.Entry o = director.entries[j];
            if (o == null || o.sticker == null || o.Consumed || !o.sticker.enabled) continue;
            if (Alpha(o.sticker, world) < Case3Director.TapAlphaThreshold) continue;
            if (o.sticker.sortingOrder > best) { best = o.sticker.sortingOrder; hit = j; }
        }
        return hit;
    }

    /// <summary>The sticker drawn immediately over the given one, or -1.</summary>
    static int CoveringOf(Case3Director director, int index)
    {
        Case3Director.Entry e = director.entries[index];
        Bounds b = e.sticker.bounds;
        int found = -1;
        int bestOrder = int.MinValue;
        for (int yi = 0; yi < Samples; yi++)
        {
            float fy = (yi + 0.5f) / Samples;
            for (int xi = 0; xi < Samples; xi++)
            {
                float fx = (xi + 0.5f) / Samples;
                Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx),
                                        Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                if (Alpha(e.sticker, p) < Case3Director.TapAlphaThreshold) continue;
                int top = TopSpriteAt(director, p);
                if (top < 0 || top == index) continue;
                int order = director.entries[top].sticker.sortingOrder;
                if (order > bestOrder) { bestOrder = order; found = top; }
            }
        }
        return found;
    }

    /// <summary>A world point where this sticker is genuinely drawn and not hidden by anything.</summary>
    static Vector3 GateArtPoint(Case3Director director, int index)
    {
        Case3Director.Entry e = director.entries[index];
        Bounds b = e.sticker.bounds;
        for (int yi = 0; yi < Samples; yi++)
        {
            float fy = (yi + 0.5f) / Samples;
            for (int xi = 0; xi < Samples; xi++)
            {
                float fx = (xi + 0.5f) / Samples;
                Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx),
                                        Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                if (Alpha(e.sticker, p) < Case3Director.TapAlphaThreshold) continue;
                if (TopSpriteAt(director, p) == index) return p;
            }
        }
        return b.center;
    }

    /// <summary>
    /// The gate's own alpha sampler, derived through the sprite's local bounds and rect rather than
    /// through pixelsPerUnit and the pivot, so the gate is not asking the code under test whether it
    /// agrees with itself. Same rule as Case3SelectionGate's.
    /// </summary>
    static float Alpha(SpriteRenderer sr, Vector3 world)
    {
        if (sr == null || sr.sprite == null) return 0f;
        Sprite sp = sr.sprite;
        Texture2D tex = sp.texture;
        if (tex == null || !tex.isReadable) return 0f;

        Vector3 local = sr.transform.InverseTransformPoint(world);
        Bounds lb = sp.bounds;
        float u = Mathf.InverseLerp(lb.min.x, lb.max.x, local.x);
        float v = Mathf.InverseLerp(lb.min.y, lb.max.y, local.y);
        if (u <= 0f || u >= 1f || v <= 0f || v >= 1f) return 0f;

        Rect r = sp.rect;
        int x = Mathf.Clamp(Mathf.FloorToInt(r.x + u * r.width), (int)r.x, (int)r.xMax - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(r.y + v * r.height), (int)r.y, (int)r.yMax - 1);
        return tex.GetPixel(x, y).a;
    }

    /// <summary>Is this sticker wearing the page's dim material? Read off the renderer, not asked.</summary>
    static bool IsDim(Case3Director director, int index)
    {
        Case3Director.Entry e = director.entries[index];
        if (e == null || e.sticker == null) return false;
        Material dim = director.dimMaterial;
        if (dim == null) return false;
        Material m = e.sticker.sharedMaterial;
        return m != null && (m == dim || m.name.StartsWith(dim.name));
    }

    /// <summary>
    /// How many pairs a bounding-box test claims overlap, against how many actually do. The box test
    /// claimed nine on this page; alpha says one. Logged so a future reader can see that the 2%
    /// threshold sits in a real gap rather than in the middle of a smear of small overlaps.
    /// </summary>
    static string BoxVsAlpha(Case3Director director)
    {
        int boxPairs = 0, alphaPairs = 0;
        float worstBelow = 0f, bestAbove = 1f;
        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry lo = director.entries[i];
            if (lo == null || lo.sticker == null) continue;
            for (int j = 0; j < director.Count; j++)
            {
                Case3Director.Entry hi = director.entries[j];
                if (i == j || hi == null || hi.sticker == null) continue;
                if (hi.sticker.sortingOrder <= lo.sticker.sortingOrder) continue;
                if (lo.sticker.bounds.Intersects(hi.sticker.bounds)) boxPairs++;
            }
            float c = GateCoverage(director, i);
            if (c >= CoverThreshold) { alphaPairs++; if (c < bestAbove) bestAbove = c; }
            else if (c > worstBelow) worstBelow = c;
        }
        return string.Format("box test claims {0} overlapping pair(s), alpha finds {1} covered sticker(s); " +
                             "gap runs {2:0.00}% .. {3:0.00}% around the {4:0.00}% threshold",
                             boxPairs, alphaPairs, worstBelow * 100f,
                             alphaPairs > 0 ? bestAbove * 100f : 100f, CoverThreshold * 100f);
    }

    static void Report(Case3Director director, string where)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Case3Coverage] ---- " + where + " ----");
        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry e = director.entries[i];
            if (e == null || e.sticker == null) continue;
            sb.AppendLine(string.Format(
                "  {0,-16} order={1} drawn={2} covered={3:0.00}%  material={4}  playable={5} consumed={6}",
                e.sticker.name, e.sticker.sortingOrder, e.sticker.enabled,
                GateCoverage(director, i) * 100f,
                e.sticker.sharedMaterial != null ? e.sticker.sharedMaterial.name : "<none>",
                director.Playable(i), e.Consumed));
        }
        Debug.Log(sb.ToString());
    }

    static void Fail(string who, string reason)
    {
        _failed++;
        Debug.LogError("[Case3Coverage] FAIL " + who + ": " + reason);
    }

    static void Finish(string fatal, int exitCode)
    {
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(fatal)) sb.AppendLine("[Case3Coverage] FATAL " + fatal);
        sb.AppendLine(string.Format("[Case3Coverage] COVERAGE_GATE {0} checks={1} failed={2}",
            exitCode == 0 ? "GREEN" : "RED", _checks, _failed));
        Debug.Log(sb.ToString());
        System.Console.WriteLine(sb.ToString());

        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else EditorApplication.isPlaying = false;
    }
}

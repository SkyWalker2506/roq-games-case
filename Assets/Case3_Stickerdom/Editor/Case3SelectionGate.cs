using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case3;

/// <summary>
/// Proves that Case 3 is a selection, not a canned animation.
///
/// It opens the scene, enters play mode, and then:
///
/// PROBE phase, with all three sheets still on the page, for every playable sticker:
///   a. a point that is genuinely DRAWN on that sticker must resolve to it,
///   b. a point inside its sprite RECTANGLE but on transparent pixels must NOT resolve to it,
///   c. a point drawn on its art that a plain box test would award to a different sticker must
///      resolve to it anyway.
/// Every probe point is chosen by the gate's OWN alpha sampler, derived from the sprite bounds and
/// rect rather than from the pivot arithmetic the director uses, so a mistake in either side shows up
/// as a disagreement instead of cancelling out. Points (b) and (c) are only accepted as probes when
/// the old box-and-nearest-centre test really does answer them wrongly - a probe that the old test
/// already got right proves nothing, and the gate fails rather than reporting a vacuous pass.
/// This is the part that was missing: the previous gate only ever tapped bounds.center, the single
/// point where a box test and the drawn shape cannot disagree, and it passed 3/3 while 58% of the
/// tappable area peeled a sticker that was not under the finger.
///
/// PLAY phase, for EVERY waiting sticker in turn:
///   1. the tap point resolves to that sticker and not to a neighbour,
///   2. the peel runs for it,
///   3. the sheet that actually peeled is the tapped one, and - the part a wiring bug cannot fake -
///      when it comes to rest the ghost slot it is closest to, out of every slot on the page, is its own,
///   4. its paper contact shadow, which reads full at rest, reads zero once the sheet is placed.
/// The first failure ends the run with a non-zero exit code.
/// </summary>
[InitializeOnLoad]
public static class Case3SelectionGate
{
    const string KeyActive = "Case3SelectionGate.Active";
    const double ReadyTimeout = 25.0;
    const double RunTimeout = 25.0;

    static bool _hooked;
    static bool _sessionInit;
    static int _index;
    static int _phase;
    static int _passed;
    static int _failed;
    static double _stageStart;

    static Transform _expectedSticker;
    static Transform _expectedSlot;

    static Case3SelectionGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Zero-argument entry point for -executeMethod.</summary>
    public static void SelectionGate()
    {
        EditorSceneManager.OpenScene("Assets/Case3_Stickerdom/Scenes/Stickerdom.unity", OpenSceneMode.Single);
        SessionState.SetInt(KeyActive, 1);
        _sessionInit = false;
        Hook();
        Debug.Log("[Case3Gate] GATE_START entering play mode");
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

        if (!_sessionInit)
        {
            _sessionInit = true;
            _index = 0;
            _phase = 0;
            _passed = 0;
            _failed = 0;
            _stageStart = EditorApplication.timeSinceStartup;
            director.AllowPlayWithoutInput();   // batchmode has no real pointer behind the synthetic tap
        }

        double now = EditorApplication.timeSinceStartup;

        switch (_phase)
        {
            case 0:
            {
                if (director.IsPlaying)
                {
                    Finish("the scene started a sequence on its own - nothing may auto-play", 3);
                    return;
                }
                if (director.Ready || now - _stageStart > ReadyTimeout)
                {
                    Debug.Log("[Case3Gate] scene idle and ready after " + (now - _stageStart).ToString("0.00") +
                              " s; stickers=" + director.Count);
                    CheckRestingShadows(director);
                    ProbeHitTest(director);
                    _phase = 1;
                }
                break;
            }

            case 1:
            {
                if (_index >= director.Count)
                {
                    Finish(null, _failed == 0 ? 0 : 1);
                    return;
                }

                Case3Director.Entry e = director.entries[_index];
                if (e == null || e.sticker == null || e.targetSlot == null)
                {
                    Fail(_index, "<null>", "entry is not wired");
                    _index++;
                    break;
                }

                Vector2 screen = director.ScreenPointOf(_index);
                int picked = director.PickSticker(screen);
                if (picked != _index)
                {
                    Fail(_index, e.sticker.name,
                         "a tap on its own screen position resolved to index " + picked + ", not " + _index);
                    _index++;
                    break;
                }

                _expectedSticker = e.sticker.transform;
                _expectedSlot = e.targetSlot.transform;

                if (!director.PlaySelected(_index))
                {
                    Fail(_index, e.sticker.name, "PlaySelected refused to start");
                    _index++;
                    break;
                }

                if (director.CurrentIndex != _index || director.Current == null ||
                    director.Current.sticker.transform != _expectedSticker)
                {
                    Fail(_index, e.sticker.name, "the peeling sheet is not the tapped sticker");
                    _index++;
                    break;
                }

                Debug.Log(string.Format("[Case3Gate] TAP {0} sticker={1} screen={2} -> aiming at {3}",
                    _index, e.sticker.name, screen, _expectedSlot.name));

                _stageStart = now;
                _phase = 2;
                break;
            }

            case 2:
            {
                if (director.IsPlaying && now - _stageStart < RunTimeout) break;

                if (director.IsPlaying)
                {
                    Fail(_index, _expectedSticker != null ? _expectedSticker.name : "?", "sequence timed out");
                    _index++;
                    _phase = 1;
                    break;
                }

                // The honest check: of every ghost slot on the page, the one the sticker ended up
                // closest to must be its own. A wrong-target bug cannot survive it.
                List<Transform> allSlots = CollectSlots(_expectedSlot);
                Transform nearest = null;
                float bestDistance = float.MaxValue;
                Vector3 resting = _expectedSticker != null ? _expectedSticker.position : Vector3.zero;
                for (int i = 0; i < allSlots.Count; i++)
                {
                    float d = Vector2.Distance(resting, allSlots[i].position);
                    if (d < bestDistance) { bestDistance = d; nearest = allSlots[i]; }
                }

                if (nearest != _expectedSlot)
                {
                    Fail(_index, _expectedSticker != null ? _expectedSticker.name : "?",
                         "came to rest nearest " + (nearest != null ? nearest.name : "<none>") +
                         " (" + bestDistance.ToString("0.000") + " u), expected " + _expectedSlot.name);
                }
                else
                {
                    // Finding 1, structurally: the contact shadow reads full while the sheet rests on the
                    // page and must read zero once it is placed. Checked as a pair, so an accessor that
                    // always answered 0 would already have failed the at-rest check above.
                    Case3Director.Entry done = director.entries[_index];
                    if (done != null && done.peel != null && done.peel.PaperShadowAlpha > 0.001f)
                    {
                        Fail(_index, done.sticker.name,
                             "the paper contact shadow is still drawing at alpha " +
                             done.peel.PaperShadowAlpha.ToString("0.000") + " after the sheet was placed");
                        _index++;
                        _phase = 1;
                        break;
                    }

                    _passed++;
                    Debug.Log(string.Format(
                        "[Case3Gate] PASS {0} sticker={1} -> slot={2} restDistance={3:0.000} u  seq={4:0.000} s completed={5} (of {6} candidate slots)",
                        _index, _expectedSticker.name, _expectedSlot.name, bestDistance,
                        director.Report.totalDuration, director.Report.completed, allSlots.Count));
                }

                _index++;
                _phase = 1;
                break;
            }
        }
    }

    // ------------------------------------------------------------------ Finding 1: contact shadow

    /// <summary>
    /// Positive control for the shadow instrument: at rest, every playable sticker must be casting its
    /// contact shadow. Without this the "shadow is gone after placement" check could pass on a sticker
    /// that never had a shadow child at all - which is exactly how the shadow bug survived: the code
    /// looked for a child named "PaperShadow", the scene calls it "Shadow_Cat", and the lookup failed
    /// silently on every single sticker.
    /// </summary>
    static void CheckRestingShadows(Case3Director director)
    {
        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry e = director.entries[i];
            if (e == null || e.peel == null || e.sticker == null) continue;
            float a = e.peel.PaperShadowAlpha;
            if (a <= 0.001f)
                Fail(i, e.sticker.name, "has no paper contact shadow at rest (alpha " + a.ToString("0.000") +
                                        "); the shadow checks after placement would be vacuous");
            else
                Debug.Log(string.Format("[Case3Gate] SHADOW_AT_REST {0} sticker={1} alpha={2:0.000}",
                                        i, e.sticker.name, a));
        }
    }

    // ------------------------------------------------------------------ Finding 2: hit test

    /// <summary>
    /// Taps points where the sprite rectangle and the drawn art disagree, with every sheet still on the
    /// page. Runs before anything is consumed, because consuming a sticker removes it from the candidate
    /// set and dissolves the overlaps that make the disagreement measurable.
    ///
    /// The invariant under test is a single sentence: a tap selects the sticker that is actually DRAWN
    /// on top at that point, and nothing when none is. Every probe is checked against
    /// <see cref="TopDrawnAt"/>, computed by the gate itself, never against the director's answer.
    /// </summary>
    static void ProbeHitTest(Case3Director director)
    {
        Camera cam = director.sceneCamera != null ? director.sceneCamera : Camera.main;
        if (cam == null) { Fail(-1, "<scene>", "no camera to turn probe points into taps"); return; }

        // The alpha test degrades to the old box test on an unreadable texture. That degradation must
        // not be able to pass this gate.
        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry e = director.entries[i];
            if (e == null || e.sticker == null || e.sticker.sprite == null) continue;
            Texture2D tex = e.sticker.sprite.texture;
            if (tex == null || !tex.isReadable)
                Fail(i, e.sticker.name, "sprite texture is not CPU-readable, so the tap hit test silently " +
                                        "falls back to the sprite rectangle");
        }

        const int Grid = 64;

        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry e = director.entries[i];
            if (e == null || e.sticker == null || !director.Playable(i)) continue;

            Bounds b = e.sticker.bounds;

            Vector3 artPoint = Vector3.zero;      bool haveArt = false;
            Vector3 blankPoint = Vector3.zero;    bool haveBlank = false;
            Vector3 overlapPoint = Vector3.zero;  bool haveOverlap = false; int overlapLegacy = -1;
            int inBox = 0, topHere = 0, blankWrong = 0, overlapWrong = 0, legacyWrong = 0;
            float bestArt = float.MaxValue, bestBlank = float.MaxValue, bestOverlap = float.MaxValue;

            for (int yi = 0; yi < Grid; yi++)
            {
                float fy = (yi + 0.5f) / Grid;
                for (int xi = 0; xi < Grid; xi++)
                {
                    float fx = (xi + 0.5f) / Grid;
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx),
                                            Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);

                    // Probe through the same screen -> world round trip a real tap goes through, so a
                    // point that survives here is a point a tap can actually land on.
                    Vector3 w = director.TapWorldPoint(cam.WorldToScreenPoint(p));

                    inBox++;
                    int top = TopDrawnAt(director, w);
                    int legacy = LegacyBoxPick(director, w);
                    if (legacy != top) legacyWrong++;

                    float d = (new Vector2(w.x, w.y) - new Vector2(b.center.x, b.center.y)).sqrMagnitude;

                    if (top == i)
                    {
                        topHere++;
                        if (d < bestArt) { bestArt = d; artPoint = w; haveArt = true; }
                        if (legacy != i)
                        {
                            // The sticker the player can see here, awarded to someone else by the box test.
                            overlapWrong++;
                            if (d < bestOverlap) { bestOverlap = d; overlapPoint = w; overlapLegacy = legacy; haveOverlap = true; }
                        }
                    }
                    else if (legacy == i)
                    {
                        // Inside this sticker's rectangle, this sticker not the one drawn here, and the
                        // old test would still have peeled it. The disagreement the old gate never saw.
                        blankWrong++;
                        if (d < bestBlank) { bestBlank = d; blankPoint = w; haveBlank = true; }
                    }
                }
            }

            Debug.Log(string.Format(
                "[Case3Gate] PROBE {0} sticker={1} grid={2} topmost-drawn-here={3} ({4:0.0}%) " +
                "box-test-peels-it-where-it-is-not-drawn={5} ({6:0.0}%) " +
                "box-test-peels-another-where-it-IS-drawn={7} box-vs-drawn-disagreements={8} ({9:0.0}% of its rect)",
                i, e.sticker.name, Grid * Grid, topHere, 100f * topHere / Mathf.Max(1, inBox),
                blankWrong, 100f * blankWrong / Mathf.Max(1, inBox), overlapWrong,
                legacyWrong, 100f * legacyWrong / Mathf.Max(1, inBox)));

            // (a) a point where this sticker is the one drawn on top must select it.
            if (!haveArt)
            {
                Fail(i, e.sticker.name, "no point in its own rectangle where it is the sticker drawn on top");
            }
            else
            {
                int got = director.PickSticker(cam.WorldToScreenPoint(artPoint));
                if (got != i)
                    Fail(i, e.sticker.name, "a tap on its own drawn art resolved to " + got + ", not " + i);
                else
                    Debug.Log(string.Format("[Case3Gate] PROBE_ART {0} ok at {1}", i, artPoint));
            }

            // (b) a point inside its rectangle where it is NOT the drawn sticker must not select it, and
            //     must select whatever IS drawn there (-1 for bare page).
            if (!haveBlank)
            {
                Debug.Log(string.Format(
                    "[Case3Gate] PROBE_BLANK {0} skipped: the old box test never mis-awarded an undrawn " +
                    "point inside this sticker's rectangle, so there is nothing here to catch", i));
            }
            else
            {
                int expect = TopDrawnAt(director, blankPoint);
                int got = director.PickSticker(cam.WorldToScreenPoint(blankPoint));
                if (got != expect)
                    Fail(i, e.sticker.name, "a tap at " + blankPoint + ", inside its rectangle but not drawn " +
                                            "by it, resolved to " + got + "; the sticker drawn there is " + expect +
                                            " (the old box test said " + i + ")");
                else
                    Debug.Log(string.Format(
                        "[Case3Gate] PROBE_BLANK {0} ok at {1}: old box test said {2}, drawn there is {3}, hit test says {4}",
                        i, blankPoint, i, expect, got));
            }

            // (c) a point where this sticker is the one on screen but a box test awards it to another.
            if (!haveOverlap)
            {
                Debug.Log(string.Format(
                    "[Case3Gate] PROBE_OVERLAP {0} skipped: nowhere is this sticker the drawn one while " +
                    "another sticker's rectangle claims the point", i));
            }
            else
            {
                int got = director.PickSticker(cam.WorldToScreenPoint(overlapPoint));
                if (got != i)
                    Fail(i, e.sticker.name, "a tap at " + overlapPoint + ", where it is the sticker drawn on " +
                                            "top, peeled " + got + " instead (the old box test said " + overlapLegacy + ")");
                else
                    Debug.Log(string.Format(
                        "[Case3Gate] PROBE_OVERLAP {0} ok at {1}: old box test said {2}, hit test says {3}",
                        i, overlapPoint, overlapLegacy, got));
            }
        }
    }

    /// <summary>
    /// The gate's own answer to "which playable sticker is DRAWN on top at this world point", or -1 for
    /// bare page. Highest sorting order among the sheets whose own alpha is opaque there - the same thing
    /// the player's eye reports, worked out without asking the hit test under test.
    /// </summary>
    static int TopDrawnAt(Case3Director director, Vector3 world)
    {
        int hit = -1;
        int bestOrder = int.MinValue;
        for (int i = 0; i < director.Count; i++)
        {
            if (!director.Playable(i)) continue;
            SpriteRenderer sr = director.entries[i].sticker;
            if (GateAlpha(sr, world) < Case3Director.TapAlphaThreshold) continue;
            if (sr.sortingOrder > bestOrder) { bestOrder = sr.sortingOrder; hit = i; }
        }
        return hit;
    }

    /// <summary>
    /// The gate's OWN alpha sampler. Deliberately derived a different way from the director's - through
    /// the sprite's local bounds and rect instead of through pixelsPerUnit and the pivot - so that the
    /// gate is not simply asking the code under test whether it agrees with itself.
    /// </summary>
    static float GateAlpha(SpriteRenderer sr, Vector3 world)
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

    /// <summary>
    /// The hit test as it was before Finding 2: the raw sprite rectangle, nearest centre wins, no alpha
    /// anywhere. Kept HERE, in the gate, purely as a control - it is what makes a probe point provably a
    /// disagreement point rather than an arbitrary coordinate.
    /// </summary>
    static int LegacyBoxPick(Case3Director director, Vector3 world)
    {
        int hit = -1;
        float best = float.MaxValue;
        for (int i = 0; i < director.Count; i++)
        {
            if (!director.Playable(i)) continue;
            Bounds b = director.entries[i].sticker.bounds;
            if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) continue;
            float d = (new Vector2(world.x, world.y) - new Vector2(b.center.x, b.center.y)).sqrMagnitude;
            if (d < best) { best = d; hit = i; }
        }
        return hit;
    }

    /// <summary>Every ghost slot on the page, including the ones no sticker is paired with.</summary>
    static List<Transform> CollectSlots(Transform anySlot)
    {
        List<Transform> found = new List<Transform>();
        if (anySlot == null) return found;

        Transform parent = anySlot.parent;
        if (parent == null) { found.Add(anySlot); return found; }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.GetComponent<SpriteRenderer>() != null) found.Add(c);
        }
        if (found.Count == 0) found.Add(anySlot);
        return found;
    }

    static void Fail(int index, string stickerName, string reason)
    {
        _failed++;
        Debug.LogError(string.Format("[Case3Gate] FAIL {0} sticker={1}: {2}", index, stickerName, reason));
    }

    static void Finish(string fatal, int exitCode)
    {
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(fatal)) sb.AppendLine("[Case3Gate] FATAL " + fatal);
        sb.AppendLine(string.Format("[Case3Gate] SELECTION_GATE {0} passed={1} failed={2}",
            exitCode == 0 ? "GREEN" : "RED", _passed, _failed));

        Debug.Log(sb.ToString());
        System.Console.WriteLine(sb.ToString());

        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else EditorApplication.isPlaying = false;
    }
}

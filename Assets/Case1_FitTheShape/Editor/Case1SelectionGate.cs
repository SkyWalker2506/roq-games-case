using System.Text;
using UnityEditor;
using UnityEngine;
using Case1;

/// <summary>
/// Proves that Case 1 is a selection, not a canned animation.
///
/// It builds the scene, enters play mode, and then for EVERY deck shape in turn:
///   1. works out where that shape is on screen and feeds that point to <see cref="ShapeTapInput"/>,
///      checking the tap resolves to that shape and not to a neighbour,
///   2. runs the sequence for it,
///   3. checks the object that actually moved is the tapped one, that it aimed at the cell the setup
///      matched to it, and - the part a wiring bug cannot fake - that when it comes to rest the cell
///      whose hole it is closest to, out of all 75 cells on the drum, is that same cell.
/// The first failure ends the run with a non-zero exit code.
/// </summary>
[InitializeOnLoad]
public static class Case1SelectionGate
{
    const string KeyActive = "Case1SelectionGate.Active";
    const double ReadyTimeout = 20.0;
    const double RunTimeout = 25.0;

    static bool _hooked;
    static bool _sessionInit;
    static int _index;
    static int _phase;
    static int _passed;
    static int _failed;
    static double _stageStart;

    static Transform _expectedShape;
    static int _expectedCell;

    static Case1SelectionGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Zero-argument entry point for -executeMethod.</summary>
    public static void SelectionGate()
    {
        Case1SceneSetup.Build();
        SessionState.SetInt(KeyActive, 1);
        _sessionInit = false;
        Hook();
        Debug.Log("[Case1Gate] GATE_START entering play mode");
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

        Case1Director director = Object.FindFirstObjectByType<Case1Director>(FindObjectsInactive.Include);
        ShapeTapInput tap = Object.FindFirstObjectByType<ShapeTapInput>(FindObjectsInactive.Include);
        if (director == null || tap == null || director.flight == null || director.drum == null)
        {
            Finish("no Case1Director / ShapeTapInput / wiring in the play-mode scene", 2);
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

        ShapeArcFlight flight = director.flight;
        DrumSlotReaction drum = director.drum;
        double now = EditorApplication.timeSinceStartup;

        switch (_phase)
        {
            case 0:   // wait for the warm-up gate; nothing must be playing yet
            {
                if (director.IsPlaying)
                {
                    Finish("the scene started a sequence on its own - nothing may auto-play", 3);
                    return;
                }
                if (director.Ready || now - _stageStart > ReadyTimeout)
                {
                    Debug.Log("[Case1Gate] scene idle and ready after " + (now - _stageStart).ToString("0.00") +
                              " s; shapes=" + flight.Count);
                    _phase = 1;
                }
                break;
            }

            case 1:   // start the next selection
            {
                if (_index >= flight.Count)
                {
                    Finish(null, _failed == 0 ? 0 : 1);
                    return;
                }

                ShapeArcFlight.Entry e = flight.entries[_index];
                if (e == null || e.shape == null)
                {
                    Fail(_index, "<null>", "entry is empty");
                    _index++;
                    break;
                }

                if (e.targetCell < 0)
                {
                    Fail(_index, e.shape.name, "no matching drum cell was found for this shape");
                    _index++;
                    break;
                }

                // A cell that is already full is SUPPOSED to reject further pieces, and several tiles
                // of the same shape all aim at the one recess that fits them. So once this entry's
                // cell has been filled by an earlier tap, the contract under test flips: the tap must
                // resolve to NOTHING. Asserting "every entry is tappable" would have demanded that a
                // second square fly into a hole that is already closed.
                if (drum.IsFilled(e.targetCell))
                {
                    Vector2 closedScreen = tap.ScreenPointOf(_index);
                    int closedPick = tap.PickShape(closedScreen);
                    if (closedPick == _index)
                    {
                        Fail(_index, e.shape.name, "its cell " + drum.CellName(e.targetCell) +
                             " is already filled, but the tap still resolved to this piece");
                    }
                    else
                    {
                        _passed++;
                        Debug.Log(string.Format("[Case1Gate] CLOSED {0} shape={1} -> {2} is filled, tap correctly ignored",
                            _index, e.shape.name, drum.CellName(e.targetCell)));
                    }
                    _index++;
                    break;
                }

                // 1) the tap has to resolve to THIS shape
                Vector2 screen = tap.ScreenPointOf(_index);
                int picked = tap.PickShape(screen);
                if (picked != _index)
                {
                    Fail(_index, e.shape.name,
                         "a tap on its own screen position resolved to index " + picked + ", not " + _index);
                    _index++;
                    break;
                }

                _expectedShape = e.shape;
                _expectedCell = e.targetCell;

                if (!director.PlaySelected(_index))
                {
                    Fail(_index, e.shape.name, "PlaySelected refused to start");
                    _index++;
                    break;
                }

                // 2) the object that is about to move has to be the tapped one, aimed at its own cell
                if (flight.CurrentShape != _expectedShape)
                {
                    Fail(_index, e.shape.name, "the flying object is " +
                         (flight.CurrentShape != null ? flight.CurrentShape.name : "<null>") + ", not the tapped shape");
                    _index++;
                    _phase = 1;
                    break;
                }
                if (flight.TargetCell != _expectedCell)
                {
                    Fail(_index, e.shape.name, "aimed at cell " + drum.CellName(flight.TargetCell) +
                         ", expected " + drum.CellName(_expectedCell));
                    _index++;
                    break;
                }

                Debug.Log(string.Format("[Case1Gate] TAP {0} shape={1} screen={2} -> aiming at {3} (hole mesh {4})",
                    _index, e.shape.name, screen, drum.CellName(_expectedCell), drum.HoleMeshName(_expectedCell)));

                _stageStart = now;
                _phase = 2;
                break;
            }

            case 2:   // wait for the run to finish, then check where it landed
            {
                if (director.IsPlaying && now - _stageStart < RunTimeout) break;

                if (director.IsPlaying)
                {
                    Fail(_index, _expectedShape != null ? _expectedShape.name : "?", "sequence timed out");
                    _index++;
                    _phase = 1;
                    break;
                }

                // The honest check: of every cell on the drum, the one whose hole the shape ended up
                // closest to must be the cell it was matched with. A wrong-target bug cannot survive it.
                int nearest = -1;
                float bestDistance = float.MaxValue;
                Vector3 resting = _expectedShape != null ? _expectedShape.position : Vector3.zero;
                for (int c = 0; c < drum.CellCount; c++)
                {
                    float d = Vector3.Distance(resting, drum.HoleCenter(c));
                    if (d < bestDistance) { bestDistance = d; nearest = c; }
                }

                if (nearest != _expectedCell)
                {
                    Fail(_index, _expectedShape != null ? _expectedShape.name : "?",
                         "came to rest nearest " + drum.CellName(nearest) + " (" + bestDistance.ToString("0.000") +
                         " u), expected " + drum.CellName(_expectedCell));
                }
                else
                {
                    _passed++;
                    Debug.Log(string.Format(
                        "[Case1Gate] PASS {0} shape={1} -> cell={2} holeMesh={3} restDistance={4:0.000} u  seq={5:0.000} s completed={6}",
                        _index, _expectedShape.name, drum.CellName(_expectedCell), drum.HoleMeshName(_expectedCell),
                        bestDistance, director.Report.totalDuration, director.Report.completed));
                }

                _index++;
                _phase = 1;
                break;
            }
        }
    }

    static void Fail(int index, string shapeName, string reason)
    {
        _failed++;
        Debug.LogError(string.Format("[Case1Gate] FAIL {0} shape={1}: {2}", index, shapeName, reason));
    }

    static void Finish(string fatal, int exitCode)
    {
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(fatal)) sb.AppendLine("[Case1Gate] FATAL " + fatal);
        sb.AppendLine(string.Format("[Case1Gate] SELECTION_GATE {0} passed={1} failed={2}",
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

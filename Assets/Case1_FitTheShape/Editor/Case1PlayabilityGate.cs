using System.Text;
using UnityEditor;
using UnityEngine;
using Case1;

/// <summary>
/// Proves the scene is PLAYABLE, which <see cref="Case1SelectionGate"/> does not.
///
/// The selection gate drives <c>Case1Director.PlaySelected(index)</c>. That is the right test for
/// "does the piece I chose fly to its own cell", but it walks straight past the code a real press goes
/// through: <c>ShapeTapInput.PickTrayShape</c> and <c>Case1Director.HandlePieceTap</c>, which is where
/// the front-row rule lives. A scene whose three tappable pieces were hidden behind the reel, and whose
/// only reachable pieces were rejected as "not in front row", passed the selection gate 5/5.
///
/// So this gate presses. For every tray occupant, at its own screen position:
///   1. the press must resolve to THAT piece and not to a neighbour,
///   2. <c>HandlePieceTap</c>'s verdict must match the front-row rule - accepted on the front row,
///      rejected behind it (a run in which nothing is ever rejected proves nothing, so the rejection
///      is asserted too),
///   3. an accepted piece must actually SEAT: when it comes to rest, the nearest of all 75 holes on the
///      drum must be the cell it was matched to.
/// Slot membership is re-read after every accepted tap, because the tray reflows underneath it.
/// </summary>
[InitializeOnLoad]
public static class Case1PlayabilityGate
{
    const string KeyActive = "Case1PlayabilityGate.Active";
    const double ReadyTimeout = 20.0;
    const double RunTimeout = 25.0;

    static bool _hooked, _sessionInit;
    static int _index, _phase, _passed, _failed, _accepted, _rejected;
    static double _stageStart;
    static Transform _pending;
    static int _pendingCell;

    static Case1PlayabilityGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Zero-argument entry point for -executeMethod. Do NOT pass -quit: this drives
    /// EditorApplication.update and calls EditorApplication.Exit itself.</summary>
    public static void PlayabilityGate()
    {
        Case1SceneSetup.Build();
        SessionState.SetInt(KeyActive, 1);
        _sessionInit = false;
        Hook();
        Debug.Log("[Case1Play] GATE_START entering play mode");
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

        Case1Director dir = Object.FindFirstObjectByType<Case1Director>(FindObjectsInactive.Include);
        ShapeTapInput tap = Object.FindFirstObjectByType<ShapeTapInput>(FindObjectsInactive.Include);
        if (dir == null || tap == null || dir.deck == null || dir.drum == null || dir.flight == null)
        { Finish("no Case1Director / ShapeTapInput / wiring in the play-mode scene", 2); return; }

        DeckReflow deck = dir.deck;
        DrumSlotReaction drum = dir.drum;
        double now = EditorApplication.timeSinceStartup;

        if (!_sessionInit)
        {
            _sessionInit = true;
            _index = 0; _phase = 0; _passed = 0; _failed = 0; _accepted = 0; _rejected = 0;
            _stageStart = now;
            dir.AllowPlayWithoutInput();
        }

        switch (_phase)
        {
            case 0:
                if (dir.IsPlaying) { Finish("the scene started a sequence on its own", 3); return; }
                if (dir.Ready || now - _stageStart > ReadyTimeout)
                {
                    Debug.Log("[Case1Play] idle and ready after " + (now - _stageStart).ToString("0.00") +
                              " s; tray occupants=" + deck.entries.Length + " columns=" + deck.columns);
                    _phase = 1;
                }
                break;

            case 1:
            {
                if (_index >= deck.entries.Length) { Report(deck); return; }
                // A refused press plays an in-place wobble; let it finish before the next one, or
                // HandlePieceTap would refuse the NEXT piece for IsPlaying and the run would read as a
                // front-row failure that never happened.
                if (dir.IsPlaying) break;

                DeckReflow.Entry e = deck.entries[_index];
                if (e == null || e.shape == null) { Fail(_index, "<null>", "entry is empty"); _index++; break; }

                Transform piece = e.shape;
                int slot = deck.SlotOf(piece);
                bool front = deck.IsInFrontRow(piece);

                // 1) the press has to land on THIS piece
                Vector2 screen = tap.ScreenPointOf(_index);
                Transform picked = tap.PickTrayShape(screen);
                if (picked != piece)
                {
                    Fail(_index, piece.name, "a press on its own screen position resolved to " +
                         (picked != null ? picked.name : "<nothing>"));
                    _index++; break;
                }

                // 2) HandlePieceTap's verdict has to match the front-row rule
                ShapeId id; bool named = ShapeIds.TryParse(piece.name, out id);
                int cellBefore = named ? drum.FindAvailableLiveSlot(id) : -1;
                bool ok = dir.HandlePieceTap(piece);
                bool shouldAccept = front && named && cellBefore >= 0;
                if (ok != shouldAccept)
                {
                    Fail(_index, piece.name, "HandlePieceTap returned " + ok + " for a piece on slot " +
                         slot + " (front=" + front + ", free matching cell=" + cellBefore + ")");
                    _index++; break;
                }

                if (!ok)
                {
                    _rejected++; _passed++;
                    Debug.Log(string.Format("[Case1Play] REJECT {0} {1} slot={2} front={3} freeCell={4} - correctly refused",
                        _index, piece.name, slot, front, cellBefore));
                    _index++; break;
                }

                _accepted++;
                _pending = piece; _pendingCell = cellBefore;
                Debug.Log(string.Format("[Case1Play] ACCEPT {0} {1} slot={2} screen={3} -> aiming at {4}",
                    _index, piece.name, slot, screen, drum.CellName(cellBefore)));
                _stageStart = now; _phase = 2;
                break;
            }

            case 2:
            {
                if (dir.IsPlaying && now - _stageStart < RunTimeout) break;
                if (dir.IsPlaying)
                { Fail(_index, Name(_pending), "sequence timed out"); _index++; _phase = 1; break; }

                int nearest = -1; float best = float.MaxValue;
                Vector3 rest = _pending != null ? _pending.position : Vector3.zero;
                for (int c = 0; c < drum.CellCount; c++)
                {
                    float d = Vector3.Distance(rest, drum.HoleCenter(c));
                    if (d < best) { best = d; nearest = c; }
                }
                if (nearest != _pendingCell)
                    Fail(_index, Name(_pending), "came to rest nearest " + drum.CellName(nearest) +
                         " (" + best.ToString("0.000") + " u), expected " + drum.CellName(_pendingCell));
                else
                {
                    _passed++;
                    Debug.Log(string.Format("[Case1Play] SEATED {0} {1} -> {2} restDistance={3:0.000} u",
                        _index, Name(_pending), drum.CellName(_pendingCell), best));
                }
                _index++; _phase = 1;
                break;
            }
        }
    }

    static string Name(Transform t) { return t != null ? t.name : "?"; }

    static void Report(DeckReflow deck)
    {
        // A run in which every press was accepted, or every press refused, tests nothing. Both arms of
        // the front-row rule must have fired at least once.
        if (_accepted == 0) Fail(-1, "-", "no press was ever accepted: the tray is not playable");
        if (_rejected == 0) Fail(-1, "-", "no press was ever refused: the front-row rule never fired");
        Finish(null, _failed == 0 ? 0 : 1);
    }

    static void Fail(int index, string name, string reason)
    {
        _failed++;
        Debug.LogError(string.Format("[Case1Play] FAIL {0} {1}: {2}", index, name, reason));
    }

    static void Finish(string fatal, int exitCode)
    {
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(fatal)) sb.AppendLine("[Case1Play] FATAL " + fatal);
        sb.AppendLine(string.Format("[Case1Play] PLAYABILITY_GATE {0} passed={1} failed={2} accepted={3} rejected={4}",
            exitCode == 0 ? "GREEN" : "RED", _passed, _failed, _accepted, _rejected));
        Debug.Log(sb.ToString());
        System.Console.WriteLine(sb.ToString());

        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else EditorApplication.isPlaying = false;
    }
}

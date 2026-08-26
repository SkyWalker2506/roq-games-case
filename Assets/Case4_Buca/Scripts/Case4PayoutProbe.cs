using System.Collections;
using System.Text;
using UnityEngine;

namespace Case4
{
    /// <summary>
    /// Multi-shot probe for the owner's report: "case 4 te bazen vursak bile altin toplama efekti
    /// calismiyor" - sometimes the puck hits and the gold payout does not play.
    ///
    /// ONE SHOT PROVES NOTHING HERE. Every gate in this tree fires the measured reference bank, which
    /// reaches the stack in 1.07-1.13 s and pays out every time; the failure only appears on shots
    /// that take LONGER than that to arrive, which is exactly what a hand-aimed shot does and what a
    /// scripted one never does. So this probe fires a spread of shots - the angle and the pull length
    /// both vary, the way they vary under a thumb - and reports a FAILURE RATE.
    ///
    /// THE PRE-REGISTERED INVARIANT is Case4Director.PayoutInvariantHolds:
    ///     every shot that registers a real solver contact with the stack emits at least 90% of the
    ///     authored coin stream, and the first coin's first drawn frame is inside the viewport.
    /// Shots that never touch the stack are not covered by it - they are supposed to pay nothing.
    ///
    /// PROVING IT RED. <see cref="mutate"/> sets Case4Director.legacyFixedFlightBudget, which restores
    /// the exact loop this replaced: the flight ends on flightTimeout whether or not the shot has
    /// resolved. The invariant must go red in that run - on the late-arriving shots specifically -
    /// while COIN_EXIT and COIN_GAP stay green in the same run. That is what says the invariant reads
    /// the change rather than the weather.
    /// </summary>
    public sealed class Case4PayoutProbe : MonoBehaviour
    {
        /// <summary>Set once the probe has finished, pass or fail.</summary>
        public static bool Finished;

        /// <summary>Whether the invariant held on every covered shot.</summary>
        public static bool Passed;

        /// <summary>Human readable transcript, written to the gate log.</summary>
        public static string Transcript = "";

        /// <summary>How many shots to fire. Set by the gate before the component is added.</summary>
        public static int Shots = 24;

        /// <summary>Restore the pre-fix flight loop, so the invariant can be shown red.</summary>
        public static bool Mutate;

        readonly StringBuilder _log = new StringBuilder();
        int _covered, _broken, _missed;
        int _exitChecked, _exitFailed, _gapChecked, _gapFailed;

        void Line(string s)
        {
            _log.AppendLine(s);
            Shared.Sequencing.SeqLog.Info("[Case4Payout] " + s);
        }

        IEnumerator Start()
        {
            // Same reason as Case4InputProbe: a managed stack trace per log line stretches frames,
            // Time.maximumDeltaTime then clamps how much physics each beat gets, and the thing being
            // measured changes under the measurement.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

            Finished = false;
            Passed = false;

            Case4Director director = Object.FindFirstObjectByType<Case4Director>(FindObjectsInactive.Include);
            PuckAimController aim = Object.FindFirstObjectByType<PuckAimController>(FindObjectsInactive.Include);
            if (director == null || aim == null || director.launcher == null || director.coins == null)
            {
                Line("FAIL scene is missing Case4Director / PuckAimController / launcher / coins");
                Done();
                yield break;
            }

            while (!director.Ready) yield return null;

            director.legacyFixedFlightBudget = Mutate;
            Line("---- CASE4_PAYOUT_PROBE ----");
            Line("mode = " + (Mutate ? "MUTATED (legacy fixed flight budget: the tree before the fix)" : "CURRENT TREE"));
            Line("invariant = a shot that registers a real stack contact emits >= " + director.PayoutCoinFloor +
                 " of " + director.coins.coinCount + " coins, and the first coin's first frame is on screen");
            Line("shots = " + Shots + ", flightTimeout = " + director.flightTimeout.ToString("0.00") + "s");

            for (int i = 0; i < Shots; i++)
            {
                yield return FireOne(director, aim, i);
            }

            Line("");
            Line("---- RESULT ----");
            Line(string.Format("{0} shots fired: {1} reached the stack (covered by the invariant), {2} missed (not covered)",
                Shots, _covered, _missed));
            Line(string.Format("PAYOUT FAILURE RATE = {0} of {1} covered shots = {2:0.0}%",
                _broken, _covered, _covered > 0 ? 100f * _broken / _covered : 0f));
            Line(string.Format("COIN_EXIT {0} of {1} armed streams failed; COIN_GAP {2} of {3} armed streams failed",
                _exitFailed, _exitChecked, _gapFailed, _gapChecked));

            // The mutated run is EXPECTED to break the invariant; a mutated run in which nothing broke
            // is a probe that cannot see the bug, and that is a failure of the probe, not a pass.
            bool ok = Mutate ? (_broken > 0) : (_broken == 0);
            if (Mutate && _broken == 0)
                Line("FAIL the mutation run broke nothing: the invariant is not reading the flight loop " +
                     "and every number above is worthless");
            // Either way COIN_EXIT and COIN_GAP must be green: they are the measured reference
            // properties this work is not allowed to disturb, in the mutated run as much as this one.
            if (_exitFailed > 0 || _gapFailed > 0) { ok = false; Line("FAIL COIN_EXIT / COIN_GAP regressed"); }

            Passed = ok;
            Done();
        }

        IEnumerator FireOne(Case4Director director, PuckAimController aim, int index)
        {
            CoinArcStream coins = director.coins;
            PuckLauncher launcher = director.launcher;

            // A thumb is not a script. The reference bank is one direction at one power; the owner
            // takes whatever angle and whatever pull length the drag happened to have, and it is the
            // long, wandering ones that expose this. Deterministic hash rather than UnityEngine.Random
            // so the same run reproduces exactly.
            float a = Hash01(index, 11);
            float b = Hash01(index, 29);
            Vector3 baseDir = launcher.referenceAimDir;
            float yaw = Mathf.Lerp(-26f, 26f, a);
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * baseDir;
            float power = Mathf.Lerp(0.55f, 1.00f, b);

            yield return aim.SimulateAimAt(dir, power, 0.10f);

            if (!director.IsPlaying)
            {
                Line(string.Format("shot {0,2}: REFUSED by the director (yaw {1:0.0} deg, power {2:0.00}) - not counted",
                    index, yaw, power));
                yield break;
            }

            while (director.IsPlaying) yield return null;

            // Read everything BEFORE the next shot: the next press runs ArmNextShot -> ResetState ->
            // coins.Clear(), which zeroes LaunchedCount.
            bool hit = launcher.StackHit;
            int launched = coins.LaunchedCount;
            string why;
            bool held = director.PayoutInvariantHolds(out why);

            if (hit)
            {
                _covered++;
                if (!held) _broken++;
                _exitChecked++;
                if (!(coins.ExitsFrame && coins.ExitViewport.x >= 0.5f && coins.ExitViewport.y >= 0.5f)) _exitFailed++;
                _gapChecked++;
                float gapDiameters = coins.NeighbourGapDiameters;
                if (gapDiameters < 1.00f || gapDiameters > 1.25f) _gapFailed++;
            }
            else _missed++;

            Line(string.Format(
                "shot {0,2}: yaw {1,6:0.0} deg  power {2:0.00}  rails {3,2}  timeToStack {4,7}  coins {5,2}  " +
                "exit ({6:0.000},{7:0.000})  gap {8:0.0}px  -> {9}",
                index, yaw, power, launcher.BounceCount,
                launcher.TimeToStack >= 0f ? launcher.TimeToStack.ToString("0.000") + "s" : "never",
                launched, coins.ExitViewport.x, coins.ExitViewport.y, coins.NeighbourGapPx,
                !hit ? "missed (not covered)" : (held ? "held" : "BROKEN")));
            if (hit && !held) Line("            " + why);
        }

        static float Hash01(int index, int salt)
        {
            uint x = 0x51ED270Bu + (uint)(index + 1) * 0x9E3779B9u + (uint)(salt + 1) * 0x85EBCA6Bu;
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }

        void Done()
        {
            Transcript = _log.ToString();
            Finished = true;
        }
    }
}

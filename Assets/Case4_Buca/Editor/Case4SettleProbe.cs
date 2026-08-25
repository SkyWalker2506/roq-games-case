#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Measures the Case 4 stack WITHOUT entering play mode, so the four scene-graph findings can be
/// checked in seconds instead of a 340-frame capture.
///
/// It never computes a settle pose of its own: it calls <see cref="Case4.GreenBlockShatter.PlanCascade"/>,
/// the same method the cascade coroutine plays. A probe that re-implements the formula measures its
/// own copy, which is how the previous audit's numbers could be green and wrong at the same time.
///
/// Run: tools/unity-run.sh -batchmode -nographics -executeMethod Case4SettleProbe.Probe -quit
/// (-quit is safe here: this is a plain synchronous editor method, no play mode, no EditorApplication.update.)
/// </summary>
public static class Case4SettleProbe
{
    const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";

    public static void Probe()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var log = new StringBuilder();

        // ---------------------------------------------------------------- arena
        Bounds? left = WorldBounds("Rail_Left"), right = WorldBounds("Rail_Right");
        Bounds? bottom = WorldBounds("Rail_Bottom"), arch = WorldBounds("Rail_Arch");
        Bounds? divider = WorldBounds("Divider"), floor = WorldBounds("Floor");
        Dump(log, "Rail_Left", left); Dump(log, "Rail_Right", right);
        Dump(log, "Rail_Bottom", bottom); Dump(log, "Rail_Arch", arch);
        Dump(log, "Divider", divider); Dump(log, "Floor", floor);

        // ---------------------------------------------------------------- stack
        var shatter = Object.FindFirstObjectByType<Case4.GreenBlockShatter>();
        if (shatter == null) { Debug.LogError("[Case4Probe] GreenBlockShatter not found"); EditorApplication.Exit(1); return; }

        // Awake never runs in edit mode, so a rest-pose backup left in the scene asset by an earlier
        // session would win over the authored transforms. Clear it first; the authored scene is truth.
        var so = new SerializedObject(shatter);
        so.FindProperty("_restBackupValid").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        shatter.Capture();

        var poses = shatter.PlanCascade();
        log.AppendLine(string.Format("blocks={0} planned={1} blockSize={2:0.0000} blockPitch={3:0.0000}",
            shatter.BlockCount, poses.Count, shatter.blockSize, shatter.blockPitch));

        // rest AABB + rest-state interpenetration
        Vector3 rMin = V(float.MaxValue), rMax = V(float.MinValue);
        for (int i = 0; i < poses.Count; i++)
        {
            Vector3 h = Case4.GreenBlockShatter.RotatedHalfExtents(poses[i].RestRot, poses[i].HalfExtents);
            rMin = Vector3.Min(rMin, poses[i].RestPos - h);
            rMax = Vector3.Max(rMax, poses[i].RestPos + h);
        }
        log.AppendLine(string.Format("REST aabb x {0:0.000}..{1:0.000}  y {2:0.000}..{3:0.000}  z {4:0.000}..{5:0.000}",
            rMin.x, rMax.x, rMin.y, rMax.y, rMin.z, rMax.z));

        int overlapPairs = 0; float worstOverlap = 0f; string worstPair = "-";
        for (int i = 0; i < poses.Count; i++)
            for (int j = i + 1; j < poses.Count; j++)
            {
                Vector3 hi = Case4.GreenBlockShatter.RotatedHalfExtents(poses[i].RestRot, poses[i].HalfExtents);
                Vector3 hj = Case4.GreenBlockShatter.RotatedHalfExtents(poses[j].RestRot, poses[j].HalfExtents);
                Vector3 d = poses[i].RestPos - poses[j].RestPos;
                float ox = hi.x + hj.x - Mathf.Abs(d.x);
                float oy = hi.y + hj.y - Mathf.Abs(d.y);
                float oz = hi.z + hj.z - Mathf.Abs(d.z);
                float pen = Mathf.Min(ox, Mathf.Min(oy, oz));   // >0 on every axis = real overlap
                if (pen > 1e-4f)
                {
                    overlapPairs++;
                    if (pen > worstOverlap) { worstOverlap = pen; worstPair = poses[i].Tr.name + "/" + poses[j].Tr.name; }
                }
            }
        log.AppendLine(string.Format("REST interpenetration: {0} overlapping pairs, worst {1:0.0000}u ({2})",
            overlapPairs, worstOverlap, worstPair));

        // ---------------------------------------------------------------- settled
        float floorTop = floor.HasValue ? floor.Value.max.y : 0f;
        float leftInner = left.HasValue ? left.Value.max.x : float.MinValue;
        float leftOuter = left.HasValue ? left.Value.min.x : float.MinValue;
        float rightInner = right.HasValue ? right.Value.min.x : float.MaxValue;
        float frontInner = bottom.HasValue ? bottom.Value.max.z : float.MinValue;
        float backInner = arch.HasValue ? arch.Value.min.z : float.MaxValue;
        log.AppendLine(string.Format("ARENA inner: x {0:0.000}..{1:0.000} (leftOuter {2:0.000})  z {3:0.000}..{4:0.000}  floorTop y {5:0.000}",
            leftInner, rightInner, leftOuter, frontInner, backInner, floorTop));

        // The settled measurements below read Unity's OWN Renderer.bounds after the end pose is
        // actually applied to the transform - not GreenBlockShatter.RotatedHalfExtents. The planner
        // now uses RotatedHalfHeight to place the block, so a probe that measured with the same
        // function would report a floor gap of exactly 0.000 whether or not the block was on the
        // floor. That number would be a tautology, not a measurement. Renderer.bounds is computed by
        // the engine from the mesh and the transform matrix and knows nothing about the formula.
        //
        // NEGATIVE CONTROL: block 0 is deliberately lifted by CtrlLift before measuring, and the
        // per-block report must show exactly that lift on it and nothing on the rest. If the control
        // block reads flush, the metric is blind and every green below is worthless.
        const float CtrlLift = 0.250f;

        var savedPos = new Vector3[poses.Count];
        var savedRot = new Quaternion[poses.Count];
        for (int i = 0; i < poses.Count; i++)
        { savedPos[i] = poses[i].Tr.position; savedRot[i] = poses[i].Tr.rotation; }

        Vector3 sMin = V(float.MaxValue), sMax = V(float.MinValue);
        int outside = 0, inDivider = 0, hovering = 0, sunken = 0, pastDividerX = 0;
        float gapSum = 0f, gapMax = float.MinValue, gapMin = float.MaxValue;
        float ctrlGap = float.NaN;
        for (int i = 0; i < poses.Count; i++)
        {
            Vector3 applied = poses[i].EndPos + (i == 0 ? Vector3.up * CtrlLift : Vector3.zero);
            poses[i].Tr.SetPositionAndRotation(applied, poses[i].EndRot);
        }
        for (int i = 0; i < poses.Count; i++)
        {
            Renderer rend = poses[i].Tr.GetComponent<Renderer>();
            if (rend == null) { log.AppendLine("no renderer on " + poses[i].Tr.name); continue; }
            Bounds wb = rend.bounds;
            Vector3 lo = wb.min, hi2 = wb.max;
            sMin = Vector3.Min(sMin, lo); sMax = Vector3.Max(sMax, hi2);
            bool bad = lo.x < leftInner - 1e-3f || hi2.x > rightInner + 1e-3f
                    || lo.z < frontInner - 1e-3f || hi2.z > backInner + 1e-3f;
            if (bad) outside++;
            if (divider.HasValue && Overlaps(lo, hi2, divider.Value)) inDivider++;
            if (divider.HasValue && hi2.x > divider.Value.min.x) pastDividerX++;
            float gap = lo.y - floorTop;
            if (i == 0) { ctrlGap = gap; continue; }        // control block is excluded from the census
            gapSum += Mathf.Abs(gap); gapMax = Mathf.Max(gapMax, gap); gapMin = Mathf.Min(gapMin, gap);
            if (gap > 0.01f) hovering++;
            if (gap < -0.01f) sunken++;
        }
        // How many blocks come to rest touching a wall, and how far the formation opened out. The
        // second is the quantity the director's PROOF line calls formationSpread and gates at >= 3.0.
        int flushLeft = 0, flushRight = 0, flushFront = 0, flushBack = 0;
        for (int i = 0; i < poses.Count; i++)
        {
            Renderer rr = poses[i].Tr.GetComponent<Renderer>();
            if (rr == null) continue;
            Bounds wb = rr.bounds;
            if (Mathf.Abs(wb.min.x - leftInner) < 0.005f) flushLeft++;
            if (divider.HasValue && Mathf.Abs(wb.max.x - divider.Value.min.x) < 0.005f) flushRight++;
            if (Mathf.Abs(wb.min.z - frontInner) < 0.005f) flushFront++;
            if (Mathf.Abs(wb.max.z - backInner) < 0.005f) flushBack++;
        }
        float spreadSettled = shatter.FormationSpread();
        for (int i = 0; i < poses.Count; i++) poses[i].Tr.SetPositionAndRotation(savedPos[i], savedRot[i]);
        float spreadRest = shatter.FormationSpread();
        log.AppendLine(string.Format("SETTLED flush against a wall: left={0} right(divider)={1} front={2} back={3}",
            flushLeft, flushRight, flushFront, flushBack));
        log.AppendLine(string.Format("formationSpread: settled x{0:0.00}  at-rest x{1:0.00} (director gates settled >= 3.0)",
            spreadSettled, spreadRest));

        log.AppendLine(string.Format("NEGATIVE CONTROL block 0 lifted {0:0.000}u -> renderer-measured gap {1:0.000} (must equal the lift)",
            CtrlLift, ctrlGap));
        log.AppendLine(string.Format("SETTLED aabb (Renderer.bounds) x {0:0.000}..{1:0.000}  y {2:0.000}..{3:0.000}  z {4:0.000}..{5:0.000}",
            sMin.x, sMax.x, sMin.y, sMax.y, sMin.z, sMax.z));
        log.AppendLine(string.Format("SETTLED outsideArena={0}/{1}  insideDivider={2}  pastDividerX={3}",
            outside, poses.Count, inDivider, pastDividerX));
        log.AppendLine(string.Format("SETTLED floor gap (35 blocks, control excluded): mean|gap|={0:0.000} max={1:0.000} min={2:0.000} hovering(>0.01)={3} sunken(<-0.01)={4}",
            gapSum / Mathf.Max(1, poses.Count - 1), gapMax, gapMin, hovering, sunken));

        Debug.Log("[Case4Probe] BEGIN\n" + log);
        Debug.Log("[Case4Probe] END");
        EditorApplication.Exit(0);
    }

    static bool Overlaps(Vector3 lo, Vector3 hi, Bounds b)
    {
        return lo.x < b.max.x && hi.x > b.min.x && lo.z < b.max.z && hi.z > b.min.z && lo.y < b.max.y && hi.y > b.min.y;
    }

    static Vector3 V(float f) { return new Vector3(f, f, f); }

    static Bounds? WorldBounds(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) return null;
        Collider c = go.GetComponent<Collider>();
        if (c != null) return c.bounds;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null) return r.bounds;
        return null;
    }

    static void Dump(StringBuilder log, string name, Bounds? b)
    {
        if (!b.HasValue) { log.AppendLine(name + ": <missing>"); return; }
        log.AppendLine(string.Format("{0}: x {1:0.000}..{2:0.000}  y {3:0.000}..{4:0.000}  z {5:0.000}..{6:0.000}",
            name, b.Value.min.x, b.Value.max.x, b.Value.min.y, b.Value.max.y, b.Value.min.z, b.Value.max.z));
    }
}
#endif

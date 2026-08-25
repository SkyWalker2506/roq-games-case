using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Measures, from the authored scene, the XZ cell footprint of every hole opening and every block,
/// in WORLD axes, offset from the object's own transform position.
///
/// Why world axes: three of these roots carry rotations (Block_Block-L and Block_Block-2 are turned,
/// Block_Block-2 also carries a 1.5 x-scale), so a footprint read in a root's local frame is not the
/// footprint the player sees. The shader's frame is the pit plate's object space, which equals the
/// hole's rotation - printed here so a nonzero one cannot be missed.
///
/// Blocks rasterise from their top faces. Hole meshes are open wells whose walls are vertical, so
/// their XZ triangle projections have zero area and rasterise to nothing; they are measured instead
/// by cross-sectioning the mesh with a horizontal plane and parity-testing each cell centre against
/// the resulting outline segments.
///
/// Zero-argument so it can be reached by -executeMethod. Never call with -quit.
/// Usage: tools/unity-run.sh -batchmode -executeMethod Case2ShapeProbe.Run -logFile ...
/// </summary>
public static class Case2ShapeProbe
{
    const string ScenePath = "Assets/Case2_BlockHole/Scenes/BlockHole.unity";

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var root in roots)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool isHole = t.name.StartsWith("Hole_Hole-Block-");
                bool isBlock = t.name.StartsWith("Block_Block-");
                if (isHole || isBlock) Report(t, isHole ? "HOLE" : "BLOCK", isHole);
            }

        Line("SHAPE_PROBE_DONE");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static List<MeshFilter> ArtMeshes(Transform frame)
    {
        var list = new List<MeshFilter>();
        foreach (var mf in frame.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<MeshRenderer>() == null) continue;
            if (mf.transform != frame && mf.transform.parent != frame) continue;
            if (mf.name.Contains("ActiveFX") || mf.name.Contains("Chain")) continue;
            list.Add(mf);
        }
        return list;
    }

    static void Report(Transform frame, string kind, bool crossSection)
    {
        var meshes = ArtMeshes(frame);
        if (meshes.Count == 0) { Line(kind + " " + frame.name + " NO_ART_MESH"); return; }

        Vector3 origin = frame.position;
        Line(string.Format("{0} {1} worldPos=({2:0.###},{3:0.###}) euler=({4:0.#},{5:0.#},{6:0.#}) lossyScale=({7:0.###},{8:0.###},{9:0.###})",
             kind, frame.name, origin.x, origin.z,
             frame.eulerAngles.x, frame.eulerAngles.y, frame.eulerAngles.z,
             frame.lossyScale.x, frame.lossyScale.y, frame.lossyScale.z));

        // Everything below is in WORLD axes, expressed as an offset from `origin`.
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var mf in meshes)
        {
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            foreach (var lv in mf.sharedMesh.vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(lv) - origin;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            Line(string.Format("{0} {1}   art='{2}' mesh='{3}' rendererEnabled={4}",
                 kind, frame.name, mf.name, mf.sharedMesh.name, mf.GetComponent<MeshRenderer>().enabled));
        }

        Line(string.Format("{0} {1} worldBBox dx[{2:0.###},{3:0.###}] dz[{4:0.###},{5:0.###}] size=({6:0.###} x {7:0.###}) centreOffset=({8:0.###},{9:0.###})  ABS x[{10:0.###},{11:0.###}] z[{12:0.###},{13:0.###}]",
             kind, frame.name, minX, maxX, minZ, maxZ, maxX - minX, maxZ - minZ,
             (minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f,
             origin.x + minX, origin.x + maxX, origin.z + minZ, origin.z + maxZ));

        int cw = Mathf.RoundToInt(maxX - minX);
        int ch = Mathf.RoundToInt(maxZ - minZ);
        if (cw < 1 || ch < 1 || cw > 6 || ch > 6) { Line(kind + " " + frame.name + " grid skipped"); return; }

        bool[,] occ = new bool[cw, ch];

        if (!crossSection)
        {
            foreach (var mf in meshes)
            {
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices; var tri = mf.sharedMesh.triangles;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[tri[i]]) - origin;
                    Vector3 b = m.MultiplyPoint3x4(v[tri[i + 1]]) - origin;
                    Vector3 c = m.MultiplyPoint3x4(v[tri[i + 2]]) - origin;
                    for (int gx = 0; gx < cw; gx++)
                        for (int gz = 0; gz < ch; gz++)
                        {
                            if (occ[gx, gz]) continue;
                            if (PointInTri(minX + gx + 0.5f, minZ + gz + 0.5f, a.x, a.z, b.x, b.z, c.x, c.z))
                                occ[gx, gz] = true;
                        }
                }
            }
        }
        else
        {
            // Slice the well at mid-height and parity-test each cell centre against the outline.
            float y0 = (minY + maxY) * 0.5f;
            var segs = new List<Vector4>();   // (x0,z0,x1,z1)
            foreach (var mf in meshes)
            {
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices; var tri = mf.sharedMesh.triangles;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[tri[i]]) - origin;
                    Vector3 b = m.MultiplyPoint3x4(v[tri[i + 1]]) - origin;
                    Vector3 c = m.MultiplyPoint3x4(v[tri[i + 2]]) - origin;
                    var hits = new List<Vector2>();
                    AddCross(hits, a, b, y0); AddCross(hits, b, c, y0); AddCross(hits, c, a, y0);
                    if (hits.Count >= 2) segs.Add(new Vector4(hits[0].x, hits[0].y, hits[1].x, hits[1].y));
                }
            }
            Line(string.Format("{0} {1} cross-section y={2:0.###} segments={3}", kind, frame.name, y0, segs.Count));
            for (int gx = 0; gx < cw; gx++)
                for (int gz = 0; gz < ch; gz++)
                {
                    float px = minX + gx + 0.5f, pz = minZ + gz + 0.5f;
                    int crossings = 0;
                    foreach (var s in segs)
                    {
                        float z0 = s.y, z1 = s.w;
                        if ((z0 > pz) == (z1 > pz)) continue;
                        float t = (pz - z0) / (z1 - z0);
                        if (s.x + t * (s.z - s.x) > px) crossings++;
                    }
                    occ[gx, gz] = (crossings % 2) == 1;
                }
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Format("{0} {1} occupancy in WORLD axes (top row = +z / up-screen, left = -x):", kind, frame.name));
        int cells = 0;
        for (int gz = ch - 1; gz >= 0; gz--)
        {
            sb.Append("    ");
            for (int gx = 0; gx < cw; gx++) { sb.Append(occ[gx, gz] ? '#' : '.'); if (occ[gx, gz]) cells++; }
            sb.AppendLine();
        }
        sb.Append("    cells=" + cells);
        Line(sb.ToString());
    }

    static void AddCross(List<Vector2> hits, Vector3 a, Vector3 b, float y)
    {
        if ((a.y > y) == (b.y > y)) return;
        float t = (y - a.y) / (b.y - a.y);
        hits.Add(new Vector2(a.x + t * (b.x - a.x), a.z + t * (b.z - a.z)));
    }

    static float Sign(float px, float pz, float ax, float az, float bx, float bz)
    {
        return (px - bx) * (az - bz) - (ax - bx) * (pz - bz);
    }

    static bool PointInTri(float px, float pz, float ax, float az, float bx, float bz, float cx, float cz)
    {
        float d1 = Sign(px, pz, ax, az, bx, bz);
        float d2 = Sign(px, pz, bx, bz, cx, cz);
        float d3 = Sign(px, pz, cx, cz, ax, az);
        bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(neg && pos);
    }

    static void Line(string s)
    {
        Debug.Log("[Case2ShapeProbe] " + s);
        System.Console.WriteLine("[Case2ShapeProbe] " + s);
    }
}

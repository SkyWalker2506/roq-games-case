#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Lays the green stack out as ONE layer stepping back in z, tapering to a point.
/// Owner: "ust uste dizme ... ileri dogru referans resimdeki gibi ... ucgen gibi bitiyor".
/// Run headless: -executeMethod Case4StackLayout.Apply
/// </summary>
public static class Case4StackLayout
{
    const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";

    // Rows of depth per column, left to right. Tapers to 1 so the plan view is a triangle.
    static readonly int[] Depths = { 8, 7, 6, 5, 4, 3, 2, 1 };

    // Pitch is the authority; a block's footprint is its pitch minus one shared seam gap. The x axis
    // already worked this way - 0.4398 pitch, 0.4354 width, a 0.0044 gap - and the z axis did not:
    // 0.4500 pitch against a 0.5000 depth is 0.0500 of MUTUAL PENETRATION on all 28 column-adjacent
    // pairs, measured by Case4SettleProbe on the authored scene. ArmPhysics then enables all 36
    // colliders and clears isKinematic in a single frame with the pile in that state, which asks
    // PhysX to resolve 28 simultaneous 10%-deep overlaps on the launch frame.
    //
    // Deriving SZ from ZPitch instead of writing it independently means the two can never drift
    // apart again. The stack's outer footprint is preserved to within 0.054u total - the plan-view
    // taper, the front-face clearance against Rail_Bottom and the left clearance against Rail_Left
    // are all unchanged, which the owner asked for explicitly.
    const float Seam = 0.0044f;    // 0.4398 - 0.4354, the gap the x axis was already using
    const float XPitch = 0.4398f;
    const float ZPitch = 0.4500f;
    const float SX = XPitch - Seam;   // 0.4354, unchanged
    const float SY = 1.24f;           // block height - owner asked for it doubled again
    const float SZ = ZPitch - Seam;   // 0.4456, was 0.5000 and overlapping its neighbour
    const float X0 = -36.2091f;
    const float ZFront = -16.2000f;

    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("Case4_Blocks");
        if (root == null) { Debug.LogError("[Case4Layout] Case4_Blocks not found"); EditorApplication.Exit(1); return; }

        var blocks = new List<Transform>();
        foreach (Transform t in root.transform) blocks.Add(t);

        int want = 0;
        for (int i = 0; i < Depths.Length; i++) want += Depths[i];

        // Duplicate or trim to the exact count. Duplicating an existing child copies every
        // component and material binding, so the new blocks are indistinguishable from the old.
        while (blocks.Count < want)
        {
            var copy = Object.Instantiate(blocks[0].gameObject, root.transform);
            copy.name = "Cube_fill_" + blocks.Count;
            blocks.Add(copy.transform);
        }
        while (blocks.Count > want)
        {
            var last = blocks[blocks.Count - 1];
            blocks.RemoveAt(blocks.Count - 1);
            Object.DestroyImmediate(last.gameObject);
        }

        int i2 = 0;
        for (int c = 0; c < Depths.Length; c++)
        {
            float x = X0 + c * XPitch;
            for (int r = 0; r < Depths[c]; r++)
            {
                var t = blocks[i2++];
                t.localPosition = new Vector3(x, SY * 0.5f, ZFront + r * ZPitch);
                t.localRotation = Quaternion.identity;
                t.localScale = new Vector3(SX, SY, SZ);
            }
        }

        // The cascade reads its blocks from a serialised array, not from the hierarchy, so new
        // children are invisible to it until the array is rewritten. PROOF reported 33 blocks
        // after 36 were laid out - the three extras were inert.
        var shatter = Object.FindFirstObjectByType<Case4.GreenBlockShatter>();
        if (shatter != null)
        {
            var so = new SerializedObject(shatter);
            var arr = so.FindProperty("blocks");
            arr.arraySize = blocks.Count;
            for (int k = 0; k < blocks.Count; k++)
                arr.GetArrayElementAtIndex(k).objectReferenceValue = blocks[k];
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[Case4Layout] registered " + blocks.Count + " blocks with GreenBlockShatter");
        }
        else Debug.LogError("[Case4Layout] GreenBlockShatter not found");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(string.Format("[Case4Layout] {0} blocks, depths {1}, x {2:0.###}..{3:0.###}, z {4:0.###}..{5:0.###}",
            want, string.Join(",", Depths), X0 - SX * 0.5f, X0 + (Depths.Length - 1) * XPitch + SX * 0.5f,
            ZFront - SZ * 0.5f, ZFront + (Depths[0] - 1) * ZPitch + SZ * 0.5f));
        EditorApplication.Exit(0);
    }
}
#endif

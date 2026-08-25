#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off diagnostic: prints the real local axes, scale and mesh extents of a few drum cells. Placing
/// the generated glyphs kept failing because the cell prefab's axes were GUESSED; this measures them.
/// </summary>
public static class Case1CellAxesDump
{
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Case1_FitTheShape/Scenes/FitTheShape.unity",
                                                   OpenSceneMode.Single);
        Transform drum = null;
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == "Drum") { drum = go.transform; break; }
        if (drum == null) { Debug.LogError("[AxesDump] no Drum root"); EditorApplication.Exit(1); return; }

        StringBuilder sb = new StringBuilder();
        int shown = 0;
        for (int i = 0; i < drum.childCount && shown < 4; i++)
        {
            Transform t = drum.GetChild(i);
            if (!t.name.StartsWith("Segment_")) continue;

            Renderer body = t.GetComponent<Renderer>();
            Transform hole = t.Find("Hole");
            MeshFilter bmf = t.GetComponent<MeshFilter>();
            MeshFilter hmf = hole != null ? hole.GetComponent<MeshFilter>() : null;

            sb.AppendLine("[AxesDump] " + t.name);
            sb.AppendLine("   localPos " + t.localPosition.ToString("0.000") +
                          "  localEuler " + t.localEulerAngles.ToString("0.0") +
                          "  localScale " + t.localScale.ToString("0.000") +
                          "  lossyScale " + t.lossyScale.ToString("0.000"));
            sb.AppendLine("   world up " + t.up.ToString("0.000") +
                          "  right " + t.right.ToString("0.000") +
                          "  fwd " + t.forward.ToString("0.000"));
            if (body != null)
                sb.AppendLine("   body worldBounds c" + body.bounds.center.ToString("0.000") +
                              " size" + body.bounds.size.ToString("0.000"));
            if (bmf != null && bmf.sharedMesh != null)
                sb.AppendLine("   body MESH local c" + bmf.sharedMesh.bounds.center.ToString("0.000") +
                              " size" + bmf.sharedMesh.bounds.size.ToString("0.000") + "  (" + bmf.sharedMesh.name + ")");
            if (hole != null)
                sb.AppendLine("   hole localPos " + hole.localPosition.ToString("0.000") +
                              "  localEuler " + hole.localEulerAngles.ToString("0.0") +
                              "  localScale " + hole.localScale.ToString("0.000"));
            if (hmf != null && hmf.sharedMesh != null)
                sb.AppendLine("   hole MESH local c" + hmf.sharedMesh.bounds.center.ToString("0.000") +
                              " size" + hmf.sharedMesh.bounds.size.ToString("0.000") + "  (" + hmf.sharedMesh.name + ")");
            shown++;
        }
        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }
}
#endif

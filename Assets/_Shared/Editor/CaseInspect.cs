#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text;

public static class CaseInspect
{
    public static void InspectAll()
    {
        InspectCase2();
        InspectCase3();
        InspectCase4();
    }

    public static void InspectCase2()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Case2_BlockHole/Scenes/BlockHole.unity", OpenSceneMode.Single);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== CASE 2 MESH BOUNDS ===");
        Transform holes = scene.GetRootGameObjects()[3].transform.Find("Holes");
        if (holes != null)
        {
            foreach (Transform h in holes)
            {
                MeshFilter mf = h.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds b = mf.sharedMesh.bounds;
                    sb.AppendLine($"Hole: {h.name} mesh={mf.sharedMesh.name} center={b.center.ToString("F3")} min={b.min.ToString("F3")} max={b.max.ToString("F3")} size={b.size.ToString("F3")}");
                }
            }
        }
        Transform blocks = scene.GetRootGameObjects()[3].transform.Find("Blocks");
        if (blocks != null)
        {
            foreach (Transform b in blocks)
            {
                MeshFilter mf = b.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds bb = mf.sharedMesh.bounds;
                    sb.AppendLine($"Block: {b.name} mesh={mf.sharedMesh.name} center={bb.center.ToString("F3")} min={bb.min.ToString("F3")} max={bb.max.ToString("F3")} size={bb.size.ToString("F3")}");
                }
            }
        }
        Debug.Log(sb.ToString());
    }

    public static void InspectCase3()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Case3_Stickerdom/Scenes/Stickerdom.unity", OpenSceneMode.Single);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== CASE 3 INSPECTION ===");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            DumpHierarchy(root.transform, "", sb);
        }
        Debug.Log(sb.ToString());
    }

    public static void InspectCase4()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Case4_Buca/Scenes/Buca.unity", OpenSceneMode.Single);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ALL COMPONENTS IN BUCA.UNITY ===");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c != null)
                {
                    sb.AppendLine($"{c.gameObject.name} :: {c.GetType().FullName}");
                }
            }
        }
        Debug.Log(sb.ToString());
    }

    static void DumpHierarchy(Transform t, string indent, StringBuilder sb)
    {
        MeshFilter mf = t.GetComponent<MeshFilter>();
        Renderer r = t.GetComponent<Renderer>();
        string extra = "";
        if (mf != null && mf.sharedMesh != null) extra += $" mesh={mf.sharedMesh.name}";
        if (r != null)
        {
            extra += $" enabled={r.enabled}";
            if (r.sharedMaterials != null && r.sharedMaterials.Length > 0)
            {
                extra += " mats=[";
                foreach (var m in r.sharedMaterials) extra += (m != null ? m.name : "null") + ",";
                extra += "]";
            }
        }
        sb.AppendLine($"{indent}- {t.name} (activeSelf={t.gameObject.activeSelf}) pos={t.localPosition} scale={t.localScale}{extra}");
        for (int i = 0; i < t.childCount; i++)
        {
            DumpHierarchy(t.GetChild(i), indent + "  ", sb);
        }
    }
}
#endif

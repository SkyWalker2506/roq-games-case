using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prints the world transform of every object the Case 1 layout places. Written because the Scene view
/// showed the tray pieces at different heights and at arbitrary angles while the Game view looked
/// acceptable - a layout driven from the camera can satisfy the frame and still be nonsense in world
/// space, and the only way to tell is to read the numbers.
/// </summary>
public static class Case1LayoutDump
{
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Case1_FitTheShape/Scenes/FitTheShape.unity",
                                                   OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();
        Debug.Log("[LayoutDump] ---- world transforms ----");
        for (int i = 0; i < roots.Length; i++) Walk(roots[i].transform, 0);
        Debug.Log("[LayoutDump] ---- end ----");
        // No Exit(0) here: it cut the log off before Unity flushed it and the dump printed nothing.
    }

    static void Walk(Transform t, int depth)
    {
        string n = t.name;
        bool interesting = n.StartsWith("Shape_") || n.StartsWith("TrayShape") || n.StartsWith("TrayRefill")
                        || n.StartsWith("DeckSlot_") || n == "Drum" || n == "Deck" || n.Contains("Camera")
                        || n == "Case1_ShapeTray" || n == "Case1_SlotBand" || n == "Case1_ReferenceChrome"
                        || n.StartsWith("Rail") || n.StartsWith("Band") || n.StartsWith("TrayFloor")
                        || n.StartsWith("Spin") || n.StartsWith("Meta") || n.StartsWith("Arrow");
        if (interesting)
        {
            Vector3 p = t.position, e = t.eulerAngles, s = t.lossyScale;
            Debug.Log(string.Format("[LayoutDump] {0,-34} pos=({1,7:0.00},{2,7:0.00},{3,7:0.00})  rot=({4,6:0.0},{5,6:0.0},{6,6:0.0})  scale=({7:0.00},{8:0.00},{9:0.00})",
                n, p.x, p.y, p.z, e.x, e.y, e.z, s.x, s.y, s.z));
        }
        // Do not descend into the drum's 75 cells; the roots are what the layout places.
        if (n == "Drum") return;
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
    }
}

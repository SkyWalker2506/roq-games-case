#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Owner: "disc objesini kullan puck olarak". The arena art carries a start disc - a dark torus with
/// a gold centre, which is also what the reference's puck actually is - while our puck drew a flat
/// gold cylinder. Two round objects sat at the start position and he wanted the disc one.
///
/// This gives the puck the disc's MESH and MATERIALS and disables the original disc renderer, so one
/// object remains and it is the one he picked. The collider lives on the same GameObject and is a
/// sphere, so swapping the mesh cannot change the physics.
/// </summary>
public static class Case4PuckFromDisc
{
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Case4_Buca/Scenes/Buca.unity", OpenSceneMode.Single);

        Renderer disc = null;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m != null && m.name.StartsWith("Case4_DiscPad")) { disc = r; break; }
        }
        if (disc == null) { Debug.LogError("[PuckFromDisc] no Case4_DiscPad renderer found"); EditorApplication.Exit(1); return; }

        var puck = GameObject.Find("Case4_Puck");
        var body = puck != null ? puck.transform.Find("Body") : null;
        if (body == null) { Debug.LogError("[PuckFromDisc] Case4_Puck/Body not found"); EditorApplication.Exit(1); return; }

        var discMf = disc.GetComponent<MeshFilter>();
        var bodyMf = body.GetComponent<MeshFilter>();
        var bodyMr = body.GetComponent<MeshRenderer>();
        if (discMf == null || bodyMf == null || bodyMr == null) { Debug.LogError("[PuckFromDisc] missing MeshFilter/Renderer"); EditorApplication.Exit(1); return; }

        Debug.Log(string.Format("[PuckFromDisc] disc='{0}' mesh='{1}' bounds={2} | body mesh='{3}' bounds={4}",
            disc.name, discMf.sharedMesh != null ? discMf.sharedMesh.name : "<null>", disc.bounds.size.ToString("F3"),
            bodyMf.sharedMesh != null ? bodyMf.sharedMesh.name : "<null>", bodyMr.bounds.size.ToString("F3")));

        Vector3 beforeSize = bodyMr.bounds.size;
        bodyMf.sharedMesh = discMf.sharedMesh;
        bodyMr.sharedMaterials = disc.sharedMaterials;

        // UNIFORM scale, not per-axis. The Body carried (1, 0.12, 1) to squash a cylinder into a
        // coin; multiplying that by a footprint ratio crushed the disc's own 0.507 height to 0.050
        // and it read as a flat ellipse instead of a torus. The disc mesh is already the right
        // shape - it only needs resizing, so every axis takes the same factor and the mesh keeps
        // its authored proportions.
        //
        // The sphere collider lives on this GameObject, so its radius follows the largest axis.
        // Scaling uniformly to the old FOOTPRINT keeps that axis at the value it had, which is
        // what leaves the physics alone; the gate is what confirms it, not this comment.
        Vector3 raw = bodyMr.bounds.size;
        if (raw.x > 1e-4f && raw.z > 1e-4f)
        {
            float k = Mathf.Min(beforeSize.x / raw.x, beforeSize.z / raw.z) * body.localScale.x;
            body.localScale = new Vector3(k, k, k);
        }
        disc.enabled = false;

        Debug.Log(string.Format("[PuckFromDisc] body drawn size {0} -> {1}; original disc renderer disabled",
            beforeSize.ToString("F3"), bodyMr.bounds.size.ToString("F3")));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorApplication.Exit(0);
    }
}
#endif

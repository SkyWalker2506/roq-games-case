using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders the Case 1 scene from several fixed angles and writes them as PNGs.
///
/// The game camera can only ever prove that the FRAME looks right. A layout computed by projecting from
/// that camera can satisfy it and still be nonsense in world space - pieces at different heights, at
/// arbitrary angles, sitting on nothing. Front, side and top views make that immediately visible, which
/// a single game-view screenshot never will.
/// </summary>
public static class Case1AngleShots
{
    const string ScenePath = "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity";
    const int Width = 900;
    const int Height = 700;

    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Frame everything the layout places, so the shots are of the whole set-up rather than a guess.
        Bounds world = new Bounds();
        bool any = false;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Renderer[] rs = roots[i].GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < rs.Length; j++)
            {
                if (rs[j] is ParticleSystemRenderer) continue;
                if (!any) { world = rs[j].bounds; any = true; }
                else world.Encapsulate(rs[j].bounds);
            }
        }
        if (!any) { Debug.LogError("[AngleShots] nothing to frame"); EditorApplication.Exit(1); return; }

        float radius = world.extents.magnitude;
        Debug.Log("[AngleShots] world centre " + world.center.ToString("0.00") +
                  " extents " + world.extents.ToString("0.00"));

        GameObject rig = new GameObject("AngleShotCamera");
        Camera cam = rig.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.16f, 0.20f, 1f);
        cam.fieldOfView = 40f;

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".plan-build", "angles");
        Directory.CreateDirectory(dir);

        // Game View shot matching 1080x1728 portrait reference
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            ShootCustom(mainCam, 1080, 1728, "0_game_view", dir);
        }

        Shoot(cam, world, radius, new Vector3(0f, 0f, -1f),           "1_front", dir);
        Shoot(cam, world, radius, new Vector3(-1f, 0f, 0f),           "2_side",  dir);
        Shoot(cam, world, radius, new Vector3(0f, 1f, -0.05f),        "3_top",   dir);
        Shoot(cam, world, radius, new Vector3(-0.8f, 0.5f, -0.8f),    "4_iso",   dir);

        Object.DestroyImmediate(rig);
        Debug.Log("[AngleShots] written to " + dir);
        EditorApplication.Exit(0);
    }

    static void ShootCustom(Camera cam, int w, int h, string name, string outDir)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log("[AngleShots] custom " + name + " rendered " + w + "x" + h);
    }

    static void Shoot(Camera cam, Bounds world, float radius, Vector3 dir, string name, string outDir)
    {
        // 2.4x the bounding radius keeps the whole set-up inside a 40 degree field with margin.
        cam.transform.position = world.center + dir.normalized * radius * 2.4f;
        cam.transform.rotation = Quaternion.LookRotation((world.center - cam.transform.position).normalized,
                                                         Mathf.Abs(dir.normalized.y) > 0.9f ? Vector3.forward : Vector3.up);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = radius * 8f;

        RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log("[AngleShots] " + name + " from " + cam.transform.position.ToString("0.0"));
    }
}

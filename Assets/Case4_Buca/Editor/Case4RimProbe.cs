using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Case4.EditorTools
{
    /// <summary>
    /// Read-only dump of what actually renders the Buca arena rim.
    /// <para>The arena's visible frame is not authored as a plain GameObject: it comes in as a
    /// prefab instance of case_test_scene.fbx, so neither the scene YAML nor Case4SceneSetup
    /// (which is inert, SceneIsAuthored = true) says which mesh draws the white tube. This
    /// prints every renderer under the arena, its submeshes, its materials and the local-space
    /// cross-section of its mesh, so a geometry claim can be made against a measurement rather
    /// than against a guess. Nothing here writes to the scene.</para>
    /// </summary>
    public static class Case4RimProbe
    {
        const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";

        public static void Dump()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            sb.AppendLine("[RimProbe] BEGIN");

            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mf = r.GetComponent<MeshFilter>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                sb.AppendFormat("[RimProbe] R name={0} path={1} enabled={2} activeInHierarchy={3} mesh={4} sub={5} mats={6}\n",
                    r.name, Path(r.transform), r.enabled, r.gameObject.activeInHierarchy,
                    m != null ? m.name : "<null>", m != null ? m.subMeshCount : -1,
                    r.sharedMaterials.Length);
                for (int i = 0; i < r.sharedMaterials.Length; i++)
                {
                    Material mat = r.sharedMaterials[i];
                    sb.AppendFormat("[RimProbe]     mat[{0}]={1} shader={2}\n", i,
                        mat != null ? mat.name : "<null>",
                        mat != null && mat.shader != null ? mat.shader.name : "<null>");
                }
                if (m != null)
                {
                    Bounds b = r.bounds;
                    sb.AppendFormat("[RimProbe]     worldBounds c=({0:0.###},{1:0.###},{2:0.###}) size=({3:0.###},{4:0.###},{5:0.###}) verts={6}\n",
                        b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z, m.vertexCount);
                }
            }

            sb.AppendLine("[RimProbe] END");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Writes every vertex of the arena frame mesh to CSV in world space, with its normal and
        /// submesh index, so the rim and divider cross-sections can be read off a measurement
        /// instead of inferred from a screenshot.
        /// </summary>
        public static void Section()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.name != "level_frame") continue;
                var mf = r.GetComponent<MeshFilter>();
                Mesh m = mf.sharedMesh;
                Vector3[] v = m.vertices;
                Vector3[] n = m.normals;
                int[] sub = new int[v.Length];
                for (int s = 0; s < m.subMeshCount; s++)
                    foreach (int i in m.GetTriangles(s)) sub[i] = s;
                var sb = new StringBuilder("x,y,z,nx,ny,nz,sub\n");
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 w = r.transform.TransformPoint(v[i]);
                    Vector3 wn = r.transform.TransformDirection(n[i]);
                    sb.AppendFormat("{0:0.####},{1:0.####},{2:0.####},{3:0.###},{4:0.###},{5:0.###},{6}\n",
                        w.x, w.y, w.z, wn.x, wn.y, wn.z, sub[i]);
                }
                System.IO.File.WriteAllText(".plan-build/logs/case4_level_frame_verts.csv", sb.ToString());
                Debug.Log("[RimSection] wrote " + v.Length + " verts, subMeshes=" + m.subMeshCount);
                return;
            }
            Debug.LogError("[RimSection] level_frame renderer not found");
        }

        /// <summary>
        /// Prints the capture camera's exact view and projection matrices for the 1080x1728 render
        /// target FrameStripCapture uses, so screen measurements can be turned into world numbers
        /// (and back) offline instead of being guessed from ratios.
        /// </summary>
        public static void Camera()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) { Debug.LogError("[RimProbe] no main camera"); return; }
            float aspect = 1080f / 1728f;
            Matrix4x4 v = cam.worldToCameraMatrix;
            Matrix4x4 pr = Matrix4x4.Perspective(cam.fieldOfView, aspect, cam.nearClipPlane, cam.farClipPlane);
            var sb = new StringBuilder();
            sb.AppendFormat("[RimCam] pos=({0:0.#####},{1:0.#####},{2:0.#####}) euler=({3:0.#####},{4:0.#####},{5:0.#####}) fov={6:0.#####} ortho={7} near={8} far={9}\n",
                cam.transform.position.x, cam.transform.position.y, cam.transform.position.z,
                cam.transform.eulerAngles.x, cam.transform.eulerAngles.y, cam.transform.eulerAngles.z,
                cam.fieldOfView, cam.orthographic, cam.nearClipPlane, cam.farClipPlane);
            for (int r = 0; r < 4; r++)
                sb.AppendFormat("[RimCam] V {0:0.#######} {1:0.#######} {2:0.#######} {3:0.#######}\n", v[r,0], v[r,1], v[r,2], v[r,3]);
            for (int r = 0; r < 4; r++)
                sb.AppendFormat("[RimCam] P {0:0.#######} {1:0.#######} {2:0.#######} {3:0.#######}\n", pr[r,0], pr[r,1], pr[r,2], pr[r,3]);
            Debug.Log(sb.ToString());
        }

        static string Path(Transform t)
        {
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}

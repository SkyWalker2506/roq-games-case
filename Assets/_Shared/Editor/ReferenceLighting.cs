#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Shared.EditorTools
{
    /// <summary>
    /// Deterministic reference-light pass used by the four case builders.  It only owns the main
    /// directional key and RenderSettings ambient values; local effect lights supplied by the starter
    /// scenes remain untouched.  Re-running a case therefore cannot accumulate or drift a key light.
    /// </summary>
    public static class ReferenceLighting
    {
        const string KeyName = "Reference_KeyLight";

        public static Light Configure(Scene scene, Color keyColor, float keyIntensity, Vector3 euler,
                                      Color ambient, float shadowStrength, float reflectionIntensity = 0.5f)
        {
            Light key = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length && key == null; r++)
            {
                Light[] lights = roots[r].GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null && lights[i].name == KeyName) { key = lights[i]; break; }
            }

            if (key == null)
            {
                GameObject go = new GameObject(KeyName);
                SceneManager.MoveGameObjectToScene(go, scene);
                key = go.AddComponent<Light>();
            }

            key.type = LightType.Directional;
            key.color = keyColor;
            key.intensity = keyIntensity;
            key.transform.rotation = Quaternion.Euler(euler);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = Mathf.Clamp01(shadowStrength);
            key.shadowBias = 0.035f;
            key.shadowNormalBias = 0.18f;
            key.enabled = true;
            EditorUtility.SetDirty(key);
            EditorUtility.SetDirty(key.gameObject);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.reflectionIntensity = Mathf.Clamp(reflectionIntensity, 0f, 1f);
            RenderSettings.fog = false;
            return key;
        }
    }
}
#endif

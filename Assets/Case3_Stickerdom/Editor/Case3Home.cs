using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Case3.EditorTools
{
    /// <summary>
    /// Puts the HOME button back into Stickerdom.
    ///
    /// Stickerdom was the only case scene with no <see cref="MenuNavigation"/> root
    /// (FitTheShape, BlockHole and Buca each have exactly one; Stickerdom had zero), so
    /// MenuSetup.NavigationTest reached the scene and then sat there with nav.HomeButton
    /// null until it timed out: NAV_TIMEOUT stage=1 case=2 scene=Stickerdom. A reviewer
    /// could enter Case 3 from the menu and had no way back.
    ///
    /// Nothing case-specific is built here on purpose. MenuNavigation decides at runtime
    /// what to build from the scene it wakes up in - the picker in MainMenu, a single
    /// top-left HOME button anywhere else - so every case scene needs exactly one empty
    /// root carrying that component and nothing else. This is the same object, created the
    /// same way, as MenuSetup.EnsureNavigationRoot creates for the other three.
    ///
    /// It lives in Case 3's own editor folder rather than being a call into MenuSetup
    /// because MenuSetup.Run also rewrites MainMenu.unity and all four case scenes, which
    /// is far more blast radius than one missing root is worth.
    ///
    /// WHY IT WENT MISSING, and how to avoid losing it again: two Case 3 passes destroy
    /// scene roots wholesale - Case3SceneSetup.ReconstructLayeredScene destroys every root
    /// before rebuilding, and the strip pass removes objects by name. Re-run either and the
    /// navigation root goes with them; re-run this afterwards.
    /// </summary>
    public static class Case3Home
    {
        const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";

        public static void AddHomeButton()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Destroy any previous root before recreating it, so the component can never be
            // carried over with stale serialised data. Lesson #4 of this project.
            int removed = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != MenuNavigation.RootName) continue;
                Object.DestroyImmediate(roots[i]);
                removed++;
            }

            GameObject go = new GameObject(MenuNavigation.RootName);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<MenuNavigation>();
            EditorUtility.SetDirty(go);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(string.Format(
                "[Case3Home] HOME_ROOT_OK scene={0} removedStaleRoots={1} roots={2}",
                scene.name, removed, scene.GetRootGameObjects().Length));
        }
    }
}

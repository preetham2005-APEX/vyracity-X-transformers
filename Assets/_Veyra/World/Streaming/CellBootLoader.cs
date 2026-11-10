using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VeyraCity.World.Streaming
{
    /// <summary>
    /// Card W7 boot scene (build index 0): loads the cell scenes additively, then the main scene, then unloads itself.
    /// Every cell is loaded before the main scene starts, so one-time world scans in Start see the whole city;
    /// <see cref="CellStreamer"/> in the main scene then unloads the far cells. A plain black screen shows a progress bar.
    /// </summary>
    public sealed class CellBootLoader : MonoBehaviour
    {
        public string MainScene = "VeyraCity_Main";
        public string[] CellScenes = new string[0];
        float progress;
        string label = "";

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            float t0 = Time.realtimeSinceStartup;
            Application.backgroundLoadingPriority = ThreadPriority.High;
            for (int i = 0; i < CellScenes.Length; i++)
            {
                if (string.IsNullOrEmpty(CellScenes[i])) continue;
                label = CellScenes[i];
                var op = SceneManager.LoadSceneAsync(CellScenes[i], LoadSceneMode.Additive);
                if (op == null) { Debug.LogError($"[CellBootLoader] {CellScenes[i]} is not in the build"); continue; }
                while (!op.isDone) yield return null;
                // behind the loading screen: wake the cell's objects at once (the streamer time-slices this in play)
                foreach (var r in SceneManager.GetSceneByName(CellScenes[i]).GetRootGameObjects())
                {
                    var content = r.GetComponent<CellContent>();
                    if (content != null) content.ActivateAll();
                }
                progress = (i + 1f) / (CellScenes.Length + 1f);
            }
            label = MainScene;
            var main = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Additive);
            while (!main.isDone) yield return null;
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(MainScene));
            Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
            Debug.Log($"[CellBootLoader] {CellScenes.Length} cells + {MainScene} loaded in {(Time.realtimeSinceStartup - t0):0.0} s");
            Destroy(gameObject);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.buildIndex == 0 && s.isLoaded) { SceneManager.UnloadSceneAsync(s); break; }
            }
        }

        void OnGUI()
        {
            var r = new Rect(Screen.width * 0.3f, Screen.height * 0.8f, Screen.width * 0.4f, 8f);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.75f, 0.35f, 1f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * progress, r.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(r.x, r.y + 12f, r.width, 24f), "VEYRA CITY  " + label);
        }
    }
}

using System;
using UnityEngine;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Silence for automated player runs (user request: "in the game keep 0 volume"). Active when the player is started with
    /// -autotest, -reviewCapture, -veyraTracePSO, -veyraIterShots or -veyraMute: all audio is paused and the listener volume
    /// is held at 0 every frame, so GameFlow's settings (which set AudioListener.volume = 1) cannot turn it back on.
    /// Normal play (no such flag) is unaffected.
    /// </summary>
    public sealed class AutomatedRunMute : MonoBehaviour
    {
        static readonly string[] Flags = { "-autotest", "-reviewCapture", "-veyraTracePSO", "-veyraIterShots", "-veyraMute" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            if (!Array.Exists(args, a => Array.Exists(Flags, f => f.Equals(a, StringComparison.OrdinalIgnoreCase)))) return;
            Silence();
            var go = new GameObject("[AutomatedRunMute]");
            DontDestroyOnLoad(go);
            go.AddComponent<AutomatedRunMute>();
            Debug.Log("[Mute] automated run: audio muted (volume 0, paused)");
        }

        static void Silence()
        {
            AudioListener.volume = 0f;
            AudioListener.pause = true;
        }

        void LateUpdate() => Silence();
    }
}

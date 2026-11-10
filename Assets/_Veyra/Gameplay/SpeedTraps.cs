using System.Collections.Generic;
using UnityEngine;
using VeyraCity.UI;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual iteration 26 (Forza Horizon speed traps): the world data's 10 speed cameras now measure the player. Passing
    /// within 24 m of one fires a camera flash and a HUD banner with the camera's name, the speed, a one to three star
    /// rating (150 / 200 / 250 km/h) and the best of this session. Each camera re-arms after 6 s.
    /// </summary>
    public sealed class SpeedTraps : MonoBehaviour
    {
        public VehicleController Player;
        public HudController Hud;
        sealed class Trap { public string Name; public Vector3 Pos; public float Best; public float Rearm; }
        readonly List<Trap> traps = new List<Trap>();
        public static string LastResult { get; private set; } = "";
        public static int Triggers { get; private set; }

        void Start()
        {
            foreach (var l in WorldData.Locations)
                if (l.category == "Speed trap") traps.Add(new Trap { Name = l.name, Pos = l.Position });
        }

        void Update()
        {
            if (Player == null || !Player.IsInitialised || Hud == null) return;
            var p = Player.transform.position;
            float dt = Time.deltaTime;
            foreach (var t in traps)
            {
                t.Rearm -= dt;
                if (t.Rearm > 0f) continue;
                var d = p - t.Pos;
                d.y *= 0.3f;
                if (d.sqrMagnitude > 24f * 24f) continue;
                float kmh = Player.State.SpeedKmh;
                if (kmh < 40f) continue;
                t.Rearm = 6f;
                bool best = kmh > t.Best;
                if (best) t.Best = kmh;
                int stars = kmh >= 250f ? 3 : kmh >= 200f ? 2 : kmh >= 150f ? 1 : 0;
                string starText = stars == 0 ? "" : "  " + new string('★', stars) + new string('☆', 3 - stars);
                string unit = Hud.Metric ? "KM/H" : "MPH";
                float shown = Hud.Metric ? kmh : kmh * 0.621371f;
                LastResult = $"{t.Name}:{kmh:0}";
                Triggers++;
                Hud.SpeedTrapFlash($"SPEED TRAP  {t.Name.ToUpperInvariant()}", $"{shown:0} {unit}{starText}",
                                   best ? "NEW BEST" : $"BEST {(Hud.Metric ? t.Best : t.Best * 0.621371f):0}", stars);
            }
        }
    }
}

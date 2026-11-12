using System;
using System.Collections.Generic;
using UnityEngine;
using VeyraCity.UI;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Card G5 (round 3): the world's activities as working hooks, driven by Resources/Activities/activities.json (built
    /// from location_registry.csv by Tools/Data/build_activities.py). Speed traps (SpeedTraps) and races (GameFlow) already
    /// exist; this adds:
    /// <list type="bullet">
    /// <item>speed zones: average speed from the start gate to the end gate (registry end point and length);</item>
    /// <item>drift zones: angle x speed x time inside the zone radius, banked when the car leaves it;</item>
    /// <item>stunt jumps: airtime and distance of a jump taken within 40 m of the ramp;</item>
    /// <item>drag strips: stop on the start line facing the end, a 3-2-1 tree, jump-start check, elapsed time and trap
    /// speed over the registry length;</item>
    /// <item>discoveries: viewpoints, photo spots (stop there), hidden shortcuts (drive through), collectible areas,
    /// garages, barn finds, dealerships, delivery hubs, car meets and time-trial starts. Interiors, collectible items,
    /// deliveries and time-trial routes are not built yet: these only mark the place found.</item>
    /// </list>
    /// Each result shows a HUD toast and is saved as an EventRecord "ACT:&lt;id&gt;" (best value in BestTime, attempts in
    /// Starts, found in FirstWinClaimed), so no save-format change.
    /// </summary>
    public sealed class ActivityHooks : MonoBehaviour
    {
        [Serializable] public sealed class Activity
        {
            public string id, name, category;
            public float x, y, z, radius, ex, ez, length;
            public bool hasEnd;
            public Vector3 Pos => new Vector3(x, y, z);
            public Vector3 End => new Vector3(ex, y, ez);
        }
        [Serializable] sealed class ActivityFile { public Activity[] activities; }

        public VehicleController Player;
        public HudController Hud;
        public Func<SaveData> Save;
        public Action Persist;

        public static IReadOnlyList<Activity> All => all;
        public static string LastEvent { get; private set; } = "";
        public static int Events { get; private set; }

        static Activity[] all = Array.Empty<Activity>();
        float scanTimer;

        // the activity in progress (one at a time)
        enum Mode { None, SpeedZone, Drift, Jump, DragStage, DragRun }
        Mode mode;
        Activity cur;
        float t, value, stillTime, aux;
        Vector3 startPos;
        bool airborne;

        public static Activity[] Load()
        {
            var ta = Resources.Load<TextAsset>("Activities/activities");
            if (ta == null) { Debug.LogWarning("[Activities] Resources/Activities/activities.json missing"); return Array.Empty<Activity>(); }
            var f = JsonUtility.FromJson<ActivityFile>(ta.text);
            return f?.activities ?? Array.Empty<Activity>();
        }

        void Start()
        {
            all = Load();
            Debug.Log($"[Activities] {all.Length} activity hooks");
        }

        void Update()
        {
            if (Player == null || !Player.IsInitialised || Hud == null) return;
            float dt = Time.deltaTime;
            Vector3 p = Player.transform.position;
            if (mode != Mode.None) Step(p, dt);
            scanTimer -= dt;
            if (scanTimer > 0f) return;
            scanTimer = 0.2f;
            foreach (var a in all) Check(a, p);
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>Radius used for arming / discovery (registry radius where given, capped for points of interest).</summary>
        public static float TriggerRadius(Activity a) => a.category switch
        {
            "Drift zone" => a.radius > 0f ? a.radius : 150f,
            "Collectible area" => a.radius > 0f ? Mathf.Min(a.radius, 250f) : 150f,
            "Hidden shortcut" => 25f,
            "Stunt jump" => 40f,
            "Speed zone" or "Drag strip" => 30f,
            _ => 35f,
        };

        void Check(Activity a, Vector3 p)
        {
            float d = Flat(p, a.Pos);
            float r = TriggerRadius(a);
            if (d > r) return;
            var st = Player.State;
            switch (a.category)
            {
                case "Speed zone":
                    if (mode == Mode.None && st.SpeedKmh > 30f) Begin(Mode.SpeedZone, a, p);
                    break;
                case "Drift zone":
                    if (mode == Mode.None) Begin(Mode.Drift, a, p);
                    break;
                case "Stunt jump":
                    if (mode == Mode.None && st.SpeedKmh > 40f) { Begin(Mode.Jump, a, p); aux = 3f; }
                    break;
                case "Drag strip":
                    if (mode == Mode.None && a.hasEnd && st.SpeedKmh < 3f) { Begin(Mode.DragStage, a, p); stillTime = 0f; }
                    break;
                case "Photo challenge":
                    if (st.SpeedKmh < 12f) Discover(a, "PHOTO TAKEN");
                    break;
                case "Hidden shortcut":
                    if (st.SpeedKmh > 40f) Discover(a, "SHORTCUT FOUND");
                    break;
                case "Scenic viewpoint": Discover(a, "VIEWPOINT"); break;
                case "Collectible area": Discover(a, "COLLECTIBLE AREA"); break;
                case "Garage": Discover(a, "GARAGE"); break;
                case "Hidden garage / barn find": Discover(a, "BARN FIND"); break;
                case "Dealership": Discover(a, "DEALERSHIP"); break;
                case "Delivery hub": Discover(a, "DELIVERY HUB"); break;
                case "Car meet": Discover(a, "CAR MEET"); break;
                case "Time trial / test": Discover(a, "TIME TRIAL"); break;
            }
        }

        void Begin(Mode m, Activity a, Vector3 p)
        {
            mode = m; cur = a; t = 0f; value = 0f; aux = 0f; airborne = false; startPos = p;
            if (m == Mode.SpeedZone) Toast($"SPEED ZONE  {a.name.ToUpperInvariant()}", UIKit.Accent);
            else if (m == Mode.Drift) Toast($"DRIFT ZONE  {a.name.ToUpperInvariant()}", UIKit.Accent2);
        }

        void Step(Vector3 p, float dt)
        {
            var st = Player.State;
            t += dt;
            switch (mode)
            {
                case Mode.SpeedZone:
                {
                    Vector3 end = cur.hasEnd ? cur.End : cur.Pos;
                    float len = cur.length > 0f ? cur.length : Flat(cur.Pos, end);
                    if (t > Mathf.Max(30f, len / 8f)) { Finish(null); break; }   // gave up (slower than ~29 km/h average)
                    if (cur.hasEnd && Flat(p, end) < 35f && t > 2f)
                    {
                        float avg = len / Mathf.Max(0.1f, t) * 3.6f;
                        bool best = Best(cur, avg, true);
                        Finish($"SPEED ZONE  {Speed(avg)} AVG{(best ? "  NEW BEST" : "")}");
                    }
                    break;
                }
                case Mode.Drift:
                {
                    float angle = Mathf.Abs(st.SlipAngleDeg);
                    if (angle > 12f && angle < 120f && st.SpeedKmh > 30f && st.GroundedWheels >= 2)
                        value += Mathf.Clamp(angle, 12f, 60f) * st.SpeedKmh * dt / 10f;
                    if (Flat(p, cur.Pos) > TriggerRadius(cur) + 10f)
                    {
                        if (value >= 100f)
                        {
                            bool best = Best(cur, value, true);
                            Finish($"DRIFT ZONE  {Mathf.RoundToInt(value):N0} PTS{(best ? "  NEW BEST" : "")}");
                        }
                        else Finish(null);
                    }
                    break;
                }
                case Mode.Jump:
                {
                    if (!airborne)
                    {
                        aux -= dt;
                        if (st.GroundedWheels == 0) { airborne = true; startPos = p; t = 0f; stillTime = 0f; }
                        else if (aux <= 0f) Finish(null);
                    }
                    else
                    {
                        stillTime = st.GroundedWheels >= 2 ? stillTime + dt : 0f;
                        if (stillTime > 0.15f)
                        {
                            float air = t - stillTime, dist = Flat(p, startPos);
                            if (air > 0.5f)
                            {
                                bool best = Best(cur, dist, true);
                                Finish($"STUNT JUMP  {dist:0} M  {air:0.0} S AIR{(best ? "  NEW BEST" : "")}");
                            }
                            else Finish(null);
                        }
                        else if (t > 8f) Finish(null);
                    }
                    break;
                }
                case Mode.DragStage:
                {
                    Vector3 dir = cur.End - cur.Pos; dir.y = 0f; dir.Normalize();
                    bool facing = Vector3.Dot(Player.transform.forward, dir) > 0.85f;
                    if (Flat(p, cur.Pos) > 35f) { Finish(null); break; }
                    if (st.SpeedKmh < 1f && facing) stillTime += dt; else if (aux <= 0f) stillTime = 0f;
                    if (stillTime > 1f && aux <= 0f)
                    {
                        aux = 3f;   // the tree: 3 s of amber, then green
                        startPos = p;
                        Toast($"DRAG  {cur.name.ToUpperInvariant()}  STAGED", UIKit.Amber, 1f);
                    }
                    if (aux > 0f)
                    {
                        float before = aux;
                        aux -= dt;
                        if (Mathf.Ceil(aux) < Mathf.Ceil(before) && aux > 0f) Toast(Mathf.CeilToInt(aux).ToString(), UIKit.Amber, 0.8f);
                        if (Flat(p, startPos) > 1.5f) { Finish("DRAG  JUMP START"); break; }
                        if (aux <= 0f) { mode = Mode.DragRun; t = 0f; value = 0f; Toast("GO", UIKit.Good, 0.8f); }
                    }
                    break;
                }
                case Mode.DragRun:
                {
                    Vector3 dir = cur.End - cur.Pos; dir.y = 0f;
                    float len = cur.length > 0f ? cur.length : dir.magnitude;
                    dir.Normalize();
                    Vector3 rel = p - startPos; rel.y = 0f;
                    float along = Vector3.Dot(rel, dir), lateral = (rel - dir * along).magnitude;
                    if (lateral > 40f || t > 60f) { Finish("DRAG  DNF"); break; }
                    if (along >= len)
                    {
                        bool best = Best(cur, t, false);
                        Finish($"DRAG  {t:0.000} S  TRAP {Speed(st.SpeedKmh)}{(best ? "  NEW BEST" : "")}");
                    }
                    break;
                }
            }
        }

        string Speed(float kmh) => Hud.Metric ? $"{kmh:0} KM/H" : $"{kmh * 0.621371f:0} MPH";

        void Finish(string result)
        {
            if (result != null && cur != null)
            {
                var rec = Record(cur, true);
                if (rec != null) rec.Starts++;   // completed attempts (including jump starts and DNFs)
                Toast(result, UIKit.Good, 3f);
                LastEvent = $"{cur.id}:{result}";
                Events++;
                Persist?.Invoke();
            }
            mode = Mode.None;
            cur = null;
        }

        void Discover(Activity a, string label)
        {
            var rec = Record(a, true);
            if (rec == null || rec.FirstWinClaimed) return;
            rec.FirstWinClaimed = true;
            Toast($"{label} FOUND  {a.name.ToUpperInvariant()}", UIKit.Accent, 2.6f);
            LastEvent = $"{a.id}:found";
            Events++;
            Persist?.Invoke();
        }

        EventRecord Record(Activity a, bool create) => Save?.Invoke()?.Record("ACT:" + a.id, create);

        /// <summary>Stores the value if it beats the saved best (higher, or lower when <paramref name="higherIsBetter"/> is
        /// false); returns whether it did.</summary>
        bool Best(Activity a, float v, bool higherIsBetter)
        {
            var rec = Record(a, true);
            if (rec == null) return false;
            rec.FirstWinClaimed = true;
            bool better = rec.BestTime <= 0f || (higherIsBetter ? v > rec.BestTime : v < rec.BestTime);
            if (better) rec.BestTime = v;
            return better;
        }

        void Toast(string text, Color c, float seconds = 2.2f) => Hud.ShowToast(text, c, seconds);
    }
}

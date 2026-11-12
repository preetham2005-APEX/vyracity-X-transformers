using System;
using System.Collections.Generic;
using UnityEngine;
using VeyraCity.UI;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

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
            public string route;   // time trials: the race route on the same road (build_activities.py)
            public int count;      // collectible areas: items in the area
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
        enum Mode { None, SpeedZone, Drift, Jump, DragStage, DragRun, RouteTrial, TopSpeed, Accel, DeliveryStage, DeliveryRun }
        Mode mode;
        Activity cur;
        float t, value, stillTime, aux;
        Vector3 startPos;
        bool airborne;
        // route trials
        RoutePath trialRoute;
        int trialHint;
        float trialS, trialProgress, offRoute;
        // acceleration test / delivery
        float t100, t200, brakeFrom = -1f, damageAtStart;
        Vector3 brakePos;
        Activity target;
        float limit;
        WorldMap map;
        CarDamage damage;

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
            if (mode == Mode.None) CheckRouteTrials(p);
            UpdateCollectibles(p);
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
                case "Delivery hub":
                    Discover(a, "DELIVERY HUB");
                    if (mode == Mode.None && st.SpeedKmh < 3f) { Begin(Mode.DeliveryStage, a, p); stillTime = 0f; }
                    break;
                case "Car meet": Discover(a, "CAR MEET"); break;
                case "Time trial / test":
                    Discover(a, "TIME TRIAL");
                    // the airfield tests start from a standstill at their marker (registry: TT-03 top speed, TT-04 0-100/0-200/100-0)
                    if (mode == Mode.None && st.SpeedKmh < 3f && a.id == "TT-03") { Begin(Mode.TopSpeed, a, p); Toast("TOP SPEED RUN  GATE AT 1.5 KM", UIKit.Amber); }
                    if (mode == Mode.None && st.SpeedKmh < 3f && a.id == "TT-04") { Begin(Mode.Accel, a, p); t100 = t200 = 0f; brakeFrom = -1f; Toast("ACCELERATION TEST  0-100 / 0-200 / 100-0", UIKit.Amber); }
                    break;
            }
        }

        /// <summary>Route time trials (registry TT rows whose road has a race route): start by driving through the route's
        /// first gate in its direction; a closed route (the Ring) is a flying lap.</summary>
        void CheckRouteTrials(Vector3 p)
        {
            foreach (var a in all)
            {
                if (string.IsNullOrEmpty(a.route) || a.category != "Time trial / test") continue;
                var rp = FindRoute(a.route);
                if (rp == null) continue;
                Vector3 s0 = rp.Path.PositionAt(0f);
                if (Flat(p, s0) > 30f || Player.State.SpeedKmh < 15f) continue;
                Vector3 tan = rp.Path.TangentAt(0f);
                if (Vector3.Dot(Player.Body.linearVelocity.normalized, tan) < 0.5f) continue;
                Begin(Mode.RouteTrial, a, p);
                trialRoute = rp; trialHint = 0; trialS = 0f; trialProgress = 0f; offRoute = 0f;
                Toast($"TIME TRIAL  {a.name.ToUpperInvariant()}  {rp.Path.Length / 1000f:0.0} KM", UIKit.Accent);
                return;
            }
        }

        public static RoutePath FindRoute(string id)
        {
            foreach (var r in WorldData.Routes) if (r.Data.id == id) return r;
            return null;
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
                case Mode.RouteTrial:
                {
                    var path = trialRoute.Path;
                    float sNow = path.Project(p, ref trialHint);
                    float d = sNow - trialS;
                    if (path.Closed) { if (d > path.Length * 0.5f) d -= path.Length; else if (d < -path.Length * 0.5f) d += path.Length; }
                    trialProgress += Mathf.Clamp(d, -60f, 60f);
                    trialS = sNow;
                    offRoute = Flat(p, path.PositionAt(sNow)) > 60f ? offRoute + dt : 0f;
                    if (offRoute > 5f) { Finish("TIME TRIAL  OFF ROUTE"); break; }
                    if (t > path.Length / 5f) { Finish(null); break; }
                    if (trialProgress >= path.Length - 25f)
                    {
                        bool best = Best(cur, t, false);
                        Finish($"TIME TRIAL  {UIKit.FormatTime(t)}{(best ? "  NEW BEST" : "")}");
                    }
                    break;
                }
                case Mode.TopSpeed:
                {
                    float dist = Flat(p, startPos);
                    if (t > 90f || (t > 6f && st.SpeedKmh < 3f && dist < 1500f)) { Finish(null); break; }
                    if (dist >= 1500f)
                    {
                        bool best = Best(cur, st.SpeedKmh, true);
                        Finish($"TOP SPEED  {Speed(st.SpeedKmh)} AT 1.5 KM{(best ? "  NEW BEST" : "")}");
                    }
                    break;
                }
                case Mode.Accel:
                {
                    if (st.SpeedKmh < 1f && t100 <= 0f && value <= 0f) { t = 0f; if (Flat(p, cur.Pos) > 40f) Finish(null); break; }   // the clock starts when the car moves
                    if (t100 <= 0f && st.SpeedKmh >= 100f) t100 = t;
                    if (t200 <= 0f && st.SpeedKmh >= 200f) t200 = t;
                    value = Mathf.Max(value, st.SpeedKmh);
                    if (brakeFrom < 0f && value >= 100f && st.SpeedKmh <= 100f && Player.Controls.Brake > 0.5f) { brakeFrom = t; brakePos = p; }
                    bool stoppedAfterBrake = brakeFrom >= 0f && st.SpeedKmh < 1f;
                    if (stoppedAfterBrake || t > 60f)
                    {
                        if (t100 <= 0f) { Finish(null); break; }
                        bool best = Best(cur, t100, false);
                        string r = $"0-100 {t100:0.00} S";
                        if (t200 > 0f) r += $"  0-200 {t200:0.00} S";
                        if (stoppedAfterBrake) r += $"  100-0 {Flat(p, brakePos):0.0} M";
                        Finish(r + (best ? "  NEW BEST" : ""));
                    }
                    break;
                }
                case Mode.DeliveryStage:
                {
                    if (Flat(p, cur.Pos) > 40f) { Finish(null); break; }
                    stillTime = st.SpeedKmh < 3f ? stillTime + dt : 0f;
                    if (stillTime < 1.5f) break;
                    target = PickDeliveryTarget(cur);
                    if (target == null) { Finish(null); break; }
                    var line = new List<Vector3>();
                    float len = RoadGraph.Route(p, target.Pos, line, out float l) ? l : Flat(p, target.Pos) * 1.4f;
                    limit = len / 13f + 25f;   // about 47 km/h average plus loading time
                    if (map == null) map = FindAnyObjectByType<WorldMap>();
                    if (map != null) map.RouteTo(target.Pos);
                    if (damage == null) damage = Player.GetComponent<CarDamage>();
                    damageAtStart = damage != null ? damage.Damage01 : 0f;
                    mode = Mode.DeliveryRun; t = 0f;
                    Toast($"DELIVERY  TO {target.name.ToUpperInvariant()}  {UIKit.FormatTime(limit)}{(Fragile(cur) ? "  FRAGILE" : "")}", UIKit.Amber, 3f);
                    break;
                }
                case Mode.DeliveryRun:
                {
                    if (t > limit * 1.5f) { Finish("DELIVERY  FAILED (LATE)"); break; }
                    if (Flat(p, target.Pos) > 35f || st.SpeedKmh > 20f) break;
                    float hit = damage != null ? damage.Damage01 - damageAtStart : 0f;
                    if (Fragile(cur) && hit > 0.1f) { Finish("DELIVERY  CARGO DAMAGED"); break; }
                    bool onTime = t <= limit;
                    bool best = onTime && Best(cur, t, false);
                    Finish($"DELIVERED  {UIKit.FormatTime(t)}{(onTime ? "" : "  LATE")}{(best ? "  NEW BEST" : "")}");
                    break;
                }
            }
        }

        /// <summary>Millbrook's produce runs carry fragile cargo (registry DEL-04): 10 % new damage fails the job.</summary>
        static bool Fragile(Activity a) => a.id == "DEL-04";

        /// <summary>The next hub in a fixed rotation (by the jobs already run from this hub), never the same hub.</summary>
        Activity PickDeliveryTarget(Activity from)
        {
            var hubs = new List<Activity>();
            foreach (var a in all) if (a.category == "Delivery hub" && a != from) hubs.Add(a);
            if (hubs.Count == 0) return null;
            var rec = Record(from, true);
            int k = rec != null ? rec.Starts : 0;
            foreach (char ch in from.id) k += ch;
            return hubs[k % hubs.Count];
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

        // ------------------------------------------------------------------ collectibles
        // Each collectible area places its registry count of glowing pickups beside the roads inside its radius
        // (deterministic per area); collected items are a bit mask in the area's save record (BestTime).

        sealed class Pickup { public int Index; public GameObject Go; public Vector3 Pos; }
        readonly Dictionary<string, List<Pickup>> spawned = new Dictionary<string, List<Pickup>>();
        static Material pickupMat;

        /// <summary>Pickup positions for an area: road points inside its radius, at least 40 m apart, beside the carriageway.</summary>
        public static List<Vector3> CollectiblePositions(Activity a)
        {
            var cands = new List<Vector3>();
            float r = a.radius > 0f ? a.radius : 300f;
            Vector3 c = a.Pos;
            foreach (var road in WorldData.Roads)
            {
                var pts = road.Path.Points;
                for (int i = 0; i < pts.Length; i += 3)
                {
                    if (Flat(pts[i], c) > r) continue;
                    int j = Mathf.Min(i + 1, pts.Length - 1), h = Mathf.Max(0, j - 1);
                    Vector3 tan = pts[j] - pts[h]; tan.y = 0f;
                    if (tan.sqrMagnitude < 1e-4f) continue;
                    tan.Normalize();
                    Vector3 right = new Vector3(tan.z, 0f, -tan.x);
                    cands.Add(pts[i] + right * (road.HalfWidth + 2f) * ((i / 3) % 2 == 0 ? 1f : -1f));
                }
            }
            // deterministic shuffle by the area id, then keep points 40 m apart
            uint seed = 2166136261u;
            foreach (char ch in a.id) seed = (seed ^ ch) * 16777619u;
            if (seed == 0) seed = 1;
            for (int i = cands.Count - 1; i > 0; i--)
            {
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                int k = (int)(seed % (uint)(i + 1));
                (cands[i], cands[k]) = (cands[k], cands[i]);
            }
            var outList = new List<Vector3>();
            foreach (var q in cands)
            {
                bool far = true;
                foreach (var o in outList) if (Flat(o, q) < 40f) { far = false; break; }
                if (!far) continue;
                outList.Add(q);
                if (outList.Count >= Mathf.Max(1, a.count)) break;
            }
            return outList;
        }

        void UpdateCollectibles(Vector3 p)
        {
            foreach (var a in all)
            {
                if (a.category != "Collectible area") continue;
                float d = Flat(p, a.Pos), r = a.radius > 0f ? a.radius : 300f;
                bool have = spawned.ContainsKey(a.id);
                if (!have && d < r + 150f) Spawn(a);
                else if (have && d > r + 400f) Despawn(a.id);
            }
        }

        void Spawn(Activity a)
        {
            var rec = Record(a, true);
            int mask = rec != null ? (int)rec.BestTime : 0;
            if (pickupMat == null)
            {
                pickupMat = RuntimeMaterials.NewUnlitTransparent("CollectiblePickup", true);
                if (pickupMat != null) pickupMat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.2f, 0.9f));
            }
            var list = new List<Pickup>();
            var pos = CollectiblePositions(a);
            for (int i = 0; i < pos.Count; i++)
            {
                if ((mask & (1 << i)) != 0) continue;
                var q = pos[i];
                q.y = WorldData.GroundHeight(q + Vector3.up * 50f, q.y) + 1.1f;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.name = $"Collectible_{a.id}_{i}";
                go.transform.SetParent(transform, false);
                go.transform.position = q;
                go.transform.localScale = Vector3.one * 0.7f;
                var rend = go.GetComponent<MeshRenderer>();
                if (pickupMat != null) rend.sharedMaterial = pickupMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                list.Add(new Pickup { Index = i, Go = go, Pos = q });
            }
            spawned[a.id] = list;
        }

        void Despawn(string id)
        {
            if (!spawned.TryGetValue(id, out var list)) return;
            foreach (var k in list) if (k.Go != null) Destroy(k.Go);
            spawned.Remove(id);
        }

        void LateUpdate()
        {
            if (Player == null || spawned.Count == 0) return;
            Vector3 p = Player.transform.position;
            float bob = Mathf.Sin(Time.time * 2.4f) * 0.15f;
            string hitArea = null; Pickup hit = null;
            foreach (var kv in spawned)
            {
                foreach (var k in kv.Value)
                {
                    if (k.Go == null) continue;
                    k.Go.transform.position = k.Pos + Vector3.up * bob;
                    if (hit == null && Flat(p, k.Pos) <= 4.5f && Mathf.Abs(p.y - k.Pos.y) <= 4f) { hitArea = kv.Key; hit = k; }
                }
            }
            if (hit != null)
            {
                Collect(hitArea, hit);
                Destroy(hit.Go);
                spawned[hitArea].Remove(hit);
            }
        }

        void Collect(string areaId, Pickup k)
        {
            Activity a = null;
            foreach (var x in all) if (x.id == areaId) { a = x; break; }
            if (a == null) return;
            var rec = Record(a, true);
            if (rec == null) return;
            int mask = (int)rec.BestTime | (1 << k.Index);
            rec.BestTime = mask;
            int got = 0;
            for (int b = 0; b < 24; b++) if ((mask & (1 << b)) != 0) got++;
            int total = Mathf.Max(1, CollectiblePositions(a).Count);
            Toast(got >= total ? $"{a.name.ToUpperInvariant()}  COMPLETE" : $"{a.name.ToUpperInvariant()}  {got}/{total}", UIKit.Amber, 2f);
            LastEvent = $"{a.id}:item{k.Index}";
            Events++;
            Persist?.Invoke();
        }

        void OnDestroy()
        {
            foreach (var id in new List<string>(spawned.Keys)) Despawn(id);
        }
    }
}

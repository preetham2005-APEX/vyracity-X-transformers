using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VeyraCity.Nitro;
using VeyraCity.Racing;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.UI
{
    /// <summary>In-game HUD: speed, gear, revs, nitro, race info, drift chains, toasts and a rotating minimap.</summary>
    public sealed class HudController : MonoBehaviour
    {
        public bool Metric = true;
        public float MinimapRange = 520f;

        Canvas canvas;
        RectTransform root;
        Text speedText, unitText, gearText, nitroLabel, positionText, lapText, timeText, bestText, driftText, bigText, promptText, walletText, districtText;
        Image rpmFill, nitroFill, nitroWindow, boostGlow, minimapImageMask;
        RectTransform racePanel, minimapRoot, mapRotator, playerArrow, markerLayer, toastLayer;
        RawImage mapImage;
        readonly List<Image> markerPool = new List<Image>();
        readonly List<(Vector3 pos, Color color, float size)> staticMarkers = new List<(Vector3, Color, float)>();
        readonly List<Toast> toasts = new List<Toast>();

        VehicleController vehicle;
        NitroSystem nitro;
        DriftScorer drift;
        RaceSession race;
        float bigTimer, driftFlashTimer;
        string lastDistrict;
        float districtTimer, districtCheck;
        static Texture2D mapTexture;

        sealed class Toast { public Text Text; public float Life; }

        public bool Visible
        {
            get => canvas != null && canvas.enabled;
            set { if (canvas != null) canvas.enabled = value; }
        }

        public void Build()
        {
            if (canvas != null) return;
            canvas = UIKit.CreateCanvas("HUD", 10, transform);
            root = (RectTransform)canvas.transform;

            // --- Speed cluster (bottom right)
            var cluster = UIKit.Box(root, "Cluster", new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(430f, 230f));
            UIKit.Image(cluster, UIKit.Panel);
            boostGlow = UIKit.Image(UIKit.Rect(cluster, "BoostGlow", Vector2.zero, Vector2.one, new Vector2(-6, -6), new Vector2(6, 6)), new Color(1f, 0.6f, 0.1f, 0f));
            boostGlow.transform.SetAsFirstSibling();
            speedText = UIKit.Text(cluster, "Speed", "0", 96, TextAnchor.MiddleRight, UIKit.TextMain, FontStyle.Bold);
            Place(speedText.rectTransform, new Vector2(0f, 0.38f), new Vector2(0.66f, 1f));
            unitText = UIKit.Text(cluster, "Unit", "KM/H", 22, TextAnchor.UpperLeft, UIKit.TextDim, FontStyle.Bold);
            Place(unitText.rectTransform, new Vector2(0.68f, 0.52f), new Vector2(1f, 0.72f));
            gearText = UIKit.Text(cluster, "Gear", "N", 64, TextAnchor.MiddleCenter, UIKit.Accent, FontStyle.Bold);
            Place(gearText.rectTransform, new Vector2(0.68f, 0.70f), new Vector2(0.95f, 1f));
            var rpm = UIKit.Rect(cluster, "Rpm", new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.38f));
            UIKit.Bar(rpm, "RpmBar", new Color(1, 1, 1, 0.08f), UIKit.Accent, out rpmFill);
            var nb = UIKit.Rect(cluster, "Nitro", new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.22f));
            UIKit.Bar(nb, "NitroBar", new Color(1, 1, 1, 0.08f), UIKit.Amber, out nitroFill);
            nitroWindow = UIKit.Image(UIKit.Rect(nb, "CleanWindow", new Vector2(0f, -0.4f), new Vector2(1f, 1.4f)), new Color(0.3f, 1f, 0.6f, 0f));
            nitroLabel = UIKit.Text(cluster, "NitroLabel", "NITRO", 18, TextAnchor.LowerLeft, UIKit.Amber, FontStyle.Bold);
            Place(nitroLabel.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.31f));

            // --- Race panel (top left)
            racePanel = UIKit.Box(root, "Race", new Vector2(0f, 1f), new Vector2(36f, -30f), new Vector2(380f, 210f));
            UIKit.Image(racePanel, UIKit.Panel);
            positionText = UIKit.Text(racePanel, "Pos", "1ST", 70, TextAnchor.UpperLeft, UIKit.TextMain, FontStyle.Bold);
            Place(positionText.rectTransform, new Vector2(0.06f, 0.48f), new Vector2(0.6f, 0.98f));
            lapText = UIKit.Text(racePanel, "Lap", "LAP 1/3", 28, TextAnchor.UpperRight, UIKit.Accent, FontStyle.Bold);
            Place(lapText.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(0.95f, 0.95f));
            timeText = UIKit.Text(racePanel, "Time", "00:00.000", 34, TextAnchor.MiddleLeft, UIKit.TextMain, FontStyle.Bold);
            Place(timeText.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.95f, 0.46f));
            bestText = UIKit.Text(racePanel, "Best", "", 22, TextAnchor.MiddleLeft, UIKit.TextDim);
            Place(bestText.rectTransform, new Vector2(0.06f, 0.02f), new Vector2(0.95f, 0.22f));
            racePanel.gameObject.SetActive(false);

            // --- Wallet (top right)
            var wallet = UIKit.Box(root, "Wallet", new Vector2(1f, 1f), new Vector2(-36f, -30f), new Vector2(360f, 70f));
            UIKit.Image(wallet, UIKit.Panel);
            walletText = UIKit.Text(wallet, "Text", "", 28, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);

            // --- Centre texts
            bigText = UIKit.Text(root, "Big", "", 150, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);
            Place(bigText.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            driftText = UIKit.Text(root, "Drift", "", 44, TextAnchor.MiddleCenter, UIKit.Accent2, FontStyle.Bold);
            Place(driftText.rectTransform, new Vector2(0.25f, 0.70f), new Vector2(0.75f, 0.80f));
            promptText = UIKit.Text(root, "Prompt", "", 32, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);
            Place(promptText.rectTransform, new Vector2(0.2f, 0.18f), new Vector2(0.8f, 0.26f));
            districtText = UIKit.Text(root, "District", "", 40, TextAnchor.MiddleCenter, UIKit.Accent, FontStyle.Bold);
            Place(districtText.rectTransform, new Vector2(0.2f, 0.86f), new Vector2(0.8f, 0.94f));
            toastLayer = UIKit.Rect(root, "Toasts", new Vector2(0.3f, 0.585f), new Vector2(0.7f, 0.70f));   // above the car, under the drift chain (iteration 4: they covered the car)

            BuildMinimap();
        }

        static void Place(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void BuildMinimap()
        {
            minimapRoot = UIKit.Box(root, "Minimap", new Vector2(0f, 0f), new Vector2(36f, 36f), new Vector2(300f, 300f));
            var frame = UIKit.Image(minimapRoot, new Color(0.02f, 0.03f, 0.05f, 0.85f), UIKit.Circle);
            var maskRt = UIKit.Rect(minimapRoot, "Mask", Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
            minimapImageMask = maskRt.gameObject.AddComponent<Image>();
            minimapImageMask.sprite = UIKit.Circle;
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            mapRotator = UIKit.Rect(maskRt, "Rotator", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            mapRotator.sizeDelta = new Vector2(288f * 1.5f, 288f * 1.5f);
            var imgRt = UIKit.Rect(mapRotator, "Map", Vector2.zero, Vector2.one);
            mapImage = imgRt.gameObject.AddComponent<RawImage>();
            mapImage.raycastTarget = false;
            mapImage.texture = MapTexture();
            markerLayer = UIKit.Rect(maskRt, "Markers", Vector2.zero, Vector2.one);
            var ring = UIKit.Image(UIKit.Rect(minimapRoot, "Ring", Vector2.zero, Vector2.one), new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.7f), UIKit.Ring);
            playerArrow = UIKit.Box(minimapRoot, "Player", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 26f));
            var arrow = UIKit.Text(playerArrow, "Glyph", "▲", 26, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var north = UIKit.Text(UIKit.Box(minimapRoot, "N", new Vector2(0.5f, 1f), new Vector2(0, 4), new Vector2(30, 30)), "T", "N", 20, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            north.transform.parent.name = "North";
        }

        /// <summary>Road map of the whole city rendered once from the road network (3.9 m per pixel).</summary>
        public static Texture2D MapTexture()
        {
            if (mapTexture != null) return mapTexture;
            const int W = 2048, H = 1536;
            var px = new Color32[W * H];
            var bg = new Color32(10, 14, 24, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var b = WorldData.Bounds;
            // districts as faint tints
            var ds = WorldData.Districts;
            if (ds?.districts != null)
                for (int y = 0; y < H; y += 4)
                    for (int x = 0; x < W; x += 4)
                    {
                        float wx = b.xMin + (x + 2f) / W * b.width, wz = b.yMin + (y + 2f) / H * b.height;
                        foreach (var d in ds.districts)
                        {
                            if (!WorldData.PointInPolygon(d.polygon, wx, wz)) continue;
                            var tint = Color32.Lerp(bg, (Color32)WorldData.ParseColor(d.color, Color.gray), 0.10f);
                            for (int yy = y; yy < y + 4 && yy < H; yy++)
                                for (int xx = x; xx < x + 4 && xx < W; xx++) px[yy * W + xx] = tint;
                            break;
                        }
                    }
            foreach (var road in WorldData.Roads)
            {
                var cls = road.Data.cls;
                Color32 col = cls == "MW" || cls == "EX" ? new Color32(255, 176, 60, 255)
                    : cls == "HW" || cls == "AR" ? new Color32(220, 226, 236, 255)
                    : cls == "GR" ? new Color32(150, 120, 90, 255)
                    : cls == "RW" ? new Color32(90, 100, 120, 255)
                    : new Color32(150, 160, 175, 255);
                float wpx = Mathf.Max(1.6f, road.Data.width / (b.width / W) * 0.55f);
                var pts = road.Path.Points;
                int n = road.Path.Closed ? pts.Length : pts.Length - 1;
                for (int i = 0; i < n; i++)
                {
                    var p0 = pts[i];
                    var p1 = pts[(i + 1) % pts.Length];
                    DrawLine(px, W, H, (p0.x - b.xMin) / b.width * W, (p0.z - b.yMin) / b.height * H,
                             (p1.x - b.xMin) / b.width * W, (p1.z - b.yMin) / b.height * H, wpx, col);
                }
            }
            mapTexture = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "CityMap" };
            mapTexture.SetPixels32(px);
            mapTexture.Apply(true, true);
            return mapTexture;
        }

        static void DrawLine(Color32[] px, int W, int H, float x0, float y0, float x1, float y1, float width, Color32 c)
        {
            float len = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            int steps = Mathf.Max(1, Mathf.CeilToInt(len));
            int r = Mathf.CeilToInt(width * 0.5f);
            float r2 = width * width * 0.25f;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float cx = Mathf.Lerp(x0, x1, t), cy = Mathf.Lerp(y0, y1, t);
                int ix = Mathf.RoundToInt(cx), iy = Mathf.RoundToInt(cy);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r2 + 0.5f) continue;
                        int x = ix + dx, y = iy + dy;
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        px[y * W + x] = c;
                    }
            }
        }

        public void Bind(VehicleController v, NitroSystem n, DriftScorer d)
        {
            if (drift != null) { drift.ChainBanked -= OnDriftBanked; drift.ChainFailed -= OnDriftFailed; }
            if (nitro != null) nitro.ModeStarted -= OnNitroMode;
            vehicle = v;
            nitro = n;
            drift = d;
            if (drift != null) { drift.ChainBanked += OnDriftBanked; drift.ChainFailed += OnDriftFailed; }
            if (nitro != null) nitro.ModeStarted += OnNitroMode;
        }

        void OnNitroMode(NitroMode m)
        {
            if (m == NitroMode.CleanBoost) ShowToast("CLEAN BOOST", UIKit.Good);
            else if (m == NitroMode.Overdrive) ShowToast("OVERDRIVE", UIKit.Accent2);
        }

        void OnDriftBanked(float points) { ShowToast("DRIFT +" + Mathf.RoundToInt(points).ToString("N0"), UIKit.Accent); driftFlashTimer = 0f; }
        void OnDriftFailed(float points) { if (points > 500f) ShowToast("DRIFT LOST", UIKit.Bad); }

        public void SetRace(RaceSession s)
        {
            race = s;
            if (racePanel != null) racePanel.gameObject.SetActive(s != null);
        }

        public void SetPrompt(string text)
        {
            if (promptText != null) promptText.text = text ?? "";
        }

        public void SetWallet(string text)
        {
            if (walletText != null) walletText.text = text;
        }

        public void SetStaticMarkers(IEnumerable<(Vector3 pos, Color color, float size)> markers)
        {
            staticMarkers.Clear();
            if (markers != null) staticMarkers.AddRange(markers);
        }

        public void BigMessage(string text, Color color, float seconds)
        {
            if (bigText == null) return;
            bigText.text = text;
            bigText.color = color;
            bigTimer = seconds;
        }

        public void ShowToast(string text, Color color, float seconds = 2.2f)
        {
            if (toastLayer == null) return;
            var t = UIKit.Text(toastLayer, "Toast", text, 34, TextAnchor.MiddleCenter, color, FontStyle.Bold);
            t.rectTransform.anchorMin = new Vector2(0f, 0f);
            t.rectTransform.anchorMax = new Vector2(1f, 0f);
            t.rectTransform.sizeDelta = new Vector2(0f, 54f);
            toasts.Add(new Toast { Text = t, Life = seconds });
            if (toasts.Count > 4) { Destroy(toasts[0].Text.gameObject); toasts.RemoveAt(0); }
        }

        void Update()
        {
            if (canvas == null) return;
            float dt = Time.unscaledDeltaTime;
            UpdateCluster();
            UpdateRace();
            UpdateDrift();
            UpdateMinimap();
            UpdateDistrict(dt);

            if (bigTimer > 0f)
            {
                bigTimer -= dt;
                var c = bigText.color;
                c.a = Mathf.Clamp01(bigTimer * 3f);
                bigText.color = c;
                if (bigTimer <= 0f) bigText.text = "";
            }
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                t.Life -= dt;
                int idx = toasts.Count - 1 - i;
                var rt = t.Text.rectTransform;
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0f, 24f + idx * 44f), dt * 10f);
                var c = t.Text.color;
                c.a = Mathf.Clamp01(t.Life * 2f);
                t.Text.color = c;
                if (t.Life <= 0f) { Destroy(t.Text.gameObject); toasts.RemoveAt(i); }
            }
        }

        void UpdateCluster()
        {
            if (vehicle == null || !vehicle.IsInitialised) return;
            var st = vehicle.State;
            float spd = Metric ? st.SpeedKmh : st.SpeedKmh * 0.621371f;
            speedText.text = Mathf.RoundToInt(Mathf.Abs(spd)).ToString();
            unitText.text = Metric ? "KM/H" : "MPH";
            gearText.text = st.Gear < 0 ? "R" : st.Gear == 0 ? "N" : st.Gear.ToString();
            float lim = vehicle.Definition.Engine.LimiterRpm;
            float r = Mathf.Clamp01(st.EngineRpm / Mathf.Max(1000f, lim));
            rpmFill.fillAmount = r;
            rpmFill.color = r > 0.9f ? UIKit.Bad : Color.Lerp(UIKit.Accent, UIKit.Accent2, Mathf.InverseLerp(0.6f, 0.9f, r));
            if (nitro != null)
            {
                nitroFill.fillAmount = nitro.Amount / NitroSystem.MeterMax;
                bool full = nitro.Amount >= NitroSystem.MeterMax - 0.01f;
                nitroFill.color = nitro.Mode == NitroMode.Overdrive ? UIKit.Accent2 : nitro.Mode == NitroMode.CleanBoost ? UIKit.Good : full ? Color.Lerp(UIKit.Amber, Color.white, Mathf.PingPong(Time.unscaledTime * 2f, 0.5f)) : UIKit.Amber;
                var wc = nitroWindow.color;
                wc.a = nitro.CleanWindowOpenNow ? 0.55f : 0f;
                nitroWindow.color = wc;
                nitroLabel.text = nitro.Mode == NitroMode.Overdrive ? "OVERDRIVE" : nitro.Mode == NitroMode.CleanBoost ? "CLEAN BOOST" : nitro.CleanWindowOpenNow ? "TAP AGAIN!" : full ? "NITRO FULL  -  DOUBLE-TAP FOR OVERDRIVE" : "NITRO";
                var g = boostGlow.color;
                g.a = nitro.BoostScalar * 0.5f;
                g.r = nitro.Mode == NitroMode.Overdrive ? 1f : 1f;
                g.g = nitro.Mode == NitroMode.Overdrive ? 0.25f : 0.6f;
                g.b = nitro.Mode == NitroMode.Overdrive ? 0.62f : 0.1f;
                boostGlow.color = g;
            }
        }

        void UpdateRace()
        {
            if (race == null || race.Player == null) return;
            var p = race.Player;
            int n = race.Participants.Count;
            positionText.text = n > 1 && p.Position > 0 ? $"{UIKit.Ordinal(p.Position)}<size=34>/{n}</size>" : "";
            positionText.supportRichText = true;
            lapText.text = race.IsCircuit ? $"LAP {Mathf.Min(p.Lap + 1, race.TotalLaps)}/{race.TotalLaps}" : $"CP {p.NextCheckpoint}/{race.Checkpoints.Count}";
            float t = race.State == RaceSession.Phase.Racing || race.State == RaceSession.Phase.PlayerFinished ? (p.Finished ? p.FinishTime : race.Clock) : 0f;
            timeText.text = UIKit.FormatTime(t);
            bestText.text = race.IsCircuit && p.BestLap < float.MaxValue ? "BEST LAP  " + UIKit.FormatTime(p.BestLap) : (p.WrongWay ? "" : $"NEXT CHECKPOINT  {Mathf.RoundToInt(race.DistanceToNextCheckpoint(p))} m");
            if (p.WrongWay && bigTimer <= 0f) BigMessage("WRONG WAY", UIKit.Bad, 0.3f);
        }

        void UpdateDrift()
        {
            if (drift == null) return;
            if (drift.ChainActive && drift.ChainPoints > 1f)
            {
                driftText.text = $"DRIFT  {Mathf.RoundToInt(drift.ChainPoints):N0}  x{drift.Multiplier:0}";
                driftText.color = drift.Circling ? UIKit.TextDim : UIKit.Accent2;
            }
            else driftText.text = "";
        }

        void UpdateMinimap()
        {
            if (vehicle == null || mapImage == null) return;
            var b = WorldData.Bounds;
            var pos = vehicle.transform.position;
            float range = MinimapRange * (1f + Mathf.Clamp01(vehicle.State.Speed / 60f) * 0.6f);
            float span = range * 1.5f;
            mapImage.uvRect = new Rect((pos.x - span * 0.5f - b.xMin) / b.width, (pos.z - span * 0.5f - b.yMin) / b.height, span / b.width, span / b.height);
            float heading = vehicle.transform.eulerAngles.y;
            mapRotator.localRotation = Quaternion.Euler(0f, 0f, heading);
            float scale = 288f / range;
            int used = 0;
            void Marker(Vector3 world, Color c, float size)
            {
                var d = world - pos;
                var v2 = Quaternion.Euler(0f, 0f, heading) * new Vector3(d.x, d.z, 0f) * scale;
                float lim = 140f;
                if (v2.magnitude > lim) v2 = v2.normalized * lim;
                if (used >= markerPool.Count)
                {
                    var rt = UIKit.Box(markerLayer, "M", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
                    markerPool.Add(UIKit.Image(rt, Color.white, UIKit.Circle));
                }
                var img = markerPool[used++];
                img.gameObject.SetActive(true);
                img.color = c;
                img.rectTransform.anchoredPosition = v2;
                img.rectTransform.sizeDelta = new Vector2(size, size);
            }
            foreach (var m in staticMarkers)
                if ((m.pos - pos).sqrMagnitude < (range * 2.2f) * (range * 2.2f)) Marker(m.pos, m.color, m.size);
            if (race != null)
            {
                foreach (var p in race.Participants)
                    if (!p.IsPlayer && p.Vehicle != null) Marker(p.Vehicle.transform.position, p.Color, 12f);
                if (race.Player != null && race.Player.NextCheckpoint < race.Checkpoints.Count)
                    Marker(race.Path.PositionAt(race.Checkpoints[race.Player.NextCheckpoint]), UIKit.Good, 16f);
            }
            for (int i = used; i < markerPool.Count; i++) markerPool[i].gameObject.SetActive(false);
        }

        void UpdateDistrict(float dt)
        {
            if (vehicle == null) return;
            districtCheck -= dt;
            if (districtCheck <= 0f)
            {
                districtCheck = 1f;
                var d = WorldData.DistrictAt(vehicle.transform.position);
                string name = d != null ? d.name : null;
                if (name != null && name != lastDistrict)
                {
                    lastDistrict = name;
                    districtText.text = name.ToUpperInvariant();
                    districtTimer = 3.5f;
                }
            }
            if (districtTimer > 0f)
            {
                districtTimer -= dt;
                var c = districtText.color;
                c.a = Mathf.Clamp01(districtTimer);
                districtText.color = c;
            }
        }
    }
}

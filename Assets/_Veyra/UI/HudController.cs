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
        // instrument cluster (round 2): tach arc, shift lights, G-meter, status lamps, nitro cells
        Image tachRed;
        readonly Image[] shiftLeds = new Image[7];
        readonly Image[] lampBacks = new Image[5];
        readonly Text[] lampTexts = new Text[5];
        readonly Image[] nitroCells = new Image[3];
        readonly List<Text> tachNumbers = new List<Text>();
        RectTransform gDot;
        Text gText;
        Vector2 gFiltered;
        float tachLimiterBuilt = -1f;
        RectTransform tachRoot;
        const float TachSweep = 0.75f;   // 270 degrees
        RectTransform racePanel, minimapRoot, mapRotator, playerArrow, markerLayer, toastLayer;
        RawImage mapImage, routeImage;
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

            BuildCluster();

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

        /// <summary>
        /// Round 2 instrument cluster, bottom right (Asphalt / NFS dial with Forza-style telemetry): a 270 degree tachometer
        /// arc with rpm numerals and a red zone, big speed digits with the gear in the arc's gap, seven shift lights,
        /// a G-meter, BRAKE / ABS / TCS / NOS / DRIFT lamps, and a three-cell nitro gauge coloured by boost mode.
        /// </summary>
        void BuildCluster()
        {
            var cluster = UIKit.Box(root, "Cluster", new Vector2(1f, 0f), new Vector2(-30f, 26f), new Vector2(520f, 330f));
            UIKit.Image(cluster, new Color(0.015f, 0.02f, 0.035f, 0.66f), UIKit.SoftRect);
            boostGlow = UIKit.Image(UIKit.Rect(cluster, "BoostGlow", Vector2.zero, Vector2.one, new Vector2(-10, -10), new Vector2(10, 10)), new Color(1f, 0.6f, 0.1f, 0f), UIKit.SoftRect);
            boostGlow.transform.SetAsFirstSibling();

            // tachometer arc (centre 160 px from the left, 170 px from the top)
            tachRoot = UIKit.Box(cluster, "Tach", new Vector2(0f, 1f), new Vector2(165f, -165f), new Vector2(270f, 270f));
            tachRed = Arc(tachRoot, "Red", new Color(1f, 0.22f, 0.2f, 0.85f), TachSweep);
            Arc(tachRoot, "Back", new Color(0.16f, 0.19f, 0.25f, 1f), TachSweep * 0.88f);
            rpmFill = Arc(tachRoot, "Rpm", UIKit.Accent, 0f);
            var inner = UIKit.Box(tachRoot, "Inner", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(214f, 214f));
            UIKit.Image(inner, new Color(0.01f, 0.015f, 0.03f, 0.8f), UIKit.Circle);
            speedText = UIKit.Text(tachRoot, "Speed", "0", 84, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);
            PlaceBox(speedText.rectTransform, new Vector2(0f, 12f), new Vector2(220f, 100f));
            unitText = UIKit.Text(tachRoot, "Unit", "KM/H", 20, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            PlaceBox(unitText.rectTransform, new Vector2(0f, -40f), new Vector2(120f, 26f));
            var gearBox = UIKit.Box(tachRoot, "GearBox", new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(64f, 64f));
            UIKit.Image(gearBox, new Color(0.02f, 0.03f, 0.06f, 0.95f), UIKit.Circle);
            // one Graphic per GameObject: the ring goes on a child (a second Image on gearBox is null in players)
            UIKit.Image(UIKit.Rect(gearBox, "Ring", Vector2.zero, Vector2.one), new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.7f), UIKit.Ring);
            gearText = UIKit.Text(gearBox, "Gear", "N", 44, TextAnchor.MiddleCenter, UIKit.Accent, FontStyle.Bold);
            UIKit.Stretch(gearText.rectTransform);

            // shift lights (top right)
            for (int i = 0; i < shiftLeds.Length; i++)
            {
                var led = UIKit.Box(cluster, "Led" + i, new Vector2(0f, 1f), new Vector2(318f + i * 26f, -26f), new Vector2(18f, 18f));
                shiftLeds[i] = UIKit.Image(led, new Color(1f, 1f, 1f, 0.08f), UIKit.Circle);
            }
            // G-meter
            var g = UIKit.Box(cluster, "GMeter", new Vector2(0f, 1f), new Vector2(372f, -110f), new Vector2(118f, 118f));
            UIKit.Image(g, new Color(1f, 1f, 1f, 0.05f), UIKit.Circle);
            UIKit.Image(UIKit.Rect(g, "Ring", Vector2.zero, Vector2.one), new Color(1f, 1f, 1f, 0.22f), UIKit.Ring);
            var half = UIKit.Box(g, "Half", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            UIKit.Image(half, new Color(1f, 1f, 1f, 0.12f), UIKit.Ring);
            UIKit.Image(UIKit.Box(g, "H", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 1.5f)), new Color(1f, 1f, 1f, 0.15f));
            UIKit.Image(UIKit.Box(g, "V", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1.5f, 110f)), new Color(1f, 1f, 1f, 0.15f));
            gDot = UIKit.Box(g, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
            UIKit.Image(gDot, UIKit.Accent2, UIKit.Circle);
            gText = UIKit.Text(cluster, "GText", "0.0 G", 16, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            PlaceTopLeft(gText.rectTransform, new Vector2(372f, -176f), new Vector2(120f, 20f));
            // status lamps
            string[] lamps = { "BRAKE", "ABS", "TCS", "NOS", "DRIFT" };
            for (int i = 0; i < lamps.Length; i++)
            {
                var lb = UIKit.Box(cluster, "Lamp" + lamps[i], new Vector2(0f, 1f), new Vector2(330f + (i % 3) * 64f, -200f - (i / 3) * 30f), new Vector2(58f, 24f));
                lb.pivot = new Vector2(0f, 1f);
                lampBacks[i] = UIKit.Image(lb, new Color(1f, 1f, 1f, 0.06f));
                lampTexts[i] = UIKit.Text(lb, "T", lamps[i], 13, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.25f), FontStyle.Bold);
                UIKit.Stretch(lampTexts[i].rectTransform);
            }
            // nitro gauge: three cells (one per bottle) along the bottom
            nitroLabel = UIKit.Text(cluster, "NitroLabel", "NITRO", 16, TextAnchor.LowerLeft, UIKit.Amber, FontStyle.Bold);
            PlaceTopLeft(nitroLabel.rectTransform, new Vector2(24f, -278f), new Vector2(470f, 20f));
            var bar = UIKit.Box(cluster, "NitroCells", new Vector2(0f, 1f), new Vector2(24f, -302f), new Vector2(472f, 16f));
            bar.pivot = new Vector2(0f, 1f);
            for (int c = 0; c < nitroCells.Length; c++)
            {
                var cell = UIKit.Rect(bar, "Cell" + c, new Vector2(c / 3f, 0f), new Vector2((c + 1) / 3f, 1f), new Vector2(c == 0 ? 0f : 3f, 0f), new Vector2(c == 2 ? 0f : -3f, 0f));
                UIKit.Image(cell, new Color(1f, 1f, 1f, 0.07f));
                var f = UIKit.Rect(cell, "Fill", Vector2.zero, Vector2.one);
                var img = f.gameObject.AddComponent<Image>();
                img.sprite = UIKit.Texture2DSprite();
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.color = UIKit.Amber;
                img.raycastTarget = false;
                nitroCells[c] = img;
            }
            nitroFill = nitroCells[2];
            nitroWindow = UIKit.Image(UIKit.Rect(bar, "CleanWindow", new Vector2(0f, -0.6f), new Vector2(1f, 1.6f)), new Color(0.3f, 1f, 0.6f, 0f));
        }

        /// <summary>An arc on the ring sprite: radial fill from 7:30 clockwise (270 degrees at fill 0.75).</summary>
        static Image Arc(RectTransform parent, string name, Color c, float fill)
        {
            var rt = UIKit.Rect(parent, name, Vector2.zero, Vector2.one);
            rt.localRotation = Quaternion.Euler(0f, 0f, -45f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UIKit.Ring;
            img.color = c;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Bottom;
            img.fillClockwise = true;
            img.fillAmount = fill;
            img.raycastTarget = false;
            return img;
        }

        static void PlaceBox(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void PlaceTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Rpm numerals around the arc (x1000), rebuilt when the car's limiter changes.</summary>
        void BuildTachNumbers(float limiter)
        {
            tachLimiterBuilt = limiter;
            foreach (var t in tachNumbers) if (t != null) Destroy(t.gameObject);
            tachNumbers.Clear();
            int max = Mathf.FloorToInt(limiter / 1000f);
            for (int k = 0; k <= max; k++)
            {
                float v = k * 1000f / limiter;
                float ang = (225f - 270f * v) * Mathf.Deg2Rad;
                var t = UIKit.Text(tachRoot, "N" + k, k.ToString(), 17, TextAnchor.MiddleCenter, v > 0.88f ? new Color(1f, 0.4f, 0.35f) : UIKit.TextDim, FontStyle.Bold);
                PlaceBox(t.rectTransform, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 96f, new Vector2(30f, 22f));
                tachNumbers.Add(t);
            }
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
            routeImage = UIKit.Rect(mapRotator, "Route", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();   // GPS route (WorldMap)
            routeImage.raycastTarget = false;
            routeImage.enabled = false;
            markerLayer = UIKit.Rect(maskRt, "Markers", Vector2.zero, Vector2.one);
            var ring = UIKit.Image(UIKit.Rect(minimapRoot, "Ring", Vector2.zero, Vector2.one), new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.7f), UIKit.Ring);
            playerArrow = UIKit.Box(minimapRoot, "Player", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 26f));
            var arrow = UIKit.Text(playerArrow, "Glyph", "▲", 26, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var north = UIKit.Text(UIKit.Box(minimapRoot, "N", new Vector2(0.5f, 1f), new Vector2(0, 4), new Vector2(30, 30)), "T", "N", 20, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            north.transform.parent.name = "North";
        }

        /// <summary>
        /// Map of the whole city rendered once (3.9 m per pixel). Iteration 9 restyle toward GTA V's radar: slate land with
        /// faint district tints, the sea south of the coastline, the river and the reservoir in blue, and roads drawn near
        /// their true width with a dark casing, graded by class (motorways amber, arterials bright, streets grey).
        /// </summary>
        public static Texture2D MapTexture()
        {
            if (mapTexture != null) return mapTexture;
            const int W = 2048, H = 1536;
            var px = new Color32[W * H];
            var bg = new Color32(24, 29, 36, 255);
            var seaCol = new Color32(18, 44, 66, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var b = WorldData.Bounds;
            float mPerPx = b.width / W;
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
                            var tint = Color32.Lerp(bg, (Color32)WorldData.ParseColor(d.color, Color.gray), 0.12f);
                            for (int yy = y; yy < y + 4 && yy < H; yy++)
                                for (int xx = x; xx < x + 4 && xx < W; xx++) px[yy * W + xx] = tint;
                            break;
                        }
                    }
            // water: the sea lies south of the coastline polyline; the reservoir is a polygon; the river a wide line
            if (ds?.coastline != null && ds.coastline.Length >= 4)
            {
                var c = ds.coastline;
                for (int x = 0; x < W; x++)
                {
                    float wx = b.xMin + (x + 0.5f) / W * b.width;
                    float coastZ = float.NaN;
                    for (int i = 0; i + 3 < c.Length; i += 2)
                        if ((c[i] <= wx && wx <= c[i + 2]) || (c[i + 2] <= wx && wx <= c[i]))
                        {
                            float t = Mathf.Abs(c[i + 2] - c[i]) > 1e-3f ? (wx - c[i]) / (c[i + 2] - c[i]) : 0f;
                            coastZ = c[i + 1] + (c[i + 3] - c[i + 1]) * t;
                            break;
                        }
                    if (float.IsNaN(coastZ)) continue;
                    int yCoast = Mathf.Clamp(Mathf.FloorToInt((coastZ - b.yMin) / b.height * H), 0, H);
                    for (int y = 0; y < yCoast; y++) px[y * W + x] = seaCol;
                }
            }
            if (ds?.lake != null && ds.lake.Length >= 6)
                for (int y = 0; y < H; y += 2)
                    for (int x = 0; x < W; x += 2)
                    {
                        float wx = b.xMin + (x + 1f) / W * b.width, wz = b.yMin + (y + 1f) / H * b.height;
                        if (!WorldData.PointInPolygon(ds.lake, wx, wz)) continue;
                        for (int yy = y; yy < y + 2 && yy < H; yy++)
                            for (int xx = x; xx < x + 2 && xx < W; xx++) px[yy * W + xx] = seaCol;
                    }
            if (ds?.river != null && ds.river.Length >= 4)
                for (int i = 0; i + 3 < ds.river.Length; i += 2)
                    DrawLine(px, W, H, (ds.river[i] - b.xMin) / b.width * W, (ds.river[i + 1] - b.yMin) / b.height * H,
                             (ds.river[i + 2] - b.xMin) / b.width * W, (ds.river[i + 3] - b.yMin) / b.height * H, 42f / mPerPx, seaCol);
            // roads: casing pass, then fill pass (so junctions merge cleanly)
            for (int pass = 0; pass < 2; pass++)
                foreach (var road in WorldData.Roads)
                {
                    var cls = road.Data.cls;
                    Color32 col = pass == 0 ? new Color32(8, 10, 14, 255)
                        : cls == "MW" || cls == "EX" ? new Color32(236, 168, 64, 255)
                        : cls == "HW" || cls == "AR" ? new Color32(214, 220, 228, 255)
                        : cls == "GR" ? new Color32(140, 116, 90, 255)
                        : cls == "RW" ? new Color32(84, 94, 112, 255)
                        : new Color32(132, 142, 156, 255);
                    float wpx = Mathf.Max(1.8f, road.Data.width / mPerPx * 0.85f) + (pass == 0 ? 1.6f : 0f);
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
            float lim = Mathf.Max(1000f, vehicle.Definition.Engine.LimiterRpm);
            if (!Mathf.Approximately(lim, tachLimiterBuilt)) BuildTachNumbers(lim);
            float r = Mathf.Clamp01(st.EngineRpm / lim);
            rpmFill.fillAmount = TachSweep * r;
            rpmFill.color = r > 0.88f ? UIKit.Bad : Color.Lerp(UIKit.Accent, UIKit.Accent2, Mathf.InverseLerp(0.55f, 0.88f, r));
            // shift lights: green, green, green, amber, amber, red, red from 70 % of the limiter; all blue flash at the cut
            bool atCut = r > 0.97f;
            bool flash = atCut && Mathf.Repeat(Time.unscaledTime * 10f, 1f) < 0.5f;
            for (int i = 0; i < shiftLeds.Length; i++)
            {
                float on = 0.70f + i * 0.04f;
                Color c = i < 3 ? new Color(0.3f, 1f, 0.45f) : i < 5 ? UIKit.Amber : UIKit.Bad;
                shiftLeds[i].color = atCut ? (flash ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 1f, 1f, 0.08f)) : r >= on ? c : new Color(1f, 1f, 1f, 0.08f);
            }
            // G-meter (lateral right = right, braking = up, as in Forza's telemetry)
            gFiltered = Vector2.Lerp(gFiltered, new Vector2(st.LateralG, -st.LongitudinalG), 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
            var gd = gFiltered * 46f;
            if (gd.magnitude > 55f) gd = gd.normalized * 55f;
            gDot.anchoredPosition = gd;
            gText.text = $"{gFiltered.magnitude:0.0} G";
            // status lamps
            float b = Mathf.Max(st.AppliedBrake, vehicle.Controls.Brake);
            bool blink = Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.5f;
            Lamp(0, b > 0.08f, UIKit.Bad);
            Lamp(1, st.AbsActive && blink, UIKit.Amber);
            Lamp(2, st.TcsCut > 0.05f && blink, UIKit.Amber);
            Lamp(3, nitro != null && nitro.BoostScalar > 0.05f, nitro != null && nitro.Mode == NitroMode.Overdrive ? UIKit.Accent2 : nitro != null && nitro.Mode == NitroMode.CleanBoost ? UIKit.Good : UIKit.Accent);
            Lamp(4, st.ArcadeDrift, UIKit.Accent2);
            if (nitro != null)
            {
                float amt = nitro.Amount / NitroSystem.MeterMax;
                bool full = nitro.Amount >= NitroSystem.MeterMax - 0.01f;
                Color nc = nitro.Mode == NitroMode.Overdrive ? UIKit.Accent2 : nitro.Mode == NitroMode.CleanBoost ? UIKit.Good : nitro.BoostScalar > 0.05f ? UIKit.Accent
                         : full ? Color.Lerp(UIKit.Amber, Color.white, Mathf.PingPong(Time.unscaledTime * 2f, 0.5f)) : UIKit.Amber;
                for (int c = 0; c < nitroCells.Length; c++)
                {
                    nitroCells[c].fillAmount = Mathf.Clamp01(amt * 3f - c);
                    nitroCells[c].color = nc;
                }
                var wc = nitroWindow.color;
                wc.a = nitro.CleanWindowOpenNow ? 0.55f : 0f;
                nitroWindow.color = wc;
                nitroLabel.color = nc;
                nitroLabel.text = nitro.Mode == NitroMode.Overdrive ? "OVERDRIVE" : nitro.Mode == NitroMode.CleanBoost ? "CLEAN BOOST" : nitro.CleanWindowOpenNow ? "TAP AGAIN!" : full ? "NITRO FULL  -  DOUBLE-TAP FOR OVERDRIVE" : $"NITRO  {Mathf.RoundToInt(amt * 100f)}%";
                var g = boostGlow.color;
                g.a = nitro.BoostScalar * 0.55f;
                g.r = nitro.Mode == NitroMode.Overdrive ? 1f : nitro.Mode == NitroMode.CleanBoost ? 0.3f : 0.25f;
                g.g = nitro.Mode == NitroMode.Overdrive ? 0.25f : nitro.Mode == NitroMode.CleanBoost ? 1f : 0.7f;
                g.b = nitro.Mode == NitroMode.Overdrive ? 0.62f : nitro.Mode == NitroMode.CleanBoost ? 0.6f : 1f;
                boostGlow.color = g;
            }
        }

        void Lamp(int i, bool on, Color c)
        {
            lampBacks[i].color = on ? new Color(c.r, c.g, c.b, 0.9f) : new Color(1f, 1f, 1f, 0.06f);
            lampTexts[i].color = on ? new Color(0.03f, 0.03f, 0.05f) : new Color(1f, 1f, 1f, 0.25f);
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
            // GPS route and waypoint from the world map (clamped to the rim when off the minimap, like GTA's blips)
            bool route = WorldMap.Waypoint.HasValue && WorldMap.RouteTexture != null;
            routeImage.enabled = route;
            if (route)
            {
                routeImage.texture = WorldMap.RouteTexture;
                routeImage.uvRect = mapImage.uvRect;
                Marker(WorldMap.Waypoint.Value, new Color(1f, 0.3f, 0.85f), 18f);
            }
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

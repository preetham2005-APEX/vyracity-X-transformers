using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VeyraCity.World;

namespace VeyraCity.UI
{
    /// <summary>
    /// Full-screen world map (M / gamepad View). Built to beat GTA V's pause map on its own terms:
    ///  * the base is a real aerial view of this city (an orthographic capture of the 3D world made once, fog off, without
    ///    the player's car), so blocks, roofs, parks and water look like the place you drive in, not a painting of it;
    ///  * a road overlay graded by class (motorways amber, arterials bright, streets grey) with a dark casing;
    ///  * district names when zoomed out, street names along the roads and landmark names when zoomed in;
    ///  * filterable points of interest from the world data (races, garages, dealerships, meets, drift/speed spots, jumps,
    ///    landmarks), a heading arrow for the car, a metric scale bar and a north mark;
    ///  * right-click (gamepad A) drops a waypoint and the GPS route is solved along the real road network
    ///    (<see cref="RoadGraph"/>) and drawn on the map and on the HUD minimap, with distance and an ETA.
    /// Zoom with the wheel (gamepad triggers), pan by dragging, WASD/arrows or the left stick, Space/Y re-centres on the car.
    /// The game is paused by GameFlow while the map is open.
    /// </summary>
    public sealed class WorldMap : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        /// <summary>The waypoint (world position), shared with the minimap.</summary>
        public static Vector3? Waypoint { get; private set; }
        /// <summary>Transparent texture with the GPS route over the whole city (same mapping as the minimap texture).</summary>
        public static Texture2D RouteTexture { get; private set; }
        public static float RouteLength { get; private set; }

        /// <summary>Next GPS instruction for the HUD (iteration 22, turn-by-turn like a car's satnav).</summary>
        public struct GpsInstruction
        {
            public int Turn;          // -1 left, +1 right, 0 straight on
            public float Distance;    // metres to the turn (or to the destination when Arriving)
            public string Street;     // the street after the turn (may be empty)
            public float Remaining;   // metres left on the route
            public bool Arriving;
        }
        public static bool HasInstruction { get; private set; }
        public static GpsInstruction Instruction { get; private set; }
        public static int Reroutes { get; private set; }
        static Color32[] routePx;
        int routeProgress;
        float gpsTimer, rerouteCooldown;

        const int OverlayW = 2048, OverlayH = 1536;
        const float MinMpp = 0.55f, MaxMpp = 5.2f;   // metres per canvas pixel

        static Texture2D roadOverlay, waterOverlay, reliefOverlay, buildingOverlay, buildingFill;
        static RenderTexture aerial;
        static bool aerialPending;

        Canvas canvas;
        RectTransform viewport, overlayLayer, labelLayer, iconLayer, legendRoot;
        RawImage aerialImg, roadImg, routeImg, waterImg, reliefImg, buildingImg, atlasImg, blockImg;
        Text viewText;
        /// <summary>Iteration 25: false = aerial (the real city from above), true = atlas (clean road-map style).</summary>
        public bool AtlasView { get; private set; }
        Text districtTitle, coordText, routeText, scaleText, captureText, hoverText, waypointLabel;
        CanvasGroup group;
        float openT = 1f;
        Camera worldCam;
        int worldCamMask;
        RectTransform scaleBar, playerArrow, waypointIcon, cursorRing;
        Transform player;
        Vector2 center;              // world x, z at the viewport centre
        float mpp = 2.6f;            // metres per canvas pixel
        bool dragging;
        Vector2 dragStart, dragCenter;
        readonly List<Vector3> routePoints = new List<Vector3>();

        sealed class Poi { public Vector3 Pos; public int Cat; public string Name; public RectTransform Icon; public Text Label; }
        sealed class Label { public Text Text; public Vector3 Pos; public float Angle; public float MaxMpp, MinMpp; }
        readonly List<Poi> pois = new List<Poi>();
        readonly List<Vector2> placedIcons = new List<Vector2>(), placedLabels = new List<Vector2>();
        readonly List<Label> labels = new List<Label>();
        static readonly string[] CatNames = { "Races", "Garages", "Dealerships", "Car meets", "Drift & speed", "Jumps & secrets", "Landmarks" };
        static readonly string[] CatGlyph = { "R", "G", "$", "M", "D", "J", "L" };
        static readonly Color[] CatColor =
        {
            new Color(1f, 0.32f, 0.42f), new Color(0.3f, 0.75f, 1f), new Color(0.35f, 1f, 0.55f), new Color(1f, 0.66f, 0.12f),
            new Color(0.75f, 0.45f, 1f), new Color(1f, 0.92f, 0.3f), new Color(0.85f, 0.88f, 0.92f),
        };
        readonly bool[] catOn = { true, true, true, true, true, true, true };
        readonly Text[] legendTexts = new Text[7];
        static Sprite arrowSprite;

        // ------------------------------------------------------------------ open / close

        /// <summary>The car the GPS follows (GameFlow calls this on every spawn; the map also takes it on Open).</summary>
        public void Track(Transform playerTransform) => player = playerTransform;

        public void Toggle(Transform playerTransform)
        {
            if (IsOpen) Close(); else Open(playerTransform);
        }

        public void Open(Transform playerTransform)
        {
            player = playerTransform;
            if (canvas == null) Build();
            canvas.gameObject.SetActive(true);
            IsOpen = true;
            openT = 0f;
            // the map covers the whole screen: the world camera draws nothing while it is open (iteration 19: 16-17 FPS
            // were read on the map in iteration 18 with the city still rendered underneath)
            worldCam = Camera.main;
            if (worldCam != null) { worldCamMask = worldCam.cullingMask; worldCam.cullingMask = 0; }
            if (player != null) center = new Vector2(player.position.x, player.position.z);
            if (aerial == null && !aerialPending) StartCoroutine(CaptureAerial());
            Refresh();
        }

        /// <summary>Centres the open map (world x, z) at a zoom in metres per canvas pixel (captures, tutorials).</summary>
        public void Frame(Vector2 worldCenter, float metresPerPixel)
        {
            center = worldCenter;
            mpp = Mathf.Clamp(metresPerPixel, MinMpp, MaxMpp);
            openT = 1f;   // no opening animation in scripted views
            Animate(0f);
            Refresh();
        }

        /// <summary>Places the waypoint and solves the GPS route (as a right-click on the map does).</summary>
        public void PlaceWaypoint(Vector3 world) => SetWaypoint(world);

        public bool AerialReady => aerial != null;

        public void Close()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (IsOpen && worldCam != null) worldCam.cullingMask = worldCamMask;
            worldCam = null;
            IsOpen = false;
            dragging = false;
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            canvas = UIKit.CreateCanvas("WorldMap", 400, transform);
            var root = canvas.transform as RectTransform;
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            UIKit.Image(UIKit.Rect(root, "Dim", Vector2.zero, Vector2.one), new Color(0.015f, 0.02f, 0.03f, 1f));

            // map viewport (right of the legend)
            viewport = UIKit.Rect(root, "Viewport", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(360f, 24f), new Vector2(-24f, -24f));
            viewport.gameObject.AddComponent<RectMask2D>();
            UIKit.Image(UIKit.Rect(viewport, "Sea", Vector2.zero, Vector2.one), new Color(0.06f, 0.13f, 0.19f, 1f));
            // atlas style (iteration 25): the HUD radar's district-tinted map plus light building blocks
            atlasImg = UIKit.Rect(viewport, "Atlas", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            atlasImg.texture = HudController.MapTexture();
            atlasImg.enabled = false;
            blockImg = UIKit.Rect(viewport, "Blocks", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            blockImg.texture = BuildingFillTexture();
            blockImg.color = new Color(0.66f, 0.73f, 0.84f, 0.32f);
            blockImg.enabled = false;
            aerialImg = UIKit.Rect(viewport, "Aerial", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            aerialImg.color = new Color(0.82f, 0.84f, 0.88f, 1f);
            reliefImg = UIKit.Rect(viewport, "Relief", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            reliefImg.texture = ReliefOverlay();
            waterImg = UIKit.Rect(viewport, "Water", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            waterImg.texture = WaterOverlay();
            buildingImg = UIKit.Rect(viewport, "Buildings", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            buildingImg.texture = BuildingOverlay();
            buildingImg.color = new Color(0f, 0.01f, 0.03f, 1f);   // alpha-only mask: shadows and footprint outlines
            roadImg = UIKit.Rect(viewport, "Roads", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            roadImg.texture = RoadOverlay();
            routeImg = UIKit.Rect(viewport, "Route", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            routeImg.texture = EnsureRouteTexture();
            overlayLayer = UIKit.Rect(viewport, "Overlay", Vector2.zero, Vector2.one);
            // iteration 23: names over the icons (an icon sat on the S of AZURE COAST)
            iconLayer = UIKit.Rect(overlayLayer, "Icons", Vector2.zero, Vector2.one);
            labelLayer = UIKit.Rect(overlayLayer, "Labels", Vector2.zero, Vector2.one);
            foreach (var g in new Graphic[] { atlasImg, blockImg, aerialImg, reliefImg, waterImg, buildingImg, roadImg, routeImg }) g.raycastTarget = false;

            // vignette frame
            var frame = UIKit.Image(UIKit.Rect(viewport, "Frame", Vector2.zero, Vector2.one), new Color(1f, 1f, 1f, 0.06f), UIKit.SoftRect);
            frame.raycastTarget = false;

            BuildLabels();

            waypointIcon = UIKit.Box(iconLayer, "Waypoint", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            UIKit.Image(waypointIcon, new Color(1f, 0.25f, 0.85f), UIKit.Ring);
            UIKit.Image(UIKit.Box(waypointIcon, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12)), new Color(1f, 0.25f, 0.85f), UIKit.Circle);
            waypointLabel = UIKit.Text(waypointIcon, "Dist", "", 18, TextAnchor.MiddleLeft, new Color(1f, 0.55f, 0.92f), FontStyle.Bold);
            var wl = waypointLabel.rectTransform;
            wl.anchorMin = wl.anchorMax = new Vector2(1f, 0.5f);
            wl.pivot = new Vector2(0f, 0.5f);
            wl.anchoredPosition = new Vector2(6f, 0f);
            wl.sizeDelta = new Vector2(160f, 26f);
            AddOutline(waypointLabel, 0.95f);
            playerArrow = UIKit.Box(iconLayer, "Player", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            UIKit.Image(playerArrow, Color.white, ArrowSprite());
            cursorRing = UIKit.Box(iconLayer, "Cursor", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
            UIKit.Image(cursorRing, new Color(1f, 1f, 1f, 0.55f), UIKit.Ring);

            // scale bar + north mark (bottom right of the viewport)
            scaleBar = UIKit.Box(viewport, "Scale", new Vector2(1f, 0f), new Vector2(-40f, 34f), new Vector2(200f, 6f));
            scaleBar.pivot = new Vector2(1f, 0f);
            UIKit.Image(scaleBar, new Color(0.94f, 0.96f, 1f, 0.95f));
            scaleText = UIKit.Text(viewport, "ScaleText", "", 20, TextAnchor.LowerRight, UIKit.TextMain, FontStyle.Bold);
            var st = scaleText.rectTransform;
            st.anchorMin = st.anchorMax = new Vector2(1f, 0f);
            st.pivot = new Vector2(1f, 0f);
            st.sizeDelta = new Vector2(300f, 30f);
            st.anchoredPosition = new Vector2(-40f, 44f);
            var north = UIKit.Text(viewport, "North", "N▲", 26, TextAnchor.UpperRight, UIKit.TextMain, FontStyle.Bold);
            var nt = north.rectTransform;
            nt.anchorMin = nt.anchorMax = new Vector2(1f, 1f);
            nt.pivot = new Vector2(1f, 1f);
            nt.sizeDelta = new Vector2(80f, 40f);
            nt.anchoredPosition = new Vector2(-30f, -24f);
            captureText = UIKit.Text(viewport, "Capture", "", 26, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            UIKit.Stretch(captureText.rectTransform);

            // legend / info panel (left)
            legendRoot = UIKit.Rect(root, "Legend", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(24f, 24f), new Vector2(336f, -24f));
            UIKit.Image(legendRoot, UIKit.Panel);
            var title = UIKit.Text(legendRoot, "Title", "VEYRA CITY", 40, TextAnchor.UpperLeft, UIKit.TextMain, FontStyle.Bold);
            Place(title.rectTransform, 24, -24, 280, 50);
            // iteration 19: two lines for long district names (IRONPORT INDUSTRIAL ZONE ran into the coordinates)
            districtTitle = UIKit.Text(legendRoot, "District", "", 22, TextAnchor.UpperLeft, UIKit.Accent, FontStyle.Bold);
            Place(districtTitle.rectTransform, 24, -76, 280, 56);
            coordText = UIKit.Text(legendRoot, "Coords", "", 18, TextAnchor.UpperLeft, UIKit.TextDim);
            Place(coordText.rectTransform, 24, -134, 280, 46);
            hoverText = UIKit.Text(legendRoot, "Hover", "", 18, TextAnchor.UpperLeft, UIKit.TextMain, FontStyle.Bold);
            Place(hoverText.rectTransform, 24, -182, 280, 46);
            routeText = UIKit.Text(legendRoot, "Route", "", 20, TextAnchor.UpperLeft, new Color(1f, 0.45f, 0.9f), FontStyle.Bold);
            Place(routeText.rectTransform, 24, -230, 280, 56);
            var lt = UIKit.Text(legendRoot, "LegendTitle", "LEGEND  (click to filter)", 16, TextAnchor.UpperLeft, UIKit.TextDim, FontStyle.Bold);
            Place(lt.rectTransform, 24, -292, 280, 24);
            for (int c = 0; c < CatNames.Length; c++)
            {
                int cat = c;
                var row = UIKit.Box(legendRoot, "Cat" + c, new Vector2(0f, 1f), new Vector2(24f, -322f - c * 42f), new Vector2(288f, 36f));
                row.pivot = new Vector2(0f, 1f);
                var bg = UIKit.Image(row, new Color(1f, 1f, 1f, 0.04f));
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => ToggleCategory(cat));
                var dot = UIKit.Box(row, "Dot", new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(26f, 26f));
                UIKit.Image(dot, CatColor[c], UIKit.Circle);
                var g = UIKit.Text(dot, "G", CatGlyph[c], 15, TextAnchor.MiddleCenter, new Color(0.05f, 0.06f, 0.08f), FontStyle.Bold);
                UIKit.Stretch(g.rectTransform);
                legendTexts[c] = UIKit.Text(row, "Name", CatNames[c], 20, TextAnchor.MiddleLeft, UIKit.TextMain, FontStyle.Bold);
                Place(legendTexts[c].rectTransform, 48, 0, 230, 36, true);
            }
            BuildPois();
            pois.Sort((a, b) => a.Cat.CompareTo(b.Cat));   // races first when icons compete for space
            for (int c = 0; c < CatNames.Length; c++)
            {
                int count = 0;
                foreach (var poi in pois) if (poi.Cat == c) count++;
                legendTexts[c].text = $"{CatNames[c]}  <color=#7d889c>{count}</color>";
            }
            // map style switch (V / gamepad right bumper)
            var vb = UIKit.Box(legendRoot, "View", new Vector2(0f, 1f), new Vector2(24f, -626f), new Vector2(288f, 36f));
            vb.pivot = new Vector2(0f, 1f);
            var vbg = UIKit.Image(vb, new Color(1f, 1f, 1f, 0.08f));
            var vbtn = vb.gameObject.AddComponent<Button>();
            vbtn.targetGraphic = vbg;
            vbtn.onClick.AddListener(ToggleView);
            viewText = UIKit.Text(vb, "T", "", 17, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);
            UIKit.Stretch(viewText.rectTransform);
            // the car and the waypoint stay above the place icons
            waypointIcon.SetAsLastSibling();
            playerArrow.SetAsLastSibling();
            cursorRing.SetAsLastSibling();
            var help = UIKit.Text(legendRoot, "Help",
                "M / Esc  close\nWheel  zoom\nDrag / WASD  pan\nRight-click  set waypoint\nBackspace  clear waypoint\nSpace  centre on car\nV  aerial / atlas view",
                17, TextAnchor.LowerLeft, UIKit.TextDim);
            var hr = help.rectTransform;
            hr.anchorMin = new Vector2(0f, 0f);
            hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0f, 0f);
            hr.offsetMin = new Vector2(24f, 22f);
            hr.offsetMax = new Vector2(-12f, 190f);
            SetView(AtlasView);
            canvas.gameObject.SetActive(false);
        }

        static void Place(RectTransform rt, float x, float y, float w, float h, bool middle = false)
        {
            rt.anchorMin = rt.anchorMax = middle ? new Vector2(0f, 0.5f) : new Vector2(0f, 1f);
            rt.pivot = middle ? new Vector2(0f, 0.5f) : new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        void BuildLabels()
        {
            var ds = WorldData.Districts;
            if (ds?.districts != null)
                foreach (var d in ds.districts)
                {
                    if (d.polygon == null || d.polygon.Length < 6) continue;
                    float sx = 0f, sz = 0f;
                    int n = d.polygon.Length / 2;
                    for (int i = 0; i < n; i++) { sx += d.polygon[i * 2]; sz += d.polygon[i * 2 + 1]; }
                    var t = UIKit.Text(labelLayer, "District", Spaced(d.name.ToUpperInvariant()), 30, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f), FontStyle.Bold);
                    t.rectTransform.sizeDelta = new Vector2(900f, 50f);
                    AddOutline(t, 0.75f);
                    labels.Add(new Label { Text = t, Pos = new Vector3(sx / n, 0f, sz / n), MinMpp = 1.6f, MaxMpp = 99f });
                }
            // street names along named roads (zoomed in)
            var named = new HashSet<string>();
            foreach (var r in WorldData.Roads)
            {
                if (string.IsNullOrEmpty(r.Data.name) || r.Path.Length < 350f) continue;
                for (float s = r.Path.Length * 0.25f; s < r.Path.Length; s += Mathf.Max(600f, r.Path.Length * 0.5f))
                {
                    var p = r.Path.PositionAt(s);
                    var tng = r.Path.TangentAt(s);
                    float ang = Mathf.Atan2(tng.z, tng.x) * Mathf.Rad2Deg;
                    if (ang > 90f) ang -= 180f;
                    if (ang < -90f) ang += 180f;
                    var t = UIKit.Text(labelLayer, "Street", r.Data.name, 15, TextAnchor.MiddleCenter, new Color(0.92f, 0.94f, 1f, 0.95f), FontStyle.Bold);
                    t.rectTransform.sizeDelta = new Vector2(400f, 24f);
                    AddOutline(t, 0.85f);
                    labels.Add(new Label { Text = t, Pos = p, Angle = ang, MinMpp = 0f, MaxMpp = 1.5f });
                }
                named.Add(r.Data.name);
            }
        }

        static string Spaced(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length * 2);
            foreach (char ch in s) { sb.Append(ch); if (ch != ' ') sb.Append(' '); }
            return sb.ToString();
        }

        static void AddOutline(Text t, float alpha)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, alpha);
            o.effectDistance = new Vector2(1.5f, -1.5f);
        }

        void BuildPois()
        {
            var locs = WorldData.Locations;
            if (locs == null) return;
            foreach (var l in locs)
            {
                int cat = CategoryOf(l.category);
                if (cat < 0) continue;
                var icon = UIKit.Box(iconLayer, "Poi", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
                UIKit.Image(UIKit.Box(icon, "Shadow", new Vector2(0.5f, 0.5f), new Vector2(1.5f, -1.5f), new Vector2(26, 26)), new Color(0f, 0f, 0f, 0.6f), UIKit.Circle);
                UIKit.Image(UIKit.Box(icon, "Disc", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24)), CatColor[cat], UIKit.Circle);
                var g = UIKit.Text(icon, "G", CatGlyph[cat], 14, TextAnchor.MiddleCenter, new Color(0.05f, 0.06f, 0.08f), FontStyle.Bold);
                UIKit.Stretch(g.rectTransform);
                var label = UIKit.Text(icon, "Name", l.name, 14, TextAnchor.MiddleLeft, UIKit.TextMain, FontStyle.Bold);
                var lr = label.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(1f, 0.5f);
                lr.pivot = new Vector2(0f, 0.5f);
                lr.anchoredPosition = new Vector2(6f, 0f);
                lr.sizeDelta = new Vector2(260f, 22f);
                AddOutline(label, 0.9f);
                pois.Add(new Poi { Pos = l.Position, Cat = cat, Name = l.name, Icon = icon, Label = label });
            }
        }

        static int CategoryOf(string c)
        {
            switch (c)
            {
                case "Race start": case "Time trial / test": return 0;
                case "Garage": case "Hidden garage / barn find": return 1;
                case "Dealership": return 2;
                case "Car meet": return 3;
                case "Drift zone": case "Speed zone": case "Speed trap": case "Drag strip": return 4;
                case "Stunt jump": case "Hidden shortcut": case "Collectible area": return 5;
                case "Landmark": case "Scenic viewpoint": return 6;
                default: return -1;
            }
        }

        void ToggleView() => SetView(!AtlasView);

        /// <summary>Switches between the aerial and the atlas map styles.</summary>
        public void SetView(bool atlas)
        {
            AtlasView = atlas;
            if (viewText != null) viewText.text = atlas ? "VIEW  ATLAS   (V: aerial)" : "VIEW  AERIAL   (V: atlas)";
            Refresh();
        }

        void ToggleCategory(int c)
        {
            catOn[c] = !catOn[c];
            legendTexts[c].color = catOn[c] ? UIKit.TextMain : UIKit.TextDim * 0.7f;
            Refresh();
        }

        // ------------------------------------------------------------------ textures

        /// <summary>Transparent road layer for the whole city (casing + class colours), drawn once.</summary>
        static Texture2D RoadOverlay()
        {
            if (roadOverlay != null) return roadOverlay;
            var px = new Color32[OverlayW * OverlayH];
            var b = WorldData.Bounds;
            float mPerPx = b.width / OverlayW;
            for (int pass = 0; pass < 2; pass++)
                foreach (var road in WorldData.Roads)
                {
                    var cls = road.Data.cls;
                    Color32 col = pass == 0 ? new Color32(10, 12, 16, 170)
                        : cls == "MW" || cls == "EX" ? new Color32(240, 172, 66, 235)
                        : cls == "HW" || cls == "AR" ? new Color32(226, 231, 238, 225)
                        : cls == "GR" ? new Color32(160, 132, 98, 200)
                        : cls == "RW" ? new Color32(110, 120, 138, 190)
                        : new Color32(168, 176, 188, 210);
                    float w = Mathf.Max(1.4f, road.Data.width / mPerPx * 0.7f) + (pass == 0 ? 1.4f : 0f);
                    var pts = road.Path.Points;
                    int n = road.Path.Closed ? pts.Length : pts.Length - 1;
                    for (int i = 0; i < n; i++)
                    {
                        var p0 = pts[i];
                        var p1 = pts[(i + 1) % pts.Length];
                        Raster(px, (p0.x - b.xMin) / b.width * OverlayW, (p0.z - b.yMin) / b.height * OverlayH,
                               (p1.x - b.xMin) / b.width * OverlayW, (p1.z - b.yMin) / b.height * OverlayH, w, col);
                    }
                }
            roadOverlay = new Texture2D(OverlayW, OverlayH, TextureFormat.RGBA32, true) { name = "MapRoads", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            roadOverlay.SetPixels32(px);
            roadOverlay.Apply(true, true);
            return roadOverlay;
        }

        /// <summary>Sea (south of the coastline), river and reservoir in a clear map blue with a lighter shoreline.</summary>
        static Texture2D WaterOverlay()
        {
            if (waterOverlay != null) return waterOverlay;
            var px = new Color32[OverlayW * OverlayH];
            var b = WorldData.Bounds;
            var water = new Color32(28, 78, 122, 240);
            var ds = WorldData.Districts;
            if (ds?.coastline != null && ds.coastline.Length >= 4)
            {
                var c = ds.coastline;
                for (int x = 0; x < OverlayW; x++)
                {
                    float wx = b.xMin + (x + 0.5f) / OverlayW * b.width;
                    float coastZ = float.NaN;
                    for (int i = 0; i + 3 < c.Length; i += 2)
                        if ((c[i] <= wx && wx <= c[i + 2]) || (c[i + 2] <= wx && wx <= c[i]))
                        {
                            float t = Mathf.Abs(c[i + 2] - c[i]) > 1e-3f ? (wx - c[i]) / (c[i + 2] - c[i]) : 0f;
                            coastZ = c[i + 1] + (c[i + 3] - c[i + 1]) * t;
                            break;
                        }
                    if (float.IsNaN(coastZ)) continue;
                    int yc = Mathf.Clamp(Mathf.FloorToInt((coastZ - b.yMin) / b.height * OverlayH), 0, OverlayH);
                    for (int y = 0; y < yc; y++) px[y * OverlayW + x] = water;
                    for (int y = yc; y < Mathf.Min(OverlayH, yc + 2); y++) px[y * OverlayW + x] = new Color32(120, 170, 200, 200);   // surf line
                }
            }
            if (ds?.lake != null && ds.lake.Length >= 6)
                for (int y = 0; y < OverlayH; y += 2)
                    for (int x = 0; x < OverlayW; x += 2)
                    {
                        float wx = b.xMin + (x + 1f) / OverlayW * b.width, wz = b.yMin + (y + 1f) / OverlayH * b.height;
                        if (!WorldData.PointInPolygon(ds.lake, wx, wz)) continue;
                        for (int yy = y; yy < y + 2 && yy < OverlayH; yy++)
                            for (int xx = x; xx < x + 2 && xx < OverlayW; xx++) px[yy * OverlayW + xx] = water;
                    }
            if (ds?.river != null && ds.river.Length >= 4)
                for (int i = 0; i + 3 < ds.river.Length; i += 2)
                    Raster(px, (ds.river[i] - b.xMin) / b.width * OverlayW, (ds.river[i + 1] - b.yMin) / b.height * OverlayH,
                           (ds.river[i + 2] - b.xMin) / b.width * OverlayW, (ds.river[i + 3] - b.yMin) / b.height * OverlayH, 44f / (b.width / OverlayW), water);
            // iteration 23: district borders as faint dashed lines (Forza draws its regions, GTA none)
            if (ds?.districts != null)
                foreach (var d in ds.districts)
                {
                    if (d.polygon == null || d.polygon.Length < 6) continue;
                    int n = d.polygon.Length / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float ax = (d.polygon[i * 2] - b.xMin) / b.width * OverlayW, ay = (d.polygon[i * 2 + 1] - b.yMin) / b.height * OverlayH;
                        int j = (i + 1) % n;
                        float bx = (d.polygon[j * 2] - b.xMin) / b.width * OverlayW, by = (d.polygon[j * 2 + 1] - b.yMin) / b.height * OverlayH;
                        float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                        for (float t = 0f; t < len; t += 14f)
                        {
                            float t1 = Mathf.Min(len, t + 8f);
                            Raster(px, ax + (bx - ax) * t / len, ay + (by - ay) * t / len, ax + (bx - ax) * t1 / len, ay + (by - ay) * t1 / len, 1.6f, new Color32(225, 232, 245, 95));
                        }
                    }
                }
            waterOverlay = new Texture2D(OverlayW, OverlayH, TextureFormat.RGBA32, true) { name = "MapWater", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            waterOverlay.SetPixels32(px);
            waterOverlay.Apply(true, true);
            return waterOverlay;
        }

        /// <summary>
        /// Hillshade from the real terrain heights (lit from the north-west, the cartographic convention): slopes facing
        /// away darken, slopes facing the light brighten, so the Blackridge Mountains read in relief like GTA's painted map.
        /// </summary>
        static Texture2D ReliefOverlay()
        {
            if (reliefOverlay != null) return reliefOverlay;
            const int W = 1024, H = 768;
            var b = WorldData.Bounds;
            var heights = new float[W * H];
            var terrains = Terrain.activeTerrains;
            Terrain lastTerrain = null;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector3(b.xMin + (x + 0.5f) / W * b.width, 0f, b.yMin + (y + 0.5f) / H * b.height);
                    float h = 0f;
                    // neighbouring samples almost always share a tile: try the last one first
                    for (int k = -1; k < terrains.Length; k++)
                    {
                        var t = k < 0 ? lastTerrain : terrains[k];
                        if (t == null || t.terrainData == null) continue;
                        var tp = t.GetPosition();
                        var sz = t.terrainData.size;
                        if (p.x < tp.x || p.z < tp.z || p.x > tp.x + sz.x || p.z > tp.z + sz.z) continue;
                        h = t.SampleHeight(p) + tp.y;
                        lastTerrain = t;
                        break;
                    }
                    heights[y * W + x] = h;
                }
            var px = new Color32[W * H];
            float cell = b.width / W;
            var light = new Vector3(-1f, 1.4f, 1f).normalized;   // from the north-west, raised
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    float dx = (heights[y * W + x + 1] - heights[y * W + x - 1]) / (2f * cell);
                    float dz = (heights[(y + 1) * W + x] - heights[(y - 1) * W + x]) / (2f * cell);
                    var n = new Vector3(-dx * 2.2f, 1f, -dz * 2.2f).normalized;   // exaggerated relief
                    float shade = Vector3.Dot(n, light) - Vector3.Dot(Vector3.up, light);
                    px[y * W + x] = shade < 0f ? new Color32(0, 6, 14, (byte)Mathf.Clamp(-shade * 520f, 0f, 150f))
                                               : new Color32(255, 248, 230, (byte)Mathf.Clamp(shade * 260f, 0f, 70f));
                }
            reliefOverlay = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "MapRelief", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            reliefOverlay.SetPixels32(px);
            reliefOverlay.Apply(true, true);
            return reliefOverlay;
        }

        /// <summary>
        /// Iteration 19: buildings as a cartographic layer from their UBX collision boxes, an alpha mask drawn once at
        /// 4096 x 3072 (2 m per pixel): a soft drop shadow cast to the south-east (the same north-west light as the
        /// hillshade, length from the building's height) and a crisp footprint outline, so blocks read in 3D like an
        /// oblique city map instead of the flat grey squares of the plain aerial.
        /// </summary>
        static Texture2D BuildingOverlay()
        {
            if (buildingOverlay != null) return buildingOverlay;
            const int W = 4096, H = 3072;
            var b = WorldData.Bounds;
            var mask = new byte[W * H];
            var foot = new List<Vector2[]>();
            foreach (var box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                var n = box.name;
                if (!n.StartsWith("UBX_") || n.EndsWith("_Leg")) continue;
                var t = box.transform;
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                if (size.y < 4f || size.x < 4f || size.z < 4f) continue;
                Vector3 c = t.TransformPoint(box.center);
                var q = new Vector2[4];
                Vector3 ax = t.right * (size.x * 0.5f), az = t.forward * (size.z * 0.5f);
                Vector3[] w = { c - ax - az, c + ax - az, c + ax + az, c - ax + az };
                for (int k = 0; k < 4; k++) q[k] = new Vector2((w[k].x - b.xMin) / b.width * W, (w[k].z - b.yMin) / b.height * H);
                foot.Add(q);
                // shadow: the footprint swept along the light direction (convex hull of the footprint and its copy)
                float len = Mathf.Min(size.y * 0.55f, 90f) / (b.width / W);
                var off = new Vector2(0.7071f, -0.7071f) * len;
                var pts = new List<Vector2>(8);
                foreach (var v in q) { pts.Add(v); pts.Add(v + off); }
                FillConvex(mask, W, H, Hull(pts), 120);
            }
            // roofs are not shadowed by their own building: clear the footprints, then draw their outlines
            foreach (var q in foot) FillConvex(mask, W, H, new List<Vector2>(q), 0);
            foreach (var q in foot)
                for (int k = 0; k < 4; k++)
                {
                    var a = q[k]; var e = q[(k + 1) % 4];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, e)));
                    for (int s = 0; s <= steps; s++)
                    {
                        var p = Vector2.Lerp(a, e, s / (float)steps);
                        int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                        if (x >= 0 && y >= 0 && x < W && y < H) mask[y * W + x] = 170;
                    }
                }
            buildingOverlay = new Texture2D(W, H, TextureFormat.Alpha8, true) { name = "MapBuildings", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            buildingOverlay.SetPixelData(mask, 0);
            buildingOverlay.Apply(true, true);
            BuildingCount = foot.Count;
            // footprints filled at half resolution for the HUD minimap (lighter blocks on its dark ground, as in GTA's radar)
            const int FW = 2048, FH = 1536;
            var fill = new byte[FW * FH];
            foreach (var q in foot)
            {
                var half = new List<Vector2>(4);
                foreach (var v in q) half.Add(v * 0.5f);
                FillConvex(fill, FW, FH, half, 255);
            }
            buildingFill = new Texture2D(FW, FH, TextureFormat.Alpha8, true) { name = "MapBuildingFill", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            buildingFill.SetPixelData(fill, 0);
            buildingFill.Apply(true, true);
            return buildingOverlay;
        }

        /// <summary>Building footprints (alpha) over the city bounds for the HUD minimap; built with the map's building layer.</summary>
        public static Texture2D BuildingFillTexture()
        {
            if (buildingFill == null) BuildingOverlay();
            return buildingFill;
        }

        /// <summary>Number of buildings on the map's building layer (logged by the iteration captures).</summary>
        public static int BuildingCount { get; private set; }

        static List<Vector2> Hull(List<Vector2> pts)
        {
            pts.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            var h = new List<Vector2>(pts.Count * 2);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(p);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>Fills a convex polygon (either winding) into the mask with a value (0 clears).</summary>
        static void FillConvex(byte[] mask, int W, int H, List<Vector2> poly, byte value)
        {
            if (poly.Count < 3) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in poly) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            float sign = 0f;
            for (int i = 0; i < poly.Count && sign == 0f; i++) sign = Mathf.Sign(Cross(poly[i], poly[(i + 1) % poly.Count], poly[(i + 2) % poly.Count]));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = true;
                    for (int i = 0; i < poly.Count && inside; i++)
                        inside = Cross(poly[i], poly[(i + 1) % poly.Count], c) * sign >= 0f;
                    if (!inside) continue;
                    int idx = y * W + x;
                    if (value == 0 || mask[idx] < value) mask[idx] = value;
                }
        }

        static Texture2D EnsureRouteTexture()
        {
            if (RouteTexture == null)
            {
                RouteTexture = new Texture2D(OverlayW, OverlayH, TextureFormat.RGBA32, false) { name = "MapRoute", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                RouteTexture.SetPixels32(new Color32[OverlayW * OverlayH]);
                RouteTexture.Apply(false);
            }
            return RouteTexture;
        }

        static void Raster(Color32[] px, float x0, float y0, float x1, float y1, float width, Color32 c)
        {
            float len = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            int steps = Mathf.Max(1, Mathf.CeilToInt(len));
            int r = Mathf.CeilToInt(width * 0.5f);
            float r2 = width * width * 0.25f;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                int ix = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), iy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r2 + 0.5f) continue;
                        int x = ix + dx, y = iy + dy;
                        if (x < 0 || y < 0 || x >= OverlayW || y >= OverlayH) continue;
                        px[y * OverlayW + x] = c;
                    }
            }
        }

        /// <summary>
        /// Renders the whole city once from straight above into a 4096 x 3072 texture (2 m per pixel): an orthographic
        /// camera at 1.5 km, everything but UI and the player's car layer, fog switched off for this camera only.
        /// </summary>
        IEnumerator CaptureAerial()
        {
            aerialPending = true;
            captureText.text = "SURVEYING THE CITY...";
            yield return null;
            var b = WorldData.Bounds;
            var go = new GameObject("MapCaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = b.height * 0.5f;
            cam.aspect = b.width / b.height;
            cam.nearClipPlane = 10f;
            cam.farClipPlane = 4000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.13f, 0.19f, 1f);
            cam.cullingMask = ~((1 << 5) | (1 << 30));
            cam.allowMSAA = false;
            cam.allowHDR = false;
            go.transform.SetPositionAndRotation(new Vector3(b.center.x, 1600f, b.center.y), Quaternion.Euler(90f, 0f, 0f));
            var rt = new RenderTexture(4096, 3072, 24, RenderTextureFormat.ARGB32) { name = "MapAerial", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            cam.targetTexture = rt;
            bool fogWas = false;
            void Begin(ScriptableRenderContext ctx, Camera c) { if (c == cam) { fogWas = RenderSettings.fog; RenderSettings.fog = false; } }
            void End(ScriptableRenderContext ctx, Camera c) { if (c == cam) RenderSettings.fog = fogWas; }
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
            cam.enabled = true;
            yield return new WaitForEndOfFrame();
            yield return null;
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            cam.targetTexture = null;
            Destroy(go);
            aerial = rt;
            aerialPending = false;
            if (aerialImg != null) aerialImg.texture = aerial;
            if (captureText != null) captureText.text = "";
            Refresh();
        }

        // ------------------------------------------------------------------ waypoint and route

        void SetWaypoint(Vector3 world)
        {
            if (player == null) return;
            world.y = WorldData.GroundHeight(world + Vector3.up * 200f, 0f);
            Waypoint = world;
            float len;
            bool ok = RoadGraph.Route(player.position, world, routePoints, out len);
            RouteLength = ok ? len : 0f;
            routeProgress = 0;
            DrawRoute(ok);
            UpdateGps();
            Refresh();
        }

        /// <summary>
        /// GPS while driving (iteration 22): follows the car along the route, re-plans when it leaves the line by more than
        /// 45 m (GTA recalculates the same way; at most every 2 s, the redraw costs ~10 ms), and works out the
        /// next turn (bearing change over 35 degrees) with its distance and the street it leads into.
        /// </summary>
        void UpdateGps()
        {
            HasInstruction = false;
            if (!Waypoint.HasValue || player == null || routePoints.Count < 2 || RouteLength <= 0f) return;
            var pos = player.position;
            // progress: nearest route point in a window ahead of the last one
            int best = routeProgress;
            float bestD = float.MaxValue;
            for (int i = routeProgress; i < Mathf.Min(routePoints.Count, routeProgress + 60); i++)
            {
                float d = (new Vector2(routePoints[i].x - pos.x, routePoints[i].z - pos.z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            routeProgress = best;
            bool off = bestD > 45f * 45f;
            if (off && rerouteCooldown <= 0f)
            {
                rerouteCooldown = 2f;
                if (RoadGraph.Route(pos, Waypoint.Value, routePoints, out float len))
                {
                    if (off) Reroutes++;
                    RouteLength = len;
                    routeProgress = 0;
                    DrawRoute(true);
                }
            }
            // remaining distance and the next turn
            float remaining = 0f;
            for (int i = routeProgress; i + 1 < routePoints.Count; i++) remaining += Vector3.Distance(routePoints[i], routePoints[i + 1]);
            var g = new GpsInstruction { Remaining = remaining, Street = "" };
            float along = 0f;
            for (int i = routeProgress + 2; i + 3 < routePoints.Count; i++)
            {
                along += Vector3.Distance(routePoints[i - 1], routePoints[i]);
                var inDir = routePoints[i] - routePoints[i - 2];
                var outDir = routePoints[i + 3] - routePoints[i];
                float turn = Vector3.SignedAngle(new Vector3(inDir.x, 0f, inDir.z), new Vector3(outDir.x, 0f, outDir.z), Vector3.up);
                if (Mathf.Abs(turn) < 35f) continue;
                g.Turn = turn > 0f ? 1 : -1;
                g.Distance = along + Vector3.Distance(pos, routePoints[routeProgress]);
                if (WorldData.NearestRoad(routePoints[i + 3], 18f, out var hit)) g.Street = WorldData.Roads[hit.Road].Data.name ?? "";
                break;
            }
            if (g.Turn == 0) { g.Distance = remaining; g.Arriving = remaining < 400f; }
            Instruction = g;
            HasInstruction = true;
        }

        public static void ClearWaypoint()
        {
            Waypoint = null;
            RouteLength = 0f;
            HasInstruction = false;
            if (RouteTexture != null)
            {
                if (routePx == null) routePx = new Color32[OverlayW * OverlayH];
                System.Array.Clear(routePx, 0, routePx.Length);
                RouteTexture.SetPixels32(routePx);
                RouteTexture.Apply(false);
            }
        }

        void DrawRoute(bool ok)
        {
            var tex = EnsureRouteTexture();
            // one buffer for every redraw (the GPS redraws while driving; a fresh 12 MB array each time fed the GC)
            if (routePx == null) routePx = new Color32[OverlayW * OverlayH];
            else System.Array.Clear(routePx, 0, routePx.Length);
            var px = routePx;
            if (ok)
            {
                var b = WorldData.Bounds;
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i + 1 < routePoints.Count; i++)
                    {
                        var p0 = routePoints[i];
                        var p1 = routePoints[i + 1];
                        Raster(px, (p0.x - b.xMin) / b.width * OverlayW, (p0.z - b.yMin) / b.height * OverlayH,
                               (p1.x - b.xMin) / b.width * OverlayW, (p1.z - b.yMin) / b.height * OverlayH,
                               pass == 0 ? 7f : 3.6f, pass == 0 ? new Color32(70, 0, 60, 200) : new Color32(255, 70, 220, 255));
                    }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            // reached the waypoint: clear it (GTA)
            if (Waypoint.HasValue && player != null && (player.position - Waypoint.Value).sqrMagnitude < 35f * 35f && !IsOpen) ClearWaypoint();
            rerouteCooldown -= Time.unscaledDeltaTime;
            gpsTimer -= Time.unscaledDeltaTime;
            if (!IsOpen && gpsTimer <= 0f) { gpsTimer = 0.25f; UpdateGps(); }
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            if (openT < 1f) Animate(dt);
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var gp = Gamepad.current;

            // zoom about the cursor
            Vector2 cursorLocal = Vector2.zero;
            bool cursorIn = false;
            if (mouse != null)
                cursorIn = RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, mouse.position.ReadValue(), null, out cursorLocal)
                           && viewport.rect.Contains(cursorLocal);
            float zoom = 0f;
            if (mouse != null && cursorIn) zoom += -mouse.scroll.ReadValue().y / 120f;
            if (kb != null) zoom += ((kb.minusKey.isPressed || kb.numpadMinusKey.isPressed ? 1f : 0f) - (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed ? 1f : 0f)) * dt * 2.5f;
            if (gp != null) zoom += (gp.leftTrigger.ReadValue() - gp.rightTrigger.ReadValue()) * dt * 2.5f;
            if (Mathf.Abs(zoom) > 1e-4f)
            {
                Vector2 anchorWorld = cursorIn ? ToWorld(cursorLocal) : center;
                float old = mpp;
                mpp = Mathf.Clamp(mpp * Mathf.Pow(1.25f, zoom), MinMpp, MaxMpp);
                if (cursorIn) center = anchorWorld - cursorLocal * mpp;   // keep the point under the cursor fixed
                if (!Mathf.Approximately(old, mpp)) Refresh();
            }
            // pan
            Vector2 pan = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pan.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pan.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) pan.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) pan.x -= 1f;
            }
            if (gp != null) pan += gp.leftStick.ReadValue();
            if (pan.sqrMagnitude > 0.01f) center += pan * (700f * mpp * dt);
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame && cursorIn) { dragging = true; dragStart = cursorLocal; dragCenter = center; }
                if (!mouse.leftButton.isPressed) dragging = false;
                if (dragging) center = dragCenter - (cursorLocal - dragStart) * mpp;
                if (mouse.rightButton.wasPressedThisFrame && cursorIn)
                {
                    var w = ToWorld(cursorLocal);
                    if (Waypoint.HasValue && (new Vector2(Waypoint.Value.x, Waypoint.Value.z) - w).magnitude < 14f * mpp) { ClearWaypoint(); Refresh(); }
                    else SetWaypoint(new Vector3(w.x, 0f, w.y));
                }
            }
            if (gp != null && gp.buttonSouth.wasPressedThisFrame) SetWaypoint(new Vector3(center.x, 0f, center.y));
            if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame)) { ClearWaypoint(); Refresh(); }
            if ((kb != null && kb.vKey.wasPressedThisFrame) || (gp != null && gp.rightShoulder.wasPressedThisFrame)) ToggleView();
            if ((kb != null && kb.spaceKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame))
                if (player != null) center = new Vector2(player.position.x, player.position.z);
            var bnd = WorldData.Bounds;
            center = new Vector2(Mathf.Clamp(center.x, bnd.xMin, bnd.xMax), Mathf.Clamp(center.y, bnd.yMin, bnd.yMax));

            Layout();
            // cursor info
            Vector2 focus = cursorIn ? ToWorld(cursorLocal) : center;
            cursorRing.gameObject.SetActive(!cursorIn && gp != null);
            var dist = WorldData.DistrictAt(new Vector3(focus.x, 0f, focus.y));
            districtTitle.text = dist != null ? dist.name.ToUpperInvariant() : "OPEN WATER / OUTSKIRTS";
            float toCar = player != null ? Vector2.Distance(focus, new Vector2(player.position.x, player.position.z)) : 0f;
            // the place under the cursor (GTA shows blip names only in its legend; here hovering names any icon)
            Poi hover = null;
            float hoverD = 20f;
            if (cursorIn)
                foreach (var poi in pois)
                {
                    if (!poi.Icon.gameObject.activeSelf) continue;
                    float d = Vector2.Distance(poi.Icon.anchoredPosition, cursorLocal);
                    if (d < hoverD) { hoverD = d; hover = poi; }
                }
            hoverText.text = hover != null ? $"{hover.Name}\n<size=15>{CatNames[hover.Cat]}</size>" : "";
            hoverText.color = hover != null ? CatColor[hover.Cat] : UIKit.TextMain;
            coordText.text = $"{focus.x:0} m E   {focus.y:0} m N\n{Km(toCar)} from your car";
            routeText.text = Waypoint.HasValue
                ? (RouteLength > 0f ? $"WAYPOINT  {Km(RouteLength)} by road\n~{Mathf.Max(1, Mathf.RoundToInt(RouteLength / (60f / 3.6f) / 60f))} min at city speed" : "WAYPOINT  off the road network")
                : "Right-click to set a waypoint";
        }

        static string Km(float m) => m >= 1000f ? $"{m / 1000f:0.0} km" : $"{m:0} m";

        /// <summary>Opening animation (0.22 s): fade in while the map settles from 94 % scale.</summary>
        void Animate(float dt)
        {
            openT = Mathf.Min(1f, openT + dt / 0.22f);
            float e = 1f - Mathf.Pow(1f - openT, 3f);
            if (group != null) group.alpha = e;
            if (viewport != null) viewport.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
        }

        Vector2 ToWorld(Vector2 local) => center + local * mpp;
        Vector2 ToLocal(Vector3 world) => (new Vector2(world.x, world.z) - center) / mpp;

        void Refresh() { if (IsOpen) Layout(); }

        void Layout()
        {
            var r = viewport.rect;
            var b = WorldData.Bounds;
            var uv = new Rect((center.x - r.width * 0.5f * mpp - b.xMin) / b.width, (center.y - r.height * 0.5f * mpp - b.yMin) / b.height,
                              r.width * mpp / b.width, r.height * mpp / b.height);
            aerialImg.uvRect = roadImg.uvRect = routeImg.uvRect = waterImg.uvRect = reliefImg.uvRect = buildingImg.uvRect = atlasImg.uvRect = blockImg.uvRect = uv;
            atlasImg.enabled = blockImg.enabled = AtlasView;
            reliefImg.color = new Color(1f, 1f, 1f, AtlasView ? 0.6f : 1f);
            // building shadows and outlines are a close-range layer: faint when zoomed out, full in the streets
            buildingImg.color = new Color(0f, 0.01f, 0.03f, Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(1.2f, MaxMpp, mpp)));
            aerialImg.enabled = aerial != null && !AtlasView;
            // roads fade in as you zoom in (the aerial carries the zoomed-out view)
            roadImg.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.95f, 0.55f, Mathf.InverseLerp(MinMpp, MaxMpp, mpp)));
            routeImg.enabled = Waypoint.HasValue;

            foreach (var l in labels)
            {
                bool on = mpp >= l.MinMpp && mpp <= l.MaxMpp;
                var p = ToLocal(l.Pos);
                on &= Mathf.Abs(p.x) < r.width * 0.5f + 200f && Mathf.Abs(p.y) < r.height * 0.5f + 60f;
                l.Text.gameObject.SetActive(on);
                if (!on) continue;
                l.Text.rectTransform.anchoredPosition = p;
                l.Text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, l.Angle);
            }
            // iteration 23: declutter (Cyberpunk's launch map was criticised for icon soup): icons are placed in category
            // priority order and one that would overlap an already placed icon is hidden; names likewise
            placedIcons.Clear();
            placedLabels.Clear();
            foreach (var poi in pois)
            {
                var p = ToLocal(poi.Pos);
                bool on = catOn[poi.Cat] && Mathf.Abs(p.x) < r.width * 0.5f + 20f && Mathf.Abs(p.y) < r.height * 0.5f + 20f;
                // fewer icons when zoomed far out: landmarks and secrets only close in
                if (mpp > 3.2f && (poi.Cat == 5 || poi.Cat == 6)) on = false;
                if (on)
                    foreach (var q in placedIcons)
                        if ((q - p).sqrMagnitude < 19f * 19f) { on = false; break; }
                poi.Icon.gameObject.SetActive(on);
                if (!on) continue;
                placedIcons.Add(p);
                poi.Icon.anchoredPosition = p;
                bool label = mpp < 1.6f;
                if (label)
                    foreach (var q in placedLabels)
                        if (Mathf.Abs(q.x - p.x) < 150f && Mathf.Abs(q.y - p.y) < 20f) { label = false; break; }
                if (label) placedLabels.Add(p);
                poi.Label.gameObject.SetActive(label);
            }
            if (player != null)
            {
                playerArrow.anchoredPosition = ToLocal(player.position);
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
            }
            waypointIcon.gameObject.SetActive(Waypoint.HasValue);
            if (Waypoint.HasValue)
            {
                waypointIcon.anchoredPosition = ToLocal(Waypoint.Value);
                waypointLabel.text = RouteLength > 0f ? Km(RouteLength) : "";
            }

            // scale bar: the largest round distance that fits in ~220 px
            float[] steps = { 25f, 50f, 100f, 200f, 250f, 500f, 1000f, 2000f };
            float meters = steps[0];
            foreach (var s in steps) if (s / mpp <= 230f) meters = s;
            scaleBar.sizeDelta = new Vector2(meters / mpp, 6f);
            scaleText.text = meters >= 1000f ? $"{meters / 1000f:0} km" : $"{meters:0} m";
        }

        static Sprite ArrowSprite()
        {
            if (arrowSprite != null) return arrowSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "MapArrow", wrapMode = TextureWrapMode.Clamp };
            var tip = new Vector2(n * 0.5f, n * 0.95f);
            var left = new Vector2(n * 0.12f, n * 0.08f);
            var right = new Vector2(n * 0.88f, n * 0.08f);
            var notch = new Vector2(n * 0.5f, n * 0.3f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = InTri(p, tip, left, notch) || InTri(p, tip, notch, right);
                    bool edge = !inside && (InTri(p, tip + new Vector2(0, 3), left + new Vector2(-3, -3), notch + new Vector2(0, -3)) || InTri(p, tip + new Vector2(0, 3), notch + new Vector2(0, -3), right + new Vector2(3, -3)));
                    tex.SetPixel(x, y, inside ? new Color(1f, 1f, 1f, 1f) : edge ? new Color(0.05f, 0.05f, 0.08f, 0.9f) : new Color(0, 0, 0, 0));
                }
            tex.Apply();
            arrowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return arrowSprite;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}

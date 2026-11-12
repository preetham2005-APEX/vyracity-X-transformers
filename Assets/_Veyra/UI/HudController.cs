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
        Text gText, nitroHint, nitroGain;
        // iteration 29: texts are only rebuilt when what they show changes (fewer strings, fewer canvas rebuilds)
        int shownSpeed = int.MinValue, shownGear = int.MinValue, shownG = int.MinValue, shownNitroKey = int.MinValue, shownHintKey = int.MinValue;
        int shownStatusKey = int.MinValue;
        int shownGpsKey = int.MinValue;
        string shownGpsStreet;
        float gainTimer, gainAmount;
        ChargeSource gainSource;
        Image throttleBar, brakeBar;
        readonly Image[] wheelBlocks = new Image[4];
        /// <summary>Brake disc temperature 0-1 per wheel (FL, FR, RL, RR), supplied by the car's detail FX.</summary>
        public System.Func<int, float> BrakeHeat;
        /// <summary>Body damage 0-1 (CarDamage), shown on the cluster's car icon (iteration 27).</summary>
        public System.Func<float> Damage;
        Image carBody;
        Text carStatus;
        Vector2 gFiltered;
        float tachLimiterBuilt = -1f;
        RectTransform tachRoot, clusterRoot;
        const float TachSweep = 0.75f;   // 270 degrees
        RectTransform racePanel, minimapRoot, mapRotator, playerArrow, markerLayer, toastLayer;
        RawImage mapImage, routeImage, buildingsImage, speedLines, lensDrops;
        static Texture2D dropTex;
        float dropScroll;
        RectTransform gpsPanel, gpsArrow, trapPanel;
        Text trapTitle, trapSpeed, trapSub;
        Image cameraFlash;
        float trapTimer, flashTimer;
        Text gpsDist, gpsStreet, gpsEta;
        float speedLineTimer;
        static Texture2D speedLineTex;
        readonly List<Image> markerPool = new List<Image>();
        readonly List<(Vector3 pos, Color color, float size)> staticMarkers = new List<(Vector3, Color, float)>();
        readonly List<Toast> toasts = new List<Toast>();

        VehicleController vehicle;
        NitroSystem nitro;
        DriftScorer drift;
        RaceSession race;
        float bigTimer;
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

            BuildSpeedLines();
            BuildLensDrops();
            BuildSpeedTrap();
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
            // iteration 29: nested canvases so the moving minimap, the cluster's per-frame fills and the screen effects
            // each re-batch on their own instead of rebuilding the whole HUD every frame
            foreach (var t in new Transform[] { clusterRoot, minimapRoot, gpsPanel, speedLines != null ? speedLines.transform : null,
                                                lensDrops != null ? lensDrops.transform : null, trapPanel })
                if (t != null && t.GetComponent<Canvas>() == null) t.gameObject.AddComponent<Canvas>();
        }

        /// <summary>
        /// Round 2 instrument cluster, bottom right (Asphalt / NFS dial with GT / Forza-style telemetry): a 270 degree
        /// tachometer with rpm numerals and a red zone, big speed digits with the gear in the arc's gap, throttle and brake
        /// pedal bars, seven shift lights, a G-meter, per-wheel brake temperature (wheel lock / spin flashes), BRAKE / ABS /
        /// TCS / NOS / DRIFT lamps and a three-cell nitro gauge coloured by boost mode with its own hint line.
        /// Layout (iteration 19): every element is placed by its centre or top-left in panel pixels (y down).
        /// </summary>
        void BuildCluster()
        {
            var cluster = clusterRoot = UIKit.Box(root, "Cluster", new Vector2(1f, 0f), new Vector2(-28f, 24f), new Vector2(600f, 300f));
            UIKit.Image(cluster, new Color(0.015f, 0.02f, 0.035f, 0.5f), UIKit.SoftRect);
            boostGlow = UIKit.Image(UIKit.Rect(cluster, "BoostGlow", Vector2.zero, Vector2.one, new Vector2(-10, -10), new Vector2(10, 10)), new Color(1f, 0.6f, 0.1f, 0f), UIKit.SoftRect);
            boostGlow.transform.SetAsFirstSibling();

            // tachometer arc, centred 160 px from the left and 150 px from the top
            tachRoot = Centred(cluster, "Tach", new Vector2(160f, -150f), new Vector2(270f, 270f));
            tachRed = Arc(tachRoot, "Red", new Color(1f, 0.22f, 0.2f, 0.7f), TachSweep);
            Arc(tachRoot, "Back", new Color(0.16f, 0.19f, 0.25f, 1f), TachSweep * 0.88f);
            rpmFill = Arc(tachRoot, "Rpm", UIKit.Accent, 0f);
            var inner = UIKit.Box(tachRoot, "Inner", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(214f, 214f));
            UIKit.Image(inner, new Color(0.01f, 0.015f, 0.03f, 0.8f), UIKit.Circle);
            speedText = UIKit.Text(tachRoot, "Speed", "0", 84, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);
            PlaceBox(speedText.rectTransform, new Vector2(0f, 14f), new Vector2(220f, 100f));
            unitText = UIKit.Text(tachRoot, "Unit", "KM/H", 20, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            PlaceBox(unitText.rectTransform, new Vector2(0f, -40f), new Vector2(120f, 26f));
            var gearBox = UIKit.Box(tachRoot, "GearBox", new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(64f, 64f));
            gearBox.pivot = new Vector2(0.5f, 0.5f);
            UIKit.Image(gearBox, new Color(0.02f, 0.03f, 0.06f, 0.95f), UIKit.Circle);
            // one Graphic per GameObject: the ring goes on a child (a second Image on gearBox is null in players)
            UIKit.Image(UIKit.Rect(gearBox, "Ring", Vector2.zero, Vector2.one), new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.7f), UIKit.Ring);
            gearText = UIKit.Text(gearBox, "Gear", "N", 44, TextAnchor.MiddleCenter, UIKit.Accent, FontStyle.Bold);
            UIKit.Stretch(gearText.rectTransform);

            // pedal bars (GT / Forza telemetry): throttle and brake, right of the dial
            throttleBar = PedalBar(cluster, "Throttle", 310f, new Color(0.35f, 1f, 0.55f), "T");
            brakeBar = PedalBar(cluster, "BrakePedal", 328f, UIKit.Bad, "B");

            // shift lights across the top of the right column
            for (int i = 0; i < shiftLeds.Length; i++)
                shiftLeds[i] = UIKit.Image(Centred(cluster, "Led" + i, new Vector2(366f + i * 34f, -30f), new Vector2(20f, 20f)), new Color(1f, 1f, 1f, 0.08f), UIKit.Circle);
            // G-meter
            var g = Centred(cluster, "GMeter", new Vector2(410f, -118f), new Vector2(104f, 104f));
            UIKit.Image(g, new Color(1f, 1f, 1f, 0.05f), UIKit.Circle);
            UIKit.Image(UIKit.Rect(g, "Ring", Vector2.zero, Vector2.one), new Color(1f, 1f, 1f, 0.22f), UIKit.Ring);
            UIKit.Image(UIKit.Box(g, "Half", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f)), new Color(1f, 1f, 1f, 0.12f), UIKit.Ring);
            UIKit.Image(UIKit.Box(g, "H", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 1.5f)), new Color(1f, 1f, 1f, 0.15f));
            UIKit.Image(UIKit.Box(g, "V", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1.5f, 96f)), new Color(1f, 1f, 1f, 0.15f));
            gDot = UIKit.Box(g, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
            UIKit.Image(gDot, UIKit.Accent2, UIKit.Circle);
            gText = UIKit.Text(cluster, "GText", "0.0 G", 15, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            PlaceBox(gText.rectTransform, Vector2.zero, new Vector2(110f, 20f));
            Recentre(gText.rectTransform, new Vector2(410f, -183f));
            // per-wheel brake temperature: a car outline with a block at each corner
            var carIcon = Centred(cluster, "BrakeTemps", new Vector2(522f, -118f), new Vector2(56f, 100f));
            carBody = UIKit.Image(UIKit.Box(carIcon, "Body", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 92f)), new Color(1f, 1f, 1f, 0.1f), UIKit.SoftRect);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 21f, y = (i < 2 ? 1f : -1f) * 30f;
                wheelBlocks[i] = UIKit.Image(UIKit.Box(carIcon, "W" + i, new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(13f, 26f)), new Color(0.3f, 0.36f, 0.45f, 0.9f));
            }
            var bt = carStatus = UIKit.Text(cluster, "BrakeTempText", "BRAKES", 13, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            PlaceBox(bt.rectTransform, Vector2.zero, new Vector2(90f, 18f));
            Recentre(bt.rectTransform, new Vector2(522f, -183f));
            // status lamps in one row
            string[] lamps = { "BRAKE", "ABS", "TCS", "NOS", "DRIFT" };
            for (int i = 0; i < lamps.Length; i++)
            {
                var lb = UIKit.Box(cluster, "Lamp" + lamps[i], new Vector2(0f, 1f), new Vector2(350f + i * 46f, -200f), new Vector2(43f, 22f));
                lampBacks[i] = UIKit.Image(lb, new Color(1f, 1f, 1f, 0.06f));
                lampTexts[i] = UIKit.Text(lb, "T", lamps[i], 12, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.25f), FontStyle.Bold);
                lampTexts[i].horizontalOverflow = HorizontalWrapMode.Overflow;   // "BRAKE" wrapped to two lines in iteration 19
                UIKit.Stretch(lampTexts[i].rectTransform);
            }
            // nitro gauge: label, three cells (one per bottle), hint line
            nitroLabel = UIKit.Text(cluster, "NitroLabel", "NITRO", 17, TextAnchor.LowerLeft, UIKit.Amber, FontStyle.Bold);
            PlaceTopLeft(nitroLabel.rectTransform, new Vector2(350f, -230f), new Vector2(230f, 22f));
            var bar = UIKit.Box(cluster, "NitroCells", new Vector2(0f, 1f), new Vector2(350f, -256f), new Vector2(228f, 14f));
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
            // iteration 28 (Asphalt): what just filled the meter, e.g. "+12% DRIFT", rising and fading by the gauge
            nitroGain = UIKit.Text(cluster, "NitroGain", "", 15, TextAnchor.LowerRight, UIKit.Good, FontStyle.Bold);
            PlaceTopLeft(nitroGain.rectTransform, new Vector2(430f, -230f), new Vector2(150f, 22f));
            nitroHint = UIKit.Text(cluster, "NitroHint", "", 12, TextAnchor.UpperLeft, UIKit.TextDim, FontStyle.Bold);
            PlaceTopLeft(nitroHint.rectTransform, new Vector2(350f, -274f), new Vector2(240f, 18f));
        }

        /// <summary>A child box centred at <paramref name="centre"/> (panel pixels from its top-left corner, y down).</summary>
        static RectTransform Centred(RectTransform parent, string name, Vector2 centre, Vector2 size)
        {
            var rt = UIKit.Rect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f));
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre;
            rt.sizeDelta = size;
            return rt;
        }

        static void Recentre(RectTransform rt, Vector2 centre)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre;
        }

        static Image PedalBar(RectTransform parent, string name, float x, Color c, string letter)
        {
            var back = Centred(parent, name, new Vector2(x, -150f), new Vector2(10f, 150f));
            UIKit.Image(back, new Color(1f, 1f, 1f, 0.07f));
            var f = UIKit.Rect(back, "Fill", Vector2.zero, Vector2.one);
            var img = f.gameObject.AddComponent<Image>();
            img.sprite = UIKit.Texture2DSprite();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Vertical;
            img.fillOrigin = (int)Image.OriginVertical.Bottom;
            img.color = c;
            img.fillAmount = 0f;
            img.raycastTarget = false;
            var t = UIKit.Text(parent, name + "Letter", letter, 12, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            PlaceBox(t.rectTransform, Vector2.zero, new Vector2(16f, 16f));
            Recentre(t.rectTransform, new Vector2(x, -236f));
            return img;
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

        /// <summary>
        /// Iteration 21 (Asphalt / NFS speed feel): radial speed lines over the screen edges, faint above ~170 km/h and
        /// strong under nitro, tinted by the boost mode; the centre (the car) stays clear. One full-screen RawImage with a
        /// procedural streak texture, re-rotated every few frames so the streaks flicker past.
        /// </summary>
        void BuildSpeedLines()
        {
            speedLines = UIKit.Rect(root, "SpeedLines", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            speedLines.raycastTarget = false;
            speedLines.texture = SpeedLineTexture();
            speedLines.color = new Color(1f, 1f, 1f, 0f);
            var rt = speedLines.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(2600f, 2600f);   // a square that covers the screen while it turns
        }

        static Texture2D SpeedLineTexture()
        {
            if (speedLineTex != null) return speedLineTex;
            const int n = 512, lines = 220;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a01 = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * lines;
                    int id = Mathf.FloorToInt(a01);
                    float h = Mathf.Abs(Mathf.Sin(id * 12.9898f) * 43758.5453f) % 1f;
                    if (h < 0.62f) continue;
                    float start = 0.5f + 0.35f * (Mathf.Abs(Mathf.Sin(id * 78.233f) * 12345.678f) % 1f);
                    float across = Mathf.Abs((a01 - id) - 0.5f) * 2f;               // 0 at the streak's middle
                    float width = 0.18f + 0.3f * ((h - 0.62f) / 0.38f);
                    float line = Mathf.Clamp01(1f - across / width);
                    float along = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + 0.22f, r)) * Mathf.Clamp01((1.42f - r) / 0.2f);
                    byte a = (byte)(255f * line * line * along);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            speedLineTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SpeedLines", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            speedLineTex.SetPixels32(px);
            speedLineTex.Apply(true, true);
            return speedLineTex;
        }

        /// <summary>
        /// Iteration 27 (Forza / NFS rain): droplets on the camera lens while it rains, bright-rimmed beads with a dark
        /// core; they bead up slowly when stopped and run down the screen with speed. One RawImage, fades with the rain.
        /// </summary>
        void BuildLensDrops()
        {
            lensDrops = UIKit.Rect(root, "LensDrops", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            lensDrops.raycastTarget = false;
            lensDrops.texture = DropTexture();
            lensDrops.color = new Color(1f, 1f, 1f, 0f);
            lensDrops.enabled = false;
        }

        static Texture2D DropTexture()
        {
            if (dropTex != null) return dropTex;
            const int n = 512;
            var px = new Color32[n * n];
            var rng = new System.Random(77);
            for (int k = 0; k < 140; k++)
            {
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float r = 2.5f + (float)(rng.NextDouble() * rng.NextDouble()) * 11f;
                float tail = rng.NextDouble() < 0.35 ? r * (2f + (float)rng.NextDouble() * 5f) : 0f;   // some drops have run
                int x0 = Mathf.FloorToInt(cx - r - 2), x1 = Mathf.CeilToInt(cx + r + 2);
                int y0 = Mathf.FloorToInt(cy - r - 2), y1 = Mathf.CeilToInt(cy + r + tail + 2);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dy = y - cy;
                        float rr = dy > 0f && tail > 0f ? Mathf.Lerp(r, r * 0.35f, Mathf.Clamp01(dy / tail)) : r;
                        float ey = dy > 0f && tail > 0f ? Mathf.Max(0f, dy - tail * Mathf.Clamp01(dy / tail)) : dy;
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + ey * ey) / Mathf.Max(0.5f, rr);
                        if (d > 1f) continue;
                        int ix = ((x % n) + n) % n, iy = ((y % n) + n) % n;
                        // rim highlight (top-left brighter), darker refracting core
                        float rim = Mathf.SmoothStep(0.6f, 1f, d) * (1f - Mathf.SmoothStep(0.92f, 1f, d));
                        float hl = Mathf.Clamp01(1f - Mathf.Sqrt((x - (cx - rr * 0.35f)) * (x - (cx - rr * 0.35f)) + (y - (cy + rr * 0.35f)) * (y - (cy + rr * 0.35f))) / (rr * 0.3f));
                        // D1: clear drops, not grey discs: near-transparent core, bright thin rim and highlight
                        byte v = (byte)Mathf.Clamp(200f + 55f * Mathf.Max(rim, hl), 0f, 255f);
                        byte a = (byte)Mathf.Clamp(255f * (0.04f + 0.45f * rim + 0.7f * hl), 0f, 255f);
                        var c = px[iy * n + ix];
                        if (a > c.a) px[iy * n + ix] = new Color32(v, v, (byte)Mathf.Min(255, v + 8), a);
                    }
            }
            dropTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "LensDrops", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            dropTex.SetPixels32(px);
            dropTex.Apply(true, true);
            return dropTex;
        }

        void UpdateLensDrops(float dt)
        {
            if (lensDrops == null) return;
            var env = WorldEnvironment.Instance;
            float rain = env != null ? env.RainIntensity : 0f;
            float kmh = vehicle != null && vehicle.IsInitialised ? vehicle.State.SpeedKmh : 0f;
            float target = rain * 0.45f * (1f - 0.5f * Mathf.Clamp01(kmh / 160f));   // wind clears the lens at speed
            var c = lensDrops.color;
            c.a = Mathf.MoveTowards(c.a, target, dt * 0.4f);
            lensDrops.color = c;
            lensDrops.enabled = c.a > 0.01f;
            if (!lensDrops.enabled) return;
            dropScroll += dt * (0.01f + 0.12f * Mathf.Clamp01(kmh / 120f));   // run down faster with speed
            lensDrops.uvRect = new Rect(0f, dropScroll, 3.4f, 3.4f * Screen.height / Mathf.Max(1f, Screen.width));   // D1: smaller drops
        }

        /// <summary>Iteration 26: speed-trap banner (top centre) and the camera's white flash.</summary>
        void BuildSpeedTrap()
        {
            cameraFlash = UIKit.Image(UIKit.Rect(root, "CameraFlash", Vector2.zero, Vector2.one), new Color(1f, 1f, 1f, 0f));
            cameraFlash.raycastTarget = false;
            cameraFlash.enabled = false;
            trapPanel = UIKit.Box(root, "SpeedTrap", new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(600f, 134f));
            trapPanel.pivot = new Vector2(0.5f, 1f);
            UIKit.Image(trapPanel, new Color(0.03f, 0.04f, 0.07f, 0.86f));
            UIKit.Image(UIKit.Rect(trapPanel, "Stripe", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), Vector2.zero), UIKit.Amber);
            trapTitle = UIKit.Text(trapPanel, "Title", "", 19, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            Place(trapTitle.rectTransform, new Vector2(0.02f, 0.72f), new Vector2(0.98f, 0.95f));
            trapSpeed = UIKit.Text(trapPanel, "Speed", "", 54, TextAnchor.MiddleCenter, UIKit.Amber, FontStyle.Bold);
            Place(trapSpeed.rectTransform, new Vector2(0.02f, 0.24f), new Vector2(0.98f, 0.76f));
            trapSub = UIKit.Text(trapPanel, "Sub", "", 18, TextAnchor.MiddleCenter, UIKit.TextMain, FontStyle.Bold);
            Place(trapSub.rectTransform, new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.25f));
            trapPanel.gameObject.SetActive(false);
        }

        /// <summary>Shows a speed-trap result (SpeedTraps): camera flash, then the banner for ~2.8 s.</summary>
        public void SpeedTrapFlash(string title, string speed, string sub, int stars)
        {
            if (trapPanel == null) return;
            trapTitle.text = title;
            trapSpeed.text = speed;
            trapSpeed.color = stars >= 3 ? UIKit.Accent2 : stars == 2 ? UIKit.Amber : stars == 1 ? UIKit.Accent : UIKit.TextMain;
            trapSub.text = sub;
            trapSub.color = sub == "NEW BEST" ? UIKit.Good : UIKit.TextMain;
            trapTimer = 2.8f;
            flashTimer = 0.25f;
            trapPanel.gameObject.SetActive(true);
        }

        void UpdateSpeedTrap(float dt)
        {
            if (trapPanel == null) return;
            if (flashTimer > 0f) flashTimer -= dt;
            cameraFlash.enabled = flashTimer > 0f;
            if (cameraFlash.enabled) cameraFlash.color = new Color(1f, 1f, 1f, 0.55f * flashTimer / 0.25f);
            if (trapTimer <= 0f) return;
            trapTimer -= dt;
            float pop = Mathf.Clamp01((trapTimer - 2.55f) / 0.25f);   // a short scale pop on arrival
            trapPanel.localScale = Vector3.one * (1f + 0.18f * pop);
            if (trapTimer <= 0f) trapPanel.gameObject.SetActive(false);
        }

        void UpdateSpeedLines(float dt)
        {
            if (speedLines == null) return;
            float kmh = vehicle != null && vehicle.IsInitialised ? vehicle.State.SpeedKmh : 0f;
            float boost = nitro != null ? nitro.BoostScalar : 0f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 280f, kmh)) * 0.3f + boost * Mathf.Clamp01(kmh / 120f) * 0.55f;
            Color c = boost > 0.05f && nitro != null
                ? (nitro.Mode == NitroMode.Overdrive ? new Color(1f, 0.45f, 0.9f) : nitro.Mode == NitroMode.CleanBoost ? new Color(0.55f, 1f, 0.75f) : new Color(0.6f, 0.85f, 1f))
                : Color.white;
            var cur = speedLines.color;
            float alpha = Mathf.Lerp(cur.a, a, 1f - Mathf.Exp(-dt * 8f));
            speedLines.color = new Color(c.r, c.g, c.b, alpha);
            speedLines.enabled = alpha > 0.005f;
            speedLineTimer -= dt;
            if (speedLines.enabled && speedLineTimer <= 0f)
            {
                speedLineTimer = 0.045f;
                speedLines.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                speedLines.rectTransform.localScale = Vector3.one * Random.Range(0.95f, 1.08f);
            }
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
            // iteration 21: building footprints from the world map's building layer (GTA-style radar blocks)
            buildingsImage = UIKit.Rect(mapRotator, "Buildings", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            buildingsImage.raycastTarget = false;
            buildingsImage.texture = WorldMap.BuildingFillTexture();
            buildingsImage.color = new Color(0.62f, 0.7f, 0.82f, 0.22f);
            routeImage = UIKit.Rect(mapRotator, "Route", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();   // GPS route (WorldMap)
            routeImage.raycastTarget = false;
            routeImage.enabled = false;
            markerLayer = UIKit.Rect(maskRt, "Markers", Vector2.zero, Vector2.one);
            var ring = UIKit.Image(UIKit.Rect(minimapRoot, "Ring", Vector2.zero, Vector2.one), new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.7f), UIKit.Ring);
            playerArrow = UIKit.Box(minimapRoot, "Player", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 26f));
            var arrow = UIKit.Text(playerArrow, "Glyph", "▲", 26, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var north = UIKit.Text(UIKit.Box(minimapRoot, "N", new Vector2(0.5f, 1f), new Vector2(0, 4), new Vector2(30, 30)), "T", "N", 20, TextAnchor.MiddleCenter, UIKit.TextDim, FontStyle.Bold);
            north.transform.parent.name = "North";
            BuildGpsPanel();
        }

        /// <summary>
        /// Iteration 22: satnav panel over the minimap while a waypoint is set (GTA V only draws the line; Forza and real
        /// satnavs also say what is next): turn arrow, distance to the turn, the street it leads into, distance left.
        /// </summary>
        void BuildGpsPanel()
        {
            gpsPanel = UIKit.Box(root, "Gps", new Vector2(0f, 0f), new Vector2(36f, 350f), new Vector2(300f, 74f));
            UIKit.Image(gpsPanel, new Color(0.03f, 0.04f, 0.07f, 0.82f));
            UIKit.Image(UIKit.Rect(gpsPanel, "Accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(5f, 0f)), new Color(1f, 0.3f, 0.85f));
            gpsArrow = UIKit.Box(gpsPanel, "Arrow", new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(44f, 44f));
            gpsArrow.pivot = new Vector2(0.5f, 0.5f);
            var glyph = UIKit.Text(gpsArrow, "G", "▲", 40, TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.9f), FontStyle.Bold);
            UIKit.Stretch(glyph.rectTransform);
            gpsDist = UIKit.Text(gpsPanel, "Dist", "", 26, TextAnchor.UpperLeft, UIKit.TextMain, FontStyle.Bold);
            Place(gpsDist.rectTransform, new Vector2(0.27f, 0.44f), new Vector2(0.98f, 0.96f));
            gpsStreet = UIKit.Text(gpsPanel, "Street", "", 16, TextAnchor.LowerLeft, UIKit.TextDim, FontStyle.Bold);
            Place(gpsStreet.rectTransform, new Vector2(0.27f, 0.06f), new Vector2(0.98f, 0.46f));
            gpsEta = UIKit.Text(gpsPanel, "Left", "", 16, TextAnchor.UpperRight, new Color(1f, 0.55f, 0.92f), FontStyle.Bold);
            Place(gpsEta.rectTransform, new Vector2(0.55f, 0.5f), new Vector2(0.97f, 0.94f));
            gpsPanel.gameObject.SetActive(false);
        }

        void UpdateGps()
        {
            if (gpsPanel == null) return;
            bool on = WorldMap.HasInstruction && WorldMap.Waypoint.HasValue;
            if (gpsPanel.gameObject.activeSelf != on) gpsPanel.gameObject.SetActive(on);
            if (!on) return;
            var g = WorldMap.Instruction;
            int gkey = (g.Turn + 2) * 10000000 + (g.Arriving ? 5000000 : 0) + Mathf.Min(49999, Mathf.RoundToInt(g.Distance / 10f)) * 100 + Mathf.Min(99, Mathf.RoundToInt(g.Remaining / 1000f * 10f));
            if (gkey == shownGpsKey && shownGpsStreet == g.Street) return;
            shownGpsKey = gkey;
            shownGpsStreet = g.Street;
            string D(float m) => Metric ? (m >= 1000f ? $"{m / 1000f:0.0} km" : $"{Mathf.RoundToInt(m / 10f) * 10} m")
                                        : (m >= 1609f ? $"{m / 1609.34f:0.0} mi" : $"{Mathf.RoundToInt(m * 1.0936f / 10f) * 10} yd");
            gpsArrow.localRotation = Quaternion.Euler(0f, 0f, g.Turn < 0 ? 90f : g.Turn > 0 ? -90f : 0f);
            gpsDist.text = g.Arriving ? $"ARRIVING  {D(g.Distance)}" : g.Turn == 0 ? $"AHEAD  {D(g.Distance)}" : $"{(g.Turn < 0 ? "LEFT" : "RIGHT")}  {D(g.Distance)}";
            gpsStreet.text = g.Turn != 0 && !string.IsNullOrEmpty(g.Street) ? "onto " + g.Street : "";
            gpsEta.text = g.Turn != 0 ? D(g.Remaining) : "";
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
            if (nitro != null) { nitro.ModeStarted -= OnNitroMode; nitro.Charged -= OnNitroCharged; }
            vehicle = v;
            nitro = n;
            drift = d;
            if (drift != null) { drift.ChainBanked += OnDriftBanked; drift.ChainFailed += OnDriftFailed; }
            if (nitro != null) { nitro.ModeStarted += OnNitroMode; nitro.Charged += OnNitroCharged; }
        }

        void OnNitroCharged(ChargeSource source, float amount)
        {
            if (source == ChargeSource.Passive) return;
            if (gainTimer <= 0f || source != gainSource) gainAmount = 0f;
            gainAmount += amount;
            gainSource = source;
            gainTimer = 1.3f;
        }

        void UpdateNitroGain(float dt)
        {
            if (nitroGain == null) return;
            if (gainTimer <= 0f) { if (nitroGain.text.Length > 0) nitroGain.text = ""; return; }
            gainTimer -= dt;
            string what = gainSource == ChargeSource.Drift ? "DRIFT" : gainSource == ChargeSource.NearMiss ? "NEAR MISS"
                        : gainSource == ChargeSource.Airtime ? "AIR" : gainSource == ChargeSource.CleanSection ? "CLEAN" : "PICKUP";
            nitroGain.text = $"+{Mathf.Max(1, Mathf.RoundToInt(gainAmount / NitroSystem.MeterMax * 100f))}% {what}";
            var c = UIKit.Good;
            c.a = Mathf.Clamp01(gainTimer * 2.5f);
            nitroGain.color = c;
            nitroGain.rectTransform.anchoredPosition = new Vector2(430f, -230f + (1.3f - gainTimer) * 14f);
        }

        void OnNitroMode(NitroMode m)
        {
            if (m == NitroMode.CleanBoost) ShowToast("CLEAN BOOST", UIKit.Good);
            else if (m == NitroMode.Overdrive) ShowToast("OVERDRIVE", UIKit.Accent2);
        }

        void OnDriftBanked(float points) { ShowToast("DRIFT +" + Mathf.RoundToInt(points).ToString("N0"), UIKit.Accent); }
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
            UpdateSpeedLines(dt);
            UpdateNitroGain(dt);
            UpdateLensDrops(dt);
            UpdateSpeedTrap(dt);
            UpdateRace();
            UpdateDrift();
            UpdateMinimap();
            UpdateGps();
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
            int sp = Mathf.RoundToInt(Mathf.Abs(spd));
            if (sp != shownSpeed) { shownSpeed = sp; speedText.text = sp.ToString(); }
            string unit = Metric ? "KM/H" : "MPH";
            if (!ReferenceEquals(unitText.text, unit)) unitText.text = unit;
            if (st.Gear != shownGear) { shownGear = st.Gear; gearText.text = st.Gear < 0 ? "R" : st.Gear == 0 ? "N" : st.Gear.ToString(); }
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
            int gq = Mathf.RoundToInt(gFiltered.magnitude * 10f);
            if (gq != shownG) { shownG = gq; gText.text = (gq / 10f).ToString("0.0") + " G"; }
            // status lamps
            float b = Mathf.Max(st.AppliedBrake, vehicle.Controls.Brake);
            // pedals and per-wheel brake temperature / lock-up
            throttleBar.fillAmount = Mathf.Lerp(throttleBar.fillAmount, Mathf.Clamp01(st.AppliedThrottle), 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
            throttleBar.color = st.TcsCut > 0.05f ? UIKit.Amber : new Color(0.35f, 1f, 0.55f);
            brakeBar.fillAmount = Mathf.Lerp(brakeBar.fillAmount, Mathf.Clamp01(b), 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
            brakeBar.color = st.AbsActive ? UIKit.Amber : UIKit.Bad;
            for (int i = 0; i < 4; i++)
            {
                float h = BrakeHeat != null ? Mathf.Clamp01(BrakeHeat(i)) : 0f;
                Color hc = h < 0.5f ? Color.Lerp(new Color(0.3f, 0.36f, 0.45f, 0.9f), new Color(1f, 0.42f, 0.08f, 1f), h * 2f)
                                    : Color.Lerp(new Color(1f, 0.42f, 0.08f, 1f), new Color(1f, 0.92f, 0.6f, 1f), (h - 0.5f) * 2f);
                float sr = vehicle.GetWheel(i).SlipRatio;
                if (Mathf.Abs(sr) > 0.25f && st.Speed > 3f && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f)
                    hc = sr < 0f ? new Color(1f, 0.25f, 0.25f) : new Color(0.35f, 0.65f, 1f);   // lock-up red, wheelspin blue
                wheelBlocks[i].color = hc;
            }
            // car status: the body tints from white to red with damage (GT / Forza style car icon)
            float dmg = Damage != null ? Mathf.Clamp01(Damage()) : 0f;
            carBody.color = Color.Lerp(new Color(1f, 1f, 1f, 0.1f), new Color(1f, 0.28f, 0.22f, 0.75f), Mathf.Clamp01(dmg * 1.6f));
            int skey = dmg > 0.04f ? Mathf.RoundToInt(dmg * 100f) : -1;   // P1 (round 3): rebuild the string only when it changes
            if (skey != shownStatusKey) { shownStatusKey = skey; carStatus.text = skey >= 0 ? $"DAMAGE {skey}%" : "BRAKES"; }
            carStatus.color = dmg > 0.04f ? Color.Lerp(UIKit.Amber, UIKit.Bad, dmg) : UIKit.TextDim;
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
                int pct = Mathf.RoundToInt(amt * 100f);
                int nkey = (int)nitro.Mode * 10000 + (nitro.CleanWindowOpenNow ? 5000 : 0) + (full ? 2000 : 0) + pct;
                if (nkey != shownNitroKey)
                {
                    shownNitroKey = nkey;
                    nitroLabel.text = nitro.Mode == NitroMode.Overdrive ? "OVERDRIVE" : nitro.Mode == NitroMode.CleanBoost ? "CLEAN BOOST" : nitro.CleanWindowOpenNow ? "TAP AGAIN!" : full ? "NITRO  FULL" : "NITRO  " + pct + "%";
                }
                bool cleanHint = nitro.Mode == NitroMode.Standard && !nitro.CleanWindowOpenNow && nitro.SinceActivation < NitroSystem.CleanWindowOpen;
                int cool = Mathf.CeilToInt(nitro.OverdriveCooldownLeft * 10f);
                int hkey = cleanHint ? -1 : cool > 0 ? cool : full && nitro.Mode == NitroMode.Off ? -2 : -3;
                if (hkey != shownHintKey)
                {
                    shownHintKey = hkey;
                    nitroHint.text = hkey == -1 ? "TAP AGAIN IN THE GREEN = CLEAN BOOST" : hkey > 0 ? "OVERDRIVE COOLING  " + (cool / 10f).ToString("0.0") + "s"
                                   : hkey == -2 ? "DOUBLE-TAP = OVERDRIVE" : "";
                }
                var g = boostGlow.color;
                g.a = nitro.BoostScalar * 0.22f;   // iteration 24: a tint, not a filled blue panel
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
            if (buildingsImage != null) buildingsImage.uvRect = mapImage.uvRect;
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

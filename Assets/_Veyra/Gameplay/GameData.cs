using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Racing;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    [Serializable]
    public sealed class CarEntry
    {
        public string Id;
        public string Name;
        public string Maker;
        [TextArea] public string Description;
        public int Price;
        public int UnlockLevel;
        public bool Starter;
        public bool AIOnly;
        public VehicleDefinition Definition;
        public GameObject Model;
        public Color DefaultPaint = new Color(0.6f, 0.62f, 0.64f);
        [Tooltip("Licence credit shown in the garage (CC-BY models)")] public string Credit;
    }

    // ------------------------------------------------------------------------------------------
    // Save data
    // ------------------------------------------------------------------------------------------
    [Serializable]
    public sealed class OwnedCar
    {
        public string CarId;
        public int[] Upgrades = new int[Upgrade.Count];
        public Color Paint = new Color(0.6f, 0.62f, 0.64f);
    }

    [Serializable]
    public sealed class EventRecord
    {
        public string Id;
        public float BestTime;
        public int BestPosition;
        public bool FirstWinClaimed;
        public int Starts;
        public int Wins;
    }

    [Serializable]
    public sealed class SettingsData
    {
        public int Quality = 2;
        public int ResolutionIndex = -1;
        public bool Fullscreen = true;
        public bool VSync = true;
        public int FpsCap = 0;
        public float MasterVolume = 0.85f;
        public float EngineVolume = 1f;
        public int AssistLevel = 0;
        public bool Metric = true;
        public float CameraEffects = 1f;
        public int TrafficDensity = 2;
        public int Difficulty = 1;
        public int TimeOfDay = 0; // 0 dynamic, 1 night, 2 golden, 3 day
    }

    [Serializable]
    public sealed class SaveData
    {
        public int Version = 1;
        public int Cash = 6000;
        public int Xp;
        public int[] CrewRep = new int[5];
        public List<OwnedCar> Cars = new List<OwnedCar>();
        public string CurrentCar;
        public List<EventRecord> Events = new List<EventRecord>();
        public int StoryFlags;
        public List<string> SeenStory = new List<string>();
        public SettingsData Settings = new SettingsData();
        public float PlaySeconds;
        public float FreeRoamTime = 21.5f;

        public int Level => 1 + Mathf.FloorToInt(Mathf.Sqrt(Xp / 400f));
        public float LevelProgress
        {
            get
            {
                float a = (Level - 1) * (Level - 1) * 400f, b = Level * Level * 400f;
                return Mathf.InverseLerp(a, b, Xp);
            }
        }

        public OwnedCar Owned(string id)
        {
            foreach (var c in Cars) if (c.CarId == id) return c;
            return null;
        }

        public EventRecord Record(string id, bool create)
        {
            foreach (var e in Events) if (e.Id == id) return e;
            if (!create) return null;
            var r = new EventRecord { Id = id };
            Events.Add(r);
            return r;
        }

        public int Wins
        {
            get { int w = 0; foreach (var e in Events) if (e.Wins > 0) w++; return w; }
        }
    }

    /// <summary>JSON save next to the game (E: drive), falling back to the per-user folder if read-only.</summary>
    public static class SaveSystem
    {
        /// <summary>Set by -veyraSaveDir (automated runs keep their own profile).</summary>
        public static string OverrideFolder;

        public static string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(OverrideFolder)) { Directory.CreateDirectory(OverrideFolder); return OverrideFolder; }
                string baseDir = Application.isEditor ? Path.Combine(Application.dataPath, "..", "Saves") : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Saves");
                try
                {
                    Directory.CreateDirectory(baseDir);
                    string probe = Path.Combine(baseDir, ".write_test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return baseDir;
                }
                catch (Exception)
                {
                    string fb = Path.Combine(Application.persistentDataPath, "Saves");
                    Directory.CreateDirectory(fb);
                    return fb;
                }
            }
        }

        public static string FilePath => Path.Combine(Folder, "profile.json");

        public static bool Exists => File.Exists(FilePath);

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                    if (data != null)
                    {
                        if (data.Settings == null) data.Settings = new SettingsData();
                        if (data.CrewRep == null || data.CrewRep.Length < 5) data.CrewRep = new int[5];
                        foreach (var c in data.Cars) if (c.Upgrades == null || c.Upgrades.Length < Upgrade.Count) Array.Resize(ref c.Upgrades, Upgrade.Count);
                        return data;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Save] load failed: " + e.Message); }
            return null;
        }

        public static void Save(SaveData data)
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("[Save] write failed: " + e.Message); }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Upgrades
    // ------------------------------------------------------------------------------------------
    public static class Upgrade
    {
        public const int Engine = 0, Nitro = 1, Tyres = 2, Brakes = 3, Suspension = 4, Weight = 5;
        public const int Count = 6;
        public const int MaxLevel = 3;
        public static readonly string[] Names = { "Engine", "Nitro system", "Tyres", "Brakes", "Suspension", "Weight reduction" };
        public static readonly string[][] LevelNames =
        {
            new[] { "Stock", "Street intake + exhaust", "Sport cams + ECU", "Race build + turbo" },
            new[] { "Stock", "Street nozzles", "Sport kit", "Race kit" },
            new[] { "Stock", "Street", "Sport", "Semi-slick" },
            new[] { "Stock", "Street pads", "Sport discs", "Race calipers" },
            new[] { "Stock", "Lowering springs", "Coilovers", "Race dampers + bars" },
            new[] { "Stock", "Light seats", "Carbon panels", "Stripped interior" },
        };
        static readonly float[] EngineMul = { 1f, 1.10f, 1.22f, 1.36f };
        static readonly float[] TyreMul = { 1f, 1.04f, 1.08f, 1.13f };
        static readonly float[] BrakeMul = { 1f, 1.12f, 1.25f, 1.38f };
        static readonly float[] SpringMul = { 1f, 1.10f, 1.22f, 1.32f };
        static readonly float[] MassMul = { 1f, 0.96f, 0.92f, 0.88f };
        public static readonly float[] NitroMul = { 1f, 1.10f, 1.20f, 1.30f };

        public static int Cost(CarEntry car, int category, int level)
        {
            float basePrice = Mathf.Max(8000f, car.Price);
            float[] share = { 0.10f, 0.06f, 0.05f, 0.04f, 0.05f, 0.05f };
            return Mathf.RoundToInt(basePrice * share[category] * level * level * 0.6f / 100f) * 100 + 500 * level;
        }

        /// <summary>Runtime copy of the car's definition with upgrades applied (never edits the asset).</summary>
        public static VehicleDefinition Build(CarEntry car, OwnedCar owned)
        {
            var d = UnityEngine.Object.Instantiate(car.Definition);
            d.name = car.Definition.name + "_Runtime";
            if (owned == null) return d;
            int[] u = owned.Upgrades;
            var curve = (Vector2[])d.Engine.TorqueCurve.Clone();
            for (int i = 0; i < curve.Length; i++) curve[i].y *= EngineMul[u[Engine]];
            d.Engine.TorqueCurve = curve;
            d.Front.Tyre.Mu *= TyreMul[u[Tyres]];
            d.Rear.Tyre.Mu *= TyreMul[u[Tyres]];
            d.Brakes.MaxTorque *= BrakeMul[u[Brakes]];
            d.Front.Suspension.SpringRate *= SpringMul[u[Suspension]];
            d.Rear.Suspension.SpringRate *= SpringMul[u[Suspension]];
            d.Front.Suspension.AntiRollRate *= SpringMul[u[Suspension]] * 1.1f;
            d.Rear.Suspension.AntiRollRate *= SpringMul[u[Suspension]] * 1.1f;
            d.Front.Suspension.BumpDamping *= Mathf.Sqrt(SpringMul[u[Suspension]]);
            d.Rear.Suspension.BumpDamping *= Mathf.Sqrt(SpringMul[u[Suspension]]);
            d.Front.Suspension.ReboundDamping *= Mathf.Sqrt(SpringMul[u[Suspension]]);
            d.Rear.Suspension.ReboundDamping *= Mathf.Sqrt(SpringMul[u[Suspension]]);
            d.Chassis.CoMHeight -= 0.012f * u[Suspension];
            d.Chassis.Mass *= MassMul[u[Weight]];
            d.TuningVersion += 1000 + u[Engine] * 100 + u[Tyres] * 10 + u[Weight];
            return d;
        }

        public struct Stats { public float PowerKw, Mass, TopKmh, Grip, Accel, Pi; public string Class; }

        public static Stats Evaluate(VehicleDefinition d)
        {
            float peakW = 0f;
            foreach (var k in d.Engine.TorqueCurve) peakW = Mathf.Max(peakW, k.y * k.x * Mathf.PI / 30f);
            float kw = peakW / 1000f;
            float top = VeyraCity.Nitro.NitroSystem.EstimateTopSpeed(d) * 3.6f;
            float grip = (d.Front.Tyre.Mu + d.Rear.Tyre.Mu) * 0.5f;
            float pw = kw / Mathf.Max(600f, d.Chassis.Mass) * 1000f; // kW per tonne
            float accel = Mathf.Clamp(pw / 3.2f, 0f, 100f);
            float pi = Mathf.Clamp(pw * 1.55f + grip * 180f + top * 0.6f - 80f, 100f, 999f);
            string cls = pi >= 800 ? "S" : pi >= 700 ? "A" : pi >= 600 ? "B" : pi >= 500 ? "C" : "D";
            return new Stats { PowerKw = kw, Mass = d.Chassis.Mass, TopKmh = top, Grip = grip, Accel = accel, Pi = pi, Class = cls };
        }
    }

    // ------------------------------------------------------------------------------------------
    // Career (original story; crews from the world design document)
    // ------------------------------------------------------------------------------------------
    [Serializable]
    public sealed class StoryLine
    {
        public string Speaker;
        public string Text;
        public StoryLine(string s, string t) { Speaker = s; Text = t; }
    }

    public sealed class CareerEvent
    {
        public RaceDefinition Race;
        public string Blurb;
        public bool Gate;
    }

    public sealed class Chapter
    {
        public string Id;
        public string Title;
        public string Crew;
        public int CrewIndex;
        public int WinsRequired;
        public string[] RequiresChapters = Array.Empty<string>();
        public StoryLine[] Intro;
        public StoryLine[] Outro;
        public List<CareerEvent> Events = new List<CareerEvent>();
    }

    public static class Career
    {
        public static readonly string[] Crews = { "The Nightshift", "Iron Wolves", "Apex Collective", "Coastline Society", "Redline Syndicate" };
        static List<Chapter> chapters;

        static RaceDefinition R(string id, string name, string route, RaceKind kind, int laps, int opponents, float skill, int cash, int rep, string crew, float time, bool rain = false)
        {
            int c2 = cash * 2 / 3, c3 = cash / 2, c4 = cash / 4;
            return new RaceDefinition
            {
                Id = id, Name = name, RouteId = route, Kind = kind, Laps = laps, Opponents = opponents, AISkill = skill,
                CashByPosition = new[] { cash, c2, c3, c4 }, Reputation = rep, Crew = crew, TimeOfDay = time, Rain = rain,
            };
        }

        public static IReadOnlyList<Chapter> Chapters
        {
            get
            {
                if (chapters != null) return chapters;
                chapters = new List<Chapter>();
                var pro = new Chapter
                {
                    Id = "CH0", Title = "Prologue: Juniper Lane", Crew = "Westhaven locals", CrewIndex = -1, WinsRequired = 0,
                    Intro = new[]
                    {
                        new StoryLine("Juno Reyes", "So you're the one who rented lock-up nine. Silver Kite, 140 horsepower, two decades of somebody else's commute. It's honest. That's a start."),
                        new StoryLine("Juno Reyes", "Nobody in Veyra knows your name yet. Westhaven runs a sprint down the avenue on Friday nights. Win it, and people start asking who you are."),
                        new StoryLine("Juno Reyes", "The glowing columns around the city are events: drive into one and press Enter (D-pad Up on a pad). Esc or Start opens the event list, the garage and the settings."),
                    },
                    Outro = new[] { new StoryLine("Juno Reyes", "Not bad. Somebody from downtown was watching. The Nightshift meet under Exchange Tower, Level B4. Bring the car clean.") },
                };
                pro.Events.Add(new CareerEvent { Race = R("C0-1", "Westhaven Sprint", "RACE-11", RaceKind.Sprint, 1, 3, 0.86f, 4000, 60, null, 21f), Blurb = "Westhaven Avenue to Redwood Parkway. Local kids, borrowed cars.", Gate = true });
                pro.Events.Add(new CareerEvent { Race = R("C0-2", "Riverside Sprint", "RACE-13", RaceKind.Sprint, 1, 3, 0.87f, 4500, 60, null, 19.2f), Blurb = "A short blast up the Sable Valley Link at golden hour." });
                chapters.Add(pro);

                var ns = new Chapter
                {
                    Id = "CH1", Title = "The Nightshift", Crew = Crews[0], CrewIndex = 0, WinsRequired = 1, RequiresChapters = new[] { "CH0" },
                    Intro = new[]
                    {
                        new StoryLine("Mara Voss", "Level B4. Sodium light, four floors of concrete above us and the best street drivers in the city around you. Don't touch anyone's car."),
                        new StoryLine("Mara Voss", "We run downtown at night. Traffic, bridges, the Mile. Clean lines, no drama. Show me Crown Sprint first."),
                    },
                    Outro = new[] { new StoryLine("Mara Voss", "You drive like you're still on the night shift. That's a compliment. The Iron Wolves and the coast crowd have both asked about you.") },
                };
                ns.Events.Add(new CareerEvent { Race = R("C1-1", "Crown Sprint", "RACE-01", RaceKind.Sprint, 1, 4, 0.89f, 7000, 120, Crews[0], 22.5f), Blurb = "Meridian Boulevard, Crown Avenue, Harbor Front Drive." });
                ns.Events.Add(new CareerEvent { Race = R("C1-2", "Lumen Circuit", "RACE-05", RaceKind.Circuit, 3, 4, 0.90f, 8000, 140, Crews[0], 23.5f, true), Blurb = "Three laps of the Mile in the rain. Neon on wet asphalt." });
                ns.Events.Add(new CareerEvent { Race = R("C1-3", "Midnight Crown Circuit", "RACE-16", RaceKind.Circuit, 4, 5, 0.92f, 12000, 220, Crews[0], 0f), Blurb = "Gate event. Four laps of Regent, Meridian, Crown and Harbor Front.", Gate = true });
                chapters.Add(ns);

                var iw = new Chapter
                {
                    Id = "CH2", Title = "Iron Wolves", Crew = Crews[1], CrewIndex = 1, WinsRequired = 3, RequiresChapters = new[] { "CH1" },
                    Intro = new[] { new StoryLine("Brann Okafor", "Foundry Nine. We build big and we build loud. You want respect at the port, you go fast in a straight line and you don't lift over the bridge.") },
                    Outro = new[] { new StoryLine("Brann Okafor", "Anvil rank. Wear it. Whatever the Redline people offer you, remember who let you through the gate.") },
                };
                iw.Events.Add(new CareerEvent { Race = R("C2-1", "Strait Dash", "RACE-04", RaceKind.Sprint, 1, 4, 0.91f, 10000, 160, Crews[1], 21.5f), Blurb = "Marina Drive onto the coastal expressway and over the Lantern Strait Bridge." });
                iw.Events.Add(new CareerEvent { Race = R("C2-2", "Ironport Industrial Circuit", "RACE-06", RaceKind.Circuit, 3, 5, 0.92f, 13000, 200, Crews[1], 2f), Blurb = "Port Road and Berth Road, between the container stacks.", Gate = true });
                iw.Events.Add(new CareerEvent { Race = R("C2-3", "Cliffside Run", "RACE-15", RaceKind.Sprint, 1, 4, 0.93f, 12000, 180, Crews[1], 20.2f), Blurb = "From the airfield gate up the sea cliffs to Corvo Junction." });
                chapters.Add(iw);

                var cs = new Chapter
                {
                    Id = "CH3", Title = "Coastline Society", Crew = Crews[3], CrewIndex = 3, WinsRequired = 3, RequiresChapters = new[] { "CH1" },
                    Intro = new[] { new StoryLine("Celeste Vane", "We don't race, darling. We drive beautifully, quickly, at sunset. The difference matters. Keep up and keep it pretty.") },
                    Outro = new[] { new StoryLine("Celeste Vane", "Commodore. The marina will hold a berth for you. Silas Crane asked for your number. I gave it to him; I hope you're ready.") },
                };
                cs.Events.Add(new CareerEvent { Race = R("C3-1", "Azure Coast Sprint", "RACE-03", RaceKind.Sprint, 1, 4, 0.91f, 10000, 160, Crews[3], 19.4f), Blurb = "Beaches, palms and the coastal expressway at golden hour." });
                cs.Events.Add(new CareerEvent { Race = R("C3-2", "Sorrel Ridge Run", "RACE-14", RaceKind.Touge, 1, 3, 0.92f, 12000, 180, Crews[3], 18.8f), Blurb = "Up into the Sorrel Hills on a narrow mountain road." });
                cs.Events.Add(new CareerEvent { Race = R("C3-3", "Redwood Rally Stage", "RACE-10", RaceKind.Rally, 1, 3, 0.92f, 12000, 180, Crews[3], 17.5f), Blurb = "Gravel between the apple orchards. Not pretty. Win it anyway.", Gate = true });
                chapters.Add(cs);

                var ap = new Chapter
                {
                    Id = "CH4", Title = "Apex Collective", Crew = Crews[2], CrewIndex = 2, WinsRequired = 6, RequiresChapters = new[] { "CH2", "CH3" },
                    Intro = new[] { new StoryLine("Ren Halloway", "Halcyon Pass has ten hairpins. Most people learn all of them the hard way. We'll see which kind you are.") },
                    Outro = new[] { new StoryLine("Ren Halloway", "Perfect Line, then Apex. Nobody gets that in a season. The Redline Vault is open to you now.") },
                };
                ap.Events.Add(new CareerEvent { Race = R("C4-1", "Halcyon Ascent", "RACE-07", RaceKind.Touge, 1, 3, 0.93f, 14000, 200, Crews[2], 22f), Blurb = "Uphill touge through the hairpins of Halcyon Pass Road." });
                ap.Events.Add(new CareerEvent { Race = R("C4-2", "Corvo Descent", "RACE-08", RaceKind.Touge, 1, 3, 0.94f, 15000, 220, Crews[2], 23f), Blurb = "Downhill through the Corvo switchbacks. Brakes matter.", Gate = true });
                ap.Events.Add(new CareerEvent { Race = R("C4-3", "Blackridge Traverse", "RACE-09", RaceKind.Sprint, 1, 4, 0.94f, 16000, 220, Crews[2], 1f), Blurb = "Tunnels and viaducts along the Ridgeline Highway." });
                chapters.Add(ap);

                var rl = new Chapter
                {
                    Id = "CH5", Title = "The Redline Series", Crew = Crews[4], CrewIndex = 4, WinsRequired = 9, RequiresChapters = new[] { "CH4" },
                    Intro = new[] { new StoryLine("Silas Crane", "Every crew in this city sent me your name. The Redline Series is two races: the Ring, and the Grand Loop. Win both and Veyra is yours.") },
                    Outro = new[] { new StoryLine("Silas Crane", "Syndicate. The city is quiet for once, listening to your engine. Enjoy it. Someone new arrives in Westhaven every week."), new StoryLine("Juno Reyes", "Lock-up nine's still yours if you want it. Somebody has to keep the legend humble.") },
                };
                rl.Events.Add(new CareerEvent { Race = R("C5-1", "Ring Run", "RACE-02", RaceKind.Circuit, 1, 5, 0.97f, 25000, 300, Crews[4], 0.5f), Blurb = "One lap of the Veyra Ring: Harbor Viaduct, Kestrel Tunnel, the whole city." });
                rl.Events.Add(new CareerEvent { Race = R("C5-2", "Grand Loop", "RACE-12", RaceKind.Endurance, 1, 5, 0.98f, 50000, 500, Crews[4], 18.5f), Blurb = "Final. 20 km: coast, mountains and valley in one run.", Gate = true });
                chapters.Add(rl);
                return chapters;
            }
        }

        public static bool ChapterComplete(SaveData s, Chapter c)
        {
            foreach (var e in c.Events) if (e.Gate && (s.Record(e.Race.Id, false)?.Wins ?? 0) == 0) return false;
            return true;
        }

        public static bool ChapterUnlocked(SaveData s, Chapter c)
        {
            if (s.Wins < c.WinsRequired) return false;
            foreach (var req in c.RequiresChapters)
            {
                var rc = chapters.Find(x => x.Id == req);
                if (rc != null && !ChapterComplete(s, rc))
                {
                    // CH4 needs either CH2 or CH3
                    if (c.Id == "CH4" && c.RequiresChapters.Length > 1) continue;
                    return false;
                }
            }
            if (c.Id == "CH4")
            {
                bool any = false;
                foreach (var req in c.RequiresChapters) { var rc = chapters.Find(x => x.Id == req); if (rc != null && ChapterComplete(s, rc)) any = true; }
                return any;
            }
            return true;
        }

        /// <summary>Free-play events: every route in the city, unlocked from the start (quick race).</summary>
        public static List<RaceDefinition> QuickRaces()
        {
            var list = new List<RaceDefinition>();
            foreach (var ch in Chapters)
                foreach (var e in ch.Events)
                {
                    var r = e.Race;
                    list.Add(new RaceDefinition
                    {
                        Id = "Q-" + r.RouteId, Name = r.Name, RouteId = r.RouteId, Kind = r.Kind, Laps = r.Laps, Opponents = r.Opponents,
                        AISkill = 0.93f, CashByPosition = new[] { r.CashByPosition[0] / 3, r.CashByPosition[0] / 5, r.CashByPosition[0] / 7, 500 },
                        Reputation = r.Reputation / 4, Crew = r.Crew, TimeOfDay = r.TimeOfDay, Rain = r.Rain,
                    });
                }
            return list;
        }
    }
}

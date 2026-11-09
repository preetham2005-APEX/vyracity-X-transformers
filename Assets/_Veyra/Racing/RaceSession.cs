using System;
using System.Collections.Generic;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Racing
{
    public enum RaceKind { Sprint, Circuit, Touge, Rally, Endurance, Drag, Drift }

    /// <summary>A race is data (RACE_RULES_SPEC): a route, a kind, laps and rewards.</summary>
    [Serializable]
    public sealed class RaceDefinition
    {
        public string Id;
        public string Name;
        public string RouteId;
        public RaceKind Kind;
        public int Laps = 1;
        public int Opponents = 5;
        [Range(0.8f, 1.05f)] public float AISkill = 0.94f;
        public int[] CashByPosition = { 12000, 8000, 6000, 3000 };
        public int Reputation = 100;
        public string Crew;
        public string District;
        public float TimeOfDay = 22f;
        public bool Rain;
        public bool Traffic;
        [Tooltip("Drift events: target score for 1st/2nd/3rd")] public float[] DriftTargets = { 60000f, 35000f, 18000f };

        public int CashFor(int position)
        {
            if (CashByPosition == null || CashByPosition.Length == 0) return 0;
            return CashByPosition[Mathf.Clamp(position - 1, 0, CashByPosition.Length - 1)];
        }
    }

    public sealed class RaceParticipant
    {
        public string Name;
        public VehicleController Vehicle;
        public VehicleDriver Driver;
        public bool IsPlayer;
        public Color Color = Color.white;
        public int Lap;
        public int NextCheckpoint;
        public float S;
        public float Progress;
        public int Hint;
        public bool Finished;
        public float FinishTime;
        public float LapStartTime;
        public float BestLap = float.MaxValue;
        public float LastLap;
        public int Position;
        public float WrongWayTimer;
        public bool WrongWay;
        public float StuckTimer;
        public float UpsideDownTimer;
        public int Respawns;
        public float LastCheckpointS;
        public float GhostTimer;
        public int Collisions;
        public float DriftScore;
        public float NitroUsed;
        public bool Projected;
        /// <summary>AI-controlled (including the player on autopilot): respawn automatically when stuck or lost.</summary>
        public bool AutoRespawn;
        public float OffRouteTimer;
    }

    /// <summary>
    /// Race state machine: Grid → Countdown → Racing → PlayerFinished → AllFinished/Timeout → Results.
    /// Checkpoints are ordered gates spaced ≤ 200 m; a lap counts only when all are passed in order.
    /// Position = laps, checkpoints, then distance along the route. Wrong-way detection and auto respawn
    /// (upside down, stuck, out of bounds, water) at the last checkpoint with 1.5 s of ghosting.
    /// The race clock advances in FixedUpdate so pausing (timeScale 0) freezes it.
    /// </summary>
    public sealed class RaceSession : MonoBehaviour
    {
        public enum Phase { Idle, Grid, Countdown, Racing, PlayerFinished, Finished }

        public RaceDefinition Definition { get; private set; }
        public PolylinePath Path { get; private set; }
        public PolylinePath RacingLine { get; private set; }
        public float[] HalfWidths { get; private set; }
        public Phase State { get; private set; } = Phase.Idle;
        public float Clock { get; private set; }
        public float CountdownLeft { get; private set; }
        public readonly List<RaceParticipant> Participants = new List<RaceParticipant>();
        public readonly List<float> Checkpoints = new List<float>();
        public RaceParticipant Player { get; private set; }
        public bool IsCircuit => Path != null && Path.Closed;
        public int TotalLaps => IsCircuit ? Mathf.Max(1, Definition.Laps) : 1;
        public float StartS { get; private set; }
        public float RaceLength => IsCircuit ? Path.Length * TotalLaps : Path.Length - StartS;

        public event Action<int> CountdownTick;
        public event Action RaceStarted;
        public event Action<RaceParticipant, int> CheckpointPassed;
        public event Action<RaceParticipant, int, float> LapCompleted;
        public event Action<RaceParticipant> ParticipantFinished;
        public event Action RaceFinished;
        public event Action<RaceParticipant> Respawned;

        const float CheckpointSpacing = 180f;
        readonly List<Action> unsubscribe = new List<Action>();

        void Unsubscribe()
        {
            foreach (var a in unsubscribe) a();
            unsubscribe.Clear();
        }
        float playerFinishClock = -1f;
        int countdownShown;

        public void Setup(RaceDefinition def, PolylinePath path)
        {
            Unsubscribe();
            Definition = def;
            Path = path;
            ComputeWidths();
            Path = path = OffsetDividedRoads(path);
            Participants.Clear();
            Player = null;
            Clock = 0f;
            playerFinishClock = -1f;
            StartS = path.Closed ? 0f : Mathf.Min(40f, path.Length * 0.05f);
            RacingLine = BuildRacingLine(path, HalfWidths);
            Checkpoints.Clear();
            float end = path.Closed ? path.Length : path.Length - 2f;
            int n = Mathf.Max(2, Mathf.CeilToInt((end - StartS) / CheckpointSpacing));
            for (int i = 1; i <= n; i++) Checkpoints.Add(Mathf.Lerp(StartS, end, i / (float)n));
            State = Phase.Grid;
        }

        float[] carriagewayOffsets;

        void ComputeWidths()
        {
            HalfWidths = new float[Path.Count];
            carriagewayOffsets = new float[Path.Count];
            float lastHalf = 5f, lastOff = 0f;
            for (int i = 0; i < Path.Count; i++)
            {
                if (i % 4 == 0 && WorldData.NearestRoad(Path.Points[i], 20f, out var hit))
                {
                    var road = hit.RoadPath;
                    if (road.Divided)
                    {
                        // Race on the right-hand carriageway, clear of the central barrier.
                        lastOff = road.Data.width * 0.25f + 0.3f;
                        lastHalf = road.Data.width * 0.25f - 0.6f;
                    }
                    else
                    {
                        lastOff = 0f;
                        lastHalf = Mathf.Clamp(road.HalfWidth, 2.5f, 16f);
                    }
                }
                HalfWidths[i] = lastHalf;
                carriagewayOffsets[i] = lastOff;
            }
        }

        PolylinePath OffsetDividedRoads(PolylinePath path)
        {
            int n = path.Count;
            bool any = false;
            foreach (var o in carriagewayOffsets) if (o != 0f) { any = true; break; }
            if (!any) return path;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                // smooth the offset over +-6 samples (24 m) so transitions are gentle
                float sum = 0f; int cnt = 0;
                for (int k = -6; k <= 6; k++)
                {
                    int j = i + k;
                    if (path.Closed) j = (j % n + n) % n; else if (j < 0 || j >= n) continue;
                    sum += carriagewayOffsets[j]; cnt++;
                }
                var t = path.TangentAt(path.S[i]);
                var right = new Vector3(t.z, 0f, -t.x).normalized;
                pts[i] = path.Points[i] + right * (sum / Mathf.Max(1, cnt));
            }
            return new PolylinePath(pts, path.Closed);
        }

        public float HalfWidthAt(float s) => HalfWidths == null ? 5f : HalfWidths[Mathf.Clamp(Path.SegmentAt(s), 0, HalfWidths.Length - 1)];

        /// <summary>Elastic-band racing line: smooths the centreline toward minimum curvature inside the road edges.</summary>
        public static PolylinePath BuildRacingLine(PolylinePath centre, float[] halfWidths)
        {
            int n = centre.Count;
            var p = (Vector3[])centre.Points.Clone();
            var right = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var t = centre.TangentAt(centre.S[i]);
                right[i] = new Vector3(t.z, 0f, -t.x).normalized;
            }
            for (int it = 0; it < 120; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = i - 1, b = i + 1;
                    if (centre.Closed) { a = (a + n) % n; b %= n; }
                    else if (a < 0 || b >= n) continue;
                    var mid = (p[a] + p[b]) * 0.5f;
                    var q = Vector3.Lerp(p[i], mid, 0.5f);
                    float lim = Mathf.Max(0f, (halfWidths != null ? halfWidths[i] : 5f) - 1.8f);
                    float off = Mathf.Clamp(Vector3.Dot(q - centre.Points[i], right[i]), -lim, lim);
                    p[i] = centre.Points[i] + right[i] * off;
                }
            }
            return new PolylinePath(p, centre.Closed);
        }

        public RaceParticipant AddParticipant(string name, VehicleController v, VehicleDriver d, bool isPlayer, Color color)
        {
            var rp = new RaceParticipant { Name = name, Vehicle = v, Driver = d, IsPlayer = isPlayer, Color = color, AutoRespawn = !isPlayer };
            Participants.Add(rp);
            if (isPlayer) Player = rp;
            if (d != null)
            {
                VehicleDriver.IntentFilter lockFilter = (ref DriverIntent intent) => LockDuringCountdown(ref intent);
                d.PreProcess += lockFilter;
                unsubscribe.Add(() => { if (d != null) d.PreProcess -= lockFilter; });
            }
            Action<VehicleController, Collision> onHit = (veh, c) => { if (c.impulse.magnitude > 2500f) rp.Collisions++; };
            v.Collided += onHit;
            unsubscribe.Add(() => { if (v != null) v.Collided -= onHit; });
            return rp;
        }

        void LockDuringCountdown(ref DriverIntent intent)
        {
            if (State == Phase.Grid || State == Phase.Countdown)
            {
                // Revving allowed; the car is held on the brakes.
                intent.Steer = 0f;
                intent.Brake = 1f;
                intent.Handbrake = 1f;
                intent.Nitro = false;
            }
        }

        /// <summary>Grid slot pose: two columns, 8 m rows behind the start line.</summary>
        public Pose GridPose(int slot)
        {
            int row = slot / 2;
            float s = StartS - 7f - 8f * row;
            if (Path.Closed) s = Path.Wrap(s);
            else s = Mathf.Max(0f, s);
            var pos = Path.PositionAt(s);
            var tan = Path.TangentAt(s);
            var right = new Vector3(tan.z, 0f, -tan.x).normalized;
            float lane = Mathf.Min(2.3f, HalfWidthAt(s) * 0.45f);
            pos += right * (slot % 2 == 0 ? -lane : lane);
            pos.y = WorldData.GroundHeight(pos, pos.y) + 0.4f;
            return new Pose(pos, Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.z)));
        }

        public void BeginCountdown(float seconds = 3f)
        {
            foreach (var p in Participants)
            {
                p.Hint = 0;
                p.S = p.Vehicle != null ? Path.Project(p.Vehicle.transform.position, ref p.Hint, 0) : 0f;
                p.Lap = 0;
                p.NextCheckpoint = 0;
                p.LastCheckpointS = StartS;
                p.Finished = false;
                p.LapStartTime = 0f;
            }
            CountdownLeft = seconds;
            countdownShown = Mathf.CeilToInt(seconds) + 1;
            State = Phase.Countdown;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (State == Phase.Countdown)
            {
                CountdownLeft -= dt;
                int shown = Mathf.CeilToInt(CountdownLeft);
                if (shown != countdownShown && shown >= 0)
                {
                    countdownShown = shown;
                    CountdownTick?.Invoke(shown);
                }
                if (CountdownLeft <= 0f)
                {
                    State = Phase.Racing;
                    Clock = 0f;
                    RaceStarted?.Invoke();
                }
                return;
            }
            if (State != Phase.Racing && State != Phase.PlayerFinished) return;
            Clock += dt;
            foreach (var p in Participants) Track(p, dt);
            UpdatePositions();

            if (State == Phase.PlayerFinished && Clock - playerFinishClock > 30f) Timeout();
            bool all = true;
            foreach (var p in Participants) if (!p.Finished) { all = false; break; }
            if (all) Finish();
        }

        void Track(RaceParticipant p, float dt)
        {
            if (p.Finished || p.Vehicle == null) return;
            var v = p.Vehicle;
            var pos = v.transform.position;
            float prevS = p.S;
            p.S = Path.Project(pos, ref p.Hint, 30);
            // Lost track (shortcut, crash): re-acquire globally.
            if (Mathf.Abs(Path.LateralOffset(pos, p.S)) > 45f) p.S = Path.Project(pos, ref p.Hint, 0);

            // Checkpoints in order.
            if (p.NextCheckpoint < Checkpoints.Count)
            {
                float cp = Checkpoints[p.NextCheckpoint];
                bool last = p.NextCheckpoint == Checkpoints.Count - 1;
                bool crossed;
                if (Path.Closed && last)
                    crossed = (prevS > Path.Length - 60f && p.S < 60f) || (p.S >= cp - 1f && prevS < cp - 1f);
                else
                    crossed = p.S >= cp && prevS < cp + 30f && prevS <= p.S + 1f;
                float lateral = Mathf.Abs(Path.LateralOffset(pos, Mathf.Min(cp, Path.Length - 0.01f)));
                if (crossed && lateral < HalfWidthAt(cp) + 10f)
                {
                    p.LastCheckpointS = Path.Closed && last ? 0f : cp;
                    p.NextCheckpoint++;
                    CheckpointPassed?.Invoke(p, p.NextCheckpoint - 1);
                    if (p.NextCheckpoint >= Checkpoints.Count)
                    {
                        if (Path.Closed)
                        {
                            float lap = Clock - p.LapStartTime;
                            p.LastLap = lap;
                            p.BestLap = Mathf.Min(p.BestLap, lap);
                            p.Lap++;
                            p.LapStartTime = Clock;
                            LapCompleted?.Invoke(p, p.Lap, lap);
                            if (p.Lap >= TotalLaps) FinishParticipant(p);
                            else p.NextCheckpoint = 0;
                        }
                        else
                        {
                            p.LastLap = Clock;
                            p.BestLap = Clock;
                            FinishParticipant(p);
                        }
                    }
                }
            }

            // Progress for ordering: never credit more than a little past the next checkpoint.
            float sClamped = p.S;
            if (p.NextCheckpoint < Checkpoints.Count) sClamped = Mathf.Min(p.S, Checkpoints[p.NextCheckpoint] + 20f);
            if (Path.Closed && p.NextCheckpoint == 0 && p.S > Path.Length * 0.5f) sClamped = 0f;
            p.Progress = p.Finished ? float.MaxValue - p.FinishTime : p.Lap * Path.Length + sClamped + p.NextCheckpoint * 0.001f;

            // Wrong way.
            var vel = v.Body.linearVelocity;
            var tan = Path.TangentAt(p.S);
            float along = Vector3.Dot(vel, tan);
            if (vel.magnitude > 10f / 3.6f && along < -0.3f * vel.magnitude) p.WrongWayTimer += dt; else p.WrongWayTimer = 0f;
            p.WrongWay = p.WrongWayTimer > 2f;

            // Auto respawn rules.
            if (v.transform.up.y < 0.25f) p.UpsideDownTimer += dt; else p.UpsideDownTimer = 0f;
            bool throttle = p.Driver != null && p.Driver.LastIntent.Throttle > 0.3f;
            if (v.State.SpeedKmh < 2f && (throttle || p.AutoRespawn)) p.StuckTimer += dt; else p.StuckTimer = 0f;
            float lateralNow = Mathf.Abs(Path.LateralOffset(pos, p.S));
            if (p.AutoRespawn && lateralNow > HalfWidthAt(p.S) + 8f) p.OffRouteTimer += dt; else p.OffRouteTimer = 0f;
            bool outOfBounds = pos.y < -15f || !WorldData.Bounds.Contains(new Vector2(pos.x, pos.z));
            bool inWater = pos.y < -0.6f;
            if (p.UpsideDownTimer > 3f || p.StuckTimer > 5f || outOfBounds || inWater || (p.AutoRespawn && (p.WrongWayTimer > 4f || p.OffRouteTimer > 4f)))
                Respawn(p);

            if (p.GhostTimer > 0f)
            {
                p.GhostTimer -= dt;
                if (p.GhostTimer <= 0f) SetGhost(p, false);
            }
        }

        public void Respawn(RaceParticipant p)
        {
            if (p.Vehicle == null) return;
            float s = p.LastCheckpointS;
            var pos = Path.PositionAt(s);
            var tan = Path.TangentAt(s);
            pos.y = WorldData.GroundHeight(pos + Vector3.up * 2f, pos.y) + 0.5f;
            p.Vehicle.ResetTo(pos, Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.z)));
            p.Hint = Path.SegmentAt(s);
            p.S = s;
            p.Respawns++;
            p.StuckTimer = p.UpsideDownTimer = p.WrongWayTimer = p.OffRouteTimer = 0f;
            p.GhostTimer = 1.5f;
            SetGhost(p, true);
            Respawned?.Invoke(p);
        }

        void SetGhost(RaceParticipant p, bool on)
        {
            var mine = p.Vehicle.GetComponentsInChildren<Collider>();
            foreach (var other in Participants)
            {
                if (other == p || other.Vehicle == null) continue;
                foreach (var a in mine)
                    foreach (var b in other.Vehicle.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(a, b, on);
            }
        }

        void FinishParticipant(RaceParticipant p)
        {
            p.Finished = true;
            p.FinishTime = Clock;
            ParticipantFinished?.Invoke(p);
            if (p.IsPlayer)
            {
                playerFinishClock = Clock;
                State = Phase.PlayerFinished;
            }
        }

        void Timeout()
        {
            foreach (var p in Participants)
            {
                if (p.Finished) continue;
                float done = Mathf.Max(1f, p.Lap * Path.Length + p.S - StartS);
                float remaining = Mathf.Max(0f, RaceLength - done);
                float avg = Mathf.Max(5f, done / Mathf.Max(1f, Clock));
                p.Finished = true;
                p.Projected = true;
                p.FinishTime = Clock + remaining / avg;
            }
            Finish();
        }

        void Finish()
        {
            if (State == Phase.Finished) return;
            UpdatePositions();
            State = Phase.Finished;
            RaceFinished?.Invoke();
        }

        void UpdatePositions()
        {
            Participants.Sort((a, b) =>
            {
                if (a.Finished && b.Finished) return a.FinishTime.CompareTo(b.FinishTime);
                if (a.Finished) return -1;
                if (b.Finished) return 1;
                return b.Progress.CompareTo(a.Progress);
            });
            for (int i = 0; i < Participants.Count; i++) Participants[i].Position = i + 1;
        }

        /// <summary>Distance in metres from the participant to the next checkpoint gate.</summary>
        public float DistanceToNextCheckpoint(RaceParticipant p)
        {
            if (p.NextCheckpoint >= Checkpoints.Count) return 0f;
            float cp = Checkpoints[p.NextCheckpoint];
            float d = cp - p.S;
            if (Path.Closed && d < -Path.Length * 0.5f) d += Path.Length;
            return Mathf.Max(0f, d);
        }

        public void Abort()
        {
            Unsubscribe();
            State = Phase.Idle;
            Participants.Clear();
            Player = null;
        }
    }
}

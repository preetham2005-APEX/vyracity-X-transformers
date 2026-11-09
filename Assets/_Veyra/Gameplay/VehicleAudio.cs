using UnityEngine;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Procedural vehicle audio (no recorded assets yet): an engine synthesised from the firing frequency of
    /// the simulated rpm with load-dependent harmonics and combustion noise, plus tyre squeal from slip, wind
    /// from speed, a nitro hiss that follows the actual boost scalar, and impact thuds from collision impulse.
    /// </summary>
    public sealed class VehicleAudio : MonoBehaviour
    {
        public static float MasterVolume = 0.85f;
        public static float EngineVolume = 1f;

        VehicleController car;
        NitroSystem nitro;
        AudioSource engine, tyres, wind, boost, impact;
        int cylinders = 4;
        int sampleRate;
        // shared with the audio thread
        volatile float pRpm = 900f, pThrottle, pLimiter, pSlip, pSpeed, pBoost;
        static AudioClip thud;

        // engine synth state (audio thread)
        double phase;
        float sRpm = 900f, sThr, lp, noiseLp;
        System.Random rng = new System.Random(1234);
        float t1, t2, wLp, bLp, bHp;

        public void Bind(VehicleController v, NitroSystem n, bool player)
        {
            car = v;
            nitro = n;
            sampleRate = AudioSettings.outputSampleRate;
            float kw = 0f;
            foreach (var k in v.Definition.Engine.TorqueCurve) kw = Mathf.Max(kw, k.y * k.x * Mathf.PI / 30f / 1000f);
            cylinders = kw > 330f ? 8 : kw > 200f ? 6 : 4;
            rng = new System.Random(v.GetHashCode());
            engine = MakeSource("Engine", AudioClip.Create("EngineSynth", sampleRate, 1, sampleRate, true, EngineRead), player);
            tyres = MakeSource("Tyres", AudioClip.Create("TyreSynth", sampleRate, 1, sampleRate, true, TyreRead), player);
            wind = MakeSource("Wind", AudioClip.Create("WindSynth", sampleRate, 1, sampleRate, true, WindRead), player);
            boost = MakeSource("Boost", AudioClip.Create("BoostSynth", sampleRate, 1, sampleRate, true, BoostRead), player);
            impact = gameObject.AddComponent<AudioSource>();
            impact.playOnAwake = false;
            impact.spatialBlend = player ? 0.3f : 1f;
            if (thud == null) thud = MakeThud();
            v.Collided += OnCollided;
        }

        void OnDestroy()
        {
            if (car != null) car.Collided -= OnCollided;
        }

        AudioSource MakeSource(string name, AudioClip clip, bool player)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = player ? 0.15f : 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 6f;
            s.maxDistance = player ? 500f : 140f;
            s.dopplerLevel = player ? 0f : 0.6f;
            s.volume = 0f;
            s.Play();
            return s;
        }

        void OnCollided(VehicleController v, Collision c)
        {
            float j = c.impulse.magnitude / Mathf.Max(1f, v.Body.mass);
            if (j < 0.4f || impact == null) return;
            impact.pitch = Random.Range(0.8f, 1.15f);
            impact.PlayOneShot(thud, Mathf.Clamp01(j / 4f) * MasterVolume);
        }

        void Update()
        {
            if (car == null || !car.IsInitialised) return;
            var st = car.State;
            pRpm = st.EngineRpm;
            pThrottle = st.AppliedThrottle;
            pLimiter = st.AtLimiter ? 1f : 0f;
            float slip = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = car.GetWheel(i);
                if (w.Grounded) slip = Mathf.Max(slip, Mathf.Abs(w.SlipAngleDeg) / 25f, Mathf.Abs(w.SlipRatio) / 0.35f);
            }
            pSlip = st.Speed > 2f ? Mathf.Clamp01(slip - 0.35f) : 0f;
            pSpeed = st.Speed;
            pBoost = nitro != null ? nitro.BoostScalar : 0f;
            float paused = Time.timeScale > 0f ? 1f : 0f;
            float mv = MasterVolume * paused;
            engine.volume = (0.35f + 0.35f * st.AppliedThrottle) * mv * EngineVolume;
            tyres.volume = pSlip * 0.55f * mv;
            wind.volume = Mathf.Clamp01(st.Speed / 70f) * Mathf.Clamp01(st.Speed / 70f) * 0.45f * mv;
            boost.volume = pBoost * 0.5f * mv;
        }

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        void EngineRead(float[] data)
        {
            float sr = sampleRate > 0 ? sampleRate : 48000;
            for (int i = 0; i < data.Length; i++)
            {
                sRpm += (pRpm - sRpm) * 0.0015f;
                sThr += (pThrottle - sThr) * 0.002f;
                float f0 = Mathf.Max(20f, sRpm / 60f * cylinders * 0.5f);
                phase += f0 / sr;
                if (phase > 1e6) phase -= 1e6;
                float ph = (float)(phase - System.Math.Floor(phase));
                float twoPi = Mathf.PI * 2f;
                float bright = 0.35f + 0.65f * sThr;
                float s = Mathf.Sin(twoPi * ph) * 1.0f
                          + Mathf.Sin(twoPi * ph * 2f) * 0.55f * bright
                          + Mathf.Sin(twoPi * ph * 3f) * 0.32f * bright
                          + Mathf.Sin(twoPi * ph * 4f) * 0.22f * bright * bright
                          + Mathf.Sin(twoPi * ph * 6f) * 0.10f * bright * bright
                          + Mathf.Sin(Mathf.PI * ph) * 0.45f;
                float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * ph)), 6f);
                noiseLp += (Noise() - noiseLp) * 0.25f;
                s += noiseLp * pulse * (0.4f + 1.1f * sThr);
                if (pLimiter > 0.5f) s *= 0.55f + 0.45f * (((int)(phase * 0.02)) % 2);
                lp += (s - lp) * (0.18f + 0.55f * bright);
                data[i] = (float)System.Math.Tanh(lp * 0.45f);
            }
        }

        void TyreRead(float[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                // resonant band-pass around ~1.4 kHz (state-variable filter)
                float x = Noise();
                float f = 0.19f, q = 0.06f;
                t2 += f * t1;
                float hp = x - t2 - q * t1;
                t1 += f * hp;
                data[i] = Mathf.Clamp(t1 * 1.6f, -1f, 1f);
            }
        }

        void WindRead(float[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                wLp += (Noise() - wLp) * 0.02f;
                data[i] = wLp * 3f;
            }
        }

        void BoostRead(float[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                float x = Noise();
                bLp += (x - bLp) * 0.35f;
                float hp = bLp - bHp;
                bHp += hp * 0.05f;
                data[i] = hp * 0.9f;
            }
        }

        static AudioClip MakeThud()
        {
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int n = sr / 3;
            var data = new float[n];
            var r = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Exp(-t * 14f);
                lp += ((float)(r.NextDouble() * 2 - 1) - lp) * 0.08f;
                data[i] = (Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.8f + lp * 1.6f) * env;
            }
            var c = AudioClip.Create("Thud", n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>Short UI/countdown beep.</summary>
        public static AudioClip Beep(float freq, float seconds)
        {
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int n = Mathf.RoundToInt(sr * seconds);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Min(1f, t * 200f) * Mathf.Clamp01((seconds - t) * 20f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * freq * 2f * t) * 0.15f) * env;
            }
            var c = AudioClip.Create("Beep" + freq, n, 1, sr, false);
            c.SetData(d, 0);
            return c;
        }
    }
}

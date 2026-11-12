using NUnit.Framework;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Tests
{
    /// <summary>
    /// BUG-087 (round 3): the tables that moved from code into ScriptableObjects keep the values the code used, so moving
    /// them changed nothing in handling. A tuning change shows up here as a deliberate, reviewed test edit.
    /// </summary>
    public class TuningAssetTests
    {
        [Test]
        public void NitroTuningDefaultsMatchSpec()
        {
            var t = ScriptableObject.CreateInstance<NitroTuning>();
            Assert.AreEqual(1.35f, t.Torque(NitroMode.Standard), 1e-6f);
            Assert.AreEqual(1.55f, t.Torque(NitroMode.CleanBoost), 1e-6f);
            Assert.AreEqual(1.90f, t.Torque(NitroMode.Overdrive), 1e-6f);
            Assert.AreEqual(1f, t.Torque(NitroMode.Off), 1e-6f);
            Assert.AreEqual(0.10f, t.Cap(NitroMode.Overdrive), 1e-6f);
            Assert.AreEqual(4.4f, t.Thrust(NitroMode.Overdrive), 1e-6f);
            Assert.AreEqual(NitroSystem.StandardDrain, t.StandardDrain);
            Assert.AreEqual(NitroSystem.CleanWindowOpen, t.CleanWindowOpen);
            Assert.AreEqual(NitroSystem.CleanWindowClose, t.CleanWindowClose);
            Assert.AreEqual(NitroSystem.OverdriveTapWindow, t.OverdriveTapWindow);
            Assert.AreEqual(NitroSystem.OverdriveDuration, t.OverdriveDuration);
            Assert.AreEqual(NitroSystem.OverdriveCooldown, t.OverdriveCooldown);
            // bounded boost (NITRO_BALANCE_SPEC): every mode extends top speed by at most 10 %
            foreach (NitroMode m in System.Enum.GetValues(typeof(NitroMode))) Assert.LessOrEqual(t.Cap(m), 0.10f);
            Object.DestroyImmediate(t);
        }

        [Test]
        public void LampMaterialsClassifyByFunction()   // BUG-064
        {
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.Rear, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_stanton_sidewinder_Brake_Lights", 0f, 2.2f));
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.Head, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_stanton_sidewinder_Headlights", 0f, 2.2f));
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.Reverse, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_ashcombe_tempest_Reverse_Lights", 0f, 2.2f));
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.None, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_ashcombe_tempest_Rear_Lights_Housing", 0f, 2.2f));
            // a combined lights-and-glass material goes by where its renderer sits
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.Rear, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_kiri_86_UCB_Lights_and_Glass", -1.9f, 2.2f));
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.Head, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_kiri_86_UCB_Lights_and_Glass", 1.9f, 2.2f));
            Assert.AreEqual(VeyraCity.Gameplay.VehicleFX.LampFn.Combined, VeyraCity.Gameplay.VehicleFX.ClassifyLamp("M_Lamp_kiri_86_UCB_Lights_and_Glass", 0.1f, 2.2f));
        }

        [Test]
        public void CrashZonesFollowTheContactPoint()   // D2 crash damage
        {
            var size = new Vector3(1.9f, 1.3f, 4.4f);
            Assert.AreEqual(VeyraCity.Gameplay.CarDamage.Zone.Front, VeyraCity.Gameplay.CarDamage.ZoneOf(new Vector3(0.3f, 0.5f, 2.1f), size));
            Assert.AreEqual(VeyraCity.Gameplay.CarDamage.Zone.Rear, VeyraCity.Gameplay.CarDamage.ZoneOf(new Vector3(-0.5f, 0.5f, -2.0f), size));
            Assert.AreEqual(VeyraCity.Gameplay.CarDamage.Zone.Left, VeyraCity.Gameplay.CarDamage.ZoneOf(new Vector3(-0.95f, 0.5f, 0.4f), size));
            Assert.AreEqual(VeyraCity.Gameplay.CarDamage.Zone.Right, VeyraCity.Gameplay.CarDamage.ZoneOf(new Vector3(0.95f, 0.5f, -0.8f), size));
        }

        [Test]
        public void NitroTuningShortTableFallsBack()
        {
            var t = ScriptableObject.CreateInstance<NitroTuning>();
            t.TorqueMul = new[] { 1f };
            Assert.AreEqual(1f, t.Torque(NitroMode.Overdrive));
            t.SpeedCap = null;
            Assert.AreEqual(0f, t.Cap(NitroMode.Standard));
            Object.DestroyImmediate(t);
        }

        [Test]
        public void HandlingFeelAndAssistDefaultsMatchFormerConstants()
        {
            var f = ScriptableObject.CreateInstance<HandlingFeel>();
            Assert.AreEqual(0.4f, f.WallYawKeepPer10ms);
            Assert.AreEqual(1.2f, f.WallAlignMaxRad);
            Assert.AreEqual(2.5f, f.WallAlignGain);
            Assert.AreEqual(2.5f, f.WallAlignMaxRate);
            Assert.AreEqual(0.05f, f.AngularDamping);
            Assert.AreEqual(30f, f.MaxAngularVelocity);
            Assert.AreEqual(0.12f, f.DriftExitSteer);
            Assert.AreEqual(0.5f, f.DriftCancelCounterSteer);
            Assert.AreEqual(0.6f, f.DriftExitSpeedShare);
            Assert.AreEqual(0.6f, f.DriftExitBrake);
            Assert.AreEqual(0.6f, f.DriftExitAirSeconds);
            Assert.AreEqual(0.85f, f.HandbrakeGripCut);
            Assert.AreEqual(2.4f, f.LeanRollPerG);
            Assert.AreEqual(7f, f.LeanResponse);
            var n = ScriptableObject.CreateInstance<NitroTuning>();
            Assert.AreEqual(DriftScorer.MinAngleDeg, n.DriftMinAngleDeg);
            Assert.AreEqual(DriftScorer.MinSpeedKmh, n.DriftMinSpeedKmh);
            Object.DestroyImmediate(n);
            var a = ScriptableObject.CreateInstance<AssistPreset>();
            Assert.AreEqual(4f, a.CounterSteerDeadBandDeg);
            Assert.AreEqual(10f, a.DriftEngageDeg);
            Assert.AreEqual(8f, a.DriftEngageRampDeg);
            Assert.AreEqual(0.55f, a.DriftHoldP);
            Assert.AreEqual(0.09f, a.DriftHoldD);
            Object.DestroyImmediate(f);
            Object.DestroyImmediate(a);
        }
    }
}

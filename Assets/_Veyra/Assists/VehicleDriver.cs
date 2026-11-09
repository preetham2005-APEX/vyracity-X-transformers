using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Assists
{
    /// <summary>
    /// Connects a driver (player input, AI or script) to a vehicle through the assist layer.
    /// Runs before the vehicle each physics step.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class VehicleDriver : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] AssistPreset preset;
        [Tooltip("A component implementing IDriverIntentSource")] [SerializeField] MonoBehaviour intentSource;

        readonly AssistProcessor processor = new AssistProcessor();
        IDriverIntentSource source;

        public AssistPreset Preset { get => preset; set => preset = value; }
        public VehicleController Vehicle => vehicle;
        public AssistProcessor Processor => processor;
        public DriverIntent LastIntent { get; private set; }
        public VehicleControls LastControls { get; private set; }

        /// <summary>Last-stage edit of the controls before they reach the vehicle (nitro, race locks, ghosting).</summary>
        public delegate void ControlsFilter(ref VehicleControls controls, in DriverIntent intent, float dt);
        public event ControlsFilter PostProcess;
        /// <summary>Optional edit of the raw intent before the assists (countdown input lock).</summary>
        public delegate void IntentFilter(ref DriverIntent intent);
        public event IntentFilter PreProcess;

        public void Bind(VehicleController v, IDriverIntentSource s, AssistPreset p)
        {
            vehicle = v;
            source = s;
            intentSource = s as MonoBehaviour;
            preset = p;
            processor.Reset();
        }

        void Awake()
        {
            if (source == null && intentSource is IDriverIntentSource s) source = s;
        }

        void FixedUpdate()
        {
            if (vehicle != null && !vehicle.ManualStepping) Step(Time.fixedDeltaTime);
        }

        public void Step(float dt)
        {
            if (vehicle == null) return;
            var intent = source != null ? source.ReadIntent() : default;
            PreProcess?.Invoke(ref intent);
            LastIntent = intent;
            var controls = processor.Process(intent, vehicle.State, preset, dt);
            PostProcess?.Invoke(ref controls, intent, dt);
            LastControls = controls;
            vehicle.SetControls(controls);
        }
    }
}

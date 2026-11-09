namespace VeyraCity.Input
{
    /// <summary>
    /// Default bindings for VEYRA CITY (INPUT_AND_ACCESSIBILITY_SPEC §1), the single source of
    /// default keys and buttons. Binding ids are fixed so saved rebinding overrides stay valid.
    /// Edit the bindings here, not in gameplay code.
    /// </summary>
    public static class VeyraControlsJson
    {
        public const string Json = @"{
  ""name"": ""VeyraControls"",
  ""maps"": [
    {
      ""name"": ""Driving"",
      ""id"": ""a557b010-3d83-50da-9c84-479b91301526"",
      ""actions"": [
        {
          ""name"": ""Steer"",
          ""type"": ""Value"",
          ""id"": ""d79533d5-89aa-5926-b907-881c1c745d42"",
          ""expectedControlType"": ""Axis"",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": true
        },
        {
          ""name"": ""Throttle"",
          ""type"": ""Value"",
          ""id"": ""6b5e584e-4031-5467-8188-a23749f7455b"",
          ""expectedControlType"": ""Axis"",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": true
        },
        {
          ""name"": ""Brake"",
          ""type"": ""Value"",
          ""id"": ""27996f58-f885-54fa-a3fc-5b099129c88e"",
          ""expectedControlType"": ""Axis"",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": true
        },
        {
          ""name"": ""Handbrake"",
          ""type"": ""Button"",
          ""id"": ""20b2d663-0bf2-5341-8d98-d7b0aa18e189"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""Nitro"",
          ""type"": ""Button"",
          ""id"": ""00b43cc9-fc33-5d47-b961-5b12939dc442"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""LookBack"",
          ""type"": ""Button"",
          ""id"": ""620a9c58-3a70-5a73-9cdd-9daf852ceeb8"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""CameraCycle"",
          ""type"": ""Button"",
          ""id"": ""f24d7f90-bf87-5155-9436-a882c5876c23"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""ShiftUp"",
          ""type"": ""Button"",
          ""id"": ""70bf6f33-7ff6-5b7e-9da5-e2c482046201"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""ShiftDown"",
          ""type"": ""Button"",
          ""id"": ""e7dd0186-71e7-5652-98c7-1408302c6542"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""Reset"",
          ""type"": ""Button"",
          ""id"": ""b06a600a-5aaa-5560-8c50-75d294311daf"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""Pause"",
          ""type"": ""Button"",
          ""id"": ""e3b40faa-3ba6-508a-a2f2-7b0588534b26"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        }
      ],
      ""bindings"": [
        {
          ""name"": ""Keys A/D"",
          ""id"": ""5c42944d-f241-55e3-875c-169b43004c9b"",
          ""path"": ""1DAxis"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Steer"",
          ""isComposite"": true,
          ""isPartOfComposite"": false
        },
        {
          ""name"": ""negative"",
          ""id"": ""94c58f4d-8098-5f78-871e-19357790ece4"",
          ""path"": ""<Keyboard>/a"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Steer"",
          ""isComposite"": false,
          ""isPartOfComposite"": true
        },
        {
          ""name"": ""positive"",
          ""id"": ""fc340185-241e-5ce8-8d66-e7f713604e28"",
          ""path"": ""<Keyboard>/d"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Steer"",
          ""isComposite"": false,
          ""isPartOfComposite"": true
        },
        {
          ""name"": ""Arrows"",
          ""id"": ""b52453e4-4dc3-5dfb-94b2-3281a6cb0504"",
          ""path"": ""1DAxis"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Steer"",
          ""isComposite"": true,
          ""isPartOfComposite"": false
        },
        {
          ""name"": ""negative"",
          ""id"": ""cc0e5033-809a-5071-b448-ca189bb42162"",
          ""path"": ""<Keyboard>/leftArrow"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Steer"",
          ""isComposite"": false,
          ""isPartOfComposite"": true
        },
        {
          ""name"": ""positive"",
          ""id"": ""cd0b2aa1-958e-5bf0-8b22-3efb8f54ebe0"",
          ""path"": ""<Keyboard>/rightArrow"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Steer"",
          ""isComposite"": false,
          ""isPartOfComposite"": true
        },
        {
          ""name"": """",
          ""id"": ""5ea51e0d-910f-504a-9a34-426b2f2cec70"",
          ""path"": ""<Gamepad>/leftStick/x"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Steer"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""72b077e7-a6e3-5168-8b21-563aeeff9105"",
          ""path"": ""<Keyboard>/w"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Throttle"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""5952296b-3cff-5fc2-aecf-898d224533f8"",
          ""path"": ""<Keyboard>/upArrow"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Throttle"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""881a078d-a484-5455-9d97-9621d10ae970"",
          ""path"": ""<Gamepad>/rightTrigger"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Throttle"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""b209631a-8849-592a-907a-e288ec74c115"",
          ""path"": ""<Keyboard>/s"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Brake"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""bdd22a12-2091-5e84-9213-54c6c5e904e0"",
          ""path"": ""<Keyboard>/downArrow"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Brake"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""b126d1d1-02ad-5050-a093-7bd1bca69b24"",
          ""path"": ""<Gamepad>/leftTrigger"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Brake"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""14ef15f7-d32b-5b93-b20f-5f99a3946a29"",
          ""path"": ""<Keyboard>/space"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Handbrake"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""a4181a08-cdd1-5bb7-99cf-f32d3002cf67"",
          ""path"": ""<Gamepad>/buttonSouth"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Handbrake"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""4c3defbb-ed90-53c8-82fb-22c875a414a3"",
          ""path"": ""<Keyboard>/leftShift"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Nitro"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""a86feb9e-72b4-529a-9958-0abc41074638"",
          ""path"": ""<Gamepad>/buttonEast"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Nitro"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""3d040486-f101-523a-bf55-6c2565ad96a1"",
          ""path"": ""<Keyboard>/c"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""LookBack"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""9f8af88f-3173-5873-89e2-5c42babef904"",
          ""path"": ""<Gamepad>/rightStickPress"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""LookBack"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""51457868-3620-581e-8dd6-d458557c6486"",
          ""path"": ""<Keyboard>/v"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""CameraCycle"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""c5278305-a6af-58ec-aace-680e56e8ded6"",
          ""path"": ""<Gamepad>/buttonNorth"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""CameraCycle"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""0b3c3b16-9676-52bf-b6d2-bcc6a2f294ae"",
          ""path"": ""<Keyboard>/e"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""ShiftUp"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""e53260be-520f-5d98-a014-796acbe48ac2"",
          ""path"": ""<Gamepad>/rightShoulder"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""ShiftUp"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""5e2223dd-27fd-5efb-837b-b52a782b8dce"",
          ""path"": ""<Keyboard>/q"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""ShiftDown"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""09a1aa30-0a82-555e-bf45-a09070c4692b"",
          ""path"": ""<Gamepad>/leftShoulder"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""ShiftDown"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""9206c281-6801-5015-9ca9-5de148fbbeaa"",
          ""path"": ""<Keyboard>/r"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Reset"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""57a74445-87c6-54f2-a40e-29de63de1272"",
          ""path"": ""<Gamepad>/select"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Reset"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""d15b9bd6-90ef-57ee-a521-673add92f3fc"",
          ""path"": ""<Keyboard>/escape"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Pause"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""4d5e8c4d-f57d-5ea4-8339-6887574ee121"",
          ""path"": ""<Gamepad>/start"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Gamepad"",
          ""action"": ""Pause"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        }
      ]
    },
    {
      ""name"": ""Debug"",
      ""id"": ""7385f1f0-1934-5a07-be84-87aafb6a2812"",
      ""actions"": [
        {
          ""name"": ""Telemetry"",
          ""type"": ""Button"",
          ""id"": ""d69026af-5b70-51c8-8614-00154c3a4f62"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""NextVehicle"",
          ""type"": ""Button"",
          ""id"": ""12021470-62a0-5d3e-aedc-5b727463226e"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""ToggleAssists"",
          ""type"": ""Button"",
          ""id"": ""f4f98717-bd2e-5870-bcb0-571902d93d8b"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        },
        {
          ""name"": ""RecordTelemetry"",
          ""type"": ""Button"",
          ""id"": ""68567e5d-b4cb-5f2c-b029-1606fa8f6723"",
          ""expectedControlType"": """",
          ""processors"": """",
          ""interactions"": """",
          ""initialStateCheck"": false
        }
      ],
      ""bindings"": [
        {
          ""name"": """",
          ""id"": ""383d3b16-f747-5bf6-8208-7d8b57bec796"",
          ""path"": ""<Keyboard>/f3"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""Telemetry"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""bf499560-f4e8-5b43-8b48-bdbbcc828873"",
          ""path"": ""<Keyboard>/f5"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""NextVehicle"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""2042fece-faac-5319-a7b4-b598bcde19da"",
          ""path"": ""<Keyboard>/f6"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""ToggleAssists"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        },
        {
          ""name"": """",
          ""id"": ""299d32ac-9b8f-5e43-8055-98dd0be9fe67"",
          ""path"": ""<Keyboard>/f7"",
          ""interactions"": """",
          ""processors"": """",
          ""groups"": ""Keyboard"",
          ""action"": ""RecordTelemetry"",
          ""isComposite"": false,
          ""isPartOfComposite"": false
        }
      ]
    }
  ],
  ""controlSchemes"": [
    {
      ""name"": ""Keyboard"",
      ""bindingGroup"": ""Keyboard"",
      ""devices"": [
        {
          ""devicePath"": ""<Keyboard>"",
          ""isOptional"": false,
          ""isOR"": false
        }
      ]
    },
    {
      ""name"": ""Gamepad"",
      ""bindingGroup"": ""Gamepad"",
      ""devices"": [
        {
          ""devicePath"": ""<Gamepad>"",
          ""isOptional"": false,
          ""isOR"": false
        }
      ]
    }
  ]
}";
    }
}

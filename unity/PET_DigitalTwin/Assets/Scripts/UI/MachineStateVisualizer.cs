using System;
using UnityEngine;
using PETDigitalTwin.API;
using PETDigitalTwin.Core;

namespace PETDigitalTwin.UI
{
    [Serializable]
    public class MachineBinding
    {
        public string machineId;
        public Renderer targetRenderer;
        public TextMesh label;
    }

    /// <summary>Minimal scene adapter for the nine official equipment IDs.</summary>
    public class MachineStateVisualizer : MonoBehaviour
    {
        public DigitalTwinApiClient apiClient;
        public MachineBinding[] machines;

        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

        private void OnEnable()
        {
            if (apiClient != null)
                apiClient.OnMachinesUpdated += ApplyStates;
        }

        private void OnDisable()
        {
            if (apiClient != null)
                apiClient.OnMachinesUpdated -= ApplyStates;
        }

        private void ApplyStates(MachineState[] states)
        {
            foreach (MachineState state in states)
            {
                MachineBinding binding = FindBinding(state.machine_id);
                if (binding == null)
                    continue;

                if (binding.targetRenderer != null)
                {
                    binding.targetRenderer.GetPropertyBlock(_properties);
                    _properties.SetColor("_Color", ColorFor(state.OperationalState));
                    binding.targetRenderer.SetPropertyBlock(_properties);
                }

                if (binding.label != null)
                    binding.label.text = $"{state.machine_id}\n{state.state}\nProd {state.produced} | Q {state.queue}";
            }
        }

        private MachineBinding FindBinding(string machineId)
        {
            if (machines == null)
                return null;
            foreach (MachineBinding binding in machines)
            {
                if (binding != null && binding.machineId == machineId)
                    return binding;
            }
            return null;
        }

        private static Color ColorFor(MachineOperationalState state)
        {
            switch (state)
            {
                case MachineOperationalState.RUNNING: return new Color(0.1f, 0.8f, 0.2f);
                case MachineOperationalState.WAITING: return new Color(1f, 0.65f, 0.1f);
                case MachineOperationalState.FAULT: return Color.red;
                case MachineOperationalState.MAINTENANCE: return new Color(0.7f, 0.2f, 1f);
                case MachineOperationalState.FINISHED: return new Color(0.1f, 0.6f, 1f);
                default: return Color.gray;
            }
        }
    }
}

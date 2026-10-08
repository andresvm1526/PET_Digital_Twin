// MachineController.cs
// Attached to each machine GameObject in the scene.
// Handles: state colour, info label, temperature/pressure display,
// queue visualisation (physical items), and delegates animation to
// machine-specific animation components.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using PETDigitalTwin.Core;

namespace PETDigitalTwin
{
    [RequireComponent(typeof(Renderer))]
    public class MachineController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────
        [Header("Identity")]
        public string machineId;
        public string machineName;

        [Header("Label")]
        [Tooltip("World-space TextMeshPro for the floating label above the machine.")]
        public TMP_Text labelText;

        [Header("Queue Visualisation")]
        [Tooltip("Prefab representing one unit in the input queue (pellet / preform / bottle).")]
        public GameObject queueItemPrefab;

        [Tooltip("Transform that acts as the anchor for queue item spawns.")]
        public Transform queueAnchor;

        [Tooltip("Max queue items shown physically.")]
        public int maxVisibleQueueItems = 10;

        [Header("Materials (auto-assigned by PlantManager if null)")]
        public Material matIdle;
        public Material matWaiting;
        public Material matRunning;
        public Material matFault;
        public Material matMaintenance;
        public Material matFinished;

        // ── State (read-only for external inspection) ────────────────────
        public MachineState CurrentState { get; private set; }
        public MachineOperationalState OperationalState => CurrentState?.OperationalState ?? MachineOperationalState.IDLE;

        // ── Internal ─────────────────────────────────────────────────────
        private Renderer _renderer;
        private readonly List<GameObject> _queueItems = new();
        private IMachineAnimation _animationComponent;

        // Pulse coroutine for FAULT / MAINTENANCE blinking
        private Coroutine _pulseCoroutine;

        // ── Unity lifecycle ──────────────────────────────────────────────
        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _animationComponent = GetComponent<IMachineAnimation>();
        }

        private void Start()
        {
            UpdateLabel(null);
        }

        // ── Public API ───────────────────────────────────────────────────
        /// <summary>Called by PlantManager when a fresh state arrives from the backend.</summary>
        public void ApplyState(MachineState state)
        {
            CurrentState = state;
            // PlantManager adds the machine-specific animation component after
            // this controller is created, so Awake may have observed no component.
            if (_animationComponent == null)
                _animationComponent = GetComponent<IMachineAnimation>();
            UpdateMaterial(state.OperationalState);
            UpdateLabel(state);
            UpdateQueueVisuals(state.queue);         // schema: "queue"
            _animationComponent?.OnStateChanged(state);
        }

        // ── Material / colour ────────────────────────────────────────────
        private void UpdateMaterial(MachineOperationalState opState)
        {
            if (_pulseCoroutine != null)
            {
                StopCoroutine(_pulseCoroutine);
                _pulseCoroutine = null;
            }

            Material mat = opState switch
            {
                MachineOperationalState.IDLE        => matIdle,
                MachineOperationalState.WAITING     => matWaiting,
                MachineOperationalState.RUNNING     => matRunning,
                MachineOperationalState.FAULT       => matFault,
                MachineOperationalState.MAINTENANCE => matMaintenance,
                MachineOperationalState.FINISHED    => matFinished,
                _                                   => matIdle
            };

            if (mat != null)
                _renderer.material = mat;

            // Blink on FAULT or MAINTENANCE for extra visibility
            if (opState == MachineOperationalState.FAULT ||
                opState == MachineOperationalState.MAINTENANCE)
            {
                _pulseCoroutine = StartCoroutine(PulseMaterial(mat));
            }
        }

        private IEnumerator PulseMaterial(Material mat)
        {
            if (mat == null) yield break;

            Color original = mat.color;
            Color dark = original * 0.4f;
            dark.a = 1f;

            while (true)
            {
                _renderer.material.color = dark;
                yield return new WaitForSeconds(0.4f);
                _renderer.material.color = original;
                yield return new WaitForSeconds(0.4f);
            }
        }

        // ── Label ────────────────────────────────────────────────────────
        private void UpdateLabel(MachineState s)
        {
            if (labelText == null) return;

            if (s == null)
            {
                labelText.text = $"<b>{machineId}</b>\n—";
                return;
            }

            // Use exact field names from machine_state.schema.json
            string line = $"<b>{s.machine_id}</b>\n" +
                          $"State: <b>{s.state}</b>\n" +
                          $"Prog: {s.progress:P0}  Q: {s.queue}\n" +
                          $"Prod: {s.produced}  Rej: {s.rejected}";

            if (s.HasTemperature) line += $"\nTemp: {s.temperature:F1} °C";
            if (s.HasPressure)    line += $"  P: {s.pressure:F1} bar";

            labelText.text = line;
        }

        // ── Queue items ──────────────────────────────────────────────────
        private void UpdateQueueVisuals(int queueLength)
        {
            if (queueItemPrefab == null || queueAnchor == null) return;

            int visible = Mathf.Clamp(queueLength, 0, maxVisibleQueueItems);

            // Spawn missing items
            while (_queueItems.Count < visible)
            {
                Vector3 offset = new Vector3(_queueItems.Count * 0.4f, 0f, 0f);
                var item = Instantiate(queueItemPrefab, queueAnchor.position + offset, Quaternion.identity, queueAnchor);
                _queueItems.Add(item);
            }

            // Destroy surplus items
            while (_queueItems.Count > visible)
            {
                var last = _queueItems[_queueItems.Count - 1];
                _queueItems.RemoveAt(_queueItems.Count - 1);
                Destroy(last);
            }
        }

        // ── Selection helper ─────────────────────────────────────────────
        private void OnMouseDown()
        {
            PlantManager.Instance?.SelectMachine(machineId);
        }
    }

    /// <summary>
    /// Interface for machine-specific animation components.
    /// Implement on InjectionMachineAnimation, BlowMoldingAnimation, etc.
    /// </summary>
    public interface IMachineAnimation
    {
        void OnStateChanged(MachineState state);
    }
}

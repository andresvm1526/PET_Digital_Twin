// PlantManager.cs
// Central coordinator for the PET Digital Twin scene.
// Responsibilities:
//   1. Create / manage all machine GameObjects procedurally from primitives.
//   2. Receive updates from DigitalTwinApiClient and route them to MachineController instances.
//   3. Maintain the "backend offline" banner.
//   4. Expose machine selection for the dashboard and camera.

using System;
using System.Collections.Generic;
using UnityEngine;
using PETDigitalTwin.API;
using PETDigitalTwin.Core;
using PETDigitalTwin.UI;

namespace PETDigitalTwin
{
    public class PlantManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────
        public static PlantManager Instance { get; private set; }

        // ── Inspector ────────────────────────────────────────────────────
        [Header("References")]
        public DigitalTwinApiClient apiClient;
        public DashboardController  dashboard;
        public GameObject           offlineBanner;   // UI panel shown when backend is offline

        [Header("Machine prefabs / queue items")]
        public GameObject queueItemPelletPrefab;
        public GameObject queueItemPreformPrefab;
        public GameObject queueItemBottlePrefab;

        // ── State ────────────────────────────────────────────────────────
        private readonly Dictionary<string, MachineController> _machines = new();
        private string _selectedMachineId;

        public event Action<MachineController> OnMachineSelected;

        // ── Layout definition ────────────────────────────────────────────
        // Each entry drives procedural creation of one machine GameObject.
        private static readonly MachineDefinition[] MachineLayout =
        {
            new("D-101",   "Dryer",                   MachineShape.Box,      new Vector3(-16f, 0f, 0f), new Vector3(2.5f, 3f, 2.5f), QueueItemType.Pellet),
            new("H-101",   "Hopper",                  MachineShape.Cone,     new Vector3(-12f, 0f, 0f), new Vector3(2f, 4f, 2f),     QueueItemType.Pellet),
            new("IMM-101", "Injection Molding Machine",MachineShape.Box,      new Vector3( -7f, 0f, 0f), new Vector3(3f, 2.5f, 2f),   QueueItemType.Preform),
            new("CV-101",  "Preform Conveyor",         MachineShape.Conveyor, new Vector3( -2f, 0f, 0f), new Vector3(4f, 0.3f, 1f),   QueueItemType.Preform),
            new("BF-101",  "Buffer",                   MachineShape.Box,      new Vector3(  3f, 0f, 0f), new Vector3(2f, 2f, 2f),     QueueItemType.Preform),
            new("OV-101",  "Reheat Oven",              MachineShape.Box,      new Vector3(  7f, 0f, 0f), new Vector3(2.5f, 2f, 2f),   QueueItemType.Preform),
            new("SBM-101", "Stretch Blow Molder",      MachineShape.Box,      new Vector3( 12f, 0f, 0f), new Vector3(3f, 2.5f, 2f),   QueueItemType.Bottle),
            new("QC-101",  "Quality Inspection",       MachineShape.Box,      new Vector3( 17f, 0f, 0f), new Vector3(2f, 1.5f, 2f),   QueueItemType.Bottle),
            new("PKG-101", "Packaging",                MachineShape.Box,      new Vector3( 22f, 0f, 0f), new Vector3(2.5f, 2f, 2f),   QueueItemType.Bottle),
        };

        // ── Unity lifecycle ──────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            BuildPlantFloor();
            BuildMachines();
            BuildConveyorBelts();

            // Wire up API events
            if (apiClient != null)
            {
                apiClient.OnMachinesUpdated += HandleMachinesUpdated;
                apiClient.OnKpisUpdated     += HandleKpisUpdated;
                apiClient.OnStatusUpdated   += HandleStatusUpdated;      // replaces OnSimTimeUpdated
                apiClient.OnBackendOffline  += HandleBackendOffline;
                apiClient.OnBackendOnline   += HandleBackendOnline;
            }

            SetOfflineBanner(true);
        }

        private void OnDestroy()
        {
            if (apiClient != null)
            {
                apiClient.OnMachinesUpdated -= HandleMachinesUpdated;
                apiClient.OnKpisUpdated     -= HandleKpisUpdated;
                apiClient.OnStatusUpdated   -= HandleStatusUpdated;
                apiClient.OnBackendOffline  -= HandleBackendOffline;
                apiClient.OnBackendOnline   -= HandleBackendOnline;
            }
        }

        // ── Procedural scene construction ────────────────────────────────

        private void BuildPlantFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "PlantFloor";
            floor.transform.localScale = new Vector3(5f, 1f, 1f);
            floor.transform.position   = new Vector3(3f, -0.01f, 0f);
            floor.GetComponent<Renderer>().material.color = new Color(0.25f, 0.25f, 0.25f);
        }

        private void BuildMachines()
        {
            foreach (var def in MachineLayout)
            {
                GameObject go = CreateMachineGO(def);
                var ctrl = go.GetComponent<MachineController>();
                _machines[def.Id] = ctrl;

                // Add machine-specific animation components
                if (def.Id == "IMM-101")
                    go.AddComponent<InjectionMachineAnimation>();
                else if (def.Id == "SBM-101")
                    go.AddComponent<BlowMoldingAnimation>();
            }
        }

        private GameObject CreateMachineGO(MachineDefinition def)
        {
            GameObject go;

            switch (def.Shape)
            {
                case MachineShape.Cone:
                    // Unity has no built-in cone; approximate with Cylinder + tapered look
                    go = new GameObject(def.Id);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    body.transform.SetParent(go.transform, false);
                    body.transform.localScale = new Vector3(def.Scale.x, def.Scale.y * 0.5f, def.Scale.z);
                    break;

                case MachineShape.Conveyor:
                    go = new GameObject(def.Id);
                    var belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    belt.name = "Belt";
                    belt.transform.SetParent(go.transform, false);
                    belt.transform.localScale = def.Scale;
                    // Add scrolling material via ProductMover
                    go.AddComponent<ConveyorVisual>();
                    break;

                default: // Box
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = def.Id;
                    go.transform.localScale = def.Scale;
                    break;
            }

            go.transform.position = def.Position + new Vector3(0f, def.Scale.y * 0.5f, 0f);

            // Add MachineController
            var ctrl = go.GetComponent<MachineController>() ?? go.AddComponent<MachineController>();
            ctrl.machineId   = def.Id;
            ctrl.machineName = def.Name;

            // Assign materials from MaterialFactory
            MaterialFactory.AssignStateMaterials(ctrl);

            // Floating label
            ctrl.labelText = CreateFloatingLabel(go, def.Id, def.Scale.y);

            // Queue anchor & prefab
            var anchor = new GameObject("QueueAnchor");
            anchor.transform.SetParent(go.transform, false);
            anchor.transform.localPosition = new Vector3(-def.Scale.x * 0.5f - 0.5f, 0f, 0f);
            ctrl.queueAnchor = anchor.transform;
            ctrl.queueItemPrefab = PickQueuePrefab(def.QueueType);

            return go;
        }

        private void BuildConveyorBelts()
        {
            // Draw simple connector pipes between consecutive machines
            for (int i = 0; i < MachineLayout.Length - 1; i++)
            {
                var from = MachineLayout[i];
                var to   = MachineLayout[i + 1];

                // Skip if already a Conveyor machine
                if (from.Shape == MachineShape.Conveyor || to.Shape == MachineShape.Conveyor)
                    continue;

                Vector3 mid = (from.Position + to.Position) * 0.5f;
                float   len = Vector3.Distance(from.Position, to.Position) - from.Scale.x * 0.5f - to.Scale.x * 0.5f;

                if (len <= 0f) continue;

                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipe.name = $"Pipe_{from.Id}_{to.Id}";
                pipe.transform.position    = mid + new Vector3(0f, 0.4f, 0f);
                pipe.transform.rotation    = Quaternion.Euler(0f, 0f, 90f);
                pipe.transform.localScale  = new Vector3(0.2f, len * 0.5f, 0.2f);
                pipe.GetComponent<Renderer>().material.color = new Color(0.5f, 0.5f, 0.6f);
            }
        }

        private TMP_Text CreateFloatingLabel(GameObject parent, string id, float machineHeight)
        {
            // Requires TextMeshPro package in the Unity project.
            // Falls back gracefully if not available.
            var labelGo  = new GameObject("Label");
            labelGo.transform.SetParent(parent.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, machineHeight + 0.8f, 0f);

            var tmp = labelGo.AddComponent<TMPro.TextMeshPro>();
            tmp.text          = id;
            tmp.fontSize      = 0.6f;
            tmp.alignment     = TMPro.TextAlignmentOptions.Center;
            tmp.color         = Color.white;
            tmp.enableWordWrapping = false;

            // Billboard: always face camera (set in LateUpdate on label)
            labelGo.AddComponent<BillboardLabel>();

            return tmp;
        }

        private GameObject PickQueuePrefab(QueueItemType type)
        {
            return type switch
            {
                QueueItemType.Pellet  => queueItemPelletPrefab,
                QueueItemType.Preform => queueItemPreformPrefab,
                QueueItemType.Bottle  => queueItemBottlePrefab,
                _                     => null,
            };
        }

        // ── API event handlers ───────────────────────────────────────────
        private void HandleMachinesUpdated(MachineState[] states)
        {
            foreach (var s in states)
            {
                if (_machines.TryGetValue(s.machine_id, out var ctrl))
                    ctrl.ApplyState(s);
            }
            dashboard?.UpdateMachineTable(states);
        }

        private void HandleKpisUpdated(KpiSnapshot kpis)
        {
            dashboard?.UpdateKpis(kpis);
        }

        private void HandleStatusUpdated(SimulationStatusResponse status)
        {
            dashboard?.UpdateSimTime(status);
        }

        private void HandleBackendOffline() => SetOfflineBanner(true);
        private void HandleBackendOnline()  => SetOfflineBanner(false);

        private void SetOfflineBanner(bool show)
        {
            if (offlineBanner != null) offlineBanner.SetActive(show);
            dashboard?.SetOffline(show);
        }

        // ── Selection ────────────────────────────────────────────────────
        public void SelectMachine(string machineId)
        {
            _selectedMachineId = machineId;
            if (_machines.TryGetValue(machineId, out var ctrl))
            {
                OnMachineSelected?.Invoke(ctrl);
                dashboard?.ShowMachineDetail(ctrl.CurrentState);
            }
        }

        public MachineController GetMachine(string machineId) =>
            _machines.TryGetValue(machineId, out var ctrl) ? ctrl : null;

        // ── Inner types ──────────────────────────────────────────────────
        private enum MachineShape { Box, Cone, Conveyor }
        private enum QueueItemType { Pellet, Preform, Bottle }

        private class MachineDefinition
        {
            public string       Id;
            public string       Name;
            public MachineShape Shape;
            public Vector3      Position;
            public Vector3      Scale;
            public QueueItemType QueueType;

            public MachineDefinition(string id, string name, MachineShape shape,
                                     Vector3 pos, Vector3 scale, QueueItemType queueType)
            {
                Id = id; Name = name; Shape = shape;
                Position = pos; Scale = scale; QueueType = queueType;
            }
        }
    }
}

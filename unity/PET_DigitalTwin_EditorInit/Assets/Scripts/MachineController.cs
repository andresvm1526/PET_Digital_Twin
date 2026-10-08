using System;
using UnityEngine;

public enum MachineState
{
    IDLE,
    WAITING,
    RUNNING,
    FAULT,
    MAINTENANCE,
    FINISHED
}

[DisallowMultipleComponent]
public sealed class MachineController : MonoBehaviour
{
    [Header("Identity")]
    public string machineId;
    public string machineType;

    [Header("Live state")]
    public MachineState state = MachineState.IDLE;
    [Range(0f, 1f)] public float utilization;
    [Min(0)] public int queue;
    [Min(0)] public int produced;
    [Min(0)] public int rejected;
    public float temperature;
    public float pressure;
    [Range(0f, 1f)] public float cycleProgress;
    [Min(0f)] public float simulationTime;
    public string sourceMachineType;

    [Header("State visuals")]
    [SerializeField] private Renderer[] visualRenderers = Array.Empty<Renderer>();
    [SerializeField] private Material idleMaterial;
    [SerializeField] private Material waitingMaterial;
    [SerializeField] private Material runningMaterial;
    [SerializeField] private Material faultMaterial;
    [SerializeField] private Material maintenanceMaterial;
    [SerializeField] private Material finishedMaterial;

    public void Configure(
        string id,
        string type,
        Renderer[] renderers,
        Material idle,
        Material waiting,
        Material running,
        Material fault,
        Material maintenance,
        Material finished)
    {
        machineId = id;
        machineType = type;
        visualRenderers = renderers ?? Array.Empty<Renderer>();
        idleMaterial = idle;
        waitingMaterial = waiting;
        runningMaterial = running;
        faultMaterial = fault;
        maintenanceMaterial = maintenance;
        finishedMaterial = finished;
        ApplyStateVisual();
    }

    public void SetState(MachineState newState)
    {
        state = newState;
        ApplyStateVisual();
    }

    public void ApplyTelemetry(MachineTelemetry telemetry)
    {
        if (telemetry == null || telemetry.machine_id != machineId)
        {
            return;
        }

        sourceMachineType = telemetry.machine_type;
        simulationTime = Mathf.Max(0f, telemetry.simulation_time);
        cycleProgress = Mathf.Clamp01(telemetry.progress);
        utilization = cycleProgress;
        queue = Mathf.Max(0, telemetry.queue);
        produced = Mathf.Max(0, telemetry.produced);
        rejected = Mathf.Max(0, telemetry.rejected);
        temperature = telemetry.temperature;
        pressure = telemetry.pressure;

        if (Enum.TryParse(telemetry.state, true, out MachineState receivedState))
        {
            SetState(receivedState);
        }
    }

    private void OnEnable()
    {
        ApplyStateVisual();
    }

    private void OnValidate()
    {
        utilization = Mathf.Clamp01(utilization);
        queue = Mathf.Max(0, queue);
        produced = Mathf.Max(0, produced);
        rejected = Mathf.Max(0, rejected);
        cycleProgress = Mathf.Clamp01(cycleProgress);
        simulationTime = Mathf.Max(0f, simulationTime);
        ApplyStateVisual();
    }

    private void ApplyStateVisual()
    {
        Material stateMaterial = GetStateMaterial();
        if (stateMaterial == null || visualRenderers == null)
        {
            return;
        }

        foreach (Renderer visualRenderer in visualRenderers)
        {
            if (visualRenderer != null)
            {
                visualRenderer.sharedMaterial = stateMaterial;
            }
        }
    }

    private Material GetStateMaterial()
    {
        switch (state)
        {
            case MachineState.WAITING:
                return waitingMaterial;
            case MachineState.RUNNING:
                return runningMaterial;
            case MachineState.FAULT:
                return faultMaterial;
            case MachineState.MAINTENANCE:
                return maintenanceMaterial;
            case MachineState.FINISHED:
                return finishedMaterial;
            default:
                return idleMaterial;
        }
    }
}

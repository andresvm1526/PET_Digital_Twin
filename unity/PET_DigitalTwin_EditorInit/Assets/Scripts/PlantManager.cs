using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlantManager : MonoBehaviour
{
    [Header("Data source")]
    [SerializeField] private bool useRestApi = true;
    [Tooltip("Runs the local demo while the REST API is unavailable.")]
    [SerializeField] private bool demoMode = true;

    [Header("Demo fallback")]
    [SerializeField, Min(1f)] private float minimumStateInterval = 4f;
    [SerializeField, Min(1f)] private float maximumStateInterval = 8f;
    [SerializeField, Min(0.5f)] private float transientStateDuration = 3f;

    private readonly List<MachineController> machines = new List<MachineController>();
    private readonly List<ProductMover> productMovers = new List<ProductMover>();
    private DigitalTwinApiClient apiClient;
    private Coroutine demoCoroutine;

    private void Awake()
    {
        RefreshMachines();

        if (useRestApi)
        {
            apiClient = GetComponent<DigitalTwinApiClient>();
            if (apiClient == null)
            {
                apiClient = gameObject.AddComponent<DigitalTwinApiClient>();
            }

            apiClient.MachinesUpdated += HandleMachinesUpdated;
            apiClient.BackendOnline += HandleBackendOnline;
            apiClient.BackendOffline += HandleBackendOffline;
        }
    }

    private void Start()
    {
        if (demoMode)
        {
            StartDemoMode();
        }
    }

    private void OnDestroy()
    {
        if (apiClient == null)
        {
            return;
        }

        apiClient.MachinesUpdated -= HandleMachinesUpdated;
        apiClient.BackendOnline -= HandleBackendOnline;
        apiClient.BackendOffline -= HandleBackendOffline;
    }

    public void RefreshMachines()
    {
        machines.Clear();
        GetComponentsInChildren(true, machines);
        productMovers.Clear();
        GetComponentsInChildren(true, productMovers);
    }

    public MachineController GetMachine(string machineId)
    {
        return machines.Find(machine => machine.machineId == machineId);
    }

    private void ApplyNominalDemoStates()
    {
        foreach (MachineController machine in machines)
        {
            bool isActiveMachine = machine.machineId == "IMM-101" || machine.machineId == "SBM-101";
            machine.SetState(isActiveMachine ? MachineState.RUNNING : MachineState.IDLE);
            machine.utilization = isActiveMachine ? 0.82f : 0f;
        }

        SetProductsMoving(true);
    }

    private void StartDemoMode()
    {
        if (demoCoroutine != null)
        {
            return;
        }

        ApplyNominalDemoStates();
        demoCoroutine = StartCoroutine(RunDemo());
    }

    private void StopDemoMode()
    {
        if (demoCoroutine == null)
        {
            return;
        }

        StopCoroutine(demoCoroutine);
        demoCoroutine = null;
    }

    private void HandleBackendOnline()
    {
        StopDemoMode();
    }

    private void HandleBackendOffline()
    {
        if (demoMode)
        {
            StartDemoMode();
        }
    }

    private void HandleMachinesUpdated(MachineTelemetry[] telemetry)
    {
        StopDemoMode();
        bool productionIsRunning = false;

        foreach (MachineTelemetry machineTelemetry in telemetry)
        {
            MachineController machine = GetMachine(machineTelemetry.machine_id);
            if (machine != null)
            {
                machine.ApplyTelemetry(machineTelemetry);
            }

            if (string.Equals(machineTelemetry.state, "RUNNING", System.StringComparison.OrdinalIgnoreCase))
            {
                productionIsRunning = true;
            }
        }

        SetProductsMoving(productionIsRunning);
    }

    private void SetProductsMoving(bool shouldMove)
    {
        foreach (ProductMover productMover in productMovers)
        {
            productMover.SetMoving(shouldMove);
        }
    }

    private IEnumerator RunDemo()
    {
        while (demoMode)
        {
            float waitTime = Random.Range(minimumStateInterval, maximumStateInterval);
            yield return new WaitForSeconds(waitTime);

            if (machines.Count == 0)
            {
                continue;
            }

            MachineController selectedMachine = machines[Random.Range(0, machines.Count)];
            MachineState nominalState = selectedMachine.state;
            MachineState transientState = Random.value < 0.65f
                ? MachineState.WAITING
                : MachineState.FAULT;

            selectedMachine.SetState(transientState);
            yield return new WaitForSeconds(transientStateDuration);
            selectedMachine.SetState(nominalState);
        }
    }

    private void OnValidate()
    {
        minimumStateInterval = Mathf.Max(1f, minimumStateInterval);
        maximumStateInterval = Mathf.Max(minimumStateInterval, maximumStateInterval);
        transientStateDuration = Mathf.Max(0.5f, transientStateDuration);
    }
}

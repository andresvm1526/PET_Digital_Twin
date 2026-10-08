// MachineState.cs
// Data contract EXACTLY mirroring contracts/machine_state.schema.json and kpis.schema.json.
// Field names must match the Python API JSON keys verbatim.
// Do NOT add properties not present in the schema.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PETDigitalTwin.Core
{
    // ── Operational States ────────────────────────────────────────────────
    public enum MachineOperationalState
    {
        IDLE,
        WAITING,
        RUNNING,
        FAULT,
        MAINTENANCE,
        FINISHED
    }

    // ── Machine State ─────────────────────────────────────────────────────
    // Maps to: contracts/machine_state.schema.json
    // Returned by: GET /machines  (as list)
    //              GET /machines/{machine_id}
    [Serializable]
    public class MachineState
    {
        // ── Required fields (exact JSON key names) ────────────────────────
        public string machine_id;       // e.g. "IMM-101"
        public string machine_type;     // e.g. "injection_molding"
        public string state;            // "IDLE" | "WAITING" | "RUNNING" | "FAULT" | "MAINTENANCE" | "FINISHED"
        public float  simulation_time;  // seconds of sim time
        public float  progress;         // cycle fraction [0..1]
        public int    queue;            // units waiting in input queue
        public int    produced;         // total units produced
        public int    rejected;         // total units rejected/scrapped

        // ── Optional / nullable fields ────────────────────────────────────
        // JSON null maps to -1 sentinel (JsonUtility cannot represent null floats).
        public float temperature = -1f; // °C, or -1 if not applicable
        public float pressure    = -1f; // bar, or -1 if not applicable

        // ── Helpers ───────────────────────────────────────────────────────
        public MachineOperationalState OperationalState
        {
            get
            {
                if (Enum.TryParse<MachineOperationalState>(state, true, out var result))
                    return result;
                return MachineOperationalState.IDLE;
            }
        }

        public bool HasTemperature => temperature >= 0f;
        public bool HasPressure    => pressure    >= 0f;

        public override string ToString() =>
            $"[{machine_id}] {state} | Prog:{progress:P0} | Prod:{produced} | Rej:{rejected} | Q:{queue}";
    }

    // ── /machines list wrapper ────────────────────────────────────────────
    // GET /machines returns a JSON array, not an object.
    // Unity's JsonUtility cannot parse top-level arrays; we wrap in a helper below.
    [Serializable]
    public class MachineListResponse
    {
        public MachineState[] machines;
    }

    // ── KPI Snapshot ──────────────────────────────────────────────────────
    // Maps to: contracts/kpis.schema.json
    // Returned by: GET /kpis
    [Serializable]
    public class KpiSnapshot
    {
        // Top-level required fields
        public int    production_total;
        public int    good_production;
        public int    rejected_units;
        public float  scrap_percent;               // 0-100
        public float  throughput_units_per_hour;
        public float  average_lead_time_seconds;
        public float  average_queue_time_seconds;
        public float  average_wip_units;
        public int    demand;
        public float  demand_compliance_percent;   // 0-100
        public float  order_completion_time_seconds;
        public float  order_completion_time_hours;
        public float  scheduled_time_seconds;
        public bool   completed_within_scheduled_time;

        // nested objects – parsed separately (see KpiExtensions, KpiBottleneck)
        // machine_metrics: dict – not trivially parseable by JsonUtility; resolved via manual JSON
        // rejections_by_stage / bottleneck / extensions: same issue
        // → parsed by DigitalTwinApiClient using SimpleJson or string manipulation
        // → these summary fields cover the dashboard requirement without nested types
    }

    // ── /status response ──────────────────────────────────────────────────
    // Maps to: contracts/status.schema.json
    // Returned by: GET /status  and  GET /simulation/time (subset)
    [Serializable]
    public class SimulationStatusResponse
    {
        public string status;              // "READY" | "RUNNING" | "PAUSED" | "FINISHED" | "STOPPED" | "ERROR"
        public float  simulation_time;     // seconds
        public string time_unit;           // always "seconds"
        public int    completed_units;
        public int    demand;
    }

    // ── /simulation/time response ─────────────────────────────────────────
    [Serializable]
    public class SimulationTimeResponse
    {
        public float  simulation_time;     // seconds
        public string unit;                // always "seconds"
    }
}

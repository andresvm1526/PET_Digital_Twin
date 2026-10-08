// DashboardController.cs
// Manages the in-scene HUD showing:
//   • Simulation time + simulation status
//   • KPI cards (field names match contracts/kpis.schema.json exactly)
//   • Machine table: Machine / State / Utilization / Queue
//   • Machine detail panel (selected machine)
//   • Backend offline banner

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PETDigitalTwin.Core;

namespace PETDigitalTwin.UI
{
    public class DashboardController : MonoBehaviour
    {
        // ── Simulation time ──────────────────────────────────────────
        [Header("Simulation Time")]
        public TMP_Text simTimeLabel;

        // ── KPI labels ───────────────────────────────────────────────
        [Header("KPI Labels")]
        public TMP_Text totalProductionLabel;
        public TMP_Text goodProductsLabel;
        public TMP_Text scrapLabel;
        public TMP_Text throughputLabel;
        public TMP_Text wipLabel;
        public TMP_Text demandCompletionLabel;

        // ── Machine table ────────────────────────────────────────────
        [Header("Machine Table")]
        public Transform  tableRowContainer;
        public GameObject tableRowPrefab;

        // ── Machine detail panel ─────────────────────────────────────
        [Header("Machine Detail Panel")]
        public GameObject detailPanel;
        public TMP_Text   detailMachineId;
        public TMP_Text   detailState;
        public TMP_Text   detailProgress;
        public TMP_Text   detailProduction;
        public TMP_Text   detailRejected;
        public TMP_Text   detailQueue;
        public TMP_Text   detailTemperature;
        public TMP_Text   detailPressure;
        public TMP_Text   detailSimTime;

        // ── Offline banner ───────────────────────────────────────────
        [Header("Offline Banner")]
        public GameObject offlineBannerPanel;
        public TMP_Text   offlineBannerLabel;

        // ── State colour map ─────────────────────────────────────────
        private static readonly Dictionary<string, Color> StateColours = new()
        {
            { "IDLE",        new Color(0.7f, 0.7f, 0.7f) },
            { "WAITING",     new Color(1.0f, 0.9f, 0.0f) },
            { "RUNNING",     new Color(0.2f, 0.9f, 0.2f) },
            { "FAULT",       new Color(1.0f, 0.1f, 0.1f) },
            { "MAINTENANCE", new Color(1.0f, 0.5f, 0.0f) },
            { "FINISHED",    new Color(0.2f, 0.5f, 1.0f) },
        };

        private readonly Dictionary<string, TableRow> _tableRows = new();

        // ── Unity lifecycle ──────────────────────────────────────────
        private void Start()
        {
            if (detailPanel != null) detailPanel.SetActive(false);
            SetOffline(true);
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Called by PlantManager.HandleStatusUpdated.
        /// Param: SimulationStatusResponse from GET /status (contracts/status.schema.json).
        /// </summary>
        public void UpdateSimTime(SimulationStatusResponse status)
        {
            if (simTimeLabel == null) return;
            float s = status.simulation_time;
            int h   = (int)(s / 3600f);
            int m   = (int)((s % 3600f) / 60f);
            int sec = (int)(s % 60f);
            simTimeLabel.text = $"SIM TIME  {h:00}:{m:00}:{sec:00}  [{status.status}]";
        }

        /// <summary>
        /// Called by PlantManager.HandleKpisUpdated.
        /// Field names map 1-to-1 to contracts/kpis.schema.json.
        /// </summary>
        public void UpdateKpis(KpiSnapshot kpis)
        {
            Set(totalProductionLabel, $"TOTAL PRODUCTION\n<b>{kpis.production_total}</b>");
            Set(goodProductsLabel,    $"GOOD PRODUCTS\n<b>{kpis.good_production}</b>");
            Set(scrapLabel,           $"SCRAP\n<b>{kpis.rejected_units}</b>  ({kpis.scrap_percent:F1} %)");
            Set(throughputLabel,      $"THROUGHPUT\n<b>{kpis.throughput_units_per_hour:F1}</b> u/hr");
            Set(wipLabel,             $"WIP\n<b>{kpis.average_wip_units:F1}</b>");
            Set(demandCompletionLabel,$"DEMAND COMPLETION\n<b>{kpis.demand_compliance_percent:F1} %</b>");
        }

        public void UpdateMachineTable(MachineState[] states)
        {
            foreach (var s in states)
            {
                if (!_tableRows.TryGetValue(s.machine_id, out var row))
                {
                    row = CreateTableRow(s.machine_id);
                    _tableRows[s.machine_id] = row;
                }
                row.Apply(s);
            }
        }

        /// <summary>
        /// Shows the detail panel for the selected machine.
        /// Field names use machine_state.schema.json names: produced, rejected, queue, progress.
        /// </summary>
        public void ShowMachineDetail(MachineState s)
        {
            if (detailPanel == null || s == null) return;
            detailPanel.SetActive(true);

            Set(detailMachineId,   $"{s.machine_id}  —  {s.machine_type}");
            Set(detailState,       s.state, StateColour(s.state));
            Set(detailProgress,    $"Progress:    {s.progress:P1}");
            Set(detailProduction,  $"Produced:    {s.produced}");
            Set(detailRejected,    $"Rejected:    {s.rejected}");
            Set(detailQueue,       $"Queue:       {s.queue}");
            Set(detailSimTime,     $"Sim time:    {s.simulation_time:F1} s");

            Set(detailTemperature, s.HasTemperature ? $"Temperature: {s.temperature:F1} C" : "");
            Set(detailPressure,    s.HasPressure    ? $"Pressure:    {s.pressure:F1} bar"  : "");
        }

        public void SetOffline(bool offline)
        {
            if (offlineBannerPanel != null) offlineBannerPanel.SetActive(offline);
            if (offlineBannerLabel  != null) offlineBannerLabel.text = offline ? "BACKEND OFFLINE" : "";
        }

        // ── Table row ────────────────────────────────────────────────
        private TableRow CreateTableRow(string machineId)
        {
            if (tableRowContainer == null || tableRowPrefab == null)
                return new TableRow();

            var go    = Instantiate(tableRowPrefab, tableRowContainer);
            go.name   = $"Row_{machineId}";
            var texts = go.GetComponentsInChildren<TMP_Text>();

            return new TableRow
            {
                IdLabel    = texts.Length > 0 ? texts[0] : null,
                StateLabel = texts.Length > 1 ? texts[1] : null,
                ProgLabel  = texts.Length > 2 ? texts[2] : null,
                QueueLabel = texts.Length > 3 ? texts[3] : null,
            };
        }

        // ── Helpers ──────────────────────────────────────────────────
        private static void Set(TMP_Text label, string text, Color? colour = null)
        {
            if (label == null) return;
            label.text = text;
            if (colour.HasValue) label.color = colour.Value;
        }

        private static Color StateColour(string state) =>
            StateColours.TryGetValue(state?.ToUpperInvariant() ?? "", out var c) ? c : Color.white;

        // ── Inner types ──────────────────────────────────────────────
        private class TableRow
        {
            public TMP_Text IdLabel;
            public TMP_Text StateLabel;
            public TMP_Text ProgLabel;   // progress (was utilization — pending KPI mapping)
            public TMP_Text QueueLabel;

            public void Apply(MachineState s)
            {
                Set(IdLabel,    s.machine_id);
                Set(StateLabel, s.state, StateColours.TryGetValue(s.state, out var c) ? c : Color.white);
                Set(ProgLabel,  $"{s.progress:P0}");
                Set(QueueLabel, s.queue.ToString());
            }

            private static void Set(TMP_Text l, string t, Color? colour = null)
            {
                if (l == null) return;
                l.text = t;
                if (colour.HasValue) l.color = colour.Value;
            }
        }
    }
}

// DigitalTwinApiClient.cs
// REST client for the Python SimPy backend (FastAPI).
// Polls: GET /machines, GET /kpis, GET /simulation/time
// Frequency: 5 updates per second (200 ms interval)
//
// NOTES on response shapes (from api/main.py):
//   GET /machines        → returns JSON array  [ {...}, {...} ]
//   GET /kpis            → returns JSON object { ... }
//   GET /simulation/time → returns JSON object { "simulation_time": float, "unit": "seconds" }
//   GET /status          → returns JSON object matching status.schema.json

using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using PETDigitalTwin.Core;

namespace PETDigitalTwin.API
{
    public class DigitalTwinApiClient : MonoBehaviour
    {
        // ── Inspector settings ───────────────────────────────────────────
        [Header("Backend Connection")]
        [Tooltip("Backend host. Loaded from Resources/Config/api_config.json if available.")]
        public string host = "127.0.0.1";

        [Tooltip("Backend port.")]
        public int port = 8000;

        [Tooltip("Polling interval in milliseconds (200 = 5 Hz).")]
        public int pollIntervalMs = 200;

        // ── Events ───────────────────────────────────────────────────────
        /// <summary>Fired when a fresh list of machine states arrives.</summary>
        public event Action<MachineState[]> OnMachinesUpdated;

        /// <summary>Fired when fresh KPIs arrive.</summary>
        public event Action<KpiSnapshot> OnKpisUpdated;

        /// <summary>Fired when a new simulation time/status arrives.</summary>
        public event Action<SimulationStatusResponse> OnStatusUpdated;

        /// <summary>Fired on first successful connection after being offline.</summary>
        public event Action OnBackendOnline;

        /// <summary>Fired when the backend becomes unreachable (after threshold failures).</summary>
        public event Action OnBackendOffline;

        // ── State ────────────────────────────────────────────────────────
        private bool _isRunning;
        private bool _wasOffline = true;

        private const int OfflineThreshold = 3;
        private int _failureStreak;

        private string BaseUrl => $"http://{host}:{port}";

        // ── Unity lifecycle ──────────────────────────────────────────────
        private void Start()
        {
            LoadConfigFromResources();
            StartPolling();
        }

        private void OnDestroy() => StopPolling();

        // ── Public API ───────────────────────────────────────────────────
        public void StartPolling()
        {
            if (_isRunning) return;
            _isRunning = true;
            StartCoroutine(PollLoop());
        }

        public void StopPolling()
        {
            _isRunning = false;
            StopAllCoroutines();
        }

        // ── Config loader ────────────────────────────────────────────────
        private void LoadConfigFromResources()
        {
            // Connection settings are separate from the SimPy process input.
            var asset = Resources.Load<TextAsset>("Config/api_config");

            if (asset == null)
            {
                Debug.Log("[ApiClient] No config found in Resources/Config/ – using inspector defaults.");
                return;
            }
            try
            {
                var cfg = JsonUtility.FromJson<ApiConfig>(asset.text);
                if (cfg?.api != null)
                {
                    if (!string.IsNullOrEmpty(cfg.api.host)) host = cfg.api.host;
                    if (cfg.api.port > 0)              port            = cfg.api.port;
                    if (cfg.api.poll_interval_ms > 0)  pollIntervalMs  = cfg.api.poll_interval_ms;
                    Debug.Log($"[ApiClient] Config loaded from {asset.name}: {BaseUrl}  poll:{pollIntervalMs}ms");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[ApiClient] Config parse error: {e.Message}"); }
        }

        // ── Poll loop ────────────────────────────────────────────────────
        private IEnumerator PollLoop()
        {
            float interval = pollIntervalMs / 1000f;
            while (_isRunning)
            {
                yield return FetchMachines();
                yield return FetchKpis();
                yield return FetchStatus();
                yield return new WaitForSeconds(interval);
            }
        }

        // ── Fetch helpers ────────────────────────────────────────────────

        // GET /machines → raw JSON array "[{...}, ...]"
        // Unity JsonUtility cannot parse top-level arrays.
        // We wrap it: {"machines":[...]} before parsing.
        private IEnumerator FetchMachines()
        {
            yield return Get($"{BaseUrl}/machines", text =>
            {
                // JsonUtility cannot assign JSON null to float. Preserve the API
                // contract and use -1 only in the Unity-side representation.
                string normalized = Regex.Replace(
                    text,
                    "\\\"(temperature|pressure)\\\"\\s*:\\s*null",
                    "\"$1\":-1"
                );
                string wrapped = "{\"machines\":" + normalized + "}";
                var response = JsonUtility.FromJson<MachineListResponse>(wrapped);
                if (response?.machines != null)
                    OnMachinesUpdated?.Invoke(response.machines);
            });
        }

        private IEnumerator FetchKpis()
        {
            yield return Get($"{BaseUrl}/kpis", text =>
            {
                var kpis = JsonUtility.FromJson<KpiSnapshot>(text);
                if (kpis != null)
                    OnKpisUpdated?.Invoke(kpis);
            });
        }

        // GET /status — richer than /simulation/time; covers the dashboard time display
        private IEnumerator FetchStatus()
        {
            yield return Get($"{BaseUrl}/status", text =>
            {
                var s = JsonUtility.FromJson<SimulationStatusResponse>(text);
                if (s != null)
                    OnStatusUpdated?.Invoke(s);
            });
        }

        // ── Generic GET ──────────────────────────────────────────────────
        private IEnumerator Get(string url, Action<string> onSuccess)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = 2;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
                HandleSuccess(req.downloadHandler.text, onSuccess);
            else
                HandleFailure(req.error);
        }

        private void HandleSuccess(string json, Action<string> onSuccess)
        {
            _failureStreak = 0;
            if (_wasOffline)
            {
                _wasOffline = false;
                Debug.Log("[ApiClient] Backend ONLINE.");
                OnBackendOnline?.Invoke();
            }
            try { onSuccess(json); }
            catch (Exception e) { Debug.LogWarning($"[ApiClient] JSON parse error: {e.Message}\nRaw: {json}"); }
        }

        private void HandleFailure(string error)
        {
            _failureStreak++;
            if (_failureStreak == OfflineThreshold)
            {
                _wasOffline = true;
                Debug.LogWarning($"[ApiClient] Backend OFFLINE ({error})");
                OnBackendOffline?.Invoke();
            }
        }

        // ── Config DTOs ──────────────────────────────────────────────────
        [Serializable] private class ApiConfig { public ApiSection api; }
        [Serializable] private class ApiSection
        {
            public string host;
            public int    port;
            public int    poll_interval_ms;
        }
    }
}

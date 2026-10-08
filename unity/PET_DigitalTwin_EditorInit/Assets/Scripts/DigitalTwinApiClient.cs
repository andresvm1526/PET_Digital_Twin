using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class DigitalTwinApiClient : MonoBehaviour
{
    [Header("REST API")]
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField, Min(1)] private int port = 8000;
    [SerializeField, Min(100)] private int pollIntervalMs = 200;
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 2;
    [SerializeField, Min(1)] private int offlineFailureThreshold = 3;

    public event Action<MachineTelemetry[]> MachinesUpdated;
    public event Action BackendOnline;
    public event Action BackendOffline;

    public bool IsOnline { get; private set; }
    public string BaseUrl => $"http://{host}:{port}";

    private bool isPolling;
    private bool connectionStateReported;
    private int failureStreak;

    private void Awake()
    {
        LoadConfiguration();
    }

    private void Start()
    {
        StartPolling();
    }

    private void OnDestroy()
    {
        StopPolling();
    }

    public void StartPolling()
    {
        if (isPolling)
        {
            return;
        }

        isPolling = true;
        StartCoroutine(PollLoop());
    }

    public void StopPolling()
    {
        isPolling = false;
        StopAllCoroutines();
    }

    private IEnumerator PollLoop()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(pollIntervalMs / 1000f);

        while (isPolling)
        {
            yield return FetchMachines();
            yield return wait;
        }
    }

    private IEnumerator FetchMachines()
    {
        using (UnityWebRequest request = UnityWebRequest.Get($"{BaseUrl}/machines"))
        {
            request.timeout = requestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                RecordFailure(request.error);
                yield break;
            }

            try
            {
                string normalizedJson = Regex.Replace(
                    request.downloadHandler.text,
                    "\\\"(temperature|pressure)\\\"\\s*:\\s*null",
                    "\"$1\":-1");

                string wrappedJson = "{\"machines\":" + normalizedJson + "}";
                MachineTelemetryList response = JsonUtility.FromJson<MachineTelemetryList>(wrappedJson);

                if (response == null || response.machines == null || response.machines.Length == 0)
                {
                    RecordFailure("The /machines response did not contain machine data.");
                    yield break;
                }

                RecordSuccess();
                MachinesUpdated?.Invoke(response.machines);
            }
            catch (Exception exception)
            {
                RecordFailure($"Invalid /machines JSON: {exception.Message}");
            }
        }
    }

    private void RecordSuccess()
    {
        failureStreak = 0;
        if (connectionStateReported && IsOnline)
        {
            return;
        }

        IsOnline = true;
        connectionStateReported = true;
        Debug.Log($"[PET REST] Backend online at {BaseUrl}.");
        BackendOnline?.Invoke();
    }

    private void RecordFailure(string error)
    {
        failureStreak++;
        if (failureStreak < offlineFailureThreshold)
        {
            return;
        }

        if (connectionStateReported && !IsOnline)
        {
            return;
        }

        IsOnline = false;
        connectionStateReported = true;
        Debug.LogWarning($"[PET REST] Backend offline: {error}");
        BackendOffline?.Invoke();
    }

    private void LoadConfiguration()
    {
        TextAsset configAsset = Resources.Load<TextAsset>("Config/api_config");
        if (configAsset == null)
        {
            return;
        }

        try
        {
            ApiConfigRoot config = JsonUtility.FromJson<ApiConfigRoot>(configAsset.text);
            if (config == null || config.api == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.api.host))
            {
                host = config.api.host;
            }

            if (config.api.port > 0)
            {
                port = config.api.port;
            }

            if (config.api.poll_interval_ms >= 100)
            {
                pollIntervalMs = config.api.poll_interval_ms;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PET REST] Could not read api_config.json: {exception.Message}");
        }
    }

    private void OnValidate()
    {
        port = Mathf.Max(1, port);
        pollIntervalMs = Mathf.Max(100, pollIntervalMs);
        requestTimeoutSeconds = Mathf.Max(1, requestTimeoutSeconds);
        offlineFailureThreshold = Mathf.Max(1, offlineFailureThreshold);
    }

    [Serializable]
    private sealed class ApiConfigRoot
    {
        public ApiConfig api = new ApiConfig();
    }

    [Serializable]
    private sealed class ApiConfig
    {
        public string host = string.Empty;
        public int port = 0;
        public int poll_interval_ms = 0;
    }
}

using System;

[Serializable]
public sealed class MachineTelemetry
{
    public string machine_id;
    public string machine_type;
    public string state;
    public float simulation_time;
    public float progress;
    public int queue;
    public float temperature = -1f;
    public float pressure = -1f;
    public int produced;
    public int rejected;
}

[Serializable]
public sealed class MachineTelemetryList
{
    public MachineTelemetry[] machines;
}

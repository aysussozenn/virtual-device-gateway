namespace Gateway.Configuration;

/// <summary>Root configuration loaded from gateway.json.</summary>
public sealed class GatewayConfig
{
    /// <summary>Adapter name to bind to. If null/empty the loopback adapter is auto-selected.</summary>
    public string? Adapter { get; set; }

    public List<DeviceConfig> Devices { get; set; } = new();
}

public sealed class DeviceConfig
{
    public string Id { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;

    /// <summary>MAC address, e.g. "02-00-00-00-10-05" or "02:00:00:00:10:05".</summary>
    public string Mac { get; set; } = string.Empty;

    public BehaviorConfig Behavior { get; set; } = new();
    public FaultConfig? Fault { get; set; }
}

public sealed class BehaviorConfig
{
    /// <summary>"canned", "playback", or "parameters".</summary>
    public string Type { get; set; } = "canned";

    /// <summary>For "canned": command code (e.g. "0x01") -> reply payload as hex (e.g. "00 C8").</summary>
    public Dictionary<string, string>? Map { get; set; }

    /// <summary>For "playback": ordered reply payloads as hex.</summary>
    public List<string>? Sequence { get; set; }

    public bool Loop { get; set; }

    /// <summary>For "parameters": the named registers the device exposes.</summary>
    public List<ParameterConfig>? Parameters { get; set; }
}

public sealed class ParameterConfig
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public double Jitter { get; set; }
    public int Decimals { get; set; }
}

public sealed class FaultConfig
{
    public double DropProbability { get; set; }
    public int ExtraLatencyMs { get; set; }
    public double CorruptProbability { get; set; }
}


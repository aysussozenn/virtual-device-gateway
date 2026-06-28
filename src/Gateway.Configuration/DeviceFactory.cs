using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Gateway.Core;
using Gateway.Devices;
using Gateway.Devices.Behaviors;
using Microsoft.Extensions.Logging;

namespace Gateway.Configuration;

/// <summary>Builds simulated devices from configuration and registers them in a <see cref="DeviceRegistry"/>.</summary>
public static class DeviceFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,           // accept camelCase JSON ("devices" -> Devices)
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GatewayConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GatewayConfig>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Configuration at '{path}' was empty.");
    }

    public static DeviceRegistry BuildRegistry(GatewayConfig config, TimeProvider clock, ILoggerFactory loggerFactory)
    {
        var registry = new DeviceRegistry();
        foreach (var dc in config.Devices)
            registry.Register(Build(dc, clock, loggerFactory));
        return registry;
    }

    public static ISimulatedDevice Build(DeviceConfig dc, TimeProvider clock, ILoggerFactory loggerFactory)
    {
        var identity = new DeviceIdentity(dc.Id, IPAddress.Parse(dc.Ip), ParseMac(dc.Mac));
        IDeviceBehavior behavior = BuildBehavior(dc.Behavior);

        if (dc.Fault is { } f && (f.DropProbability > 0 || f.ExtraLatencyMs > 0 || f.CorruptProbability > 0))
        {
            behavior = new FaultInjectingBehavior(behavior,
                new FaultPolicy(f.DropProbability, TimeSpan.FromMilliseconds(f.ExtraLatencyMs), f.CorruptProbability));
        }

        return new SimulatedDevice(identity, behavior, clock, loggerFactory.CreateLogger($"Device.{dc.Id}"));
    }

    private static IDeviceBehavior BuildBehavior(BehaviorConfig b) => b.Type.ToLowerInvariant() switch
    {
        "canned" => new CannedBehavior(
            (b.Map ?? new()).ToDictionary(
                kv => ParseCommand(kv.Key),
                kv => new DeviceReply(0, ParseHex(kv.Value)))),
        "playback" => new PlaybackBehavior(
            (b.Sequence ?? new()).Select(h => new DeviceReply(0, ParseHex(h))).ToList(),
            b.Loop),
        "parameters" => new ParameterBehavior(
            (b.Parameters ?? new()).Select(p =>
                new ParameterSpec(p.Name, p.Unit, p.Value, p.Min, p.Max, p.Jitter, p.Decimals))),
        _ => throw new InvalidOperationException($"Unknown behavior type '{b.Type}'.")
    };

    private static ushort ParseCommand(string s)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ushort.Parse(s, CultureInfo.InvariantCulture);
    }

    private static byte[] ParseHex(string s)
    {
        var tokens = s.Split(new[] { ' ', '-', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Select(t => byte.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
    }

    private static PhysicalAddress ParseMac(string s)
        => PhysicalAddress.Parse(s.Replace(":", "-").ToUpperInvariant());
}

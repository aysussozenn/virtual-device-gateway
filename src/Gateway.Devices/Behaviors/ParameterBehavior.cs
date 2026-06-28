using Gateway.Core;

namespace Gateway.Devices.Behaviors;

/// <summary>Specification for one simulated parameter (register).</summary>
public sealed record ParameterSpec(
    string Name,
    string Unit,
    double Initial,
    double Min,
    double Max,
    double Jitter,
    int Decimals);

/// <summary>
/// Models a device with a set of named parameters that drift within bounds on each
/// request (a bounded random walk). This is behavioral data generation — deliberately
/// not physics — so the UI can show many live parameter values per device.
/// </summary>
public sealed class ParameterBehavior : IDeviceBehavior, IParameterProvider
{
    private sealed class Reg
    {
        public required ParameterSpec Spec;
        public double Value;
    }

    private readonly Reg[] _regs;
    private readonly Random _rng = new();

    public ParameterBehavior(IEnumerable<ParameterSpec> specs)
        => _regs = specs.Select(s => new Reg { Spec = s, Value = s.Initial }).ToArray();

    public IReadOnlyList<DeviceParameter> Parameters =>
        _regs.Select(r => new DeviceParameter(r.Spec.Name, Math.Round(r.Value, r.Spec.Decimals), r.Spec.Unit)).ToList();

    public ValueTask<DeviceReply?> RespondAsync(DeviceRequest request, IDeviceContext context, CancellationToken ct)
    {
        // Advance each parameter by a bounded random step.
        foreach (var r in _regs)
        {
            if (r.Spec.Jitter > 0)
                r.Value += (_rng.NextDouble() * 2 - 1) * r.Spec.Jitter;
            r.Value = Math.Clamp(r.Value, r.Spec.Min, r.Spec.Max);
        }

        // Encode the snapshot as a simple payload (each parameter -> big-endian uint16, x10),
        // so the wire traffic carries the values too.
        var payload = new byte[_regs.Length * 2];
        for (var i = 0; i < _regs.Length; i++)
        {
            var raw = (ushort)Math.Clamp((int)Math.Round(_regs[i].Value * 10), 0, ushort.MaxValue);
            payload[i * 2] = (byte)(raw >> 8);
            payload[i * 2 + 1] = (byte)(raw & 0xFF);
        }

        return ValueTask.FromResult<DeviceReply?>(new DeviceReply(0, payload));
    }

    public void Reset()
    {
        foreach (var r in _regs) r.Value = r.Spec.Initial;
    }
}

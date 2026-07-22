using Gateway.Core;
using Microsoft.Extensions.Logging;

namespace Gateway.Devices;

/// <summary>
/// Concrete device that delegates all response logic to an <see cref="IDeviceBehavior"/>.
/// </summary>
public sealed class SimulatedDevice : ISimulatedDevice
{
    private readonly IDeviceBehavior _behavior;
    private readonly DeviceContext _context;

    public SimulatedDevice(DeviceIdentity identity, IDeviceBehavior behavior, IClock clock, ILogger logger)
    {
        _behavior = behavior;
        _context = new DeviceContext(identity, clock, logger);
    }

    public DeviceIdentity Identity => _context.Identity;

    public IReadOnlyList<DeviceParameter> Parameters =>
        _behavior is IParameterProvider p ? p.Parameters : Array.Empty<DeviceParameter>();

    public ValueTask<DeviceReply?> HandleAsync(DeviceRequest request, CancellationToken ct)
        => _behavior.RespondAsync(request, _context, ct);

    public void Reset()
    {
        _context.State.Clear();
        _behavior.Reset();
    }
}

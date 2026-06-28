namespace Gateway.Core;

/// <summary>
/// A named output value a device produces (e.g. "Temperature" = 21.6 °C). Real devices
/// expose many of these; the simulation models them as a live register set.
/// </summary>
public sealed record DeviceParameter(string Name, double Value, string Unit);

/// <summary>
/// Implemented by behaviors that expose a named parameter set, so the UI can show each
/// parameter's current value rather than just the raw reply payload.
/// </summary>
public interface IParameterProvider
{
    IReadOnlyList<DeviceParameter> Parameters { get; }
}

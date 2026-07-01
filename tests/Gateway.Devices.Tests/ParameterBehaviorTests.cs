using System.Net;
using System.Net.NetworkInformation;
using Gateway.Core;
using Gateway.Devices.Behaviors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gateway.Devices.Tests;

public class ParameterBehaviorTests
{
    private static readonly PeerEndpoint Peer =
        new(IPAddress.Parse("192.168.50.1"), PhysicalAddress.Parse("02-00-00-00-00-01"));

    private static IDeviceContext Context() => new DeviceContext(
        new DeviceIdentity("d", IPAddress.Parse("192.168.50.10"), PhysicalAddress.Parse("02-00-00-00-50-10")),
        SystemClock.Instance, NullLogger.Instance);

    [Fact]
    public void Exposes_named_parameters()
    {
        var sut = new ParameterBehavior(new[]
        {
            new ParameterSpec("Temperature", "°C", 21.5, 18, 26, 0.25, 1),
            new ParameterSpec("Humidity", "%", 45, 38, 55, 0.8, 0)
        });

        var names = sut.Parameters.Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "Temperature", "Humidity" }, names);
        Assert.Equal("°C", sut.Parameters[0].Unit);
    }

    [Fact]
    public async Task Values_stay_within_bounds_after_many_steps()
    {
        var sut = new ParameterBehavior(new[] { new ParameterSpec("V", "V", 230, 224, 236, 1.0, 1) });

        for (var i = 0; i < 500; i++)
            await sut.RespondAsync(new DeviceRequest(Peer, 0, ReadOnlyMemory<byte>.Empty, 0), Context(), default);

        var v = sut.Parameters[0].Value;
        Assert.InRange(v, 224, 236);
    }

    [Fact]
    public async Task Reply_payload_encodes_one_uint16_per_parameter()
    {
        var sut = new ParameterBehavior(new[]
        {
            new ParameterSpec("A", "", 1, 0, 100, 0, 0),
            new ParameterSpec("B", "", 2, 0, 100, 0, 0)
        });

        var reply = await sut.RespondAsync(new DeviceRequest(Peer, 0, ReadOnlyMemory<byte>.Empty, 0), Context(), default);

        Assert.NotNull(reply);
        Assert.Equal(4, reply!.Data.Length); // 2 parameters x 2 bytes
    }
}

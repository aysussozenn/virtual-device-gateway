using System.Net;
using System.Net.NetworkInformation;
using Gateway.Core;
using Gateway.Devices;
using Gateway.Devices.Behaviors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gateway.Devices.Tests;

public class BehaviorTests
{
    private static readonly PeerEndpoint Peer =
        new(IPAddress.Parse("192.168.50.1"), PhysicalAddress.Parse("02-00-00-00-00-01"));

    private static DeviceRequest Request(ushort command = 0, params byte[] data)
        => new(Peer, command, data, Sequence: 1);

    private static IDeviceContext Context() => new DeviceContext(
        new DeviceIdentity("d", IPAddress.Parse("192.168.50.10"), PhysicalAddress.Parse("02-00-00-00-50-10")),
        TimeProvider.System, NullLogger.Instance);

    [Fact]
    public async Task Canned_returns_mapped_reply()
    {
        var sut = new CannedBehavior(new Dictionary<ushort, DeviceReply> { [0x01] = new(0, new byte[] { 0xCA, 0xFE }) });

        var reply = await sut.RespondAsync(Request(0x01), Context(), default);

        Assert.NotNull(reply);
        Assert.Equal(new byte[] { 0xCA, 0xFE }, reply!.Data.ToArray());
    }

    [Fact]
    public async Task Canned_unmapped_command_is_silent()
    {
        var sut = new CannedBehavior(new Dictionary<ushort, DeviceReply>());
        Assert.Null(await sut.RespondAsync(Request(0x99), Context(), default));
    }

    [Fact]
    public async Task Playback_returns_sequence_then_goes_silent()
    {
        var sut = new PlaybackBehavior(new[] { new DeviceReply(0, new byte[] { 1 }), new DeviceReply(0, new byte[] { 2 }) });

        Assert.Equal(new byte[] { 1 }, (await sut.RespondAsync(Request(), Context(), default))!.Data.ToArray());
        Assert.Equal(new byte[] { 2 }, (await sut.RespondAsync(Request(), Context(), default))!.Data.ToArray());
        Assert.Null(await sut.RespondAsync(Request(), Context(), default));
    }

    [Fact]
    public async Task Playback_reset_restarts_sequence()
    {
        var sut = new PlaybackBehavior(new[] { new DeviceReply(0, new byte[] { 7 }) });
        await sut.RespondAsync(Request(), Context(), default);
        sut.Reset();
        Assert.Equal(new byte[] { 7 }, (await sut.RespondAsync(Request(), Context(), default))!.Data.ToArray());
    }

    [Fact]
    public async Task Fault_drop_with_probability_one_is_always_silent()
    {
        var inner = new CannedBehavior(new Dictionary<ushort, DeviceReply> { [0] = new(0, new byte[] { 1 }) });
        var sut = new FaultInjectingBehavior(inner, new FaultPolicy(DropProbability: 1.0));

        Assert.Null(await sut.RespondAsync(Request(), Context(), default));
    }

    [Fact]
    public async Task SimulatedDevice_delegates_to_behavior()
    {
        var identity = new DeviceIdentity("d", IPAddress.Parse("192.168.50.10"), PhysicalAddress.Parse("02-00-00-00-50-10"));
        var behavior = new CannedBehavior(new Dictionary<ushort, DeviceReply> { [0] = new(0, new byte[] { 0xAB }) });
        var device = new SimulatedDevice(identity, behavior, TimeProvider.System, NullLogger.Instance);

        var reply = await device.HandleAsync(Request(), default);

        Assert.Equal(new byte[] { 0xAB }, reply!.Data.ToArray());
    }
}

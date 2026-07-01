using System.Net;
using System.Net.NetworkInformation;
using Gateway.Core;
using Gateway.Devices;
using Gateway.Devices.Behaviors;
using Gateway.Ethernet;
using Gateway.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;

namespace Gateway.Devices.Tests;

/// <summary>
/// Drives the full pipeline (decode → route → device → encode → send) over an in-memory
/// Ethernet bus, deterministically and without Npcap. This is the reliable round-trip
/// proof; live Npcap on a real adapter is a thin layer over the same engine.
/// </summary>
public class EngineRoundTripTests
{
    private static GatewayEngine BuildEngine(InMemoryTransport port, params ISimulatedDevice[] devices)
    {
        var registry = new DeviceRegistry();
        foreach (var d in devices) registry.Register(d);
        return new GatewayEngine(port, registry, new PassthroughProtocolCodec(),
            new EthernetGatewayOptions(), NullLogger<GatewayEngine>.Instance);
    }

    private static SimulatedDevice CannedDevice(string ip, string mac, byte[] reply)
    {
        var identity = new DeviceIdentity("dut", IPAddress.Parse(ip), PhysicalAddress.Parse(mac));
        var behavior = new CannedBehavior(new Dictionary<ushort, DeviceReply> { [0] = new(0, reply) });
        return new SimulatedDevice(identity, behavior, SystemClock.Instance, NullLogger.Instance);
    }

    private static byte[] BuildEthernetIpv4(string srcIp, string dstIp, string srcMac, string dstMac, byte[] payload)
    {
        var ip = new IPv4Packet(IPAddress.Parse(srcIp), IPAddress.Parse(dstIp))
        {
            Protocol = ProtocolType.Udp, TimeToLive = 64, PayloadData = payload
        };
        ip.UpdateIPChecksum();
        ip.UpdateCalculatedValues();
        return LinkEncap.WrapIpv4(LinkLayers.Ethernet, ip, PhysicalAddress.Parse(srcMac), PhysicalAddress.Parse(dstMac));
    }

    [Fact]
    public async Task Request_is_routed_to_device_and_reply_returns_to_sender()
    {
        var bus = new InMemoryBus(LinkLayers.Ethernet);
        var gatewayPort = bus.CreatePort();
        var appB = bus.CreatePort();

        await using var engine = BuildEngine(gatewayPort,
            CannedDevice("192.168.50.10", "02-00-00-00-50-10", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));

        var replySeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        appB.PacketReceived += (_, e) =>
        {
            var ip = LinkEncap.UnwrapIpv4(appB.LinkType, e.Data, out EthernetPacket? _, out ArpPacket? _);
            if (ip is not null) replySeen.TrySetResult(e.Data);
        };
        appB.Start();
        engine.Start();

        appB.Send(BuildEthernetIpv4("192.168.50.1", "192.168.50.10",
            "02-00-00-00-00-01", "02-00-00-00-50-10", new byte[] { 1, 2, 3 }));

        var done = await Task.WhenAny(replySeen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(done == replySeen.Task, "No reply observed within timeout.");

        var replyFrame = await replySeen.Task;
        var reply = LinkEncap.UnwrapIpv4(LinkLayers.Ethernet, replyFrame, out _, out _)!;
        Assert.Equal(IPAddress.Parse("192.168.50.10"), reply.SourceAddress);
        Assert.Equal(IPAddress.Parse("192.168.50.1"), reply.DestinationAddress);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, LinkEncap.IpPayload(LinkLayers.Ethernet, replyFrame));
    }

    [Fact]
    public async Task Arp_request_for_device_is_answered_with_device_mac()
    {
        var bus = new InMemoryBus(LinkLayers.Ethernet);
        var gatewayPort = bus.CreatePort();
        var appB = bus.CreatePort();

        await using var engine = BuildEngine(gatewayPort,
            CannedDevice("192.168.50.10", "02-00-00-00-50-10", new byte[] { 0x01 }));

        var arpReply = new TaskCompletionSource<ArpPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        appB.PacketReceived += (_, e) =>
        {
            var packet = Packet.ParsePacket(LinkLayers.Ethernet, e.Data);
            if (packet.Extract<ArpPacket>() is { Operation: ArpOperation.Response } arp)
                arpReply.TrySetResult(arp);
        };
        appB.Start();
        engine.Start();

        var appBMac = PhysicalAddress.Parse("02-00-00-00-00-01");
        var request = new ArpPacket(ArpOperation.Request,
            targetHardwareAddress: PhysicalAddress.Parse("00-00-00-00-00-00"),
            targetProtocolAddress: IPAddress.Parse("192.168.50.10"),
            senderHardwareAddress: appBMac,
            senderProtocolAddress: IPAddress.Parse("192.168.50.1"));
        var frame = new EthernetPacket(appBMac, PhysicalAddress.Parse("FF-FF-FF-FF-FF-FF"), EthernetType.Arp)
        {
            PayloadPacket = request
        };
        appB.Send(frame.Bytes);

        var done = await Task.WhenAny(arpReply.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(done == arpReply.Task, "No ARP reply observed within timeout.");

        var reply = await arpReply.Task;
        Assert.Equal(PhysicalAddress.Parse("02-00-00-00-50-10"), reply.SenderHardwareAddress);
        Assert.Equal(IPAddress.Parse("192.168.50.10"), reply.SenderProtocolAddress);
    }
}

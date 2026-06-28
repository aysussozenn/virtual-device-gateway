using System.Net;
using System.Net.NetworkInformation;
using Gateway.Icd;
using Gateway.Peers;

namespace Gateway.Devices.Tests;

public class PeerChannelTests
{
    private static IcdSpec AbcSpec() => new()
    {
        Device = "abc",
        Messages = new[]
        {
            new IcdMessage("STRUCT_ABC", 0x0010, Direction.Inbound, new[]
            {
                new IcdField("a", FieldTypes.U16),
                new IcdField("b", FieldTypes.I16),
            }),
            new IcdMessage("ACK_ABC", 0x0090, Direction.Outbound, new[]
            {
                new IcdField("a", FieldTypes.U16),
                new IcdField("status", FieldTypes.U8),
            }),
        }
    };

    [Fact]
    public async Task Demo_echo_reflects_same_named_field_back_to_peer()
    {
        var spec = AbcSpec();
        var desc = new PeerDescriptor("abc", spec,
            IPAddress.Parse("192.168.50.60"), PhysicalAddress.Parse("02-00-00-00-50-60"));
        await using var session = PeerSession.CreateDemo(new[] { desc },
            IPAddress.Parse("192.168.50.1"), PhysicalAddress.Parse("02-00-00-00-00-01"));

        DecodedMessage? got = null;
        var channel = session.Channels[0];
        channel.MessageDecoded += (m, _) => got = m;
        session.Start();

        // Peer pushes STRUCT_ABC; the in-memory echo DUT replies with ACK_ABC copying 'a'.
        channel.Send(spec.Messages[0], new Dictionary<string, double> { ["a"] = 1234, ["b"] = -5 }, 1);

        Assert.NotNull(got);
        Assert.Equal("ACK_ABC", got!.Name);
        Assert.Equal(1234, got["a"]);
    }

    [Fact]
    public void Custom_q8_8_field_type_roundtrips_through_codec()
    {
        // A type registered from outside Gateway.Icd is honored by the codec end to end.
        var registry = FieldTypeRegistry.CreateDefault();
        registry.Register(new FixedPointFieldType());
        var q88 = registry.Find("q8_8");
        Assert.NotNull(q88);

        var spec = new IcdSpec
        {
            Device = "x",
            Messages = new[]
            {
                new IcdMessage("M", 0x0001, Direction.Inbound, new[] { new IcdField("gain", q88!, "x", 0, 20) })
            }
        };
        var codec = new IcdCodec(spec);

        var frame = codec.Encode(spec.Messages[0], 1, new Dictionary<string, double> { ["gain"] = 1.5 });
        var dec = codec.TryDecode(frame);

        Assert.Equal(1.5, dec.Message!["gain"], 3);   // value survives, even though the wire holds 0x0180
    }

    [Fact]
    public async Task Peer_does_not_decode_its_own_outbound()
    {
        var spec = AbcSpec();
        var desc = new PeerDescriptor("abc", spec,
            IPAddress.Parse("192.168.50.60"), PhysicalAddress.Parse("02-00-00-00-50-60"));
        // Live-style session has no echo DUT, so the only inbound a peer could see is its own send.
        var bus = new Gateway.Ethernet.InMemoryBus(PacketDotNet.LinkLayers.Ethernet);
        var port = bus.CreatePort();
        var channel = new PeerChannel("abc", spec, port,
            desc.Ip, desc.Mac, IPAddress.Parse("192.168.50.1"), PhysicalAddress.Parse("02-00-00-00-00-01"));

        var decoded = 0;
        channel.MessageDecoded += (_, _) => decoded++;
        port.Start();
        channel.Send(spec.Messages[0], new Dictionary<string, double> { ["a"] = 7 }, 1);

        Assert.Equal(0, decoded);
        channel.Dispose();
        await port.DisposeAsync();
    }
}

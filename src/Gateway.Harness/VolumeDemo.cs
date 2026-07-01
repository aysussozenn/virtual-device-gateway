using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;
using Gateway.Peers;
using PacketDotNet;

namespace Gateway.Harness;

/// <summary>
/// The volume round-trip: A (DUT) sends SET_VOLUME to B; B (emulated peer) applies it, clamps to
/// 0..100, and reports the value back in VOLUME_ACK — so A sees its OWN value come back (and learns
/// when B had to adjust it). This is the "peer reflects/updates a value the DUT sent" pattern, run
/// automatically by a tiny reflect handler on B's channel. In-memory bus, no npcap. Command: <c>volume</c>.
/// </summary>
public static class VolumeDemo
{
    public static Task<int> RunAsync()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "examples", "volume");
        var spec = IcdLoader.LoadSpec(Path.Combine(dir, "spk.icd.json"));

        var dutIp = IPAddress.Parse("192.168.50.1");
        var dutMac = PhysicalAddress.Parse("02-00-00-00-00-01");
        var bIp = IPAddress.Parse("192.168.50.70");
        var bMac = PhysicalAddress.Parse("02-00-00-00-50-70");

        var bus = new InMemoryBus(LinkLayers.Ethernet);
        var bPort = bus.CreatePort();
        var aPort = bus.CreatePort();

        // B = emulated speaker peer.
        using var bChan = new PeerChannel("b", spec, bPort, bIp, bMac, dutIp, dutMac);
        var setVolume = spec.Messages.First(m => m.Name == "SET_VOLUME");
        var volumeAck = spec.Messages.First(m => m.Name == "VOLUME_ACK");

        // B's reflect-and-update behavior: take the volume A sent, clamp it, and send it back.
        bChan.MessageDecoded += (msg, _) =>
        {
            if (msg.Name != "SET_VOLUME") return;
            var asked = msg.Fields["volume"];
            var applied = Math.Clamp(asked, 0, 100);
            var muted = applied == 0 ? 1.0 : 0.0;
            var note = applied != asked ? $"  (clamped from {asked})" : "";
            Console.WriteLine($"  B  received volume={asked} -> applies {applied}{note}, acks back");
            bChan.Send(volumeAck, new Dictionary<string, double> { ["volume"] = applied, ["muted"] = muted }, 1);
        };

        // A = the DUT. It sends SET_VOLUME and prints the VOLUME_ACK it gets back.
        var codec = CodecRegistry.Default.Build(spec);
        aPort.PacketReceived += (_, e) =>
        {
            var ip = LinkEncap.UnwrapIpv4(aPort.LinkType, e.Data, out EthernetPacket? _, out ArpPacket? _);
            if (ip is null || !ip.DestinationAddress.Equals(dutIp)) return;
            var dec = codec.TryDecode(LinkEncap.IpPayload(aPort.LinkType, e.Data)).Message;
            if (dec is null || dec.Name != "VOLUME_ACK") return;
            var muted = dec.Fields["muted"] == 1 ? " (MUTED)" : "";
            Console.WriteLine($"  A  <- VOLUME_ACK volume={dec.Fields["volume"]}{muted}   <-- A sees its own value\n");
        };

        bPort.Start();
        aPort.Start();

        Console.WriteLine("Volume round-trip: A sets a volume, B applies+clamps it and reports it back.\n");
        foreach (var v in new double[] { 30, 0, 130 })
        {
            Console.WriteLine($"  A  -> SET_VOLUME volume={v}");
            var payload = codec.Encode(setVolume, 1, new Dictionary<string, double> { ["volume"] = v });
            var ip = new IPv4Packet(dutIp, bIp) { Protocol = ProtocolType.Udp, TimeToLive = 64, PayloadData = payload };
            ip.UpdateIPChecksum();
            ip.UpdateCalculatedValues();
            aPort.Send(LinkEncap.WrapIpv4(aPort.LinkType, ip, dutMac, bMac));
        }

        Console.WriteLine("Done. A asked for 30 -> saw 30; asked for 0 -> saw 0 (muted); asked for 130 -> saw 100 (clamped).");
        return Task.FromResult(0);
    }
}

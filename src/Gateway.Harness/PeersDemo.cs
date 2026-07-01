using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;
using Gateway.Peers;
using PacketDotNet;

namespace Gateway.Harness;

/// <summary>
/// Shows the TWO ways an emulated peer (B, C) sends values back to the DUT (A), so both are
/// easy to see side by side. Runs entirely on an in-memory bus — no npcap, no real adapter.
///
///   PART 1 — request/reply : A sends CMD_*, the peer COMPUTES a reply and returns DATA_*.
///   PART 2 — autonomous push: each peer pushes DATA_* on its OWN period (different rates),
///                             without the DUT asking.
///
/// The peers reuse the examples/multi ICDs, so the same message definitions drive both paths.
/// Command: <c>peers</c>.
/// </summary>
public static class PeersDemo
{
    private static readonly object Sync = new();
    private static long _t0;
    private static int _seq = 1;

    public static async Task<int> RunAsync()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "examples", "multi");
        var bSpec = IcdLoader.LoadSpec(Path.Combine(dir, "b.icd.json"));
        var cSpec = IcdLoader.LoadSpec(Path.Combine(dir, "c.icd.json"));

        var dutIp = IPAddress.Parse("192.168.50.1");
        var dutMac = PhysicalAddress.Parse("02-00-00-00-00-01");
        var bIp = IPAddress.Parse("192.168.50.70"); var bMac = PhysicalAddress.Parse("02-00-00-00-50-70");
        var cIp = IPAddress.Parse("192.168.50.80"); var cMac = PhysicalAddress.Parse("02-00-00-00-50-80");

        var bus = new InMemoryBus(LinkLayers.Ethernet);

        // The two emulated peers. Each PeerChannel only reacts to frames addressed to its own IP.
        var bPort = bus.CreatePort();
        var cPort = bus.CreatePort();
        using var bChan = new PeerChannel("b", bSpec, bPort, bIp, bMac, dutIp, dutMac);
        using var cChan = new PeerChannel("c", cSpec, cPort, cIp, cMac, dutIp, dutMac);

        // The DUT (A): its own port. It sends CMD_* and prints every DATA_* it receives.
        var aPort = bus.CreatePort();
        var bCodec = CodecRegistry.Default.Build(bSpec);
        var cCodec = CodecRegistry.Default.Build(cSpec);
        aPort.PacketReceived += (_, e) =>
        {
            var ip = LinkEncap.UnwrapIpv4(aPort.LinkType, e.Data, out EthernetPacket? _, out ArpPacket? _);
            if (ip is null || !ip.DestinationAddress.Equals(dutIp)) return;
            var payload = LinkEncap.IpPayload(aPort.LinkType, e.Data);
            var dec = bCodec.TryDecode(payload).Message ?? cCodec.TryDecode(payload).Message;
            if (dec is null) return;
            Log($"  A  <- {dec.Name,-7} @ +{Environment.TickCount64 - _t0,4}ms   {Fields(dec)}");
        };

        var cmdB = bSpec.Messages.First(m => m.Name == "CMD_B");
        var dataB = bSpec.Messages.First(m => m.Name == "DATA_B");
        var cmdC = cSpec.Messages.First(m => m.Name == "CMD_C");
        var dataC = cSpec.Messages.First(m => m.Name == "DATA_C");

        // PART 1 wiring: each peer computes its reply from the command it received.
        // B is an actuator that saturates the setpoint to its physical limit (+/-300) and
        // flags ERR when the DUT asked for more than it can deliver.
        bChan.MessageDecoded += (msg, _) =>
        {
            if (msg.Name != "CMD_B") return;
            var sp = msg.Fields["setpoint"];
            var achieved = Math.Clamp(sp, -300, 300);
            var status = Math.Abs(sp) > 300 ? 1.0 : 0.0;       // 0=OK, 1=ERR
            Log($"  B  got CMD_B setpoint={sp}  -> computes value={achieved} status={(status == 1 ? "ERR" : "OK")}");
            bChan.Send(dataB, new Dictionary<string, double> { ["value"] = achieved, ["status"] = status }, NextSeq());
        };
        // C is a heater: mode ON -> 60 C, OFF -> ambient 20 C.
        cChan.MessageDecoded += (msg, _) =>
        {
            if (msg.Name != "CMD_C") return;
            var mode = msg.Fields["mode"];
            var temp = mode >= 1 ? 60.0 : 20.0;
            Log($"  C  got CMD_C mode={(mode >= 1 ? "ON" : "OFF")}  -> computes temp={temp} status=OK");
            cChan.Send(dataC, new Dictionary<string, double> { ["temp"] = temp, ["status"] = 0 }, NextSeq());
        };

        bPort.Start(); cPort.Start(); aPort.Start();

        // ---------------- PART 1: request/reply with a computed value ----------------
        Console.WriteLine("PART 1 — request/reply: A commands a peer, the peer returns a COMPUTED value\n");
        _t0 = Environment.TickCount64;
        ASend(aPort, dutIp, dutMac, bIp, bMac, bCodec, cmdB, new() { ["setpoint"] = 250 });  // within limit
        ASend(aPort, dutIp, dutMac, bIp, bMac, bCodec, cmdB, new() { ["setpoint"] = 400 });  // exceeds +/-300 -> ERR
        ASend(aPort, dutIp, dutMac, cIp, cMac, cCodec, cmdC, new() { ["mode"] = 1 });         // heater ON
        ASend(aPort, dutIp, dutMac, cIp, cMac, cCodec, cmdC, new() { ["mode"] = 0 });         // heater OFF

        // ---------------- PART 2: autonomous push at different periods ----------------
        Console.WriteLine("\nPART 2 — autonomous push: B every 100ms, C every 250ms (no command from A)\n");
        _t0 = Environment.TickCount64;
        var end = _t0 + 1000;

        var bLoop = Task.Run(async () =>
        {
            double v = 100;
            while (Environment.TickCount64 < end)
            {
                bChan.Send(dataB, new Dictionary<string, double> { ["value"] = v, ["status"] = 0 }, NextSeq());
                v += 10;
                await Task.Delay(100);
            }
        });
        var cLoop = Task.Run(async () =>
        {
            double t = 20;
            while (Environment.TickCount64 < end)
            {
                cChan.Send(dataC, new Dictionary<string, double> { ["temp"] = t, ["status"] = 0 }, NextSeq());
                t += 2;
                await Task.Delay(250);
            }
        });
        await Task.WhenAll(bLoop, cLoop);

        Console.WriteLine("\nDone. B answered A on request AND pushed on its own; C did the same on a slower period.");
        return 0;
    }

    /// <summary>A (the DUT) encodes a command and puts it on the wire, addressed to one peer.</summary>
    private static void ASend(IPacketTransport port, IPAddress srcIp, PhysicalAddress srcMac,
        IPAddress dstIp, PhysicalAddress dstMac, IFrameCodec codec, IcdMessage msg, Dictionary<string, double> fields)
    {
        var payload = codec.Encode(msg, NextSeq(), fields);
        var ip = new IPv4Packet(srcIp, dstIp) { Protocol = ProtocolType.Udp, TimeToLive = 64, PayloadData = payload };
        ip.UpdateIPChecksum();
        ip.UpdateCalculatedValues();
        Log($"  A  -> {msg.Name,-7}          {string.Join(" ", fields.Select(kv => $"{kv.Key}={kv.Value}"))}");
        port.Send(LinkEncap.WrapIpv4(port.LinkType, ip, srcMac, dstMac));
    }

    private static ushort NextSeq() => (ushort)System.Threading.Interlocked.Increment(ref _seq);

    private static string Fields(DecodedMessage m) => string.Join(" ", m.Fields.Select(kv => $"{kv.Key}={kv.Value}"));

    private static void Log(string line) { lock (Sync) Console.WriteLine(line); }
}

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;
using PacketDotNet;

namespace Gateway.Harness;

/// <summary>
/// The final piece: choreography over real asynchronous traffic. Emulated peers and a
/// reactive DUT stand-in run on a shared in-memory bus; nav pushes NAV_STATE, the DUT
/// reacts (after a small processing delay) by sending SetElevator, and a promiscuous
/// <see cref="SystemConformanceTap"/> observes every frame on the wire. Per-frame
/// conformance, cross-peer choreography, and topology coverage are all evaluated over the
/// resulting live trace — nothing is pre-scripted by timestamp. The reactive DUT is the
/// only stand-in; swapping in the developer's real code leaves the pipeline unchanged.
/// </summary>
public static class LiveChoreographyDemo
{
    public const string Scenario = "elevator-live-choreo";

    public static async Task<VerifyOutcome> ExecuteAsync(string? topologyPath, bool print)
    {
        void Log(string s = "") { if (print) Console.WriteLine(s); }
        var path = topologyPath ?? FileConformanceDemo.DefaultTopologyPath;

        SystemTopology topo;
        IcdSpec nav, surf, logsvc;
        try
        {
            topo = IcdLoader.LoadTopology(path);
            nav = IcdLoader.LoadSpec(topo.Find("nav")!.IcdPath);
            surf = IcdLoader.LoadSpec(topo.Find("surf")!.IcdPath);
            logsvc = IcdLoader.LoadSpec(topo.Find("logsvc")!.IcdPath);
        }
        catch (IcdLoadException ex) { Console.WriteLine($"Load error: {ex.Message}"); return new VerifyOutcome(null, 2, Scenario); }

        Log($"Loaded topology '{path}'  dut={topo.Dut}");
        Log($"  emulated peers: {string.Join(", ", topo.Neighbours().Select(p => p.Id))}\n");

        var recorder = new ConformanceRecorder();
        var tap = new SystemConformanceTap(new[] { nav, surf, logsvc }, recorder);
        var clock = Stopwatch.StartNew();

        var bus = new InMemoryBus(LinkLayers.Ethernet);
        var navPort = bus.CreatePort();
        var dutPort = bus.CreatePort();
        var tapPort = bus.CreatePort();

        var navIp = IPAddress.Parse(topo.Find("nav")!.Ip!);
        var navMac = Mac(topo.Find("nav")!.Mac!);
        var surfIp = IPAddress.Parse(topo.Find("surf")!.Ip!);
        var surfMac = Mac(topo.Find("surf")!.Mac!);
        var dutIp = IPAddress.Parse("192.168.50.1");
        var dutMac = Mac("02-00-00-00-00-01");

        var navCodec = new IcdCodec(nav);
        var surfCodec = new IcdCodec(surf);
        var navState = nav.Messages.First(m => m.Name == "NAV_STATE");
        var setElevator = surf.Messages.First(m => m.Name == "SetElevator");

        // Promiscuous tap: validate every IP frame on the bus against the system's ICDs.
        tapPort.PacketReceived += (_, e) =>
        {
            if (LinkEncap.UnwrapIpv4(tapPort.LinkType, e.Data, out EthernetPacket? _, out ArpPacket? _) is null) return;
            var payload = LinkEncap.IpPayload(tapPort.LinkType, e.Data);
            if (payload.Length == 0) return;
            var t = (long)clock.Elapsed.TotalMilliseconds;
            var name = tap.Observe(payload, t);
            if (name is not null) Log($"  t={t,4}ms  observed {name}");
        };

        // Reactive DUT stand-in: on NAV_STATE, after a small delay send SetElevator to surf.
        // Buggy on purpose: no saturation, and it drops the response when pitch == 50.
        var dutSeq = (ushort)100;
        dutPort.PacketReceived += (_, e) =>
        {
            if (LinkEncap.UnwrapIpv4(dutPort.LinkType, e.Data, out EthernetPacket? _, out ArpPacket? _) is null) return;
            var dec = navCodec.TryDecode(LinkEncap.IpPayload(dutPort.LinkType, e.Data));
            if (dec.Message is not { } m || m.Name != "NAV_STATE") return;
            var pitch = m["pitch"];
            if (pitch == 50) return; // dropped reaction -> choreography timeout

            _ = Task.Run(async () =>
            {
                await Task.Delay(3);
                var deflection = (short)(-pitch); // no clamp
                var frame = surfCodec.Encode(setElevator, dutSeq++, new Dictionary<string, double> { ["deflection"] = deflection });
                dutPort.Send(LinkEncap.WrapIpv4(dutPort.LinkType, BuildIp(dutIp, surfIp, frame), dutMac, surfMac));
            });
        };

        navPort.Start();
        dutPort.Start();
        tapPort.Start();

        Log("Live traffic:");
        var navSeq = (ushort)1;
        foreach (var pitch in new short[] { 120, 340, 50 })
        {
            var frame = navCodec.Encode(navState, navSeq++, new Dictionary<string, double> { ["heading"] = 900, ["pitch"] = pitch });
            navPort.Send(LinkEncap.WrapIpv4(navPort.LinkType, BuildIp(navIp, dutIp, frame), navMac, dutMac));
            await Task.Delay(40);
        }
        await Task.Delay(120); // let reactions drain

        // Same checks as the deterministic path, now over the LIVE cross-peer trace.
        var monitor = new ResponseMonitor("NAV_STATE", "SetElevator", 30,
            (trig, resp) => resp["deflection"] == Math.Clamp(-trig["pitch"], -300, 300),
            "deflection == clamp(-pitch, +/-300) within 30ms");
        recorder.AddRange(MonitorEngine.Evaluate(new[] { monitor }, tap.Trace));
        recorder.AddRange(TopologyValidator.Check(topo, tap.Trace));

        Log();
        Log(recorder.Report(Scenario));
        return new VerifyOutcome(recorder, 0, Scenario);
    }

    private static IPv4Packet BuildIp(IPAddress src, IPAddress dst, byte[] payload)
        => LinkEncap.BuildUdpIp(src, dst, payload);

    private static PhysicalAddress Mac(string s) => PhysicalAddress.Parse(s.Replace(":", "-").ToUpperInvariant());
}

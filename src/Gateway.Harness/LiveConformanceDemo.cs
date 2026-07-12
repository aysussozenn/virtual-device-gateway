using System.Net;
using System.Net.NetworkInformation;
using Gateway.Configuration;
using Gateway.Core;
using Gateway.Ethernet;
using Gateway.Icd;
using Gateway.Protocol;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;

namespace Gateway.Harness;

/// <summary>
/// Phase-4 demonstration: the same elevator conformance, now sourced from <em>real frames</em>
/// flowing through a live <see cref="GatewayEngine"/> over the in-memory Ethernet bus — the
/// stub byte-producer is gone. A DUT stand-in injects SetElevator frames as IP payloads; the
/// engine routes them to the managed <c>surf</c> device; <see cref="LiveConformanceMonitor"/>
/// taps the frame observer and validates each one against the ICD as it arrives.
/// </summary>
public static class LiveConformanceDemo
{
    public static async Task<int> RunAsync()
    {
        // Quiet logger so the engine's traffic logs don't drown the conformance output.
        using var quiet = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        var bus = new InMemoryBus(LinkLayers.Ethernet);
        var gatewayPort = bus.CreatePort();
        var dutPort = bus.CreatePort();

        var surfIp = IPAddress.Parse("192.168.50.30");
        var surfMac = PhysicalAddress.Parse("02-00-00-00-50-30");
        var dutIp = IPAddress.Parse("192.168.50.1");
        var dutMac = PhysicalAddress.Parse("02-00-00-00-00-01");

        // surf is a managed device that ACKs; the inbound SetElevator frames are what we verify.
        var config = new GatewayConfig
        {
            Devices =
            {
                new DeviceConfig
                {
                    Id = "surf", Ip = surfIp.ToString(), Mac = "02-00-00-00-50-30",
                    Behavior = new BehaviorConfig { Type = "canned", Map = new() { ["0x00"] = "90 00" } }
                }
            }
        };

        var registry = DeviceFactory.BuildRegistry(config, SystemClock.Instance, quiet);
        await using var engine = new GatewayEngine(gatewayPort, registry, new PassthroughProtocolCodec(),
            new EthernetGatewayOptions(), quiet.CreateLogger<GatewayEngine>());

        var recorder = new ConformanceRecorder();
        var monitor = new LiveConformanceMonitor(ElevatorScenario.Surf, recorder);
        engine.FrameObserved += monitor.OnFrame;
        engine.Start();
        dutPort.Start();

        Console.WriteLine("Scenario: elevator-live  (real frames over in-memory Ethernet bus -> GatewayEngine -> ICD check)\n");
        Console.WriteLine("Live verdicts:");

        var codec = new IcdCodec(ElevatorScenario.Surf);
        var setElevator = ElevatorScenario.Surf.Messages[0];
        short[] deflections = { -120, -340, 0 }; // good, out-of-range, good
        ushort seq = 1;
        foreach (var deflection in deflections)
        {
            var payload = codec.Encode(setElevator, seq++,
                new Dictionary<string, double> { ["deflection"] = deflection });
            var ip = LinkEncap.BuildUdpIp(dutIp, surfIp, payload);
            dutPort.Send(LinkEncap.WrapIpv4(dutPort.LinkType, ip, dutMac, surfMac));
            await Task.Delay(40);
        }
        await Task.Delay(200); // let the worker drain the queue

        Console.WriteLine();
        Console.WriteLine(recorder.Report("elevator-live"));
        return recorder.AllPassed ? 0 : 1;
    }
}

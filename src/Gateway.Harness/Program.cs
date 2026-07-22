using System.Net;
using System.Net.NetworkInformation;
using Gateway.Configuration;
using Gateway.Core;
using Gateway.Ethernet;
using Gateway.Harness;
using Gateway.Protocol;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;

// Virtual Device Gateway — console harness (Phase 0+1).
//   list                 -> enumerate capture adapters
//   run [gateway.json]   -> start the gateway with the configured devices
//   selftest             -> inject a request on the loopback adapter and confirm a reply

using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
var log = loggerFactory.CreateLogger("Harness");

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

try
{
    switch (command)
    {
        case "list":
            ListAdapters();
            break;
        case "run":
            await RunAsync(args.Length > 1 ? args[1] : "gateway.json");
            break;
        case "selftest":
            await SelfTestAsync();
            break;
        case "verify":
            Environment.ExitCode = await VerifyCommand.Run(args);
            break;
        case "conformance":
            var which = args.Length > 1 ? args[1].ToLowerInvariant() : "elevator";
            Environment.ExitCode = which switch
            {
                "engine" => EngineScenario.Run(),
                "live" => await LiveConformanceDemo.RunAsync(),
                "file" => FileConformanceDemo.Run(args.Length > 2 ? args[2] : null),
                _ => ElevatorScenario.Run()
            };
            break;
        case "demo":
            await DemoAsync(args.Length > 1 ? args[1] : "gateway.json");
            break;
        default:
            Console.WriteLine("Usage: Gateway.Harness [list|run <config>|selftest|conformance|verify [--system <p>] [--scenario <p>] [--json <p>] [--junit <p>] [--strict] [--live]]");
            break;
    }
}
catch (Exception ex)
{
    log.LogError(ex, "Fatal error.");
    Environment.ExitCode = 1;
}

void ListAdapters()
{
    var devices = AdapterDiscovery.List();
    Console.WriteLine($"Found {devices.Count} capture adapter(s):\n");
    for (var i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        Console.WriteLine($"  [{i}] {d.Description ?? "(no description)"}");
        Console.WriteLine($"      name: {d.Name}");
    }
    var loop = AdapterDiscovery.FindLoopback();
    Console.WriteLine(loop is null
        ? "\nNo loopback adapter detected (install Npcap with loopback support)."
        : $"\nLoopback adapter: {loop.Description}");
}

GatewayEngine BuildEngine(GatewayConfig config, IPacketTransport transport)
{
    var registry = DeviceFactory.BuildRegistry(config, SystemClock.Instance, loggerFactory);
    return new GatewayEngine(transport, registry, new PassthroughProtocolCodec(),
        new EthernetGatewayOptions(), loggerFactory.CreateLogger<GatewayEngine>());
}

async Task RunAsync(string configPath)
{
    var config = DeviceFactory.Load(configPath);
    var adapter = string.IsNullOrWhiteSpace(config.Adapter)
        ? AdapterDiscovery.FindLoopback() ?? throw new InvalidOperationException("No loopback adapter found.")
        : AdapterDiscovery.FindByName(config.Adapter!) ?? throw new InvalidOperationException($"Adapter '{config.Adapter}' not found.");

    await using var engine = BuildEngine(config, new PcapTransport(adapter));
    engine.Start();
    Console.WriteLine($"Gateway running on '{adapter.Description ?? adapter.Name}'. Press Ctrl+C to stop.");

    var tcs = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
    await tcs.Task;
}

// Mirrors the UI "demo mode": engine on an in-memory bus + App B generator. Prints
// every observed frame so we can confirm live traffic flows (console-logged).
async Task DemoAsync(string configPath)
{
    var config = DeviceFactory.Load(configPath);
    var bus = new InMemoryBus(LinkLayers.Ethernet);
    var gatewayPort = bus.CreatePort();
    await using var engine = BuildEngine(config, gatewayPort);

    var frames = 0;
    engine.FrameObserved += (_, e) =>
    {
        Interlocked.Increment(ref frames);
        Console.WriteLine($"  [{e.Direction}] {e.Kind} {e.Summary}");
    };
    engine.Start();

    var simulator = new AppBSimulator(
        bus.CreatePort(),
        config.Devices.Select(d => new AppBSimulator.Target(
            IPAddress.Parse(d.Ip),
            PhysicalAddress.Parse(d.Mac.Replace(":", "-").ToUpperInvariant()))).ToList(),
        IPAddress.Parse("192.168.50.1"),
        PhysicalAddress.Parse("02-00-00-00-00-01"),
        TimeSpan.FromSeconds(1));
    simulator.Start();

    Console.WriteLine($"Demo running for ~3.5s with {config.Devices.Count} device(s)...");
    await Task.Delay(3500);
    await simulator.DisposeAsync();
    Console.WriteLine($"\nTotal frames observed: {frames}");
}

// End-to-end proof over an in-memory Ethernet bus (no Npcap / no OS loopback needed):
// "App B" and the gateway share a bus; App B injects a request and must see the reply.
async Task SelfTestAsync()
{
    var bus = new InMemoryBus(LinkLayers.Ethernet);
    var gatewayPort = bus.CreatePort();
    var appB = bus.CreatePort();

    var deviceConfig = new DeviceConfig
    {
        Id = "selftest-echo",
        Ip = "192.168.50.10",
        Mac = "02-00-00-00-50-10",
        Behavior = new BehaviorConfig { Type = "canned", Map = new() { ["0x00"] = "DE AD BE EF" } }
    };
    var config = new GatewayConfig { Devices = { deviceConfig } };

    await using var engine = BuildEngine(config, gatewayPort);

    var replySeen = new TaskCompletionSource<byte[]>();
    appB.PacketReceived += (_, e) =>
    {
        var reply = LinkEncap.UnwrapIpv4(appB.LinkType, e.Data, out EthernetPacket? _, out ArpPacket? _);
        if (reply is not null) replySeen.TrySetResult(e.Data);
    };
    appB.Start();
    engine.Start();

    var appBMac = PhysicalAddress.Parse("02-00-00-00-00-01");
    var deviceMac = PhysicalAddress.Parse("02-00-00-00-50-10");
    var request = LinkEncap.BuildUdpIp(
        IPAddress.Parse("192.168.50.1"), IPAddress.Parse("192.168.50.10"),
        new byte[] { 0x01, 0x02, 0x03 });

    Console.WriteLine("Injecting 192.168.50.1 -> 192.168.50.10 over in-memory Ethernet bus ...");
    appB.Send(LinkEncap.WrapIpv4(appB.LinkType, request, appBMac, deviceMac));

    var completed = await Task.WhenAny(replySeen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
    if (completed == replySeen.Task)
    {
        var replyFrame = replySeen.Task.Result;
        var reply = LinkEncap.UnwrapIpv4(appB.LinkType, replyFrame, out _, out _)!;
        Console.WriteLine($"\nPASS — reply {reply.SourceAddress} -> {reply.DestinationAddress}, payload {Convert.ToHexString(LinkEncap.IpPayload(appB.LinkType, replyFrame))}");
    }
    else
    {
        Console.WriteLine("\nFAIL — no reply observed within 5s.");
        Environment.ExitCode = 2;
    }
}

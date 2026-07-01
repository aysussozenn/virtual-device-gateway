using System.Net;
using Gateway.Configuration;
using Gateway.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gateway.Devices.Tests;

public class ConfigLoadTests
{
    private const string Json = """
        {
          "adapter": null,
          "devices": [
            { "id": "echo", "ip": "192.168.50.10", "mac": "02-00-00-00-50-10",
              "behavior": { "type": "canned", "map": { "0x00": "DE AD BE EF" } } },
            { "id": "sensor", "ip": "192.168.50.11", "mac": "02-00-00-00-50-11",
              "behavior": { "type": "playback", "sequence": [ "00 64", "00 65" ], "loop": true } }
          ]
        }
        """;

    [Fact]
    public void Load_parses_camelcase_json_into_devices()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Json);
            var config = DeviceFactory.Load(path);

            Assert.Equal(2, config.Devices.Count);
            Assert.Equal("echo", config.Devices[0].Id);
            Assert.Equal("192.168.50.10", config.Devices[0].Ip);
            Assert.Equal("canned", config.Devices[0].Behavior.Type);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BuildRegistry_creates_and_registers_devices()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Json);
            var config = DeviceFactory.Load(path);
            var registry = DeviceFactory.BuildRegistry(config, SystemClock.Instance, NullLoggerFactory.Instance);

            Assert.Equal(2, registry.Devices.Count);
            Assert.True(registry.Owns(IPAddress.Parse("192.168.50.10")));
            Assert.True(registry.TryResolve(IPAddress.Parse("192.168.50.11"), out var sensor));
            Assert.Equal("sensor", sensor.Identity.Id);
        }
        finally { File.Delete(path); }
    }
}

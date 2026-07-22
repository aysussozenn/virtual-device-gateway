using PacketDotNet;
using SharpPcap;

namespace Gateway.Ethernet;

/// <summary>Live <see cref="IPacketTransport"/> backed by a SharpPcap/Npcap adapter.</summary>
public sealed class PcapTransport : IPacketTransport
{
    private readonly ILiveDevice _device;
    private readonly string _filter;
    private readonly int _readTimeoutMs;
    private readonly bool _immediate;
    private bool _open;

    /// <param name="readTimeoutMs">
    /// pcap read timeout. Kept low (default 10 ms) so the kernel flushes captured frames to the
    /// callback promptly instead of batching them; a large value adds visible receive latency.
    /// </param>
    /// <param name="immediate">
    /// Immediate mode: Npcap delivers each frame the instant it arrives rather than buffering until
    /// the read timeout / min-to-copy threshold. This is the main knob for low-latency monitoring.
    /// </param>
    public PcapTransport(ILiveDevice device, string filter = "ip or arp", int readTimeoutMs = 10, bool immediate = true)
    {
        _device = device;
        _filter = filter;
        _readTimeoutMs = readTimeoutMs;
        _immediate = immediate;
    }

    public LinkLayers LinkType => _device.LinkType;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    public void Start()
    {
        _device.OnPacketArrival += OnArrival;
        _device.Open(new DeviceConfiguration
        {
            Mode = DeviceModes.Promiscuous,
            ReadTimeout = _readTimeoutMs,
            Immediate = _immediate,
        });
        _open = true;
        if (!string.IsNullOrWhiteSpace(_filter))
            _device.Filter = _filter;
        _device.StartCapture();
    }

    private void OnArrival(object sender, PacketCapture e)
    {
        // Copy out of the capture buffer immediately; the span is only valid in-callback.
        PacketReceived?.Invoke(this, new PacketReceivedEventArgs(e.GetPacket().Data));
    }

    public void Send(byte[] frame) => _device.SendPacket(frame);

    public ValueTask DisposeAsync()
    {
        if (_open)
        {
            try { _device.StopCapture(); } catch { /* ignore */ }
            try { _device.Close(); } catch { /* ignore */ }
        }
        _device.OnPacketArrival -= OnArrival;
        return ValueTask.CompletedTask;
    }
}

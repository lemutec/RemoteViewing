namespace RemoteViewing.Vnc.Server;

public struct VncServerSessionStatistics
{
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }

    public double BytesReceivedPerSecond { get; set; }
    public double BytesSentPerSecond { get; set; }

    public double CpuUsage { get; set; }
}

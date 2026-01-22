using System.Diagnostics;

namespace RemoteViewing.Vnc;

internal sealed class VncStatisticsHelper
{
    private const double RateLowPassTimeConstant = 1; // We'll low-pass our measurement with this time constant.
    private const double RateMaxTime = 5; // Beyond this, we will consider the rate 0 until we get the next packet.

    private long _bytesReceived, _bytesSent;
    private double _bytesReceivedPerSecond;
    private double _bytesSentPerSecond;
    private double _cpuTime, _cpuUsage;
    private long _timestamp;

    public VncStatisticsHelper()
    {
        SyncRoot = new object();
        Reset();
    }

    public void Reset()
    {
        lock (SyncRoot)
        {
            _bytesReceived = 0; _bytesSent = 0;
            _bytesReceivedPerSecond = 0;
            _bytesSentPerSecond = 0;
            _cpuTime = _cpuUsage = 0;
            _timestamp = Stopwatch.GetTimestamp();
        }
    }

    public void AddCpuTime(double seconds)
    {
        if (seconds < 0) { return; }

        lock (SyncRoot)
        {
            _cpuTime += seconds;
        }
    }

    public void Update(VncStream stream)
    {
        lock (SyncRoot)
        {
            long newTimestamp = Stopwatch.GetTimestamp();
            double dt = (double)(newTimestamp - _timestamp) / (double)Stopwatch.Frequency;
            if (dt < 0 || dt > RateMaxTime) { dt = 0; }
            _timestamp = newTimestamp;

            double alpha = dt / (dt + RateLowPassTimeConstant);
            if (dt >= 1e-6)
            {
                UpdateRate(alpha, dt, ref _bytesReceived, stream.BytesReceived, ref _bytesReceivedPerSecond);
                UpdateRate(alpha, dt, ref _bytesSent, stream.BytesSent, ref _bytesSentPerSecond);
                UpdateRate(alpha, dt, _cpuTime, ref _cpuUsage); _cpuTime = 0;
            }
        }
    }

    private void UpdateRate(double alpha, double dt, ref long oldValue, long newValue, ref double rate)
    {
        long delta = newValue - oldValue; oldValue = newValue;
        UpdateRate(alpha, dt, delta, ref rate);
    }

    private void UpdateRate(double alpha, double dt, double delta, ref double rate)
    {
        double instantRate = delta / dt;
        rate += (instantRate - rate) * alpha;
    }

    public long BytesReceived => _bytesReceived;

    public double BytesReceivedPerSecond => _bytesReceivedPerSecond;

    public long BytesSent => _bytesSent;

    public double BytesSentPerSecond => _bytesSentPerSecond;

    public double CpuUsage => _cpuUsage;

    public object SyncRoot { get; private set; }
}

using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;

namespace KighmuVpnWindows.Utils
{
    public class TrafficMonitor : IDisposable
    {
        private const string TAG = "TrafficMonitor";
        private const string TUN_NAME = "KighmuVPN";

        private readonly string _adapterName;
        private Timer? _timer;
        private long _baselineRx;
        private long _baselineTx;

        public event Action<long, long>? TrafficUpdated;

        public TrafficMonitor(string adapterName = TUN_NAME)
        {
            _adapterName = adapterName;
        }

        public void Start()
        {
            CaptureBaseline();
            _timer = new Timer(_ => Poll(), null, 0, 1000);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void CaptureBaseline()
        {
            var iface = GetTunInterface();
            if (iface != null)
            {
                var stats = iface.GetIPv4Statistics();
                _baselineRx = stats.BytesReceived;
                _baselineTx = stats.BytesSent;
            }
        }

        private void Poll()
        {
            var iface = GetTunInterface();
            if (iface == null) return;

            try
            {
                var stats = iface.GetIPv4Statistics();
                long rx = Math.Max(0, stats.BytesReceived - _baselineRx);
                long tx = Math.Max(0, stats.BytesSent - _baselineTx);
                TrafficUpdated?.Invoke(rx, tx);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG}: Poll error: {ex.Message}");
            }
        }

        private NetworkInterface? GetTunInterface()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name.Contains(_adapterName))
                    return ni;
            }
            return null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

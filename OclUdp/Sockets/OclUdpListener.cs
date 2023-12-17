using OclUdp.Consts;
using OclUdp.Enums;
using OclUdp.Internals;
using System.Net;
using System.Net.Sockets;

namespace OclUdp.Sockets
{
    public sealed class OclUdpListener : IDisposable
    {
        private readonly TimeSpan _reserveTimeout = new(0, 0, 10);
        private readonly byte[] _redirectPortDatagram = new byte[] { (byte)StatusCode.RedirectPort, 0x0, 0x0 };

        private readonly UdpClient _udpClient;
        private readonly Dictionary<IPEndPoint, ReservedUdpClient> _reservedUdpClientTable;
        private readonly Dictionary<IPEndPoint, WeakReference<OclUdpStream>> _connectionTable;

        public OclUdpListener(int port)
        {
            _udpClient = new(port);
            _reservedUdpClientTable = new();
            _connectionTable = new();
        }

        public async Task<OclUdpClient> AcceptOclUdpClientAsync()
        {
            while (true)
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync();
                byte[] buffer = result.Buffer;

                if (buffer.Length == 0)
                    continue;

                StatusCode resultStatusCode = (StatusCode)result.Buffer[0];

                if (resultStatusCode != StatusCode.Connect && resultStatusCode != StatusCode.PortRedirected)
                    continue;

                IPEndPoint remoteEndPoint = result.RemoteEndPoint;

                if (_connectionTable.TryGetValue(remoteEndPoint, out WeakReference<OclUdpStream> weakReference))
                {
                    if (!weakReference.TryGetTarget(out OclUdpStream s) || !s.IsOpened)
                        _connectionTable.Remove(remoteEndPoint);
                    else continue;
                }

                ReservedUdpClient reservedUdpClient;

                if (resultStatusCode == StatusCode.Connect)
                {
                    if (!_reservedUdpClientTable.TryGetValue(remoteEndPoint, out reservedUdpClient))
                        reservedUdpClient = ReserveUdpClient(remoteEndPoint);

                    await SendRedirectPortAsync(reservedUdpClient);

                    continue;
                }

                if (!_reservedUdpClientTable.Remove(remoteEndPoint, out reservedUdpClient))
                {
                    reservedUdpClient = ReserveUdpClient(remoteEndPoint);
                    await SendRedirectPortAsync(reservedUdpClient);

                    continue;
                }

                UdpClient udpClient = reservedUdpClient.UdpClientWithPortBytes.UdpClient;
                udpClient.Connect(remoteEndPoint);

                OclUdpStream stream = new(udpClient);
                OclUdpClient client = new(stream);

                _connectionTable.Add(remoteEndPoint, new(stream));

                return client;
            }
        }

        public void ClearConnectionCache()
        {
            DateTime now = DateTime.Now;

            foreach (var kvp in _reservedUdpClientTable)
            {
                if (now - kvp.Value.ReservedAt >= _reserveTimeout)
                {
                    kvp.Value.UdpClientWithPortBytes.UdpClient.Dispose();
                    _reservedUdpClientTable.Remove(kvp.Key);
                }
            }
                
            foreach (var kvp in _connectionTable)
                if (!kvp.Value.TryGetTarget(out _))
                    _connectionTable.Remove(kvp.Key);
        }

        public void Dispose()
        {
            _udpClient.Dispose();
            GC.SuppressFinalize(this);
        }

        private ReservedUdpClient ReserveUdpClient(IPEndPoint remoteEndPoint)
        {
            ReservedUdpClient reservedUdpClient = new()
            {
                ReservedByEndpoint = remoteEndPoint,
                UdpClientWithPortBytes = new(),
                ReservedAt = DateTime.Now
            };

            _reservedUdpClientTable.Add(remoteEndPoint, reservedUdpClient);

            return reservedUdpClient;
        }

        private async Task SendRedirectPortAsync(ReservedUdpClient reservedUdpClient)
        {
            UdpClientWithPortBytes udpClient = reservedUdpClient.UdpClientWithPortBytes;

            _redirectPortDatagram[1] = udpClient.FirstPortByte;
            _redirectPortDatagram[2] = udpClient.SecondPortByte;

            await _udpClient.SendAsync(_redirectPortDatagram, DatagramLength.RedirectPortLength, reservedUdpClient.ReservedByEndpoint);
        }
    }
}

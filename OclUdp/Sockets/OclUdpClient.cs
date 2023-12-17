using OclUdp.Consts;
using OclUdp.Enums;
using System.Net;
using System.Net.Sockets;

namespace OclUdp.Sockets
{
    public sealed class OclUdpClient : IDisposable
    {
        private const int _maxRedirectPortTimeoutCount = 5;
        private readonly TimeSpan _redirectPortWaitTime = new(0, 0, 0, 0, 100);
        
        private static readonly byte[] _connectDatagram = new byte[] { (byte)StatusCode.Connect };
        private static readonly byte[] _portRedirectedDatagram = new byte[] { (byte)StatusCode.PortRedirected };

        private readonly UdpClient _udpClient;

        private OclUdpStream _stream;

        public bool IsConnected
        {
            get => _stream != null && _stream.IsOpened;
        }

        internal OclUdpClient(OclUdpStream stream)
        {
            _stream = stream;
        }

        public OclUdpClient()
        {
            _udpClient = new(0);
        }

        public async Task ConnectAsync(IPEndPoint remoteEndPoint)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(remoteEndPoint);

            if (_udpClient == null)
                throw new InvalidOperationException("Connect is not allowed on OclUdpClient created by OclUdpListener.");

            int redirectPortTimeoutCount = 0;

            Task<UdpReceiveResult> resultTask = _udpClient.ReceiveAsync();
            Task delayTask = null;
            Task<Task> whenAnyTask = null;

            while (redirectPortTimeoutCount != _maxRedirectPortTimeoutCount)
            {
                await _udpClient.SendAsync(_connectDatagram, DatagramLength.ConnectLength, remoteEndPoint);

                bool taskDelayed = false;

                if (whenAnyTask == null
                    || (taskDelayed = whenAnyTask.IsCompletedSuccessfully && whenAnyTask.Result == delayTask))
                {
                    delayTask = Task.Delay(_redirectPortWaitTime);
                    whenAnyTask = Task.WhenAny(resultTask, delayTask);

                    if (taskDelayed)
                        redirectPortTimeoutCount++;
                }
                
                while (await whenAnyTask == resultTask)
                {
                    UdpReceiveResult result = resultTask.Result;
                    byte[] buffer = result.Buffer;
                    redirectPortTimeoutCount = 0;

                    if (!result.RemoteEndPoint.Equals(remoteEndPoint) || buffer.Length != DatagramLength.RedirectPortLength
                        || (StatusCode)buffer[0] != StatusCode.RedirectPort)
                    {
                        resultTask = _udpClient.ReceiveAsync();
                        delayTask = Task.Delay(_redirectPortWaitTime);
                        whenAnyTask = Task.WhenAny(resultTask, delayTask);

                        continue;
                    }

                    await _udpClient.SendAsync(_portRedirectedDatagram, DatagramLength.PortRedirectedLength, remoteEndPoint);

                    byte[] portBytes = new byte[2];
                    portBytes[0] = buffer[1];
                    portBytes[1] = buffer[2];

                    int port = BitConverter.ToUInt16(portBytes);
                    IPEndPoint redirectedEndPoint = new(remoteEndPoint.Address, port);

                    _udpClient.Connect(redirectedEndPoint);
                    _stream = new(_udpClient);

                    return;
                }
            }

            throw new TimeoutException("Failed to connect to the remote host in the allotted time.");
        }

        public OclUdpStream GetStream()
        {
            ThrowIfDisposed();

            if (!IsConnected)
                throw new InvalidOperationException("The OclUdpClient is not connected to a remote host.");

            return _stream;
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_stream == null)
                _udpClient.Dispose();
            else
                _stream.Dispose();

            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}

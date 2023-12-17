using System.Net;
using System.Net.Sockets;

namespace OclUdp.Internals
{
    internal class UdpClientWithPortBytes
    {
        public UdpClient UdpClient { get; init; }

        public byte FirstPortByte { get; init; }

        public byte SecondPortByte { get; init; }

        public UdpClientWithPortBytes()
        {
            UdpClient = new(0);

            IPEndPoint localEndPoint = (IPEndPoint)UdpClient.Client.LocalEndPoint;
            byte[] portBytes = BitConverter.GetBytes(localEndPoint.Port);
            FirstPortByte = portBytes[0];
            SecondPortByte = portBytes[1];
        }
    }
}

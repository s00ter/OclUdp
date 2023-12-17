using System.Net;

namespace OclUdp.Internals
{
    internal class ReservedUdpClient
    {
        public IPEndPoint ReservedByEndpoint { get; set; }

        public UdpClientWithPortBytes UdpClientWithPortBytes { get; init; }

        public DateTime ReservedAt { get; init; }
    }
}

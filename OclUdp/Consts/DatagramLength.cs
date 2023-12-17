namespace OclUdp.Consts
{
    internal static class DatagramLength
    {
        public static int ConnectLength { get; } = 1;

        public static int RedirectPortLength { get; } = 3;

        public static int PortRedirectedLength { get; } = 1;

        public static int DataSentMinLength { get; } = 5;

        public static int DataReceivedLength { get; } = 5;
    }
}

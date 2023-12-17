using OclUdp.Sockets;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

//IPEndPoint ep = new(IPAddress.Loopback, 7734);
//UdpClient client = new(8932);
//client.Connect(ep);

//await client.SendAsync(new byte[] { 0x13, 0x37 }, 2);

OclUdpListener oclUdpListener = new(7734);
OclUdpClient oclUdpClient = await oclUdpListener.AcceptOclUdpClientAsync();
OclUdpStream stream = oclUdpClient.GetStream();

byte[] buffer = new byte[4];
stream.Read(buffer, 0, 4);

int length = BitConverter.ToInt32(buffer);

byte[] bytes = new byte[length];

Stopwatch sw = new();
sw.Start();

stream.Read(bytes, 0, length);

sw.Stop();

Console.WriteLine($"Elapsed: {sw.Elapsed}");

Stream catz = File.OpenWrite("meha.jpeg");
catz.Write(bytes);

Console.ReadKey();

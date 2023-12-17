using OclUdp.Sockets;
using System.Net;
using System.Net.Sockets;

//IPEndPoint ep = new(IPAddress.Loopback, 8931);
//UdpClient client = new(7734);

//var r = await client.ReceiveAsync();
//int i = 0;

Stream cats = File.OpenRead("meha.jpg");
byte[] bytes = new byte[cats.Length];
cats.Read(bytes);

//IPEndPoint ep = new(IPAddress.Parse("192.168.43.94"), 7734);
IPEndPoint ep = new(IPAddress.Loopback, 7734);
OclUdpClient oclUdpClient = new();

Console.ReadKey();

await oclUdpClient.ConnectAsync(ep);

OclUdpStream stream = oclUdpClient.GetStream();

byte[] buffer = BitConverter.GetBytes((int)cats.Length);
stream.Write(buffer, 0, buffer.Length);

stream.Write(bytes, 0, bytes.Length);

Console.ReadKey();

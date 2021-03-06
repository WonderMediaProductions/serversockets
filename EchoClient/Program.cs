﻿// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Threading;

namespace EchoClient
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Specify at least port number and IPv4 address.");
                Console.WriteLine("EchoClient Port IPv4 [IPv6]");
                Console.WriteLine("Example: EchoClient 5070 192.168.1.1 ::1");
                return;
            }

            int port;
            if (int.TryParse(args[0], out port) == false || port > 65535 || port < 1024)
            {
                Console.WriteLine("Invalid Port Number #1");
                return;
            }

            IPAddress ip4;
            if (IPAddress.TryParse(args[1], out ip4) == false)
            {
                Console.WriteLine("Invalid IP #1");
                return;
            }

            IPAddress ip6 = null;
            if (args.Length >= 3 && IPAddress.TryParse(args[2], out ip6) == false)
            {
                Console.WriteLine("Invalid IP #2");
                return;
            }

            IPEndPoint server1 = new IPEndPoint(ip4, port);
            IPEndPoint server2 = (ip6 != null) ? new IPEndPoint(ip6, port) : null;

            Console.WriteLine(@"EchoClient");
            Console.WriteLine(@"Sleep 5 seconds");
            System.Threading.Thread.Sleep(5000);

            //for (; ; )
            {
                MultiConnection(server1, 1, 1024);
                MultiConnection(server1, 4096, 1);
                MultiConnection(server1, 512, 16);
                //Thread.Sleep(250000);
            }
            EchoTls(new IPEndPoint(server1.Address, server1.Port + 1), 64);
            EchoTlsSpliter(new IPEndPoint(server1.Address, server1.Port + 1));
            EchoTcp(server1);
            if (server2 != null)
                EchoTcp(server2);
            EchoUdp(server1);
            if (server2 != null)
                EchoUdp(server2);

            Console.WriteLine(@"Press any key to stop client...");
            Console.ReadKey();
            Console.WriteLine();
        }

        //Create -----> Disconnect            Create ---------> Close
        //219 ms        749 ms                265 ms            405 ms
        //266 ms        687 ms                218 ms            452 ms
        //358 ms        889 ms                234 ms            328 ms
        //453 ms        702 ms                234 ms            468 ms
        private static void MeasureConnectDisconnect(int count, IPEndPoint server, bool disconnectOrClose)
        {
            Socket[] sockets = new Socket[count];

            int start1 = Environment.TickCount;

            for (int x = 0; x < sockets.Length; x++)
                sockets[x] = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            Console.WriteLine(@"Create sockets: {0} ms", Environment.TickCount - start1);


            for (int x = 0; x < sockets.Length; x++)
                sockets[x].Connect(server);


            int start2 = Environment.TickCount;

            for (int x = 0; x < sockets.Length; x++)
            {
                sockets[x].Shutdown(SocketShutdown.Both);
                if (disconnectOrClose)
                    sockets[x].Disconnect(true);
                else
                    sockets[x].Close();
            }

            Console.WriteLine((disconnectOrClose ? "Disconnect(reuse)" : "Close") + @" sockets: {0} ms",
                Environment.TickCount - start2);
        }

        private static void MultiConnection(IPEndPoint server, int repeat, int count)
        {
            Console.WriteLine(@"Multi Connection {1} x {2} sockets: {0}", server.ToString(), repeat, count);

            Socket[] sockets = new Socket[count * repeat];
            IAsyncResult[] results = new IAsyncResult[count];
            byte[] data1 = new byte[] { 1, 2, 3, 4 };

            for (int x = 0; x < sockets.Length; x++)
                sockets[x] = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            int start = Environment.TickCount;

            for (int j = 0; j < repeat; j++)
            {
                for (int i = 0; i < count; i++)
                {
                    results[i] = sockets[j * count + i].BeginConnect(server, null, null);
                }

                for (int i = 0; i < count; i++)
                {
                    int n = j * count + i;

                    sockets[n].EndConnect(results[i]);

                    if (sockets[n].Send(data1) != data1.Length)
                        Console.Write("Ooppss...");
                    TcpReceive(sockets[n], data1);

                    sockets[n].Close();
                }
            }

            Console.WriteLine(@"Elapsed: {0} ms", Environment.TickCount - start);

            for (int x = 0; x < sockets.Length; x++)
                sockets[x].Close();
        }

        private static void WriteResult(int start, int transfered)
        {
            int period = Environment.TickCount - start;
            double mbits = (double)transfered * 8 / ((double)period / 1000) / (1024 * 1024);
            Console.WriteLine(@"Result: {0} ms; -> {1:F} Mbit/s; <-> {2:F} Mbit/s", period, mbits, mbits * 2);
        }

        private static void EchoUdp(IPEndPoint server)
        {
            var data1 = new byte[1024];
            for (int i = 0; i < data1.Length; i++)
                data1[i] = (byte)i;

            var data2 = new byte[data1.Length];

            Socket socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint((server.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Any : IPAddress.IPv6Any, 0));

            Console.WriteLine(@"UDP: Send from {0} to {1}", socket.LocalEndPoint.ToString(), server.ToString());

            int start = Environment.TickCount;

            for (int i = 0; i < 1024 * 256; i++)
            {
                data1[i % 16] = (byte)i;
                socket.SendTo(data1, server);
                socket.SendTo(data1, server);
                socket.SendTo(data1, server);

                socket.Receive(data2);
                socket.Receive(data2);

                if (socket.Receive(data2) != data1.Length)
                    Console.WriteLine(@"UDP: Echo Error #1");
                else
                    for (int j = 0; j < 16; j++)
                        if (data1[j] != data2[j])
                        {
                            Console.WriteLine(@"UDP: Echo Error #2");
                            break;
                        }
            }

            socket.Close();

            WriteResult(start, 1024 * 256 * data1.Length * 3);
        }

        private static void EchoTcp(IPEndPoint server)
        {
            int start = Environment.TickCount;

            Socket[] sockets = new Socket[64];

            Console.WriteLine(@"TCP: Create {0} TCP connections", sockets.Length);

            for (int i = 0; i < sockets.Length; i++)
            {
                sockets[i] = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                sockets[i].Bind(new IPEndPoint((server.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Any : IPAddress.IPv6Any, 0));
                sockets[i].ReceiveTimeout = 5000;
                sockets[i].Connect(server);
            }

            var data1 = new byte[1024];
            for (int i = 0; i < data1.Length; i++)
                data1[i] = (byte)i;

            var data2 = new byte[data1.Length];

            Console.WriteLine(@"TCP: Send to {0}", server.ToString());

            for (int i = 0; i < 1024 * 256; i++)
            {
                int x = i % sockets.Length;

                sockets[x].Send(data1);
                sockets[x].Send(data1);
                data1[i % 16] = (byte)i;
                sockets[x].Send(data1);

                TcpReceive(sockets[x], data2);
                TcpReceive(sockets[x], data2);

                if (TcpReceive(sockets[x], data2) != data1.Length)
                    Console.WriteLine(@"TCP: Echo Error #1");
                else
                    for (int j = 0; j < 16; j++)
                        if (data1[j] != data2[j])
                        {
                            Console.WriteLine(@"TCP: Echo Error #2");
                            break;
                        }
            }

            for (int i = 0; i < sockets.Length; i++)
            {
                sockets[i].Shutdown(SocketShutdown.Both);
                sockets[i].Close();
            }

            WriteResult(start, 1024 * 256 * data1.Length * 3);
        }

        static int TcpReceive(Socket socket, byte[] buffer)
        {
            int offset = 0;
            for (; offset < buffer.Length; )
                offset += socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
            return offset;
        }

        private static void EchoTls(IPEndPoint server, int maxConnections)
        {
            int start = Environment.TickCount;

            SslStream[] streams = new SslStream[maxConnections];

            Console.WriteLine(@"TLS: Create {0} TCP connections", streams.Length);

            for (int i = 0; i < streams.Length; i++)
            {
                var client = new TcpClient();
                client.Connect(server);
                streams[i] = new SslStream(client.GetStream(), false,
                    new RemoteCertificateValidationCallback(ValidateServerCertificate));
                streams[i].AuthenticateAsClient("localhost");
            }

            var data1 = new byte[1024];
            for (int i = 0; i < data1.Length; i++)
                data1[i] = (byte)i;

            var data2 = new byte[data1.Length];

            Console.WriteLine(@"TLS: Send to {0}", server.ToString());

            for (int i = 0; i < 1024 * 256; i++)
            {
                int x = i % streams.Length;

                streams[x].Write(data1);
                streams[x].Write(data1);
                data1[i % 16] = (byte)i;
                streams[x].Write(data1);

                TlsReceive(streams[x], data2);
                TlsReceive(streams[x], data2);

                if (TlsReceive(streams[x], data2) != data1.Length)
                    Console.WriteLine(@"TLS: Echo Error #1");
                else
                    for (int j = 0; j < 16; j++)
                        if (data1[j] != data2[j])
                        {
                            Console.WriteLine(@"TLS: Echo Error #2");
                            break;
                        }
            }

            for (int i = 0; i < streams.Length; i++)
                streams[i].Close();

            WriteResult(start, 1024 * 256 * data1.Length * 3);
        }

        private static void EchoTlsSpliter(IPEndPoint server)
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, 0));
            listener.Listen(1024);

            ThreadPool.QueueUserWorkItem(ThreadProc, (listener.LocalEndPoint as IPEndPoint).Port);

            var accepted = listener.Accept();

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            socket.Connect(server);
            socket.SendBufferSize = 0;


            var buffer = new byte[8192];
            bool reverse = false;
            int splitSize = 2;

            ThreadPool.QueueUserWorkItem(SendDataBack, new SendDataBackParam() { accepted = accepted, connected = socket, });

            while (accepted.Connected && socket.Connected)
            {
                int received = accepted.Receive(buffer);
                if (received == 0)
                    break;

                for (int offset = 0; offset < received; offset += splitSize)
                {
                    int size = (received - offset > splitSize) ? splitSize : received - offset;
                    socket.Send(buffer, offset, size, SocketFlags.None);
                    Console.Write("Splitter Offset: {0}\t\r", offset);
                }

                splitSize += reverse ? -1 : +1;

                if (splitSize > 1024 || splitSize < 2)
                    reverse = !reverse;
            }

            Thread.Sleep(2000);

            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        static void ThreadProc(Object stateInfo)
        {
            EchoTls(new IPEndPoint(IPAddress.Loopback, (int)stateInfo), 1);
        }

        class SendDataBackParam
        {
            public Socket accepted;
            public Socket connected;
        }

        static void SendDataBack(Object stateInfo)
        {
            var o = stateInfo as SendDataBackParam;
            var buffer = new byte[8192];

            try
            {
                while (o.accepted.Connected && o.connected.Connected)
                {
                    int received = o.connected.Receive(buffer);
                    if (received == 0)
                        break;
                    o.accepted.Send(buffer, received, SocketFlags.None);
                }
            }
            catch
            {
            }
            o.accepted.Close();
            o.connected.Close();
        }

        static int TlsReceive(Stream stream, byte[] buffer)
        {
            int offset = 0;
            for (; offset < buffer.Length; )
                offset += stream.Read(buffer, offset, buffer.Length - offset);
            return offset;
        }

        public static bool ValidateServerCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

    }
}

using System;
using System.Threading.Tasks;
using Grpc.Core;
using GrpcSampleServer;

namespace GrpcCoreServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var server = new Server
            {
                Services = { GrpcSample.Greeter.BindService(new GreeterService()) }
            };

            var host = "0.0.0.0";
            var port = 5001;
            server.Ports.Add(host, port, ServerCredentials.Insecure);
            Console.WriteLine("Running server on " + string.Format("{0}:{1}", host, port));
            server.Start();

            await server.ShutdownTask;
        }
    }
}

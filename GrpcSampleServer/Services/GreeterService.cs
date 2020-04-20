using Grpc.Core;
using GrpcSample;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace GrpcSampleServer
{
    public class GreeterService : Greeter.GreeterBase
    {
        private readonly ILogger<GreeterService> _logger;
        public GreeterService(ILogger<GreeterService> logger)
        {
            _logger = logger;
        }

        public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return new HelloReply
            {
                Message = "Hello " + request.Name
            };
        }
    }
}

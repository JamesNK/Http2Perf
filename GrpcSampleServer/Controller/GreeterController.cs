using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrpcSampleServer.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class GreeterController : ControllerBase
    {
        [HttpPost("SayHello")]
        public Task<HelloReply> SayHello(HelloRequest request)
        {
            return Task.FromResult<HelloReply>(new HelloReply
            {
                Message = "Hello " + request.Name
            });
        }
    }

    public class HelloRequest
    {
        public string Name { get; set; }
    }

    public class HelloReply
    {
        public string Message { get; set; }
    }
}

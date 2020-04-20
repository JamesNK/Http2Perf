using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Google.Protobuf;
using GrpcSample;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IO;

namespace GrpcSampleServer
{
    public class Startup
    {
        private static readonly RecyclableMemoryStreamManager StreamPool = new RecyclableMemoryStreamManager();

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<GreeterService>();

                endpoints.MapPost("/", async context =>
                {
                    using var memStream = StreamPool.GetStream();
                    await context.Request.Body.CopyToAsync(memStream);
                    memStream.Position = 0;
                    var request = HelloRequest.Parser.ParseDelimitedFrom(memStream);
                    var response = new HelloReply
                    {
                        Message = "Hello " + request.Name
                    };
                    memStream.Position = 0;
                    memStream.SetLength(0);
                    response.WriteDelimitedTo(memStream);
                    memStream.Position = 0;
                    context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                    await memStream.CopyToAsync(context.Response.Body);
                });
            });
        }
    }
}

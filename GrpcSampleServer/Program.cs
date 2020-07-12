using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrpcSampleServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(builder =>
                {
                    //builder.SetMinimumLevel(LogLevel.Error);
                    //builder.ClearProviders();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(o =>
                    {
                        o.ListenLocalhost(5000, endpoint => endpoint.Protocols = HttpProtocols.Http1);
                        o.ListenLocalhost(5001, endpoint => endpoint.Protocols = HttpProtocols.Http2);
                        o.ListenLocalhost(5002, endpoint =>
                        {
                            endpoint.Protocols = HttpProtocols.Http1AndHttp2;
                            endpoint.UseHttps();
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}

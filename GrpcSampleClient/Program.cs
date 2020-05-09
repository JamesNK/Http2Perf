using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcSample;
using Microsoft.IO;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcSampleClient
{
    public class Program
    {
        private static readonly RecyclableMemoryStreamManager StreamPool = new RecyclableMemoryStreamManager();
        private static readonly Dictionary<int, Greeter.GreeterClient> GrpcClientCache = new Dictionary<int, Greeter.GreeterClient>();
        private static readonly Dictionary<int, HttpClient> HttpClientCache = new Dictionary<int, HttpClient>();
        private static readonly Dictionary<int, HttpMessageInvoker> HttpMessageInvokerCache = new Dictionary<int, HttpMessageInvoker>();
        private static Uri RawGrpcUri= new Uri($"http://localhost:5001/greet.Greeter/SayHello");
        private static bool ClientPerThread;

        static async Task Main(string[] args)
        {
            ClientPerThread = bool.Parse(args[2]);
            Console.WriteLine("ClientPerThread: " + ClientPerThread);
            var stopwatch = Stopwatch.StartNew();
            long successCounter = 0;
            long errorCounter = 0;
            long lastElapse = 0;
            Exception lastError = null;

            new Thread(() =>
            {
                long pastRequests = 0;
                while (true)
                {
                    var e = lastElapse;
                    var ex = lastError;
                    Console.WriteLine($"Successfully processed {successCounter}; RPS {successCounter - pastRequests}; Errors {errorCounter}; Last elapsed {TimeSpan.FromTicks(lastElapse).TotalMilliseconds}ms");
                    if (ex != null)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                    pastRequests = successCounter;
                    Thread.Sleep(1000);
                }
            })
            { IsBackground = true }.Start();

            Func<int, Task> request;
            string clientType;
            if (args[0] == "g")
            {
                request = (i) => MakeGrpcCall(new HelloRequest() { Name = "foo" }, GetGrpcNetClient(i));
                clientType = "Grpc.Net.Client";
            }
            else if (args[0] == "c")
            {
                request = (i) => MakeGrpcCall(new HelloRequest() { Name = "foo" }, GetGrpcCoreClient(i));
                clientType = "Grpc.Core";
            }
            else if (args[0] == "r")
            {
                request = (i) => MakeRawGrpcCall(new HelloRequest() { Name = "foo" }, GetHttpMessageInvoker(i));
                clientType = "Raw HttpMessageInvoker";
            }
            else if (args[0] == "h2")
            {
                request = (i) => MakeHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i, 5001), HttpVersion.Version20);
                clientType = "HttpClient+HTTP/2";
            }
            else if (args[0] == "h1")
            {
                request = (i) => MakeHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i, 5000), HttpVersion.Version11);
                clientType = "HttpClient+HTTP/1.1";
            }
            else
            {
                throw new ArgumentException("Argument missing");
            }
            Console.WriteLine("Client type: " + clientType);

            var parallelism = int.Parse(args[1]);
            Console.WriteLine("Parallelism: " + parallelism);
            Console.WriteLine($"{nameof(GCSettings.IsServerGC)}: {GCSettings.IsServerGC}");

            await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async i =>
            {
                Stopwatch s = Stopwatch.StartNew();
                while (true)
                {
                    var start = s.ElapsedTicks;
                    try
                    {
                        await request(i);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Interlocked.Increment(ref errorCounter);
                    }
                    lastElapse = s.ElapsedTicks - start;

                    Interlocked.Increment(ref successCounter);
                }
            }));
        }

        private static Greeter.GreeterClient GetGrpcNetClient(int i)
        {
            if (!ClientPerThread)
            {
                i = 0;
            }
            if (!GrpcClientCache.TryGetValue(i, out var client))
            {
                client = GetGrpcNetClient("localhost", 5001);
                GrpcClientCache.Add(i, client);
            }

            return client;
        }

        private static Greeter.GreeterClient GetGrpcCoreClient(int i)
        {
            if (!ClientPerThread)
            {
                i = 0;
            }
            if (!GrpcClientCache.TryGetValue(i, out var client))
            {
                client = GetGrpcCoreClient("localhost", 5001);
                GrpcClientCache.Add(i, client);
            }

            return client;
        }

        private static HttpClient GetHttpClient(int i, int port)
        {
            if (!ClientPerThread)
            {
                i = 0;
            }
            if (!HttpClientCache.TryGetValue(i, out var client))
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                client = new HttpClient { BaseAddress = new Uri("http://localhost:" + port) };
                HttpClientCache.Add(i, client);
            }

            return client;
        }

        private static HttpMessageInvoker GetHttpMessageInvoker(int i)
        {
            if (!ClientPerThread)
            {
                i = 0;
            }
            if (!HttpMessageInvokerCache.TryGetValue(i, out var client))
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                client = new HttpMessageInvoker(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
                HttpMessageInvokerCache.Add(i, client);
            }

            return client;
        }

        private static Greeter.GreeterClient GetGrpcNetClient(string host, int port)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var httpHandler = new HttpClientHandler() { UseProxy = false, AllowAutoRedirect = false };
            var baseUri = new UriBuilder
            {
                Scheme = Uri.UriSchemeHttp,
                Host = host,
                Port = port

            };
            var channelOptions = new GrpcChannelOptions
            {
                HttpHandler = httpHandler
            };
            return new Greeter.GreeterClient(GrpcChannel.ForAddress(baseUri.Uri, channelOptions));
        }

        private static Greeter.GreeterClient GetGrpcCoreClient(string host, int port)
        {
            var channel = new Channel(host + ":" + port, ChannelCredentials.Insecure);
            return new Greeter.GreeterClient(channel);
        }

        private static async Task<HelloReply> MakeGrpcCall(HelloRequest request, Greeter.GreeterClient client)
        {
            return await client.SayHelloAsync(request);
        }

        private static async Task<HelloReply> MakeHttpCall(HelloRequest request, HttpClient client, Version httpVersion)
        {
            using var memStream = StreamPool.GetStream();
            request.WriteDelimitedTo(memStream);
            memStream.Position = 0;
            using var httpRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                Content = new StreamContent(memStream),
                Version = httpVersion
            };
            httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
            using var httpResponse = await client.SendAsync(httpRequest);
            var responseStream = await httpResponse.Content.ReadAsStreamAsync();
            return HelloReply.Parser.ParseDelimitedFrom(responseStream);
        }

        private static async Task<HelloReply> MakeRawGrpcCall(HelloRequest request, HttpMessageInvoker client)
        {
            var messageSize = request.CalculateSize();
            var data = new byte[messageSize + 5];
            request.WriteTo(new CodedOutputStream(data));

            Array.Copy(data, 0, data, 5, messageSize);
            data[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)messageSize);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, RawGrpcUri);
            httpRequest.Version = HttpVersion.Version20;
            httpRequest.Content = new ByteArrayContent(data);
            httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/grpc");
            httpRequest.Headers.TryAddWithoutValidation("TE", "trailers");

            using var response = await client.SendAsync(httpRequest, default);
            response.EnsureSuccessStatusCode();

            data = await response.Content.ReadAsByteArrayAsync();
            var responseMessage = HelloReply.Parser.ParseFrom(data.AsSpan(5).ToArray());

            var grpcStatus = response.TrailingHeaders.GetValues("grpc-status").SingleOrDefault();
            if (grpcStatus != "0")
            {
                throw new InvalidOperationException($"Unexpected grpc-status: {grpcStatus}");
            }

            return responseMessage;
        }
    }
}

using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcSample;
using Microsoft.IO;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcSampleClient
{
    public class Program
    {
        private static readonly RecyclableMemoryStreamManager StreamPool = new RecyclableMemoryStreamManager();
        private static readonly Dictionary<int, Greeter.GreeterClient> GrpcClientCache = new Dictionary<int, Greeter.GreeterClient>();
        private static readonly Dictionary<int, HttpClient> HttpClientCache = new Dictionary<int, HttpClient>();
        private static bool ClientPerThread;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            ClientPerThread = bool.Parse(args[2]);
            var stopwatch = Stopwatch.StartNew();
            long successCounter = 0;
            long errorCounter = 0;
            long lastElapse = 0;

            _ = Task.Run(async () =>
            {
                long pastRequests = 0;
                while (true)
                {
                    var e = lastElapse;
                    Console.WriteLine($"Successfully processed {successCounter}; RPS {successCounter - pastRequests}; Errors {errorCounter}; Last elapsed {TimeSpan.FromTicks(lastElapse).TotalMilliseconds}ms");
                    pastRequests = successCounter;
                    await Task.Delay(1000);
                }
            });

            Func<int, Task> request = null;
            if (args[0] == "g")
            {
                request = (i) => MakeGrpcCall(new HelloRequest() { Name = "foo" }, GetGrpcNetClient(i));
            }
            else if (args[0] == "c")
            {
                request = (i) => MakeGrpcCall(new HelloRequest() { Name = "foo" }, GetGrpcCoreClient(i));
            }
            else if (args[0] == "r")
            {
                request = (i) => MakeRawGrpcCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i));
            }
            else if (args[0] == "h2")
            {
                request = (i) => MakeHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i), new Version(2, 0));
            }
            else if (args[0] == "h1")
            {
                request = (i) => MakeHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i), new Version(1, 1));
            }
            else
            {
                throw new ArgumentException("Argument missing");
            }

            var parallelism = int.Parse(args[1]);
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
                    catch
                    {
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

        private static HttpClient GetHttpClient(int i)
        {
            if (!ClientPerThread)
            {
                i = 0;
            }
            if (!HttpClientCache.TryGetValue(i, out var client))
            {
                client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
                HttpClientCache.Add(i, client);
            }

            return client;
        }

        private static Greeter.GreeterClient GetGrpcNetClient(string host, int port)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var httpClient = new HttpClient();
            var baseUri = new UriBuilder
            {
                Scheme = Uri.UriSchemeHttp,
                Host = host,
                Port = port

            };
            var channelOptions = new GrpcChannelOptions
            {
                HttpClient = httpClient
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
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var httpResponse = await client.SendAsync(httpRequest);
            var responseStream = await httpResponse.Content.ReadAsStreamAsync();
            return HelloReply.Parser.ParseDelimitedFrom(responseStream);
        }

        private static async Task<HelloReply> MakeRawGrpcCall(HelloRequest request, HttpClient client)
        {
            var messageSize = request.CalculateSize();
            var messageBytes = new byte[messageSize];
            request.WriteTo(new CodedOutputStream(messageBytes));

            var data = new byte[messageSize + 5];
            data[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)messageSize);
            messageBytes.CopyTo(data.AsSpan(5));

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/grpc.testing.BenchmarkService/UnaryCall");
            httpRequest.Version = new Version(2, 0);
            httpRequest.Content = new StreamContent(new MemoryStream(data));
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            httpRequest.Headers.TE.Add(new TransferCodingWithQualityHeaderValue("trailers"));

            var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            data = await response.Content.ReadAsByteArrayAsync();
            var responseMessage = HelloReply.Parser.ParseFrom(data.AsSpan(5, data.Length - 5).ToArray());

            var grpcStatus = response.TrailingHeaders.GetValues("grpc-status").SingleOrDefault();
            if (grpcStatus != "0")
            {
                throw new InvalidOperationException($"Unexpected grpc-status: {grpcStatus}");
            }

            return responseMessage;
        }
    }
}

using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcSample;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Channel = Grpc.Core.Channel;

namespace GrpcSampleClient
{
    public class Program
    {
        private static readonly RecyclableMemoryStreamManager StreamPool = new RecyclableMemoryStreamManager();
        private static readonly Dictionary<int, Greeter.GreeterClient> GrpcClientCache = new Dictionary<int, Greeter.GreeterClient>();
        private static readonly Dictionary<int, AsyncDuplexStreamingCall<HelloRequest, HelloReply>> GrpcStreamCache = new Dictionary<int, AsyncDuplexStreamingCall<HelloRequest, HelloReply>>();
        private static readonly Dictionary<int, HttpClient> HttpClientCache = new Dictionary<int, HttpClient>();
        private static readonly Dictionary<int, HttpMessageInvoker> HttpMessageInvokerCache = new Dictionary<int, HttpMessageInvoker>();
        private static readonly ConcurrentDictionary<int, SignalRStreamingCall> SignalRCache = new ConcurrentDictionary<int, SignalRStreamingCall>();
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
            switch (args[0])
            {
                case "g":
                    request = (i) => MakeGrpcCall(new HelloRequest() { Name = "foo" }, GetGrpcNetClient(i));
                    clientType = "Grpc.Net.Client";
                    break;
                case "gs":
                    request = (i) => MakeGrpcCallBiDi(new HelloRequest() { Name = "foo" }, GetGrpcNetClientStream(i));
                    clientType = "Grpc.Net.Client BiDi";
                    break;
                case "c":
                    request = (i) => MakeGrpcCall(new HelloRequest() { Name = "foo" }, GetGrpcCoreClient(i));
                    clientType = "Grpc.Core";
                    break;
                case "r":
                    request = (i) => MakeRawGrpcCall(new HelloRequest() { Name = "foo" }, GetHttpMessageInvoker(i), streamRequest: false, streamResponse: false);
                    clientType = "Raw HttpMessageInvoker";
                    break;
                case "r-stream-request":
                    request = (i) => MakeRawGrpcCall(new HelloRequest() { Name = "foo" }, GetHttpMessageInvoker(i), streamRequest: true, streamResponse: false);
                    clientType = "Raw HttpMessageInvoker";
                    break;
                case "r-stream-response":
                    request = (i) => MakeRawGrpcCall(new HelloRequest() { Name = "foo" }, GetHttpMessageInvoker(i), streamRequest: false, streamResponse: true);
                    clientType = "Raw HttpMessageInvoker";
                    break;
                case "r-stream-all":
                    request = (i) => MakeRawGrpcCall(new HelloRequest() { Name = "foo" }, GetHttpMessageInvoker(i), streamRequest: true, streamResponse: true);
                    clientType = "Raw HttpMessageInvoker";
                    break;
                case "h2":
                    request = (i) => MakeProtobufHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i, 5001), HttpVersion.Version20);
                    clientType = "HttpClient+HTTP/2";
                    break;
                case "h1":
                    request = (i) => MakeProtobufHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i, 5000), HttpVersion.Version11);
                    clientType = "HttpClient+HTTP/1.1";
                    break;
                case "h2-json":
                    request = (i) => MakeJsonHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i, 5001), HttpVersion.Version20);
                    clientType = "HttpClient+JSON+HTTP/2";
                    break;
                case "h1-json":
                    request = (i) => MakeJsonHttpCall(new HelloRequest() { Name = "foo" }, GetHttpClient(i, 5000), HttpVersion.Version11);
                    clientType = "HttpClient+JSON+HTTP/1.1";
                    break;
                case "s":
                    request = async (i) => await MakeSignalRCall(new HelloRequest() { Name = "foo" }, await GetSignalRStateAsync(i));
                    clientType = "SignalR BiDi";
                    break;
                default:
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

        private static async Task<SignalRStreamingCall> GetSignalRStateAsync(int i)
        {
            if (!SignalRCache.TryGetValue(i, out var state))
            {
                var builder = new HubConnectionBuilder();
                builder.WithUrl("http://localhost:5000/greeterhub");
                builder.AddMessagePackProtocol();
                var hubConnection = builder.Build();
                await hubConnection.StartAsync();

                var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true
                });

                var result = hubConnection.StreamAsync<string>("SayHelloBiDi", channel.Reader);
                state = new SignalRStreamingCall
                {
                    RequestStream = channel.Writer,
                    ResponseStream = result.GetAsyncEnumerator()
                };

                bool success = SignalRCache.TryAdd(i, state);
                if (!success)
                {
                    throw new InvalidOperationException();
                }
            }

            return state;
        }

        private static AsyncDuplexStreamingCall<HelloRequest, HelloReply> GetGrpcNetClientStream(int i)
        {
            if (!GrpcStreamCache.TryGetValue(i, out var stream))
            {
                var client = GetGrpcNetClient(i);
                stream = client.SayHelloBiDi();
                GrpcStreamCache.Add(i, stream);
            }

            return stream;
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

        private static async Task<HelloReply> MakeGrpcCallBiDi(HelloRequest request, AsyncDuplexStreamingCall<HelloRequest, HelloReply> client)
        {
            await client.RequestStream.WriteAsync(request);
            await client.ResponseStream.MoveNext();
            return client.ResponseStream.Current;
        }

        private static async Task<HelloReply> MakeSignalRCall(HelloRequest request, SignalRStreamingCall state)
        {
            await state.RequestStream.WriteAsync(request.Name);
            if (!await state.ResponseStream.MoveNextAsync())
            {
                throw new InvalidOperationException("Unexpected end of stream.");
            }
            return new HelloReply
            {
                Message = state.ResponseStream.Current
            };
        }

        private static async Task<HelloReply> MakeProtobufHttpCall(HelloRequest request, HttpClient client, Version httpVersion)
        {
            using var memStream = StreamPool.GetStream();
            request.WriteDelimitedTo(memStream);
            memStream.Position = 0;
            using var httpRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                Content = new StreamContent(memStream),
                Version = httpVersion,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                RequestUri = new Uri("/protobuf", UriKind.Relative)
            };
            httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
            using var httpResponse = await client.SendAsync(httpRequest);
            var responseStream = await httpResponse.Content.ReadAsStreamAsync();
            return HelloReply.Parser.ParseDelimitedFrom(responseStream);
        }

        private static async Task<HelloReply> MakeJsonHttpCall(HelloRequest request, HttpClient client, Version httpVersion)
        {
            using var httpRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                Content = new StringContent(JsonSerializer.Serialize(request)),
                Version = httpVersion,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                RequestUri = new Uri("/api/greeter/sayhello", UriKind.Relative)
            };
            httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            using var httpResponse = await client.SendAsync(httpRequest);
            //Debugger.Launch();
            return await httpResponse.Content.ReadFromJsonAsync<HelloReply>();
        }

        private const int HeaderSize = 5;
        private static readonly CancellationTokenSource Cts = new CancellationTokenSource();

        private static async Task<HelloReply> MakeRawGrpcCall(HelloRequest request, HttpMessageInvoker client, bool streamRequest, bool streamResponse)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, RawGrpcUri);
            httpRequest.Version = HttpVersion.Version20;
            httpRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            if (!streamRequest)
            {
                var messageSize = request.CalculateSize();
                var data = new byte[messageSize + HeaderSize];
                request.WriteTo(new CodedOutputStream(data));

                Array.Copy(data, 0, data, HeaderSize, messageSize);
                data[0] = 0;
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)messageSize);

                httpRequest.Content = new ByteArrayContent(data);
                httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/grpc");
            }
            else
            {
                httpRequest.Content = new PushUnaryContent<HelloRequest>(request);
            }

            httpRequest.Headers.TryAddWithoutValidation("TE", "trailers");

            using var response = await client.SendAsync(httpRequest, Cts.Token);
            response.EnsureSuccessStatusCode();

            HelloReply responseMessage;
            if (!streamResponse)
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                responseMessage = HelloReply.Parser.ParseFrom(data.AsSpan(5).ToArray());
            }
            else
            {
                var responseStream = await response.Content.ReadAsStreamAsync();
                var data = new byte[HeaderSize];

                int read;
                var received = 0;
                while ((read = await responseStream.ReadAsync(data.AsMemory(received, HeaderSize - received), Cts.Token).ConfigureAwait(false)) > 0)
                {
                    received += read;

                    if (received == HeaderSize)
                    {
                        break;
                    }
                }

                if (received < HeaderSize)
                {
                    throw new InvalidDataException("Unexpected end of content while reading the message header.");
                }

                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1, 4));

                if (data.Length < length)
                {
                    data = new byte[length];
                }

                received = 0;
                while ((read = await responseStream.ReadAsync(data.AsMemory(received, length - received), Cts.Token).ConfigureAwait(false)) > 0)
                {
                    received += read;

                    if (received == length)
                    {
                        break;
                    }
                }

                read = await responseStream.ReadAsync(data, Cts.Token);
                if (read > 0)
                {
                    throw new InvalidDataException("Extra data returned.");
                }

                responseMessage = HelloReply.Parser.ParseFrom(data);
            }

            var grpcStatus = response.TrailingHeaders.GetValues("grpc-status").SingleOrDefault();
            if (grpcStatus != "0")
            {
                throw new InvalidOperationException($"Unexpected grpc-status: {grpcStatus}");
            }

            return responseMessage;
        }

        private class SignalRStreamingCall
        {
            public ChannelWriter<string> RequestStream { get; set; }
            public IAsyncEnumerator<string> ResponseStream { get; set; }
        }
    }
}

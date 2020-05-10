# HTTP/2 Perf on .NET

Run the server:

```
dotnet run -c Release -p GrpcSampleServer
```

Run the client:

```
dotnet run -c Release -p GrpcSampleClient client concurrency connectionPerThread
```

Clients:
* r = gRPC with raw HttpClient
* r-stream-request = gRPC with raw HttpClient (stream request)
* r-stream-response = gRPC with raw HttpClient (stream response)
* r-stream-all = gRPC with raw HttpClient (stream all)
* g = gRPC with Grpc.Net.Client
* c = gRPC with Grpc.Core
* h1 = Protobuf with HttpClient+HTTP/1
* h2 = Protobuf with HttpClient+HTTP/2

Example - Grpc.Net.Client + 100 callers + connection per thread (100 connections)

```
dotnet run -c Release -p GrpcSampleClient g 100 true
```
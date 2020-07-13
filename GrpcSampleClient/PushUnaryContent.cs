using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace GrpcSampleClient
{
    internal class PushUnaryContent<TRequest> : HttpContent where TRequest : IMessage
    {
        private readonly TRequest _content;

        public PushUnaryContent(TRequest content)
        {
            _content = content;
            Headers.TryAddWithoutValidation("Content-Type", "application/grpc");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            const int headerSize = 5;

            var messageSize = _content.CalculateSize();

            var data = new byte[headerSize];
            data[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)messageSize);
            await stream.WriteAsync(data, CancellationToken.None).ConfigureAwait(false);

            data = new byte[messageSize];
            _content.WriteTo(new CodedOutputStream(data));
            await stream.WriteAsync(data, CancellationToken.None).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            // We can't know the length of the content being pushed to the output stream.
            length = -1;
            return false;
        }
    }
}

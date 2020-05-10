using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace GrpcSampleClient
{
    internal class PushUnaryContent<TRequest, TResponse> : HttpContent
        where TRequest : IMessage
        where TResponse : class
    {
        private readonly TRequest _content;

        public PushUnaryContent(TRequest content)
        {
            _content = content;
            Headers.TryAddWithoutValidation("Content-Type", "application/grpc");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            var writeMessageTask = WriteMessageAsync(stream, _content);
            if (writeMessageTask.IsCompletedSuccessfully)
            {
                //GrpcEventSource.Log.MessageSent();
                return Task.CompletedTask;
            }

            return WriteMessageCore(writeMessageTask);
        }

        private async ValueTask WriteMessageAsync(Stream stream, TRequest content)
        {
            const int headerSize = 5;

            var data = new byte[headerSize];
            data[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)content.CalculateSize());
            await stream.WriteAsync(data, CancellationToken.None).ConfigureAwait(false);

            data = new byte[headerSize];
            content.WriteTo(new CodedOutputStream(data));
            await stream.WriteAsync(data, CancellationToken.None).ConfigureAwait(false);

            await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);

            //throw new InvalidOperationException("test");
        }

        private static async Task WriteMessageCore(ValueTask writeMessageTask)
        {
            await writeMessageTask.ConfigureAwait(false);
            //GrpcEventSource.Log.MessageSent();
        }

        protected override bool TryComputeLength(out long length)
        {
            // We can't know the length of the content being pushed to the output stream.
            length = -1;
            return false;
        }
    }

}

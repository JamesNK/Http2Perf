using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace GrpcSampleServer.Hubs
{
    public class GreeterHub : Hub
    {
        public async IAsyncEnumerable<string> SayHelloBiDi(
            IAsyncEnumerable<string> names,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return "Hello " + name;
            }
        }
    }
}

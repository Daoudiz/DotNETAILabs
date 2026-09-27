using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Application
{
    public interface IQSLAbsIA
    {
        public Task Lab1SimpleIACall(IChatClient chatClient);

        public Task Lab2SimpleIAChat(
            IChatClient chatClient,
            CancellationToken cancellationToken = default);

        public Task Lab3StructedOutput(IChatClient chatClient);

    }
}

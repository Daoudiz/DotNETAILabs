using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Models
{
    public sealed class CloudServiceRecord
    {
        public required int Key {  get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required ReadOnlyMemory<float> Embedding { get; set; }
    }
}

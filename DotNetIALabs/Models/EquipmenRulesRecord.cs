using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Models
{
    public sealed class EquipmentRulesRecord
    {
        public required int Key { get; set; }
        public required string Name { get; set; }
        public required string Rule { get; set; }
        public required ReadOnlyMemory<float> Embedding { get; set; }
    }
}

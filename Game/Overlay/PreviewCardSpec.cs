#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal sealed class PreviewCardSpec
{
    public string TemplateId { get; set; } = string.Empty;

    public int Tier { get; set; }

    public string Enchant { get; set; } = "None";

    public Dictionary<int, int> Attributes { get; set; } = new Dictionary<int, int>();
}

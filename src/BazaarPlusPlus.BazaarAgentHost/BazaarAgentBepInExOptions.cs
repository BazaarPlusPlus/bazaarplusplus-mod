#nullable enable
using System.IO;
using BazaarPlusPlus.BazaarAgent;
using UnityEngine;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentBepInExOptions : IBazaarAgentOptions
{
    public string DecisionLogRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BazaarPlusPlus", "BazaarAgent"));
}

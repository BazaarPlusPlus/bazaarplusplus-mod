// Guards the source-level premises of the online/PTR dual-version seams
// (docs/drafts/2026-07-03-online-ptr-dual-version-compat.md). The decompiled trees are
// gitignored local artifacts, so this is a LOCAL-ONLY gate: a missing tree is skipped
// with a notice, never a failure. Rerun after every `./run.sh decompile-all` /
// `decompile-all-ptr` refresh — a failure here means a seam premise drifted.
#nullable enable
var repoRoot = FindRepoRoot();
var failures = new List<string>();

CheckTree(
    "online (decompiled/)",
    Path.Combine(repoRoot, "decompiled"),
    netMessageProcessor =>
    {
        // NetMessageDispatchSeam premise: online has no private Receive funnel, the
        // public one-arg ReceiveOrQueue is the dispatch point and self-recurses
        // aggregate children. If a refreshed online tree gains the private Receive,
        // online adopted the PTR dispatch shape — the seam then routes there, verify
        // capture end-to-end before shipping.
        Require(
            netMessageProcessor,
            "public void ReceiveOrQueue(INetMessage message)",
            "online dispatch entry point"
        );
        RequireAbsent(
            netMessageProcessor,
            "void Receive(INetMessage message, bool",
            "online must not have the PTR private Receive funnel (shape key of NetMessageDispatchSeam)"
        );
        Require(
            netMessageProcessor,
            "ReceiveOrQueue(message2);",
            "online aggregate children must recurse through the patched public overload"
        );
    },
    (root, tree) =>
    {
        // Convergence trigger: when a refreshed online tree loses this method, the
        // PTR cosmetics layout was promoted — do plan PR4 (repoint
        // RandomHeroSkinPoolTogglePatch to CosmeticsPanelController, retire the skip).
        Require(
            ReadTreeFile(root, tree, "TheBazaar", "CosmeticsListManager.cs"),
            "OnRandomizeToggleChanged",
            "RandomHeroSkinPoolTogglePatch online target (failure = PTR layout promoted, do PR4)"
        );
        // GameBuildInfoResolver probe premise: ServerOption must stay PTR-only. If it
        // appears in online, the resolver would classify online as Ptr on the probe
        // side and (per the disagreement rule) pause uploads — revisit the resolver.
        RequireAbsent(
            ReadTreeFile(root, tree, "TheBazaar", "Config.cs"),
            "class ServerOption",
            "GameBuildInfoResolver probe premise: ServerOption is PTR-only"
        );
    }
);

CheckTree(
    "PTR (decompiled-vptr/)",
    Path.Combine(repoRoot, "decompiled-vptr"),
    netMessageProcessor =>
    {
        Require(
            netMessageProcessor,
            "public void ReceiveOrQueue(INetMessage message)",
            "PTR public one-arg overload (seam fallback target)"
        );
        Require(
            netMessageProcessor,
            "private void Receive(INetMessage message, bool allowGameSimAfterStateSync)",
            "PTR private Receive funnel (NetMessageDispatchSeam primary target)"
        );
        Require(
            netMessageProcessor,
            "Receive(message2, allowGameSimAfterStateSync);",
            "PTR aggregate children must recurse through the patched private Receive"
        );
    },
    (root, tree) =>
    {
        RequireAbsent(
            ReadTreeFile(root, tree, "TheBazaar", "CosmeticsListManager.cs"),
            "OnRandomizeToggleChanged",
            "PTR CosmeticsListManager must lack the toggle handler (Prepare()-skip premise)"
        );
        Require(
            ReadTreeFile(root, tree, "TheBazaar", "CosmeticsPanelController.cs"),
            "OnRandomizeToggleChanged",
            "PTR relocated toggle handler (PR4 target)"
        );
        Require(
            ReadTreeFile(root, tree, "TheBazaar", "Config.cs"),
            "class ServerOption",
            "GameBuildInfoResolver probe premise: PTR Config declares ServerOption"
        );
    }
);

if (failures.Count > 0)
{
    Console.Error.WriteLine("PTR compatibility premise failures:");
    foreach (var failure in failures)
        Console.Error.WriteLine($"  - {failure}");
    throw new InvalidOperationException($"{failures.Count} PTR compatibility premise(s) drifted.");
}

Console.WriteLine("PTR compatibility checks passed.");
return;

void CheckTree(
    string label,
    string treeRoot,
    Action<string> netMessageProcessorChecks,
    Action<string, string> otherChecks
)
{
    if (!Directory.Exists(Path.Combine(treeRoot, "TheBazaarRuntime")))
    {
        Console.WriteLine(
            $"[skip] {label} tree absent — run the matching ./run.sh decompile command to enable these checks."
        );
        return;
    }

    _currentTreeLabel = label;
    netMessageProcessorChecks(
        ReadTreeFile(treeRoot, label, "TheBazaar", "NetMessageProcessor.cs")
    );
    otherChecks(treeRoot, label);
}

string ReadTreeFile(string treeRoot, string label, string namespaceDir, string fileName)
{
    var path = Path.Combine(treeRoot, "TheBazaarRuntime", namespaceDir, fileName);
    if (!File.Exists(path))
    {
        failures.Add($"{label}: required decompiled file missing: {path}");
        return string.Empty;
    }

    return File.ReadAllText(path);
}

void Require(string text, string literal, string why)
{
    if (text.Length > 0 && !text.Contains(literal, StringComparison.Ordinal))
        failures.Add($"{_currentTreeLabel}: expected \"{literal}\" ({why})");
}

void RequireAbsent(string text, string literal, string why)
{
    if (text.Contains(literal, StringComparison.Ordinal))
        failures.Add($"{_currentTreeLabel}: expected NO \"{literal}\" ({why})");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "run.sh")))
            return current.FullName;
        current = current.Parent;
    }

    throw new InvalidOperationException("Repository root not found from test base directory.");
}

public partial class Program
{
    private static string _currentTreeLabel = string.Empty;
}

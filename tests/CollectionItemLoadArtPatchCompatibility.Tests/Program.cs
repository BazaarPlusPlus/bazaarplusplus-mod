var repoRoot = FindRepoRoot();
var patchPath = Path.Combine(
    repoRoot,
    "src",
    "BazaarPlusPlus",
    "Patches",
    "CollectionPanel",
    "CollectionItemLoadArtPatch.cs"
);
var collectionCardFactoryPath = Path.Combine(
    repoRoot,
    "src",
    "BazaarPlusPlus",
    "Game",
    "CollectionPanel",
    "Grid",
    "CollectionCardFactory.cs"
);
if (!File.Exists(patchPath))
{
    throw new InvalidOperationException($"Required source file is missing: {patchPath}");
}
if (!File.Exists(collectionCardFactoryPath))
{
    throw new InvalidOperationException(
        $"Required source file is missing: {collectionCardFactoryPath}"
    );
}

var patchText = File.ReadAllText(patchPath);
var clearCachedMaterialAssignmentBody = GetRequiredMethodBody(
    patchPath,
    patchText,
    "private static void ClearCachedMaterialAssignment"
);
RequireNativeOwnedMaterialCleanup(patchPath, clearCachedMaterialAssignmentBody);

var collectionCardFactoryText = File.ReadAllText(collectionCardFactoryPath);
var collectionCardBindBody = GetRequiredMethodBody(
    collectionCardFactoryPath,
    collectionCardFactoryText,
    "private async Task<CollectionCardBindResult> BindAsync"
);
RequireCreateAsyncUsesCollectionPreparationCallback(
    collectionCardFactoryPath,
    collectionCardBindBody
);
var prepareCollectionCardBody = GetRequiredMethodBody(
    collectionCardFactoryPath,
    collectionCardFactoryText,
    "PrepareCollectionCardForBind(Component card)"
);
RequireInstanceCollectionCardPreparationHelper(
    collectionCardFactoryPath,
    collectionCardFactoryText
);
RequireCollectionCardPreparationAttachesMarker(
    collectionCardFactoryPath,
    prepareCollectionCardBody
);

Console.WriteLine("Collection item LoadArt cleanup compatibility checks passed.");

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        var hasClaude = File.Exists(Path.Combine(current.FullName, "CLAUDE.md"));
        var hasSrc = Directory.Exists(Path.Combine(current.FullName, "src"));
        if (hasClaude && hasSrc)
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException(
        $"Could not locate repository root by walking up from {AppContext.BaseDirectory}; expected CLAUDE.md and src directory."
    );
}

static string GetRequiredMethodBody(string path, string text, string methodSignature)
{
    var signatureIndex = text.IndexOf(methodSignature, StringComparison.Ordinal);
    if (signatureIndex < 0)
    {
        throw new InvalidOperationException(
            $"Required method signature missing from {path}: {methodSignature}"
        );
    }

    var bodyStart = text.IndexOf('{', signatureIndex);
    if (bodyStart < 0)
    {
        throw new InvalidOperationException(
            $"Required method body start missing from {path}: {methodSignature}"
        );
    }

    var depth = 0;
    for (var i = bodyStart; i < text.Length; i++)
    {
        if (text[i] == '{')
        {
            depth++;
        }
        else if (text[i] == '}')
        {
            depth--;
            if (depth == 0)
            {
                return text.Substring(bodyStart, i - bodyStart + 1);
            }
        }
    }

    throw new InvalidOperationException(
        $"Required method body end missing from {path}: {methodSignature}"
    );
}

static void RequireNativeOwnedMaterialCleanup(string path, string methodBody)
{
    var nativeGuardIndex = RequireIndexOf(
        path,
        methodBody,
        "if (!marker.CardMaterialOwnedByCache && instance._cardMaterial != null)"
    );
    var destroyIndex = RequireIndexOf(path, methodBody, "Object.Destroy(instance._cardMaterial);");
    var clearFieldIndex = RequireIndexOf(path, methodBody, "instance._cardMaterial = null!;");
    var clearImageIndex = RequireIndexOf(path, methodBody, "instance._cardImage.material = null;");
    var resetOwnershipIndex = RequireIndexOf(
        path,
        methodBody,
        "marker.CardMaterialOwnedByCache = false;"
    );
    var releaseIndex = RequireIndexOf(path, methodBody, "marker.ReleaseCurrentArtKey();");

    if (
        nativeGuardIndex > destroyIndex
        || destroyIndex > clearFieldIndex
        || clearFieldIndex > clearImageIndex
        || clearImageIndex > resetOwnershipIndex
        || resetOwnershipIndex > releaseIndex
    )
    {
        throw new InvalidOperationException(
            $"ClearCachedMaterialAssignment must destroy native-owned material before clearing assignment and releasing cache refs: {path}"
        );
    }
}

static void RequireCreateAsyncUsesCollectionPreparationCallback(string path, string methodBody)
{
    var createAsyncIndex = RequireIndexOf(path, methodBody, "_nativeFactory.CreateAsync(");
    var callbackIndex = RequireIndexOf(path, methodBody, "PrepareCollectionCardForBind");
    var callEndIndex = methodBody.IndexOf(");", createAsyncIndex, StringComparison.Ordinal);
    if (callEndIndex < 0 || callbackIndex < createAsyncIndex || callbackIndex > callEndIndex)
    {
        throw new InvalidOperationException(
            $"CollectionCardFactory must pass PrepareCollectionCardForBind into _nativeFactory.CreateAsync before activation: {path}"
        );
    }
}

static void RequireInstanceCollectionCardPreparationHelper(string path, string text)
{
    if (
        text.Contains(
            "private static void PrepareCollectionCardForBind(Component card)",
            StringComparison.Ordinal
        )
    )
    {
        throw new InvalidOperationException(
            $"PrepareCollectionCardForBind must be an instance helper so it can attach this factory's cache owner: {path}"
        );
    }

    RequireIndexOf(path, text, "private void PrepareCollectionCardForBind(Component card)");
}

static void RequireCollectionCardPreparationAttachesMarker(string path, string methodBody)
{
    var getMarkerIndex = RequireIndexOf(
        path,
        methodBody,
        "GetComponent<CollectionPanelOwnedMarker>()"
    );
    var markerNullGuardIndex = RequireIndexOf(path, methodBody, "if (marker == null)");
    var addMarkerIndex = RequireIndexOf(
        path,
        methodBody,
        "AddComponent<CollectionPanelOwnedMarker>()"
    );
    var assignOwnerIndex = RequireIndexOf(path, methodBody, "marker.CacheOwner = _cacheSession;");
    var getCanvasIndex = RequireIndexOf(path, methodBody, "GetComponent<CanvasGroup>()");
    var addCanvasIndex = RequireIndexOf(path, methodBody, "AddComponent<CanvasGroup>()");
    var alphaIndex = RequireIndexOf(path, methodBody, "canvasGroup.alpha = 0f;");

    if (
        getMarkerIndex > markerNullGuardIndex
        || markerNullGuardIndex > addMarkerIndex
        || addMarkerIndex > assignOwnerIndex
        || getCanvasIndex > addCanvasIndex
        || addCanvasIndex > alphaIndex
    )
    {
        throw new InvalidOperationException(
            $"PrepareCollectionCardForBind must idempotently attach CollectionPanelOwnedMarker, update CacheOwner, and keep CanvasGroup alpha at zero: {path}"
        );
    }
}

static int RequireIndexOf(string path, string text, string requiredText)
{
    var index = text.IndexOf(requiredText, StringComparison.Ordinal);
    if (index < 0)
    {
        throw new InvalidOperationException(
            $"Required cleanup fragment missing from {path}: {requiredText}"
        );
    }

    return index;
}

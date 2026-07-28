using System.Text;
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

var failures = new List<string>();
var root = Path.Combine(
    Path.GetTempPath(),
    "bpp-report-asset-cache-tests-" + Guid.NewGuid().ToString("N")
);

try
{
    Directory.CreateDirectory(root);
    CanonicalKeyChangesForEveryRendererInput();
    AttributeOrderIsCanonical();
    TypedBindingsCannotConfuseStatusIconsWithEntities();
    StatusSemanticsUseExactNativeMappingsAndFailClosed();
    AlphaCropUsesVisibleBoundsInsteadOfCanvasGeometry();
    VerticalOrientationFlipPreservesRows();
    ReadbackSourceOrientationContractsAreExplicit();
    await ThreeFreshCachesAreDeterministic();
    await SameLineupSecondPassCreatesNoMaterializer();
    await CacheLockWaitDoesNotBlockCallingThread();
    await ConcurrentMissesSingleflight();
    await CorruptMappingAndObjectAreQuarantinedAndRecovered();
    TruncatedAndCorruptPngsAreRejected();
    CacheDirectoryLinksAreRejected();
    SameKeySameContentIsIdempotentAndDifferentContentFailsClosed();
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("CombatReplayReportAssetCache tests passed.");
}

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void AlphaCropUsesVisibleBoundsInsteadOfCanvasGeometry()
{
    const int width = 6;
    const int height = 5;
    var pixels = new byte[width * height * 4];
    pixels[(1 * width + 2) * 4 + 3] = ReportAssetAlphaCrop.DefaultAlphaThreshold - 1;
    pixels[(1 * width + 3) * 4 + 3] = ReportAssetAlphaCrop.DefaultAlphaThreshold;
    pixels[(3 * width + 4) * 4 + 3] = byte.MaxValue;

    Check(
        ReportAssetAlphaCrop.TryResolveBounds(pixels, width, height, padding: 1, out var bounds),
        "Alpha crop must find pixels at or above the visibility threshold."
    );
    Check(
        bounds == new ReportAssetPixelBounds(2, 0, 4, 5),
        $"Alpha crop used the wrong padded bounds: {bounds}."
    );

    var cropped = ReportAssetAlphaCrop.Crop(pixels, width, height, bounds);
    Check(
        cropped.Length == bounds.Width * bounds.Height * 4,
        "Alpha crop output must use the resolved pixel dimensions."
    );
    Check(
        cropped[(1 * bounds.Width + 1) * 4 + 3] == ReportAssetAlphaCrop.DefaultAlphaThreshold,
        "Alpha crop must preserve the visible source pixel at its translated position."
    );
    Check(
        cropped[(3 * bounds.Width + 2) * 4 + 3] == byte.MaxValue,
        "Alpha crop must preserve the far visible source pixel."
    );

    Check(
        !ReportAssetAlphaCrop.TryResolveBounds(
            new byte[width * height * 4],
            width,
            height,
            padding: 0,
            out _
        ),
        "A fully transparent canvas must not produce a crop rectangle."
    );
}

void VerticalOrientationFlipPreservesRows()
{
    const int width = 2;
    const int height = 3;
    var pixels = new byte[]
    {
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8,
        11,
        12,
        13,
        14,
        15,
        16,
        17,
        18,
        21,
        22,
        23,
        24,
        25,
        26,
        27,
        28,
    };

    ReportAssetPixelOrientation.FlipVertical(pixels, width, height);

    Check(
        pixels.SequenceEqual(
            new byte[]
            {
                21,
                22,
                23,
                24,
                25,
                26,
                27,
                28,
                11,
                12,
                13,
                14,
                15,
                16,
                17,
                18,
                1,
                2,
                3,
                4,
                5,
                6,
                7,
                8,
            }
        ),
        "Vertical orientation correction must swap complete RGBA rows without changing pixels."
    );
}

void ReadbackSourceOrientationContractsAreExplicit()
{
    Check(
        ReportAssetPixelOrientation.RequiresVerticalFlip(
            graphicsUvStartsAtTop: true,
            ReportAssetReadbackSource.UnitySprite
        ),
        "Top-origin Unity sprite readbacks must be flipped into browser orientation."
    );
    Check(
        !ReportAssetPixelOrientation.RequiresVerticalFlip(
            graphicsUvStartsAtTop: false,
            ReportAssetReadbackSource.UnitySprite
        ),
        "Bottom-origin Unity sprite readbacks must preserve their browser orientation."
    );

    foreach (
        var source in new[]
        {
            ReportAssetReadbackSource.MaterialTexture,
            ReportAssetReadbackSource.OffscreenCamera,
        }
    )
    {
        Check(
            ReportAssetPixelOrientation.RequiresVerticalFlip(graphicsUvStartsAtTop: true, source),
            $"Top-origin {source} output must be flipped into browser orientation."
        );
        Check(
            !ReportAssetPixelOrientation.RequiresVerticalFlip(graphicsUvStartsAtTop: false, source),
            $"Bottom-origin {source} output must preserve its browser orientation."
        );
    }
}

void TypedBindingsCannotConfuseStatusIconsWithEntities()
{
    var entity = new PostCombatReportAssetFile(
        PostCombatReportAssetBindingKind.Entity,
        "player:Player",
        "hero-portrait",
        "/cache/hero.png"
    );
    var status = new PostCombatReportAssetFile(
        PostCombatReportAssetBindingKind.EventSemantic,
        "status.freeze",
        "status-effect-icon",
        "/cache/freeze.png"
    );

    Check(
        entity.BindingKind == PostCombatReportAssetBindingKind.Entity
            && entity.BindingKey == "player:Player"
            && entity.SemanticRole == "hero-portrait",
        "Entity bindings must retain their typed entity ID and semantic role."
    );
    Check(
        status.BindingKind == PostCombatReportAssetBindingKind.EventSemantic
            && status.BindingKey == "status.freeze"
            && status.SemanticRole == "status-effect-icon",
        "Status bindings must carry a stable semantic key and explicit role."
    );
}

void StatusSemanticsUseExactNativeMappingsAndFailClosed()
{
    var expected = new (string Kind, string Action, string Stable, string Native)[]
    {
        ("player-attribute", "Burn", "status.burn", "BurnApplyAmount"),
        ("player-attribute", "HealthMax", "status.heal", "HealAmount"),
        ("player-attribute", "Poison", "status.poison", "PoisonApplyAmount"),
        ("player-attribute", "HealthRegen", "status.regen", "RegenApplyAmount"),
        ("player-attribute", "Rage", "status.rage", "RageApplyAmount"),
        ("player-attribute", "Shield", "status.shield", "ShieldApplyAmount"),
        ("card-attribute", "ChargeAmount", "status.charge", "ChargeAmount"),
        ("card-attribute", "BurnApplyAmount", "status.burn", "BurnApplyAmount"),
        ("card-attribute", "PoisonRemoveAmount", "status.poison", "PoisonApplyAmount"),
        ("card-attribute", "RegenCrit", "status.regen", "RegenApplyAmount"),
        ("card-attribute", "ShieldApplyAmount", "status.shield", "ShieldApplyAmount"),
        ("card-attribute", "Haste", "status.haste", "HasteAmount"),
        ("card-attribute", "SlowAmount", "status.slow", "SlowAmount"),
        ("card-attribute", "Freeze", "status.freeze", "FreezeAmount"),
        ("effect-executed", "CardFreeze", "status.freeze", "FreezeAmount"),
        ("effect-executed", "PlayerDamage", "status.damage", "DamageAmount"),
        ("effect-executed", "PlayerHeal", "status.heal", "HealAmount"),
        ("effect-executed", "PlayerRegenRemove", "status.regen", "RegenApplyAmount"),
        ("health", "Health:Damage", "status.damage", "DamageAmount"),
        ("health", "Health:Heal", "status.heal", "HealAmount"),
        ("health", "Shield:Burn", "status.burn", "BurnApplyAmount"),
    };

    foreach (var item in expected)
    {
        Check(
            ReportStatusIconSemanticResolver.TryResolve(item.Kind, item.Action, out var semantic),
            $"{item.Kind}/{item.Action} must resolve to a proven native semantic."
        );
        Check(
            semantic?.StableKey == item.Stable && semantic?.NativeAttributeKey == item.Native,
            $"{item.Kind}/{item.Action} resolved to the wrong native attribute key."
        );
        Check(
            ReportStatusIconSemanticResolver.TryGetByStableKey(item.Stable, out var roundTrip)
                && roundTrip.NativeAttributeKey == item.Native,
            $"{item.Stable} must round-trip without localized/display identity."
        );
    }

    var signedHealth = new (long Delta, string Stable, string Native)[]
    {
        (20, "status.heal", "HealAmount"),
        (-20, "status.damage", "DamageAmount"),
    };
    foreach (var item in signedHealth)
    {
        var reportEvent = new CombatReportEventV1
        {
            Kind = "player-attribute",
            Action = "Health",
            Value = item.Delta,
        };
        Check(
            ReportStatusIconSemanticResolver.TryResolve(reportEvent, out var semantic)
                && semantic.StableKey == item.Stable
                && semantic.NativeAttributeKey == item.Native,
            $"Signed Health delta {item.Delta} must resolve to {item.Stable}."
        );
    }
    Check(
        !ReportStatusIconSemanticResolver.TryResolve(
            new CombatReportEventV1
            {
                Kind = "player-attribute",
                Action = "Health",
                Value = 0,
            },
            out _
        ),
        "A zero Health delta must not invent a native status icon."
    );

    var unsupported = new (string Kind, string Action)[]
    {
        ("player-attribute", "Health"),
        ("skill-trigger", "CardFreeze"),
        ("card-attribute", "FreezeTargets"),
        ("card-attribute-extra", "Freeze"),
    };
    foreach (var item in unsupported)
    {
        Check(
            !ReportStatusIconSemanticResolver.TryResolve(item.Kind, item.Action, out _),
            $"Unsupported semantic {item.Kind}/{item.Action} must fail closed."
        );
    }
}

void CanonicalKeyChangesForEveryRendererInput()
{
    var baseline = CreateKey();
    var changes = new (string Name, ReportAssetRenderKey Key)[]
    {
        ("schema", baseline with { SchemaVersion = 2 }),
        ("game build", baseline with { GameBuild = "build-2" }),
        ("game data", baseline with { GameDataIdentity = "etag:data-2" }),
        ("template version", baseline with { ResolvedTemplateVersion = "2.0.0" }),
        ("skin", baseline with { ResolvedSkinIdentity = "art-key:skin-2" }),
        ("renderer", baseline with { Renderer = "renderer-2" }),
        ("renderer version", baseline with { RendererVersion = "2" }),
        ("asset type", baseline with { AssetType = "skill-card-preview" }),
        ("template", baseline with { TemplateId = "22222222222222222222222222222222" }),
        ("locale", baseline with { Locale = "zh-CN" }),
        ("size", baseline with { Size = "Large" }),
        ("tier", baseline with { Tier = "Gold" }),
        ("enchantment", baseline with { Enchantment = "Heavy" }),
        ("socket", baseline with { Socket = "Socket_4" }),
        ("variant", baseline with { Variant = "back" }),
        (
            "capture profile name",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { Name = "capture-2" },
            }
        ),
        (
            "capture profile version",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { Version = "2" },
            }
        ),
        (
            "fixed canvas",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { FixedCanvasWidth = 2560 },
            }
        ),
        (
            "fixed canvas height",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { FixedCanvasHeight = 1440 },
            }
        ),
        (
            "capture width",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { CapturePixelWidth = 2560 },
            }
        ),
        (
            "capture geometry",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { CapturePixelHeight = 1440 },
            }
        ),
        (
            "layout bounds",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { MaxWidthBasisPoints = 7100 },
            }
        ),
        (
            "layout height bounds",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { MaxHeightBasisPoints = 5700 },
            }
        ),
        (
            "backplate padding",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { BackplatePaddingPixels = 32 },
            }
        ),
        (
            "backplate",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { BackplateColor = "#000000ff" },
            }
        ),
        (
            "export layer",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { ExportLayer = 29 },
            }
        ),
        (
            "color space",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { ColorSpace = "Gamma" },
            }
        ),
        (
            "encoder",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { Encoder = "png-2" },
            }
        ),
        (
            "encoder version",
            baseline with
            {
                CaptureProfile = baseline.CaptureProfile with { EncoderVersion = "3.0.0" },
            }
        ),
        (
            "attribute value",
            baseline with
            {
                Attributes = [new ReportAssetRenderAttribute("Damage", 99)],
            }
        ),
        (
            "attribute name",
            baseline with
            {
                Attributes = [new ReportAssetRenderAttribute("Health", 10)],
            }
        ),
    };

    Check(
        !baseline.CanonicalJson.StartsWith("{,", StringComparison.Ordinal),
        "Canonical JSON must not begin with an empty property."
    );
    foreach (var change in changes)
    {
        Check(
            !string.Equals(
                baseline.RenderKeyHash,
                change.Key.RenderKeyHash,
                StringComparison.Ordinal
            ),
            $"Changing {change.Name} must change the render-key hash."
        );
    }
}

void AttributeOrderIsCanonical()
{
    var first = CreateKey() with
    {
        Attributes =
        [
            new ReportAssetRenderAttribute("Health", 20),
            new ReportAssetRenderAttribute("Ammo", 3),
            new ReportAssetRenderAttribute("Damage", 10),
        ],
    };
    var second = first with
    {
        Attributes =
        [
            new ReportAssetRenderAttribute("Damage", 10),
            new ReportAssetRenderAttribute("Health", 20),
            new ReportAssetRenderAttribute("Ammo", 3),
        ],
    };

    Check(
        first.CanonicalJson == second.CanonicalJson,
        "Attribute insertion order must not affect canonical JSON."
    );
    Check(
        first.RenderKeyHash == second.RenderKeyHash,
        "Attribute insertion order must not affect the render-key hash."
    );
}

async Task ThreeFreshCachesAreDeterministic()
{
    var key = CreateKey();
    var mappingBytes = new List<byte[]>();
    var objectRelativePaths = new List<string>();
    var renderRelativePaths = new List<string>();
    for (var index = 0; index < 3; index++)
    {
        var cacheRoot = Path.Combine(root, "fresh-" + index);
        var cache = new ReportAssetCache(cacheRoot);
        var coordinator = new ReportAssetMaterializationCoordinator(cache);
        var resolved = await coordinator.ResolveOrCreateAsync(
            key,
            () =>
                async (path, _) =>
                {
                    await Task.Yield();
                    WritePng(path, 17, 29, 7);
                    return new ReportAssetProducedFile(17, 29);
                }
        );
        Check(resolved != null, $"Fresh cache {index} must resolve its materialized asset.");
        if (resolved == null)
            continue;

        var mappingPath = Path.Combine(
            cacheRoot,
            "keys",
            key.RenderKeyHash[..2],
            key.RenderKeyHash + ".json"
        );
        mappingBytes.Add(File.ReadAllBytes(mappingPath));
        objectRelativePaths.Add(Path.GetRelativePath(cacheRoot, resolved.FilePath));
        renderRelativePaths.Add(Path.GetRelativePath(cacheRoot, mappingPath));
    }

    Check(
        mappingBytes.Count == 3
            && mappingBytes.Skip(1).All(bytes => bytes.SequenceEqual(mappingBytes[0])),
        "Three fresh processes must emit byte-identical mappings."
    );
    Check(
        objectRelativePaths.Distinct(StringComparer.Ordinal).Count() == 1,
        "Three fresh processes must choose the same object address."
    );
    Check(
        renderRelativePaths.Distinct(StringComparer.Ordinal).Count() == 1,
        "Three fresh processes must choose the same render-key address."
    );
}

async Task SameLineupSecondPassCreatesNoMaterializer()
{
    var cache = new ReportAssetCache(Path.Combine(root, "lineup"));
    var coordinator = new ReportAssetMaterializationCoordinator(cache);
    var lineup = new[]
    {
        CreateKey(),
        CreateKey() with
        {
            TemplateId = "22222222222222222222222222222222",
        },
        CreateKey() with
        {
            TemplateId = "33333333333333333333333333333333",
            Tier = "Silver",
        },
    };
    var factoryCount = 0;
    var invocationCount = 0;

    foreach (var key in lineup)
        await Resolve(key);
    Check(
        factoryCount == lineup.Length,
        "First lineup pass must create one materializer per distinct miss."
    );
    Check(
        invocationCount == lineup.Length,
        "First lineup pass must invoke one materializer per distinct miss."
    );

    factoryCount = 0;
    invocationCount = 0;
    foreach (var key in lineup)
        await Resolve(key);
    Check(factoryCount == 0, "A valid second lineup pass must construct zero materializers.");
    Check(
        invocationCount == 0,
        "A valid second lineup pass must invoke the materializer zero times."
    );

    async Task Resolve(ReportAssetRenderKey key)
    {
        var resolved = await coordinator.ResolveOrCreateAsync(
            key,
            () =>
            {
                factoryCount++;
                return async (path, _) =>
                {
                    invocationCount++;
                    await Task.Yield();
                    WritePng(path, 40, 60, 11);
                    return new ReportAssetProducedFile(40, 60);
                };
            }
        );
        Check(resolved != null, "Every lineup key must resolve.");
    }
}

async Task CacheLockWaitDoesNotBlockCallingThread()
{
    var cacheRoot = Path.Combine(root, "async-lock-wait");
    var cache = new ReportAssetCache(cacheRoot);
    var coordinator = new ReportAssetMaterializationCoordinator(cache);
    var key = CreateKey() with { Variant = "async-lock-wait" };
    var renderKeyHash = key.RenderKeyHash;
    var mappingDirectory = Path.Combine(cacheRoot, "keys", renderKeyHash[..2]);
    Directory.CreateDirectory(mappingDirectory);
    File.WriteAllText(Path.Combine(mappingDirectory, renderKeyHash + ".json"), "{broken");

    var lockDirectory = Path.Combine(cacheRoot, "locks", "render", renderKeyHash[..2]);
    Directory.CreateDirectory(lockDirectory);
    var lockPath = Path.Combine(lockDirectory, renderKeyHash + ".lock");
    Task<ReportAssetResolvedAsset?>? operation = null;
    using var invocationReturned = new ManualResetEventSlim(initialState: false);
    using (
        var heldLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        )
    )
    {
        var caller = new Thread(() =>
        {
            operation = coordinator.ResolveOrCreateAsync(
                key,
                () =>
                    async (path, _) =>
                    {
                        await Task.Yield();
                        WritePng(path, 23, 31, 41);
                        return new ReportAssetProducedFile(23, 31);
                    }
            );
            invocationReturned.Set();
        });
        caller.Start();

        Check(
            invocationReturned.Wait(TimeSpan.FromMilliseconds(250)),
            "Starting materialization must not synchronously wait through the cache lock retry loop."
        );
        caller.Join(TimeSpan.FromSeconds(1));
    }

    var resolved = operation == null ? null : await operation;
    Check(
        resolved != null,
        "Materialization must resume and publish after the contended cache lock is released."
    );
}

async Task ConcurrentMissesSingleflight()
{
    var cache = new ReportAssetCache(Path.Combine(root, "concurrent"));
    var coordinator = new ReportAssetMaterializationCoordinator(cache);
    var key = CreateKey() with { Variant = "concurrent" };
    var factoryCount = 0;
    var invocationCount = 0;
    var tasks = Enumerable
        .Range(0, 64)
        .Select(_ =>
            coordinator.ResolveOrCreateAsync(
                key,
                () =>
                {
                    Interlocked.Increment(ref factoryCount);
                    return async (path, _) =>
                    {
                        Interlocked.Increment(ref invocationCount);
                        await Task.Delay(40);
                        WritePng(path, 31, 47, 19);
                        return new ReportAssetProducedFile(31, 47);
                    };
                }
            )
        )
        .ToArray();
    var results = await Task.WhenAll(tasks);

    Check(factoryCount == 1, "Concurrent misses must construct exactly one materializer.");
    Check(invocationCount == 1, "Concurrent misses must invoke exactly one materializer.");
    Check(
        results.All(result => result != null),
        "Every concurrent follower must receive the terminal asset."
    );
    Check(
        results.Select(result => result!.FilePath).Distinct(StringComparer.Ordinal).Count() == 1,
        "Every concurrent follower must resolve the same immutable object."
    );
}

async Task CorruptMappingAndObjectAreQuarantinedAndRecovered()
{
    var cacheRoot = Path.Combine(root, "repair");
    var cache = new ReportAssetCache(cacheRoot);
    var coordinator = new ReportAssetMaterializationCoordinator(cache);
    var key = CreateKey() with { Variant = "repair" };
    var first = await Materialize(seed: 23);
    Check(first != null, "Repair setup must publish an initial asset.");
    if (first == null)
        return;

    var mappingPath = Path.Combine(
        cacheRoot,
        "keys",
        key.RenderKeyHash[..2],
        key.RenderKeyHash + ".json"
    );
    File.WriteAllText(mappingPath, "{broken", Encoding.UTF8);
    var calls = 0;
    var repairedMapping = await Materialize(seed: 23, onCall: () => calls++);
    Check(
        calls == 1 && repairedMapping != null,
        "A corrupt mapping must become one cache miss and recover."
    );
    Check(
        Directory.Exists(Path.Combine(cacheRoot, "quarantine", "mapping")),
        "A corrupt mapping must be quarantined."
    );

    var validObjectBytes = File.ReadAllBytes(repairedMapping!.FilePath);
    var truncatedBytes = validObjectBytes[..Math.Max(33, validObjectBytes.Length / 2)];
    var truncatedSourcePath = Path.Combine(root, "repair-truncated.png");
    File.WriteAllBytes(truncatedSourcePath, truncatedBytes);
    var truncatedHash = ReportAssetHash.Sha256File(truncatedSourcePath);
    var truncatedObjectPath = Path.Combine(
        cacheRoot,
        "objects",
        truncatedHash[..2],
        truncatedHash + ".png"
    );
    Directory.CreateDirectory(Path.GetDirectoryName(truncatedObjectPath)!);
    File.WriteAllBytes(truncatedObjectPath, truncatedBytes);
    var mappingDocument = JObject.Parse(File.ReadAllText(mappingPath, Encoding.UTF8));
    mappingDocument["contentHash"] = truncatedHash;
    File.WriteAllText(
        mappingPath,
        mappingDocument.ToString(Formatting.None),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
    );
    calls = 0;
    var repairedObject = await Materialize(seed: 23, onCall: () => calls++);
    Check(
        calls == 1 && repairedObject != null,
        "A corrupt object must become one cache miss and recover."
    );
    Check(
        Directory.Exists(Path.Combine(cacheRoot, "quarantine", "object")),
        "A corrupt object must be quarantined."
    );
    Check(
        cache.TryResolve(key, out _),
        "Recovered mapping and object must pass hash and geometry validation."
    );

    async Task<ReportAssetResolvedAsset?> Materialize(int seed, Action? onCall = null) =>
        await coordinator.ResolveOrCreateAsync(
            key,
            () =>
                async (path, _) =>
                {
                    onCall?.Invoke();
                    await Task.Yield();
                    WritePng(path, 64, 96, seed);
                    return new ReportAssetProducedFile(64, 96);
                }
        );
}

void TruncatedAndCorruptPngsAreRejected()
{
    var cacheRoot = Path.Combine(root, "invalid-png");
    var cache = new ReportAssetCache(cacheRoot);
    var validPath = Path.Combine(root, "valid-for-corruption.png");
    WritePng(validPath, 32, 48, 43);
    var validBytes = File.ReadAllBytes(validPath);

    var truncatedPath = Path.Combine(root, "truncated.png");
    File.WriteAllBytes(truncatedPath, validBytes[..Math.Max(33, validBytes.Length / 2)]);
    CheckPublishRejects(truncatedPath, "A truncated IDAT stream must not publish.");

    var corruptPath = Path.Combine(root, "corrupt.png");
    var corruptBytes = validBytes.ToArray();
    corruptBytes[Math.Max(33, corruptBytes.Length / 2)] ^= 0x5A;
    File.WriteAllBytes(corruptPath, corruptBytes);
    CheckPublishRejects(corruptPath, "A corrupt PNG payload must not publish.");

    var ihdrOnlyPath = Path.Combine(root, "ihdr-only.png");
    File.WriteAllBytes(ihdrOnlyPath, validBytes[..33]);
    CheckPublishRejects(
        ihdrOnlyPath,
        "A signature and valid IHDR without IDAT/IEND must not publish."
    );

    void CheckPublishRejects(string path, string message)
    {
        var rejected = false;
        try
        {
            cache.Publish(CreateKey() with { Variant = Path.GetFileName(path) }, path);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, message);
    }
}

void CacheDirectoryLinksAreRejected()
{
    var sourcePath = Path.Combine(root, "symlink-source.png");
    WritePng(sourcePath, 24, 36, 47);
    if (!CanCreateDirectoryLink())
        return;
    var sourceContentHash = ReportAssetHash.Sha256File(sourcePath);

    RejectLinkedDirectory(
        "keys-root",
        _ => "keys",
        (cache, key) => cache.TryResolve(key, out _),
        "The keys directory chain must reject symlinks."
    );
    RejectLinkedDirectory(
        "keys-prefix",
        key => Path.Combine("keys", key.RenderKeyHash[..2]),
        (cache, key) => cache.TryResolve(key, out _),
        "The keys prefix directory must reject symlinks."
    );
    RejectLinkedDirectory(
        "objects-root",
        _ => "objects",
        (cache, key) => cache.Publish(key, sourcePath, 24, 36),
        "The objects directory chain must reject symlinks."
    );
    RejectLinkedDirectory(
        "objects-prefix",
        _ => Path.Combine("objects", sourceContentHash[..2]),
        (cache, key) => cache.Publish(key, sourcePath, 24, 36),
        "The objects prefix directory must reject symlinks."
    );
    RejectLinkedDirectory(
        "locks-root",
        _ => "locks",
        (cache, key) => cache.Publish(key, sourcePath, 24, 36),
        "The locks directory chain must reject symlinks."
    );
    RejectLinkedDirectory(
        "locks-prefix",
        key => Path.Combine("locks", "render", key.RenderKeyHash[..2]),
        (cache, key) => cache.Publish(key, sourcePath, 24, 36),
        "The lock prefix directory must reject symlinks."
    );
    RejectCacheRootLink();
    RejectQuarantineLink(nestedPrefix: false);
    RejectQuarantineLink(nestedPrefix: true);

    void RejectLinkedDirectory(
        string caseName,
        Func<ReportAssetRenderKey, string> linkRelativePath,
        Action<ReportAssetCache, ReportAssetRenderKey> operation,
        string message
    )
    {
        var caseRoot = Path.Combine(root, "symlink-" + caseName);
        var outside = Path.Combine(root, "outside-" + caseName);
        var key = CreateKey() with { Variant = "symlink-" + caseName };
        var linkPath = Path.Combine(caseRoot, linkRelativePath(key));
        Directory.CreateDirectory(caseRoot);
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        Directory.CreateSymbolicLink(linkPath, outside);
        var rejected = false;
        try
        {
            operation(new ReportAssetCache(caseRoot), key);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, message);
        Check(
            !Directory.EnumerateFileSystemEntries(outside).Any(),
            $"Rejecting {caseName} symlink must not write outside the cache."
        );
    }

    void RejectCacheRootLink()
    {
        var physicalRoot = Path.Combine(root, "outside-cache-root");
        var linkedRoot = Path.Combine(root, "symlink-cache-root");
        Directory.CreateDirectory(physicalRoot);
        Directory.CreateSymbolicLink(linkedRoot, physicalRoot);
        var rejected = false;
        try
        {
            new ReportAssetCache(linkedRoot).TryResolve(
                CreateKey() with
                {
                    Variant = "symlink-cache-root",
                },
                out _
            );
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, "The cache root itself must reject symlinks.");
        Check(
            !Directory.EnumerateFileSystemEntries(physicalRoot).Any(),
            "Rejecting a linked cache root must not write through it."
        );
    }

    void RejectQuarantineLink(bool nestedPrefix)
    {
        var suffix = nestedPrefix ? "prefix" : "root";
        var caseRoot = Path.Combine(root, "symlink-quarantine-" + suffix);
        var outside = Path.Combine(root, "outside-quarantine-" + suffix);
        var key = CreateKey() with { Variant = "symlink-quarantine-" + suffix };
        var cache = new ReportAssetCache(caseRoot);
        var resolved = cache.Publish(key, sourcePath, 24, 36);
        var mappingPath = Path.Combine(
            caseRoot,
            "keys",
            key.RenderKeyHash[..2],
            key.RenderKeyHash + ".json"
        );
        File.WriteAllText(mappingPath, "{broken", Encoding.UTF8);
        Directory.CreateDirectory(outside);
        var linkPath = nestedPrefix
            ? Path.Combine(caseRoot, "quarantine", "mapping", key.RenderKeyHash[..2])
            : Path.Combine(caseRoot, "quarantine");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        Directory.CreateSymbolicLink(linkPath, outside);
        var rejected = false;
        try
        {
            cache.TryResolve(key, out _);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(
            rejected,
            nestedPrefix
                ? "The quarantine prefix directory must reject symlinks."
                : "The quarantine directory chain must reject symlinks."
        );
        Check(
            !Directory.EnumerateFileSystemEntries(outside).Any(),
            "Rejecting quarantine symlink must not write outside the cache."
        );
        Check(File.Exists(resolved.FilePath), "A rejected quarantine must not move the object.");
    }

    bool CanCreateDirectoryLink()
    {
        var target = Path.Combine(root, "symlink-probe-target");
        var link = Path.Combine(root, "symlink-probe-link");
        try
        {
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(link, target);
            Directory.Delete(link);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is UnauthorizedAccessException
                        or PlatformNotSupportedException
                        or IOException
            )
        {
            return false;
        }
    }
}

void SameKeySameContentIsIdempotentAndDifferentContentFailsClosed()
{
    var cacheRoot = Path.Combine(root, "conflict");
    var cache = new ReportAssetCache(cacheRoot);
    var key = CreateKey() with { Variant = "conflict" };
    var sourceA = Path.Combine(root, "source-a.png");
    var sourceB = Path.Combine(root, "source-b.png");
    WritePng(sourceA, 80, 120, 31);
    WritePng(sourceB, 80, 120, 32);

    var first = cache.Publish(key, sourceA, 80, 120);
    var mappingPath = Path.Combine(
        cacheRoot,
        "keys",
        key.RenderKeyHash[..2],
        key.RenderKeyHash + ".json"
    );
    var mappingBefore = File.ReadAllBytes(mappingPath);
    var objectBefore = File.ReadAllBytes(first.FilePath);
    var second = cache.Publish(key, sourceA, 80, 120);
    Check(first == second, "Publishing the same key and content must be idempotent.");
    Check(
        mappingBefore.SequenceEqual(File.ReadAllBytes(mappingPath)),
        "Idempotent publication must not rewrite the mapping."
    );
    Check(
        objectBefore.SequenceEqual(File.ReadAllBytes(first.FilePath)),
        "Idempotent publication must not rewrite the object."
    );

    var conflictThrown = false;
    try
    {
        cache.Publish(key, sourceB, 80, 120);
    }
    catch (ReportAssetCacheConflictException)
    {
        conflictThrown = true;
    }
    Check(conflictThrown, "The same render key with different content must fail closed.");
    Check(
        cache.TryResolve(key, out var terminal) && terminal.ContentHash == first.ContentHash,
        "A conflicting publish must preserve the original valid mapping."
    );
}

ReportAssetRenderKey CreateKey() =>
    new(
        SchemaVersion: 1,
        GameBuild: "1.0.11362-prod",
        GameDataIdentity: "etag:data-1",
        ResolvedTemplateVersion: "1.2.3",
        ResolvedSkinIdentity: "art-key:default|premium:false",
        Renderer: "native-card-preview",
        RendererVersion: "1",
        AssetType: "item-card-preview",
        TemplateId: "11111111111111111111111111111111",
        Locale: "en-US",
        Size: "Medium",
        Tier: "Silver",
        Enchantment: "None",
        Socket: "Socket_0",
        Variant: "front",
        CaptureProfile: new ReportAssetCaptureProfile(
            "screen-space-overlay",
            "1",
            1920,
            1080,
            1920,
            1080,
            7200,
            5800,
            24,
            "#080f19ff",
            30,
            "Linear",
            "imagesharp-png",
            "2.1.11"
        ),
        Attributes: [new ReportAssetRenderAttribute("Damage", 10)]
    );

void WritePng(string path, int width, int height, int seed)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var image = new Image<Rgba32>(width, height);
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            image[x, y] = new Rgba32(
                (byte)(seed + x),
                (byte)(seed * 3 + y),
                (byte)(seed * 7 + x + y),
                byte.MaxValue
            );
        }
    }

    image.SaveAsPng(path, new PngEncoder());
}

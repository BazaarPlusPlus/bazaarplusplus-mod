#nullable enable
using System.Collections.Concurrent;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal sealed record ReportAssetProducedFile(int PixelWidth, int PixelHeight);

internal enum ReportAssetMaterializationReservationKind
{
    Hit,
    Owner,
    Follower,
}

internal sealed class ReportAssetMaterializationCoordinator
{
    private static readonly ConcurrentDictionary<string, InFlightMaterialization> InFlight = new(
        StringComparer.Ordinal
    );

    private readonly ReportAssetCache _cache;

    internal ReportAssetMaterializationCoordinator(ReportAssetCache cache) =>
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    internal ReportAssetMaterializationReservation Reserve(ReportAssetRenderKey key)
    {
        if (_cache.TryResolve(key, out var hit))
            return ReportAssetMaterializationReservation.Hit(hit);

        var renderKeyHash = key.RenderKeyHash;
        var flightKey = _cache.RootPath + "|" + renderKeyHash;
        var candidate = new InFlightMaterialization();
        var flight = InFlight.GetOrAdd(flightKey, candidate);
        if (!ReferenceEquals(candidate, flight))
            return ReportAssetMaterializationReservation.Follower(flight.Completion.Task);

        // Double lookup closes the race where another process committed the immutable mapping
        // after the optimistic lookup but before this process won its in-memory singleflight.
        try
        {
            if (_cache.TryResolve(key, out hit))
            {
                flight.Completion.TrySetResult(hit);
                InFlight.TryRemove(flightKey, out _);
                return ReportAssetMaterializationReservation.Hit(hit);
            }

            return ReportAssetMaterializationReservation.Owner(
                _cache,
                key,
                flightKey,
                flight,
                CompleteFlight
            );
        }
        catch
        {
            flight.Completion.TrySetResult(null);
            InFlight.TryRemove(flightKey, out _);
            throw;
        }
    }

    internal Task<ReportAssetMaterializationReservation> ReserveAsync(
        ReportAssetRenderKey key,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Reserve(key);
            },
            cancellationToken
        );

    internal async Task<ReportAssetResolvedAsset?> ResolveOrCreateAsync(
        ReportAssetRenderKey key,
        Func<Func<string, CancellationToken, Task<ReportAssetProducedFile>>> materializerFactory,
        CancellationToken cancellationToken = default
    )
    {
        if (materializerFactory == null)
            throw new ArgumentNullException(nameof(materializerFactory));

        using var reservation = await ReserveAsync(key, cancellationToken).ConfigureAwait(false);
        if (reservation.Kind == ReportAssetMaterializationReservationKind.Hit)
            return reservation.ResolvedAsset;
        if (reservation.Kind == ReportAssetMaterializationReservationKind.Follower)
            return await AwaitWithCancellation(reservation.Completion, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            var materializer = materializerFactory();
            if (materializer == null)
                throw new InvalidOperationException(
                    "Report asset materializer factory returned null."
                );
            var produced = await materializer(reservation.StagingFilePath, cancellationToken)
                .ConfigureAwait(false);
            return await reservation
                .PublishAsync(produced.PixelWidth, produced.PixelHeight, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            reservation.Fail();
            throw;
        }
    }

    private static void CompleteFlight(
        string flightKey,
        InFlightMaterialization flight,
        ReportAssetResolvedAsset? resolvedAsset
    )
    {
        flight.Completion.TrySetResult(resolvedAsset);
        if (InFlight.TryGetValue(flightKey, out var current) && ReferenceEquals(current, flight))
        {
            InFlight.TryRemove(flightKey, out _);
        }
    }

    private static async Task<T> AwaitWithCancellation<T>(
        Task<T> task,
        CancellationToken cancellationToken
    )
    {
        if (!cancellationToken.CanBeCanceled)
            return await task.ConfigureAwait(false);
        if (task.IsCompleted)
            return await task.ConfigureAwait(false);

        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using (
            cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancellation
            )
        )
        {
            if (task != await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false))
                throw new OperationCanceledException(cancellationToken);
        }
        return await task.ConfigureAwait(false);
    }

    internal sealed class InFlightMaterialization
    {
        internal TaskCompletionSource<ReportAssetResolvedAsset?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class ReportAssetMaterializationReservation : IDisposable
    {
        private readonly ReportAssetCache? _cache;
        private readonly ReportAssetRenderKey? _key;
        private readonly string? _flightKey;
        private readonly InFlightMaterialization? _flight;
        private readonly Action<
            string,
            InFlightMaterialization,
            ReportAssetResolvedAsset?
        >? _complete;
        private readonly object _terminalGate = new();
        private bool _terminal;

        private ReportAssetMaterializationReservation(
            ReportAssetMaterializationReservationKind kind,
            ReportAssetResolvedAsset? resolvedAsset,
            Task<ReportAssetResolvedAsset?> completion,
            ReportAssetCache? cache = null,
            ReportAssetRenderKey? key = null,
            string? flightKey = null,
            InFlightMaterialization? flight = null,
            Action<string, InFlightMaterialization, ReportAssetResolvedAsset?>? complete = null
        )
        {
            Kind = kind;
            ResolvedAsset = resolvedAsset;
            Completion = completion;
            _cache = cache;
            _key = key;
            _flightKey = flightKey;
            _flight = flight;
            _complete = complete;
            StagingFilePath =
                kind == ReportAssetMaterializationReservationKind.Owner
                    ? cache!.CreateStagingFilePath(key!.RenderKeyHash)
                    : string.Empty;
        }

        internal ReportAssetMaterializationReservationKind Kind { get; }

        internal ReportAssetResolvedAsset? ResolvedAsset { get; private set; }

        internal Task<ReportAssetResolvedAsset?> Completion { get; }

        internal string StagingFilePath { get; }

        internal ReportAssetResolvedAsset Publish(int pixelWidth, int pixelHeight)
        {
            EnsureOwner();
            lock (_terminalGate)
            {
                if (_terminal)
                    throw new InvalidOperationException(
                        "Report asset reservation is already terminal."
                    );
                // Claim the reservation before starting filesystem work. Dispose/Cancel may run
                // concurrently while the publish is off-thread; it must not delete the staging
                // PNG or complete the shared flight as a failure underneath the publisher.
                _terminal = true;
            }

            try
            {
                ResolvedAsset = _cache!.Publish(_key!, StagingFilePath, pixelWidth, pixelHeight);
                _complete!(_flightKey!, _flight!, ResolvedAsset);
                return ResolvedAsset;
            }
            catch
            {
                _complete!(_flightKey!, _flight!, null);
                throw;
            }
            finally
            {
                TryDeleteStagingFile();
            }
        }

        internal Task<ReportAssetResolvedAsset> PublishAsync(
            int pixelWidth,
            int pixelHeight,
            CancellationToken cancellationToken = default
        ) =>
            Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Publish(pixelWidth, pixelHeight);
                },
                cancellationToken
            );

        internal void Fail()
        {
            if (Kind != ReportAssetMaterializationReservationKind.Owner)
                return;
            lock (_terminalGate)
            {
                if (_terminal)
                    return;
                _terminal = true;
            }
            TryDeleteStagingFile();
            _complete!(_flightKey!, _flight!, null);
        }

        public void Dispose() => Fail();

        internal static ReportAssetMaterializationReservation Hit(
            ReportAssetResolvedAsset resolvedAsset
        ) =>
            new(
                ReportAssetMaterializationReservationKind.Hit,
                resolvedAsset,
                Task.FromResult<ReportAssetResolvedAsset?>(resolvedAsset)
            );

        internal static ReportAssetMaterializationReservation Follower(
            Task<ReportAssetResolvedAsset?> completion
        ) => new(ReportAssetMaterializationReservationKind.Follower, null, completion);

        internal static ReportAssetMaterializationReservation Owner(
            ReportAssetCache cache,
            ReportAssetRenderKey key,
            string flightKey,
            InFlightMaterialization flight,
            Action<string, InFlightMaterialization, ReportAssetResolvedAsset?> complete
        ) =>
            new(
                ReportAssetMaterializationReservationKind.Owner,
                null,
                flight.Completion.Task,
                cache,
                key,
                flightKey,
                flight,
                complete
            );

        private void EnsureOwner()
        {
            if (Kind != ReportAssetMaterializationReservationKind.Owner)
                throw new InvalidOperationException("Only an owner reservation can publish.");
        }

        private void TryDeleteStagingFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(StagingFilePath) && File.Exists(StagingFilePath))
                    File.Delete(StagingFilePath);
            }
            catch
            {
                // Staging files are not authoritative cache state.
            }
        }
    }
}

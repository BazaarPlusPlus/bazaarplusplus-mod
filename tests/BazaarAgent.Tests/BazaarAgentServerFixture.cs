#nullable enable
using BazaarPlusPlus.BazaarAgent;

/// <summary>Per-test HTTP server fixture: each test news one up and disposes it at the end
/// of the test body. Tests that never call <see cref="SetSnapshot"/> serve a null snapshot.</summary>
internal sealed class ServerFixture : IDisposable
{
    public int Port { get; }
    public BazaarAgentHttpServer Server { get; }
    public BazaarAgentCommandQueue<BazaarAgentAction> Queue { get; }
    public CapturingBazaarAgentLogger Logger { get; } = new();
    private BazaarAgentContextSnapshot? _snapshot;
    public BazaarAgentContextSnapshot? CurrentSnapshot => _snapshot;

    public void SetSnapshot(BazaarAgentContextSnapshot? s) => _snapshot = s;

    public ServerFixture(
        int timeoutMs = 5000,
        Func<BazaarAgentContextSnapshot?>? snapshotGetter = null,
        Func<string>? requestIdFactory = null,
        Func<
            System.Net.HttpListenerContext,
            int,
            string,
            BazaarAgentHttpLogRoute,
            System.Threading.Tasks.Task<byte[]?>
        >? requestBodyReaderOverride = null,
        Func<
            System.Net.HttpListenerContext,
            int,
            string,
            string?,
            Exception?
        >? errorEnvelopeWriter = null
    )
    {
        Port = PickFreePort();
        Queue = new BazaarAgentCommandQueue<BazaarAgentAction>(timeoutMs);
        Server = new BazaarAgentHttpServer(
            Port,
            snapshotGetter ?? (() => CurrentSnapshot),
            null,
            Queue,
            Logger,
            requestIdFactory ?? BazaarAgentUlid.New,
            requestBodyReaderOverride,
            errorEnvelopeWriter
        );
        Server.Start();
    }

    public void Dispose()
    {
        try
        {
            Server.Dispose();
        }
        catch { }
        try
        {
            Queue.Dispose();
        }
        catch { }
    }

    private static int PickFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

#nullable enable
using System;
using BazaarPlusPlus.BazaarAgent;

/// <summary>Per-test HTTP server fixture: each test news one up and disposes it at the end
/// of the test body. Tests that never call <see cref="SetSnapshot"/> serve a null snapshot.</summary>
internal sealed class ServerFixture : IDisposable
{
    public int Port { get; }
    public BazaarAgentHttpServer Server { get; }
    public BazaarAgentCommandQueue<BazaarAgentAction> Queue { get; }
    public BazaarAgentCommandQueue<BazaarAgentReplayCommand> ReplayQueue { get; }
    private BazaarAgentContextSnapshot? _snapshot;
    public BazaarAgentContextSnapshot? CurrentSnapshot => _snapshot;

    public void SetSnapshot(BazaarAgentContextSnapshot? s) => _snapshot = s;

    public ServerFixture(int timeoutMs = 5000, int replayTimeoutMs = 5000)
    {
        Port = PickFreePort();
        Queue = new BazaarAgentCommandQueue<BazaarAgentAction>(timeoutMs);
        ReplayQueue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(replayTimeoutMs);
        Server = new BazaarAgentHttpServer(
            Port,
            () => CurrentSnapshot,
            Queue,
            ReplayQueue,
            new TestLogger()
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
        try
        {
            ReplayQueue.Dispose();
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

internal sealed class TestLogger : IBazaarAgentLogger
{
    public void Info(string message) { }

    public void Warning(string message) { }

    public void Error(string message, Exception? exception = null) { }
}

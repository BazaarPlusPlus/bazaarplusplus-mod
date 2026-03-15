#nullable enable
namespace BazaarPlusPlus.Game.RunLogging.Json;

public static class RunLogJsonSchema
{
    public static int CurrentSchemaVersion => 1;

    public static string MetaFileName => "meta.json";

    public static string EventsFileName => "events.ndjson";

    public static string CheckpointFileName => "checkpoint.json";

    public static string StatusFileName => "status.json";

    public static string ActiveRunFileName => "active-run.json";

    public static string RunsIndexFileName => "runs-index.json";
}

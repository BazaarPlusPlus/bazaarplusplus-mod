#nullable enable

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal static class RunUploadDefaults
{
    public const string UploadEndpoint = "https://mod-api.bazaarplusplus.com/runs/upload";
    public const string RegistrationEndpoint = "https://mod-api.bazaarplusplus.com/clients/register";
    public const int StartupDelaySeconds = 20;
    public const int IntervalSeconds = 180;
    public const int BatchSize = 3;
    public const int RequestTimeoutSeconds = 60;
}

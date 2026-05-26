#nullable enable
using TheBazaar;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// <c>Data.GetStatic()</c> returns the manager synchronously. This helper centralises
/// the readiness check so a future change to the upstream API only needs updating in
/// one place.
/// </summary>
internal static class BppStaticDataAccess
{
    public static object? TryGet()
    {
        if (!Data.IsManagerCreated())
            return null;

        return Data.GetStatic();
    }
}

#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewAssetInvocation
{
    internal static bool TryBuildArguments(
        MethodInfo? method,
        object? assetReference,
        out object?[] arguments
    )
    {
        arguments = Array.Empty<object?>();
        if (method == null || assetReference == null)
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length == 1 && Accepts(parameters[0], assetReference))
        {
            arguments = new object?[] { assetReference };
            return true;
        }

        if (
            parameters.Length == 2
            && Accepts(parameters[0], assetReference)
            && IsOptionalAssetScope(parameters[1])
        )
        {
            // Passing null preserves the native `scope ?? CurrentScope` behavior.
            arguments = new object?[] { assetReference, null };
            return true;
        }

        return false;
    }

    private static bool Accepts(ParameterInfo parameter, object value) =>
        parameter.ParameterType.IsInstanceOfType(value);

    private static bool IsOptionalAssetScope(ParameterInfo parameter)
    {
        var underlying = Nullable.GetUnderlyingType(parameter.ParameterType);
        return parameter.IsOptional && underlying?.Name == "AssetScope";
    }
}

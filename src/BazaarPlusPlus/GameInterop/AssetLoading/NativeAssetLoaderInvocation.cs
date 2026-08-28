#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.GameInterop.AssetLoading;

internal enum NativeAssetScopeIntent
{
    Current,
    Global,
}

internal static class NativeAssetLoaderInvocation
{
    private const string AssetScopeTypeName = "AssetScope";
    private const string GlobalScopeName = "Global";

    internal static bool TryBuildArguments(
        MethodInfo? method,
        object? firstArgument,
        NativeAssetScopeIntent scopeIntent,
        out object?[] arguments
    )
    {
        arguments = Array.Empty<object?>();
        if (method == null || firstArgument == null)
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length == 1 && Accepts(parameters[0], firstArgument))
        {
            arguments = [firstArgument];
            return true;
        }

        if (parameters.Length != 2 || !Accepts(parameters[0], firstArgument))
            return false;

        if (parameters[1].ParameterType == typeof(bool))
        {
            arguments = [firstArgument, false];
            return true;
        }

        var scopeType = Nullable.GetUnderlyingType(parameters[1].ParameterType);
        if (
            !parameters[1].IsOptional
            || scopeType?.IsEnum != true
            || !string.Equals(scopeType.Name, AssetScopeTypeName, StringComparison.Ordinal)
        )
        {
            return false;
        }

        var scope = scopeIntent switch
        {
            NativeAssetScopeIntent.Current => null,
            NativeAssetScopeIntent.Global => Enum.Parse(
                scopeType,
                GlobalScopeName,
                ignoreCase: false
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scopeIntent), scopeIntent, null),
        };
        arguments = [firstArgument, scope];
        return true;
    }

    private static bool Accepts(ParameterInfo parameter, object value) =>
        parameter.ParameterType.IsInstanceOfType(value);
}

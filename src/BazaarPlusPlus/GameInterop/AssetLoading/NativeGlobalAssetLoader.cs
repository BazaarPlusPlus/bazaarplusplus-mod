#nullable enable
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.GameInterop.AssetLoading;

internal static class NativeGlobalAssetLoader
{
    private const string LoadByAddressMethodName = "LoadAssetAsyncByAddress";
    private const string LoadByReferenceMethodName = "LoadAssetAsyncByReference";
    private const string GlobalScopeName = "Global";
    private const string InRunScopeName = "InRun";

    private static readonly MethodInfo? LoadByAddressMethod = ResolveGenericMethod(
        LoadByAddressMethodName,
        typeof(string)
    );
    private static readonly MethodInfo? LoadByReferenceMethod = ResolveGenericMethod(
        LoadByReferenceMethodName,
        typeof(AssetReference)
    );
    private static readonly PropertyInfo? CurrentScopeProperty = typeof(AssetLoader).GetProperty(
        "CurrentScope",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );

    internal static Task<T?> LoadByAddressAsync<T>(AssetLoader assetLoader, string address)
        where T : UnityEngine.Object =>
        InvokeAsync<T>(assetLoader, LoadByAddressMethod, LoadByAddressMethodName, address);

    internal static Task<T?> LoadByReferenceAsync<T>(
        AssetLoader assetLoader,
        AssetReference assetReference
    )
        where T : UnityEngine.Object =>
        InvokeAsync<T>(
            assetLoader,
            LoadByReferenceMethod,
            LoadByReferenceMethodName,
            assetReference
        );

    internal static bool CanRunNativeSharedPreload(AssetLoader assetLoader)
    {
        if (CurrentScopeProperty == null)
            return true;

        return string.Equals(
            CurrentScopeProperty.GetValue(assetLoader)?.ToString(),
            InRunScopeName,
            StringComparison.Ordinal
        );
    }

    private static async Task<T?> InvokeAsync<T>(
        AssetLoader assetLoader,
        MethodInfo? method,
        string methodName,
        object firstArgument
    )
        where T : UnityEngine.Object
    {
        if (assetLoader == null || firstArgument == null)
            return null;
        if (firstArgument is AssetReference assetReference && !assetReference.RuntimeKeyIsValid())
            return null;
        if (firstArgument is string address && string.IsNullOrWhiteSpace(address))
            return null;
        if (method == null)
            throw new MissingMethodException(typeof(AssetLoader).FullName, methodName);

        var arguments = BuildArguments(method, firstArgument);
        if (arguments == null)
            throw new MissingMethodException(typeof(AssetLoader).FullName, methodName);

        try
        {
            var raw = method.MakeGenericMethod(typeof(T)).Invoke(assetLoader, arguments);
            if (raw is not Task<T> task)
            {
                throw new InvalidOperationException(
                    $"{typeof(AssetLoader).FullName}.{methodName} returned an unexpected task type."
                );
            }

            return await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo? ResolveGenericMethod(string methodName, Type firstParameterType) =>
        typeof(AssetLoader)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method =>
                method.Name == methodName
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 1
            )
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length is 1 or 2
                    && parameters[0].ParameterType.IsAssignableFrom(firstParameterType);
            })
            .FirstOrDefault();

    private static object?[]? BuildArguments(MethodInfo method, object firstArgument)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 1)
            return [firstArgument];
        if (parameters.Length != 2)
            return null;

        var secondParameterType = parameters[1].ParameterType;
        var scopeType = Nullable.GetUnderlyingType(secondParameterType);
        if (scopeType?.IsEnum == true)
        {
            var globalScope = Enum.Parse(scopeType, GlobalScopeName, ignoreCase: false);
            return [firstArgument, globalScope];
        }

        if (secondParameterType == typeof(bool))
            return [firstArgument, false];

        return null;
    }
}

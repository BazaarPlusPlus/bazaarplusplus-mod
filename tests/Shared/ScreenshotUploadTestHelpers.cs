#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.Tests.Shared;

internal static class ScreenshotUploadTestHelpers
{
    public static object CreateUploadImage(
        Type uploadImageType,
        byte[] bytes,
        string contentType,
        string sourcePath
    )
    {
        var image =
            Activator.CreateInstance(uploadImageType)
            ?? throw new InvalidOperationException("Upload image should be constructible.");
        SetProperty(image, "Bytes", bytes);
        SetProperty(image, "ContentType", contentType);
        SetProperty(image, "SourcePath", sourcePath);
        return image;
    }

    public static void SetProperty(object instance, string name, object? value)
    {
        instance
            .GetType()
            .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(instance, value);
    }

    public static Type RequireType(string fullName)
    {
        return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
            ?? Type.GetType($"{fullName}, BazaarPlusPlus")
            ?? throw new InvalidOperationException($"Type not found: {fullName}");
    }

    public static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

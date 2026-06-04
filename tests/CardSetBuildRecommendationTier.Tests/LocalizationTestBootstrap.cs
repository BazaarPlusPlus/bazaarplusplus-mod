#nullable enable

using System.Runtime.CompilerServices;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Tests;

internal static class LocalizationTestBootstrap
{
    [ModuleInitializer]
    internal static void Install() =>
        L.Install(new EnglishLanguageProvider(), new MainlandLocaleModeProvider());

    private sealed class EnglishLanguageProvider : ILanguageProvider
    {
        public string CurrentLanguageCode => string.Empty;
    }

    private sealed class MainlandLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}

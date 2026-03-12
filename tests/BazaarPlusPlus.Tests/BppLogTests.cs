using System;
using Xunit;

namespace BazaarPlusPlus.Tests;

public class BppLogTests
{
    [Fact]
    public void FormatPrefixesComponentName()
    {
        var message = BppLog.Format("EncounterTracker", "Updated map encounters");

        Assert.Equal("[BPP][EncounterTracker] Updated map encounters", message);
    }

    [Fact]
    public void FormatErrorIncludesExceptionDetails()
    {
        var ex = new InvalidOperationException("boom");

        var message = BppLog.FormatError("MonsterDatabase", "Load failed", ex);

        Assert.Contains("[BPP][MonsterDatabase] Load failed", message);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("boom", message);
    }
}

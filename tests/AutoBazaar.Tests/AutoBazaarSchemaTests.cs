using BazaarPlusPlus.Game.AutoBazaar;
using Xunit;

public class AutoBazaarSchemaTests
{
    [Fact]
    public void Context_DefaultSchemaVersion_IsCurrentMinorVersion()
    {
        var context = new AutoBazaarContext();

        Assert.Equal("1.2.0", context.SchemaVersion);
    }
}

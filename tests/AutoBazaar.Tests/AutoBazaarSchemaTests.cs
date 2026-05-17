using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarSchemaTests
{
    [Fact]
    public void Context_DefaultSchemaVersion_IsCurrentMinorVersion()
    {
        var context = new AutoBazaarContext();

        Assert.Equal("1.1.0", context.SchemaVersion);
    }
}

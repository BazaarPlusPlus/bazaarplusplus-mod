using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarResponseJsonTests
{
    [Fact]
    public void BuildValidationErrorBody_NestsExtraFields()
    {
        var validation = new AutoBazaarValidationResult(
            AutoBazaarValidationCode.Cooldown,
            429,
            "action min-delay not yet elapsed",
            new Dictionary<string, object?> { ["retryAfterSeconds"] = 0.75 });

        var json = AutoBazaarResponseJson.BuildValidationErrorBody(validation);

        Assert.Contains("\"error\":\"cooldown\"", json);
        Assert.Contains("\"details\":\"action min-delay not yet elapsed\"", json);
        Assert.Contains("\"extra\":{\"retryAfterSeconds\":0.75}", json);
    }

    [Fact]
    public void BuildValidationErrorBody_OmitsExtraWhenEmpty()
    {
        var validation = new AutoBazaarValidationResult(
            AutoBazaarValidationCode.Invalid,
            400,
            "unknown actionKind",
            null);

        var json = AutoBazaarResponseJson.BuildValidationErrorBody(validation);

        Assert.Contains("\"error\":\"invalid\"", json);
        Assert.DoesNotContain("\"extra\"", json);
    }
}

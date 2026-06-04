using System.Collections.Generic;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentResponseJsonTests
{
    [Fact]
    public void BuildValidationErrorBody_NestsExtraFields()
    {
        var validation = new BazaarAgentValidationResult(
            BazaarAgentValidationCode.Cooldown,
            429,
            "action min-delay not yet elapsed",
            new Dictionary<string, object?> { ["retryAfterSeconds"] = 0.75 }
        );

        var json = BazaarAgentResponseJson.BuildValidationErrorBody(validation);

        Assert.Contains("\"error\":\"cooldown\"", json);
        Assert.Contains("\"details\":\"action min-delay not yet elapsed\"", json);
        Assert.Contains("\"extra\":{\"retryAfterSeconds\":0.75}", json);
    }

    [Fact]
    public void BuildValidationErrorBody_OmitsExtraWhenEmpty()
    {
        var validation = new BazaarAgentValidationResult(
            BazaarAgentValidationCode.Invalid,
            400,
            "unknown actionKind",
            null
        );

        var json = BazaarAgentResponseJson.BuildValidationErrorBody(validation);

        Assert.Contains("\"error\":\"invalid\"", json);
        Assert.DoesNotContain("\"extra\"", json);
    }
}

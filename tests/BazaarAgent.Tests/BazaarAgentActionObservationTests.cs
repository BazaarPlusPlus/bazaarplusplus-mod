using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentActionObservationTests
{
    [Fact]
    public void Evaluate_UnchangedBeforeDeadline_RemainsPending()
    {
        var baseline = new BazaarAgentContext { StateName = BazaarAgentRunStateName.Choice };

        var status = BazaarAgentActionObservation.Evaluate(
            baseline,
            baseline,
            nowSeconds: 9.9,
            deadlineSeconds: 10
        );

        Assert.Equal(BazaarAgentActionObservationStatus.Pending, status);
    }

    [Fact]
    public void Evaluate_GameplayChange_ConfirmsAction()
    {
        var baseline = new BazaarAgentContext
        {
            StateName = BazaarAgentRunStateName.Choice,
            PlayerGold = 10,
        };
        var current = new BazaarAgentContext
        {
            StateName = BazaarAgentRunStateName.Choice,
            PlayerGold = 9,
        };

        var status = BazaarAgentActionObservation.Evaluate(
            baseline,
            current,
            nowSeconds: 1,
            deadlineSeconds: 10
        );

        Assert.Equal(BazaarAgentActionObservationStatus.Confirmed, status);
    }

    [Fact]
    public void Evaluate_UnchangedAtDeadline_TimesOut()
    {
        var baseline = new BazaarAgentContext { StateName = BazaarAgentRunStateName.Choice };

        var status = BazaarAgentActionObservation.Evaluate(
            baseline,
            baseline,
            nowSeconds: 10,
            deadlineSeconds: 10
        );

        Assert.Equal(BazaarAgentActionObservationStatus.TimedOut, status);
    }
}

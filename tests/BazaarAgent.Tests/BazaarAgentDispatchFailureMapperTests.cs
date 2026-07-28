using BazaarPlusPlus.BazaarAgent;
using Xunit;

public sealed class BazaarAgentDispatchFailureMapperTests
{
    [Theory]
    [InlineData(BazaarAgentDispatchFailureKind.Invalid, 400, "invalid")]
    [InlineData(BazaarAgentDispatchFailureKind.Unavailable, 503, "unavailable")]
    [InlineData(BazaarAgentDispatchFailureKind.Internal, 500, "internal")]
    public void Typed_dispatch_failures_map_to_non_ambiguous_http_responses(
        BazaarAgentDispatchFailureKind failureKind,
        int expectedHttpStatus,
        string expectedErrorCode
    )
    {
        var response = BazaarAgentDispatchFailureMapper.Map(failureKind);

        Assert.Equal(expectedHttpStatus, response.HttpStatus);
        Assert.Equal(expectedErrorCode, response.ErrorCode);
    }

    [Fact]
    public void Existing_untyped_dispatch_failures_remain_internal_errors()
    {
        var result = new BazaarAgentDispatchResult(false, "existing failure");
        var response = BazaarAgentDispatchFailureMapper.Map(result.FailureKind);

        Assert.Equal(500, response.HttpStatus);
        Assert.Equal("internal", response.ErrorCode);
    }
}

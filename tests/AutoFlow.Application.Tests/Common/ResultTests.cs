using AutoFlow.Application.Common;

namespace AutoFlow.Application.Tests.Common;

public class ResultTests
{
    private static readonly Error SomeError = Error.NotFound("thing.not_found", "Thing was not found.");

    [Fact]
    public void Success_exposes_value_and_hides_error()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_exposes_error_and_hides_value()
    {
        var result = Result.Failure<int>(SomeError);

        Assert.True(result.IsFailure);
        Assert.Equal(SomeError, result.Error);
        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("thing.not_found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_calls_the_branch_for_the_outcome()
    {
        Assert.Equal("ok 1", Result.Success(1).Match(v => $"ok {v}", e => e.Code));
        Assert.Equal("thing.not_found", Result.Failure<int>(SomeError).Match(v => $"ok {v}", e => e.Code));
    }

    [Fact]
    public void Error_factories_set_kind()
    {
        Assert.Equal(ErrorKind.NotFound, Error.NotFound("a", "b").Kind);
        Assert.Equal(ErrorKind.Conflict, Error.Conflict("a", "b").Kind);
    }
}

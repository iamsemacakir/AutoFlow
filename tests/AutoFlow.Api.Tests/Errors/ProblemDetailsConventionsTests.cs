using AutoFlow.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AutoFlow.Api.Tests.Errors;

public sealed class ProblemDetailsConventionsTests
{
    [Fact]
    public void Apply_fills_status_instance_code_and_trace_id_from_the_request()
    {
        var context = NewContext(StatusCodes.Status500InternalServerError, new ProblemDetails());

        ProblemDetailsConventions.Apply(context);

        var problem = context.ProblemDetails;
        Assert.Equal(500, problem.Status);
        Assert.Equal("/api/customers", problem.Instance);
        Assert.Equal(ProblemCodes.ServerError, problem.Extensions["code"]);
        Assert.Equal("trace-1", problem.Extensions["traceId"]);
    }

    [Fact]
    public void Apply_keeps_a_code_and_instance_that_are_already_set()
    {
        var context = NewContext(
            StatusCodes.Status404NotFound,
            new ProblemDetails { Status = 404, Instance = "/custom", Extensions = { ["code"] = "customer.not_found" } });

        ProblemDetailsConventions.Apply(context);

        Assert.Equal("/custom", context.ProblemDetails.Instance);
        Assert.Equal("customer.not_found", context.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData(400, ProblemCodes.MalformedRequest)]
    [InlineData(404, ProblemCodes.RouteNotFound)]
    [InlineData(405, ProblemCodes.MethodNotAllowed)]
    [InlineData(415, ProblemCodes.UnsupportedMediaType)]
    [InlineData(422, ProblemCodes.BusinessRuleViolated)]
    [InlineData(503, ProblemCodes.ServerError)]
    [InlineData(429, ProblemCodes.RequestFailed)]
    public void ForStatus_maps_statuses_to_stable_codes(int status, string code) =>
        Assert.Equal(code, ProblemCodes.ForStatus(status));

    private static ProblemDetailsContext NewContext(int status, ProblemDetails problem)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        http.Request.Path = "/api/customers";
        http.Response.StatusCode = status;
        return new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem };
    }
}

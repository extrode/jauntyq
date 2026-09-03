using Microsoft.AspNetCore.Http;

namespace Conduit.SqlServer.Tests.Contracts;

public sealed record ErrorResponse(Dictionary<string, string[]> Errors);

/// <summary>
/// RealWorld's spec expects a bare {"errors": {"field": ["msg"]}} shape.
/// Results.ValidationProblem's default RFC7807 shape adds title/status/
/// traceId siblings that don't match, so this is a small custom 422 result
/// instead of the built-in helper.
/// </summary>
public static class Results422
{
    public static IResult ValidationError(string field, params string[] messages) =>
        Results.Json(new ErrorResponse(new Dictionary<string, string[]> { [field] = messages }), statusCode: 422);

    public static IResult ValidationError(Dictionary<string, string[]> errors) =>
        Results.Json(new ErrorResponse(errors), statusCode: 422);
}

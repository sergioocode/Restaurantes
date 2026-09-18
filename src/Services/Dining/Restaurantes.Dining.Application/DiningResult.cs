namespace Restaurantes.Dining.Application;

public enum DiningOutcome
{
    Ok,
    Created,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Problem,
}

public sealed record DiningResult(
    DiningOutcome Outcome,
    object? Value = null,
    string? Location = null,
    int? GuestCount = null,
    int? ProblemStatusCode = null,
    string? ProblemTitle = null,
    string? ProblemDetail = null
);

internal static class DiningResults
{
    public static DiningResult Ok(object value)
    {
        return new(DiningOutcome.Ok, value);
    }

    public static DiningResult Created(string location, object value)
    {
        return new(DiningOutcome.Created, value, location);
    }

    public static DiningResult NoContent(int? guestCount = null)
    {
        return new(DiningOutcome.NoContent, GuestCount: guestCount);
    }

    public static DiningResult BadRequest(object value)
    {
        return new(DiningOutcome.BadRequest, value);
    }

    public static DiningResult Unauthorized()
    {
        return new(DiningOutcome.Unauthorized);
    }

    public static DiningResult Forbid()
    {
        return new(DiningOutcome.Forbidden);
    }

    public static DiningResult NotFound(object? value = null)
    {
        return new(DiningOutcome.NotFound, value);
    }

    public static DiningResult Conflict(object value)
    {
        return new(DiningOutcome.Conflict, value);
    }

    public static DiningResult Problem(int statusCode, string? title = null, string? detail = null)
    {
        return new(
            DiningOutcome.Problem,
            ProblemStatusCode: statusCode,
            ProblemTitle: title,
            ProblemDetail: detail
        );
    }
}

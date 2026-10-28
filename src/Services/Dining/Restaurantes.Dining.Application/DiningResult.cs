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
    Gone,
    Problem,
}

public sealed record DiningResult(
    DiningOutcome Outcome,
    object? Value = null,
    string? Location = null,
    int? GuestCount = null,
    int? ProblemStatusCode = null,
    string? ProblemTitle = null,
    string? ProblemDetail = null,
    Guid? ZoneId = null,
    string? ZoneName = null,
    Guid? RestaurantId = null
);

internal static class DiningResults
{
    public static DiningResult Ok(object value, Guid? restaurantId = null)
    {
        return new(DiningOutcome.Ok, value, RestaurantId: restaurantId);
    }

    public static DiningResult Created(string location, object value, Guid? restaurantId = null)
    {
        return new(DiningOutcome.Created, value, location, RestaurantId: restaurantId);
    }

    public static DiningResult NoContent(
        int? guestCount = null,
        Guid? zoneId = null,
        string? zoneName = null,
        Guid? restaurantId = null
    )
    {
        return new(
            DiningOutcome.NoContent,
            GuestCount: guestCount,
            ZoneId: zoneId,
            ZoneName: zoneName,
            RestaurantId: restaurantId
        );
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

    public static DiningResult Gone(object value, Guid? restaurantId = null)
    {
        return new(DiningOutcome.Gone, value, RestaurantId: restaurantId);
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

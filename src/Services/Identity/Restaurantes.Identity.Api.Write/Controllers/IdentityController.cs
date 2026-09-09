using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Write.Controllers;

[ApiController, Authorize, Route("api/identity")]
public sealed class IdentityController(IdentityService service) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")]
    public Task<IActionResult> Login(LoginRequest request, CancellationToken ct) =>
        Execute(() => service.Login(request, ct));

    [HttpGet("me")]
    public IActionResult Me() =>
        Ok(
            new
            {
                id = User.FindFirstValue(ClaimTypes.NameIdentifier),
                name = User.Identity?.Name,
                globalRoles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(),
                restaurants = User.FindAll(RestaurantClaimTypes.RestaurantId)
                    .Select(x => x.Value)
                    .Distinct(),
            }
        );

    [HttpGet("users")]
    public Task<IActionResult> ListUsers(CancellationToken ct) =>
        Execute(() => service.ListUsers(CurrentActor(), ct));

    [HttpPost("users")]
    public Task<IActionResult> CreateUser(CreateStaffUserRequest request, CancellationToken ct) =>
        Execute(() => service.CreateUser(request, CurrentActor(), ct));

    [HttpPost("users/{userId:guid}/restaurants")]
    public Task<IActionResult> AssignRestaurant(
        Guid userId,
        AssignRestaurantRequest request,
        CancellationToken ct
    ) => Execute(() => service.AssignRestaurant(userId, request, CurrentActor(), ct));

    [HttpPut("users/{userId:guid}/restaurants/{restaurantId:guid}")]
    public Task<IActionResult> UpdateRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        UpdateRestaurantAssignmentRequest request,
        CancellationToken ct
    ) =>
        Execute(() =>
            service.UpdateRestaurantAssignment(userId, restaurantId, request, CurrentActor(), ct)
        );

    [HttpDelete("users/{userId:guid}/restaurants/{restaurantId:guid}")]
    public Task<IActionResult> DisableRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        CancellationToken ct
    ) =>
        Execute(() =>
            service.DisableRestaurantAssignment(userId, restaurantId, CurrentActor(), ct)
        );

    private IdentityActor CurrentActor()
    {
        HashSet<Guid> manageableRestaurantIds = User.FindAll(RestaurantClaimTypes.RestaurantId)
            .Select(claim => Guid.TryParse(claim.Value, out Guid id) ? id : (Guid?)null)
            .Where(id =>
                id.HasValue
                && User.CanAccessRestaurant(id.Value, RestaurantPermissions.IdentityManage)
            )
            .Select(id => id!.Value)
            .ToHashSet();
        return new IdentityActor(User.IsInRole("Admin"), manageableRestaurantIds);
    }

    private async Task<IActionResult> Execute(Func<Task<IdentityResult>> operation)
    {
        IdentityResult result = await operation();
        return result.Outcome switch
        {
            IdentityOutcome.Ok => Ok(result.Value),
            IdentityOutcome.Created => Created(result.Location!, result.Value),
            IdentityOutcome.NoContent => NoContent(),
            IdentityOutcome.BadRequest => BadRequest(result.Value),
            IdentityOutcome.Unauthorized => Unauthorized(),
            IdentityOutcome.Forbidden => Forbid(),
            IdentityOutcome.NotFound => NotFound(),
            IdentityOutcome.Conflict => Conflict(result.Value),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

using System.Security.Claims;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Auth;

public static class AppClaims
{
    public const string TenantId = "tenant_id";
}

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
        => int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static int? GetTenantId(this ClaimsPrincipal user)
        => int.TryParse(user.FindFirstValue(AppClaims.TenantId), out var id) ? id : null;

    public static string GetNombre(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? "";

    public static Rol? GetRol(this ClaimsPrincipal user)
        => Enum.TryParse<Rol>(user.FindFirstValue(ClaimTypes.Role), out var r) ? r : null;
}

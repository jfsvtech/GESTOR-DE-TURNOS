using GeneradorTurnos.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeneradorTurnos.Tenancy;

/// <summary>
/// Filtro de autorización (corre antes de [Authorize] y del controlador): si la ruta tiene
/// {slug}, resuelve la empresa y la guarda en ITenantContext. 404 si no existe o está inactiva.
/// Las rutas sin {slug} (landing, super admin) se omiten.
/// </summary>
public class TenantResolutionFilter : IAsyncAuthorizationFilter
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenants;

    public TenantResolutionFilter(ITenantContext tenantContext, ITenantRepository tenants)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.RouteData.Values.TryGetValue("slug", out var slugObj)
            && slugObj is string slug && !string.IsNullOrWhiteSpace(slug))
        {
            var tenant = await _tenants.GetBySlugAsync(slug);
            if (tenant is null || !tenant.Activo)
            {
                context.Result = new NotFoundObjectResult($"No se encontró el negocio '{slug}'.");
                return;
            }
            _tenantContext.Set(tenant);
        }
    }
}

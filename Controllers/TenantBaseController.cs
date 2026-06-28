using GeneradorTurnos.Auth;
using GeneradorTurnos.Models;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace GeneradorTurnos.Controllers;

/// <summary>
/// Base para controladores con ruta {slug}. Expone el tenant resuelto y valida
/// que el usuario autenticado pertenezca a esta empresa.
/// </summary>
public abstract class TenantBaseController : Controller
{
    protected readonly ITenantContext Tenant;
    protected TenantBaseController(ITenantContext tenant) => Tenant = tenant;

    protected int TenantId => Tenant.TenantId;
    protected string Slug => Tenant.Slug;

    /// <summary>True si hay un usuario autenticado que pertenece a esta empresa.</summary>
    protected bool UsuarioPerteneceAlTenant()
    {
        if (!Tenant.Resolved) return false;
        if (!(User.Identity?.IsAuthenticated ?? false)) return false;
        var t = User.GetTenantId();
        return t.HasValue && t.Value == TenantId;
    }

    protected int CurrentUserId => User.GetUserId();
    protected Rol? CurrentRol => User.GetRol();

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        // Disponible para las vistas (layout, navbar).
        ViewData["TenantNombre"] = Tenant.Current?.Nombre;
        ViewData["TenantSlug"] = Tenant.Current?.Slug;
        ViewData["TenantFotoUrl"] = Tenant.Current?.FotoUrl;
        ViewData["UsuarioNombre"] = User.Identity?.IsAuthenticated == true ? User.GetNombre() : null;
        ViewData["UsuarioRol"] = UsuarioPerteneceAlTenant() ? User.GetRol()?.ToString() : null;
        base.OnActionExecuting(context);
    }
}

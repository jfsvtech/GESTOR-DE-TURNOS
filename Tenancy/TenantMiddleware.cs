using GeneradorTurnos.Repositories;

namespace GeneradorTurnos.Tenancy;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantHostResolver hostResolver, ITenantContext tenantContext, ITenantRepository tenants)
    {
        if (hostResolver.IsStaticOrSystemPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (hostResolver.IsReservedHost(context.Request.Host, "admin"))
        {
            MapAdminHost(context);
            await _next(context);
            return;
        }

        if (!hostResolver.TryGetSlugFromHost(context.Request.Host, out var hostSlug))
        {
            await _next(context);
            return;
        }

        var tenant = await tenants.GetBySlugAsync(hostSlug);
        if (tenant is null || !tenant.Activo)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Negocio no encontrado.");
            return;
        }

        tenantContext.Set(tenant);
        context.Items["TenantId"] = tenant.Id;
        context.Items["TenantSlug"] = tenant.Slug;
        context.Items["TenantNombre"] = tenant.Nombre;

        var path = context.Request.Path.Value ?? "/";
        var normalizedSlugPath = "/" + tenant.Slug;

        if (path.Equals(normalizedSlugPath, StringComparison.OrdinalIgnoreCase))
        {
            RedirectClean(context, "/");
            return;
        }

        if (path.StartsWith(normalizedSlugPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            var cleanPath = path[normalizedSlugPath.Length..];
            RedirectClean(context, string.IsNullOrWhiteSpace(cleanPath) ? "/" : cleanPath);
            return;
        }

        context.Request.Path = MapTenantPath(tenant.Slug, context.Request.Path);
        await _next(context);
    }

    private static PathString MapTenantPath(string slug, PathString path)
    {
        var value = path.Value ?? "/";
        if (value == "/") return new PathString($"/{slug}");

        if (value.Equals("/login", StringComparison.OrdinalIgnoreCase))
            return new PathString($"/{slug}/cuenta/login");

        if (value.Equals("/registro", StringComparison.OrdinalIgnoreCase))
            return new PathString($"/{slug}/cuenta/registro");

        if (value.Equals("/verificacion-pendiente", StringComparison.OrdinalIgnoreCase))
            return new PathString($"/{slug}/cuenta/verificacion-pendiente");

        if (value.StartsWith("/cuenta", StringComparison.OrdinalIgnoreCase))
            return new PathString($"/{slug}{value}");

        return new PathString($"/{slug}{value}");
    }

    private static void MapAdminHost(HttpContext context)
    {
        var value = context.Request.Path.Value ?? "/";
        if (value == "/")
        {
            context.Request.Path = "/super";
            return;
        }

        if (value.Equals("/login", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Path = "/super/login";
            return;
        }

        if (!value.StartsWith("/super", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Path = "/super" + value;
        }
    }

    private static void RedirectClean(HttpContext context, string cleanPath)
    {
        var target = cleanPath + context.Request.QueryString;
        context.Response.StatusCode = CanRedirectPermanent(context.Request.Method)
            ? StatusCodes.Status308PermanentRedirect
            : StatusCodes.Status307TemporaryRedirect;
        context.Response.Headers.Location = target;
    }

    private static bool CanRedirectPermanent(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
}

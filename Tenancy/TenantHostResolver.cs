using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;

namespace GeneradorTurnos.Tenancy;

public interface ITenantHostResolver
{
    bool IsStaticOrSystemPath(PathString path);
    bool TryGetSlugFromHost(HostString host, out string slug);
    Task<Tenant?> ResolveAsync(HostString host);
}

public class TenantHostResolver : ITenantHostResolver
{
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "admin", "app", "api", "prd"
    };

    private static readonly string[] IgnoredPathPrefixes =
    [
        "/css",
        "/js",
        "/lib",
        "/images",
        "/img",
        "/uploads",
        "/_framework",
        "/health"
    ];

    private static readonly HashSet<string> IgnoredExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/favicon.ico",
        "/manifest.json",
        "/manifest.webmanifest",
        "/service-worker.js",
        "/sw.js"
    };

    private readonly IConfiguration _configuration;
    private readonly ITenantRepository _tenants;

    public TenantHostResolver(IConfiguration configuration, ITenantRepository tenants)
    {
        _configuration = configuration;
        _tenants = tenants;
    }

    public bool IsStaticOrSystemPath(PathString path)
    {
        var value = path.Value ?? "/";
        if (IgnoredExactPaths.Contains(value)) return true;
        return IgnoredPathPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryGetSlugFromHost(HostString host, out string slug)
    {
        slug = "";
        var baseDomain = (_configuration["App:BaseDomain"] ?? "").Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseDomain)) return false;

        var currentHost = host.Host.Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(currentHost)) return false;
        if (currentHost == baseDomain || currentHost == $"www.{baseDomain}") return false;
        if (!currentHost.EndsWith($".{baseDomain}", StringComparison.OrdinalIgnoreCase)) return false;

        var subdomain = currentHost[..^($".{baseDomain}".Length)];
        var firstLabel = subdomain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLabel)) return false;
        if (ReservedSubdomains.Contains(firstLabel)) return false;

        slug = firstLabel;
        return true;
    }

    public async Task<Tenant?> ResolveAsync(HostString host)
    {
        if (!TryGetSlugFromHost(host, out var slug)) return null;
        var tenant = await _tenants.GetBySlugAsync(slug);
        return tenant is { Activo: true } ? tenant : null;
    }
}

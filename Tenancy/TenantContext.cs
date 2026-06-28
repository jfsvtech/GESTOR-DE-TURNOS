using GeneradorTurnos.Models;

namespace GeneradorTurnos.Tenancy;

/// <summary>Datos de la empresa (tenant) resuelta para la petición actual.</summary>
public interface ITenantContext
{
    bool Resolved { get; }
    Tenant? Current { get; }
    int TenantId { get; }
    string Slug { get; }
    void Set(Tenant tenant);
}

public class TenantContext : ITenantContext
{
    public bool Resolved => Current is not null;
    public Tenant? Current { get; private set; }
    public int TenantId => Current?.Id ?? throw new InvalidOperationException("No hay tenant resuelto.");
    public string Slug => Current?.Slug ?? "";
    public void Set(Tenant tenant) => Current = tenant;
}

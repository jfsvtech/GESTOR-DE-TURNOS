using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetBySlugAsync(string slug);
    Task<Tenant?> GetByIdAsync(int id);
    Task<List<Tenant>> GetAllAsync();
    Task<List<SaasTenantResumen>> GetDashboardAsync();
    Task<bool> SlugExistsAsync(string slug);
    Task<int> CreateAsync(Tenant t);
    Task SetActivoAsync(int id, bool activo);
    Task UpdateMaxUsuariosAsync(int id, int maxUsuarios);
    Task SetFotoAsync(int id, string fotoUrl);
}

public class TenantRepository : ITenantRepository
{
    private readonly IDbConnectionFactory _db;
    public TenantRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Tenant?> GetBySlugAsync(string slug)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Tenant>(
            "SELECT * FROM tenants WHERE slug = @slug", new { slug });
    }

    public async Task<Tenant?> GetByIdAsync(int id)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Tenant>(
            "SELECT * FROM tenants WHERE id = @id", new { id });
    }

    public async Task<List<Tenant>> GetAllAsync()
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Tenant>("SELECT * FROM tenants ORDER BY nombre");
        return r.ToList();
    }

    public async Task<List<SaasTenantResumen>> GetDashboardAsync()
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<SaasTenantResumen>(@"
            SELECT
                t.id,
                t.nombre,
                t.slug,
                t.plan,
                t.max_usuarios,
                t.activo,
                t.fecha_creacion,
                COALESCE(u.usuarios, 0) AS usuarios,
                COALESCE(u.trabajadores, 0) AS trabajadores,
                COALESCE(u.clientes, 0) AS clientes,
                COALESCE(tr.turnos, 0) AS turnos,
                COALESCE(tr.ventas, 0) AS ventas
            FROM tenants t
            LEFT JOIN (
                SELECT tenant_id,
                       COUNT(*)::int AS usuarios,
                       COUNT(*) FILTER (WHERE rol = 3)::int AS trabajadores,
                       COUNT(*) FILTER (WHERE rol = 4)::int AS clientes
                FROM usuarios
                WHERE tenant_id IS NOT NULL
                GROUP BY tenant_id
            ) u ON u.tenant_id = t.id
            LEFT JOIN (
                SELECT tenant_id,
                       COUNT(*)::int AS turnos,
                       COALESCE(SUM(precio) FILTER (WHERE estado = 3), 0) AS ventas
                FROM turnos
                GROUP BY tenant_id
            ) tr ON tr.tenant_id = t.id
            ORDER BY ventas DESC, t.nombre");
        return r.ToList();
    }

    public async Task<bool> SlugExistsAsync(string slug)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int?>(
            "SELECT 1 FROM tenants WHERE slug = @slug", new { slug }) is not null;
    }

    public async Task<int> CreateAsync(Tenant t)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO tenants (nombre, slug, plan, max_usuarios, activo)
            VALUES (@Nombre, @Slug, @Plan, @MaxUsuarios, @Activo) RETURNING id", t);
    }

    public async Task SetActivoAsync(int id, bool activo)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE tenants SET activo = @activo WHERE id = @id", new { id, activo });
    }

    public async Task SetFotoAsync(int id, string fotoUrl)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE tenants SET foto_url=@fotoUrl WHERE id=@id", new { id, fotoUrl });
    }

    public async Task UpdateMaxUsuariosAsync(int id, int maxUsuarios)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "UPDATE tenants SET max_usuarios = @maxUsuarios WHERE id = @id",
            new { id, maxUsuarios });
    }
}

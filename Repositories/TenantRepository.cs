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
    Task DeleteAsync(int id);
    Task SetActivoAsync(int id, bool activo);
    Task UpdateMaxUsuariosAsync(int id, int maxUsuarios);
    Task UpdateSuscripcionAsync(Tenant t);
    Task SetFotoAsync(int id, string fotoUrl);
    Task<List<PagoSuscripcion>> GetPagosAsync(int tenantId);
    Task RegistrarPagoAsync(PagoSuscripcion pago);
    Task<List<Auditoria>> GetAuditoriaAsync(int tenantId, int limit = 80);
    Task AddAuditoriaAsync(Auditoria auditoria);
    Task<int> ResetProductionDataAsync(string actor);
}

public class TenantRepository : ITenantRepository
{
    private readonly IDbConnectionFactory _db;
    public TenantRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Tenant?> GetBySlugAsync(string slug)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Tenant>(
            "SELECT * FROM tenants WHERE lower(slug) = lower(@slug) LIMIT 1", new { slug });
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
                t.valor_suscripcion,
                t.max_usuarios,
                t.activo,
                t.suscripcion_vencimiento,
                t.estado_suscripcion,
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
            INSERT INTO tenants
                (nombre, slug, plan, valor_suscripcion, max_usuarios, activo,
                 suscripcion_inicio, suscripcion_vencimiento, estado_suscripcion, recordatorio_pago_dias)
            VALUES
                (@Nombre, @Slug, @Plan, @ValorSuscripcion, @MaxUsuarios, @Activo,
                 @SuscripcionInicio, @SuscripcionVencimiento, @EstadoSuscripcion, @RecordatorioPagoDias)
            RETURNING id", t);
    }

    public async Task DeleteAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM tenants WHERE id = @id", new { id });
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

    public async Task UpdateSuscripcionAsync(Tenant t)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            UPDATE tenants
            SET plan=@Plan,
                valor_suscripcion=@ValorSuscripcion,
                suscripcion_inicio=@SuscripcionInicio,
                suscripcion_vencimiento=@SuscripcionVencimiento,
                estado_suscripcion=@EstadoSuscripcion,
                recordatorio_pago_dias=@RecordatorioPagoDias
            WHERE id=@Id", t);
    }

    public async Task<List<PagoSuscripcion>> GetPagosAsync(int tenantId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<PagoSuscripcion>(
            "SELECT * FROM pagos_suscripcion WHERE tenant_id=@tenantId ORDER BY fecha_pago DESC",
            new { tenantId });
        return r.ToList();
    }

    public async Task RegistrarPagoAsync(PagoSuscripcion pago)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            INSERT INTO pagos_suscripcion (tenant_id, monto, periodo_inicio, periodo_fin, metodo, referencia, nota)
            VALUES (@TenantId, @Monto, @PeriodoInicio, @PeriodoFin, @Metodo, @Referencia, @Nota)", pago);
    }

    public async Task<List<Auditoria>> GetAuditoriaAsync(int tenantId, int limit = 80)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Auditoria>(@"
            SELECT * FROM auditoria
            WHERE tenant_id=@tenantId
            ORDER BY fecha_creacion DESC
            LIMIT @limit", new { tenantId, limit });
        return r.ToList();
    }

    public async Task AddAuditoriaAsync(Auditoria auditoria)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            INSERT INTO auditoria (tenant_id, usuario_id, actor_nombre, accion, entidad, entidad_id, detalle)
            VALUES (@TenantId, @UsuarioId, @ActorNombre, @Accion, @Entidad, @EntidadId, @Detalle)", auditoria);
    }

    public async Task<int> ResetProductionDataAsync(string actor)
    {
        using var c = _db.Create();
        c.Open();
        using var tx = c.BeginTransaction();

        var superAdmins = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM usuarios WHERE tenant_id IS NULL AND rol = @superAdmin",
            new { superAdmin = (int)Rol.SuperAdmin },
            tx);
        if (superAdmins == 0)
        {
            tx.Rollback();
            throw new InvalidOperationException(
                "No hay superadmins globales configurados. Define SuperAdmin__BootstrapPassword y reinicia antes de limpiar datos.");
        }

        var tenants = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tenants", transaction: tx);
        await c.ExecuteAsync("DELETE FROM tenants", transaction: tx);
        await c.ExecuteAsync(
            "DELETE FROM usuarios WHERE tenant_id IS NULL AND rol <> @superAdmin",
            new { superAdmin = (int)Rol.SuperAdmin },
            tx);
        await c.ExecuteAsync(@"
            INSERT INTO auditoria (tenant_id, actor_nombre, accion, entidad, detalle)
            VALUES (NULL, @actor, 'Reset produccion', 'SaaS', @detalle)",
            new { actor, detalle = $"Se eliminaron {tenants} empresas y sus datos asociados." },
            tx);

        tx.Commit();
        return tenants;
    }
}

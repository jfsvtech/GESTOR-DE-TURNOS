using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface IServicioRepository
{
    Task<List<Servicio>> GetByTenantAsync(int tenantId, bool soloActivos = false);
    Task<Servicio?> GetByIdAsync(int tenantId, int id);
    Task<int> CreateAsync(Servicio s);
    Task UpdateAsync(Servicio s);
    Task SetActivoAsync(int tenantId, int id, bool activo);
    Task<List<Servicio>> GetByEmpleadoAsync(int tenantId, int empleadoId, bool soloActivos = true);
    Task<Servicio?> GetEfectivoAsync(int tenantId, int empleadoId, int servicioId);
    Task<List<int>> GetServicioIdsByEmpleadoAsync(int tenantId, int empleadoId);
    Task SetServiciosEmpleadoAsync(int tenantId, int empleadoId, IEnumerable<int> servicioIds);
    Task<List<ProfesionalDeServicio>> GetProfesionalesPorServicioAsync(int tenantId, int servicioId);
    Task<List<ServicioBarbero>> GetCatalogoConOverridesAsync(int tenantId, int empleadoId);
    Task UpsertOverrideAsync(int tenantId, int empleadoId, int servicioId, bool ofrecido, decimal? precio, int? duracion);
    Task SolicitarCambioAsync(ServicioSolicitud solicitud);
    Task<List<ServicioSolicitudDetalle>> GetSolicitudesPendientesAsync(int tenantId);
    Task<ServicioSolicitud?> GetSolicitudAsync(int tenantId, int id);
    Task ResolverSolicitudAsync(int tenantId, int id, bool aprobada);
}

public class ServicioRepository : IServicioRepository
{
    private readonly IDbConnectionFactory _db;
    public ServicioRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<Servicio>> GetByTenantAsync(int tenantId, bool soloActivos = false)
    {
        using var c = _db.Create();
        var sql = "SELECT * FROM servicios WHERE tenant_id=@tenantId"
                  + (soloActivos ? " AND activo=TRUE" : "") + " ORDER BY nombre";
        return (await c.QueryAsync<Servicio>(sql, new { tenantId })).ToList();
    }

    public async Task<Servicio?> GetByIdAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Servicio>(
            "SELECT * FROM servicios WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId });
    }

    public async Task<int> CreateAsync(Servicio s)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO servicios (tenant_id, nombre, duracion_minutos, precio, activo)
            VALUES (@TenantId, @Nombre, @DuracionMinutos, @Precio, @Activo) RETURNING id", s);
    }

    public async Task UpdateAsync(Servicio s)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            UPDATE servicios SET nombre=@Nombre, duracion_minutos=@DuracionMinutos,
                   precio=@Precio, activo=@Activo
            WHERE id=@Id AND tenant_id=@TenantId", s);
    }

    public async Task SetActivoAsync(int tenantId, int id, bool activo)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "UPDATE servicios SET activo=@activo WHERE id=@id AND tenant_id=@tenantId",
            new { tenantId, id, activo });
    }

    public async Task<List<Servicio>> GetByEmpleadoAsync(int tenantId, int empleadoId, bool soloActivos = true)
    {
        using var c = _db.Create();
        // Precio y duración EFECTIVOS del profesional (override si existe, si no el del catálogo).
        var sql = @"SELECT s.id, s.tenant_id, s.nombre,
                           COALESCE(es.duracion_override, s.duracion_minutos) AS duracion_minutos,
                           COALESCE(es.precio_override, s.precio)             AS precio,
                           s.activo
                    FROM servicios s
                    JOIN empleado_servicios es ON es.servicio_id = s.id AND es.tenant_id = s.tenant_id
                    WHERE s.tenant_id=@tenantId AND es.tenant_id=@tenantId AND es.empleado_id=@empleadoId"
                  + (soloActivos ? " AND s.activo=TRUE" : "") + " ORDER BY s.nombre";
        return (await c.QueryAsync<Servicio>(sql, new { tenantId, empleadoId })).ToList();
    }

    public async Task<Servicio?> GetEfectivoAsync(int tenantId, int empleadoId, int servicioId)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Servicio>(@"
            SELECT s.id, s.tenant_id, s.nombre,
                   COALESCE(es.duracion_override, s.duracion_minutos) AS duracion_minutos,
                   COALESCE(es.precio_override, s.precio)             AS precio,
                   s.activo
            FROM servicios s
            JOIN empleado_servicios es ON es.servicio_id = s.id AND es.tenant_id = s.tenant_id
            WHERE s.tenant_id=@tenantId AND es.tenant_id=@tenantId AND es.empleado_id=@empleadoId AND s.id=@servicioId",
            new { tenantId, empleadoId, servicioId });
    }

    public async Task<List<ProfesionalDeServicio>> GetProfesionalesPorServicioAsync(int tenantId, int servicioId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<ProfesionalDeServicio>(@"
            SELECT u.id AS empleado_id, u.nombre, u.foto_url,
                   COALESCE(es.precio_override, s.precio)             AS precio,
                   COALESCE(es.duracion_override, s.duracion_minutos) AS duracion_minutos
            FROM empleado_servicios es
            JOIN usuarios u  ON u.id = es.empleado_id AND u.tenant_id = es.tenant_id
            JOIN servicios s ON s.id = es.servicio_id AND s.tenant_id = es.tenant_id
            WHERE es.tenant_id=@tenantId AND es.servicio_id=@servicioId
              AND u.activo = TRUE AND u.atiende = TRUE AND s.activo = TRUE
            ORDER BY u.nombre", new { tenantId, servicioId });
        return r.ToList();
    }

    public async Task<List<ServicioBarbero>> GetCatalogoConOverridesAsync(int tenantId, int empleadoId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<ServicioBarbero>(@"
            SELECT s.id AS servicio_id, s.nombre, s.duracion_minutos AS duracion_base, s.precio AS precio_base,
                   s.activo, (es.id IS NOT NULL) AS ofrecido,
                   es.precio_override, es.duracion_override
            FROM servicios s
            LEFT JOIN empleado_servicios es ON es.servicio_id = s.id AND es.empleado_id = @empleadoId AND es.tenant_id = s.tenant_id
            WHERE s.tenant_id=@tenantId AND s.activo = TRUE
            ORDER BY s.nombre", new { tenantId, empleadoId });
        return r.ToList();
    }

    public async Task UpsertOverrideAsync(int tenantId, int empleadoId, int servicioId, bool ofrecido, decimal? precio, int? duracion)
    {
        using var c = _db.Create();
        if (!ofrecido)
        {
            await c.ExecuteAsync(
                "DELETE FROM empleado_servicios WHERE tenant_id=@tenantId AND empleado_id=@empleadoId AND servicio_id=@servicioId",
                new { tenantId, empleadoId, servicioId });
            return;
        }
        await c.ExecuteAsync(@"
            INSERT INTO empleado_servicios (tenant_id, empleado_id, servicio_id, precio_override, duracion_override)
            VALUES (@tenantId, @empleadoId, @servicioId, @precio, @duracion)
            ON CONFLICT (empleado_id, servicio_id)
            DO UPDATE SET precio_override = EXCLUDED.precio_override, duracion_override = EXCLUDED.duracion_override",
            new { tenantId, empleadoId, servicioId, precio, duracion });
    }

    public async Task SolicitarCambioAsync(ServicioSolicitud solicitud)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            INSERT INTO servicio_solicitudes
                (tenant_id, empleado_id, servicio_id, ofrecido, precio_override, duracion_override)
            VALUES
                (@TenantId, @EmpleadoId, @ServicioId, @Ofrecido, @PrecioOverride, @DuracionOverride)",
            solicitud);
    }

    public async Task<List<ServicioSolicitudDetalle>> GetSolicitudesPendientesAsync(int tenantId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<ServicioSolicitudDetalle>(@"
            SELECT ss.id,
                   ss.empleado_id,
                   u.nombre AS empleado_nombre,
                   ss.servicio_id,
                   s.nombre AS servicio_nombre,
                   ss.ofrecido,
                   ss.precio_override,
                   ss.duracion_override,
                   ss.fecha_creacion
            FROM servicio_solicitudes ss
            JOIN usuarios u ON u.id = ss.empleado_id
            JOIN servicios s ON s.id = ss.servicio_id
            WHERE ss.tenant_id=@tenantId AND ss.estado='Pendiente'
            ORDER BY ss.fecha_creacion DESC", new { tenantId });
        return r.ToList();
    }

    public async Task<ServicioSolicitud?> GetSolicitudAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<ServicioSolicitud>(
            "SELECT * FROM servicio_solicitudes WHERE tenant_id=@tenantId AND id=@id",
            new { tenantId, id });
    }

    public async Task ResolverSolicitudAsync(int tenantId, int id, bool aprobada)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            UPDATE servicio_solicitudes
            SET estado=@estado, fecha_decision=now()
            WHERE tenant_id=@tenantId AND id=@id",
            new { tenantId, id, estado = aprobada ? "Aprobada" : "Rechazada" });
    }

    public async Task<List<int>> GetServicioIdsByEmpleadoAsync(int tenantId, int empleadoId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<int>(
            "SELECT servicio_id FROM empleado_servicios WHERE tenant_id=@tenantId AND empleado_id=@empleadoId",
            new { tenantId, empleadoId });
        return r.ToList();
    }

    public async Task SetServiciosEmpleadoAsync(int tenantId, int empleadoId, IEnumerable<int> servicioIds)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "DELETE FROM empleado_servicios WHERE tenant_id=@tenantId AND empleado_id=@empleadoId",
            new { tenantId, empleadoId });
        foreach (var s in servicioIds.Distinct())
            await c.ExecuteAsync(@"
                INSERT INTO empleado_servicios (tenant_id, empleado_id, servicio_id)
                VALUES (@tenantId, @empleadoId, @s)", new { tenantId, empleadoId, s });
    }
}

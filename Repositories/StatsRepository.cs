using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

/// <summary>Consultas de agregación para los dashboards. Ingresos = turnos completados.</summary>
public interface IStatsRepository
{
    Task<ResumenStats> ResumenAsync(int tenantId, DateTime desde, DateTime hasta, int? empleadoId = null);
    Task<List<SerieFecha>> SeriePorDiaAsync(int tenantId, DateTime desde, DateTime hasta, int? empleadoId = null);
    Task<List<RankingItem>> ServiciosTopAsync(int tenantId, DateTime desde, DateTime hasta, int? empleadoId = null);
    Task<List<RankingItem>> IngresosPorBarberoAsync(int tenantId, DateTime desde, DateTime hasta);
    Task<List<ResumenBarbero>> ResumenPorBarberoAsync(int tenantId, DateTime desde, DateTime hasta);
}

public class StatsRepository : IStatsRepository
{
    private readonly IDbConnectionFactory _db;
    public StatsRepository(IDbConnectionFactory db) => _db = db;

    private const string EmpFilter = " AND (@empleadoId IS NULL OR t.empleado_id = @empleadoId) ";

    public async Task<ResumenStats> ResumenAsync(int tenantId, DateTime desde, DateTime hasta, int? empleadoId = null)
    {
        using var c = _db.Create();
        var p = new { tenantId, desde, hasta, empleadoId,
                      comp = (int)EstadoTurno.Completado, canc = (int)EstadoTurno.Cancelado,
                      pend = (int)EstadoTurno.Pendiente, conf = (int)EstadoTurno.Confirmado };
        return await c.QuerySingleAsync<ResumenStats>($@"
            SELECT
              COUNT(*)                                                   AS turnos_totales,
              COUNT(*) FILTER (WHERE t.estado = @comp)                   AS turnos_completados,
              COUNT(*) FILTER (WHERE t.estado = @canc)                   AS turnos_cancelados,
              COUNT(*) FILTER (WHERE t.estado IN (@pend, @conf))         AS turnos_pendientes,
              COALESCE(SUM(t.precio) FILTER (WHERE t.estado = @comp), 0) AS ingresos_totales,
              COALESCE(AVG(t.precio) FILTER (WHERE t.estado = @comp), 0) AS ticket_promedio,
              COUNT(DISTINCT t.cliente_id)                              AS clientes_unicos
            FROM turnos t
            WHERE t.tenant_id=@tenantId
              AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta {EmpFilter}", p);
    }

    public async Task<List<SerieFecha>> SeriePorDiaAsync(int tenantId, DateTime desde, DateTime hasta, int? empleadoId = null)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<SerieFecha>($@"
            SELECT date_trunc('day', t.fecha_hora_inicio) AS fecha,
                   COALESCE(SUM(t.precio) FILTER (WHERE t.estado=@comp), 0) AS valor,
                   COUNT(*) AS cantidad
            FROM turnos t
            WHERE t.tenant_id=@tenantId
              AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta {EmpFilter}
            GROUP BY 1 ORDER BY 1",
            new { tenantId, desde, hasta, empleadoId, comp = (int)EstadoTurno.Completado });
        return r.ToList();
    }

    public async Task<List<RankingItem>> ServiciosTopAsync(int tenantId, DateTime desde, DateTime hasta, int? empleadoId = null)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<RankingItem>($@"
            SELECT s.nombre AS etiqueta,
                   COALESCE(SUM(t.precio) FILTER (WHERE t.estado=@comp), 0) AS valor,
                   COUNT(*) AS cantidad
            FROM turnos t JOIN servicios s ON s.id = t.servicio_id
            WHERE t.tenant_id=@tenantId
              AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta {EmpFilter}
            GROUP BY s.nombre ORDER BY cantidad DESC LIMIT 10",
            new { tenantId, desde, hasta, empleadoId, comp = (int)EstadoTurno.Completado });
        return r.ToList();
    }

    public async Task<List<RankingItem>> IngresosPorBarberoAsync(int tenantId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<RankingItem>(@"
            SELECT e.nombre AS etiqueta,
                   COALESCE(SUM(t.precio) FILTER (WHERE t.estado=@comp), 0) AS valor,
                   COUNT(*) FILTER (WHERE t.estado=@comp) AS cantidad
            FROM turnos t JOIN usuarios e ON e.id = t.empleado_id
            WHERE t.tenant_id=@tenantId
              AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta
            GROUP BY e.nombre ORDER BY valor DESC",
            new { tenantId, desde, hasta, comp = (int)EstadoTurno.Completado });
        return r.ToList();
    }

    public async Task<List<ResumenBarbero>> ResumenPorBarberoAsync(int tenantId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        var p = new
        {
            tenantId,
            desde,
            hasta,
            pend = (int)EstadoTurno.Pendiente,
            conf = (int)EstadoTurno.Confirmado,
            comp = (int)EstadoTurno.Completado,
            canc = (int)EstadoTurno.Cancelado,
            noshow = (int)EstadoTurno.NoShow
        };
        var r = await c.QueryAsync<ResumenBarbero>(@"
            SELECT e.id AS empleado_id, e.nombre,
                   e.comision_tipo, e.comision_valor,
                   COUNT(*) FILTER (WHERE t.estado=@comp)                    AS completados,
                   COUNT(*) FILTER (WHERE t.estado=@canc)                    AS cancelados,
                   COUNT(*) FILTER (WHERE t.estado=@noshow)                  AS no_show,
                   COUNT(*) FILTER (WHERE t.estado=@pend)                    AS pendientes,
                   COUNT(*) FILTER (WHERE t.estado=@conf)                    AS confirmados,
                   COUNT(t.id)                                               AS total,
                   COALESCE(SUM(t.precio) FILTER (WHERE t.estado=@comp), 0)  AS ingresos,
                   COALESCE(AVG(t.precio) FILTER (WHERE t.estado=@comp), 0)  AS ticket_promedio,
                   COUNT(DISTINCT t.cliente_id) FILTER (WHERE t.id IS NOT NULL) AS clientes_unicos
            FROM usuarios e
            LEFT JOIN turnos t ON t.empleado_id = e.id AND t.tenant_id=@tenantId
                 AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta
            WHERE e.tenant_id=@tenantId AND e.atiende = TRUE
            GROUP BY e.id, e.nombre, e.comision_tipo, e.comision_valor
            ORDER BY ingresos DESC", p);
        return r.ToList();
    }
}

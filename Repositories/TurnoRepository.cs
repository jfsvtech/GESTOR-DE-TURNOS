using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface ITurnoRepository
{
    Task<List<Turno>> GetActivosDelDiaAsync(int tenantId, int empleadoId, DateTime dia);
    Task<bool> HaySolapamientoAsync(int tenantId, int empleadoId, DateTime inicio, DateTime fin, int? excluirId = null);
    Task<int> CrearAsync(Turno t);
    Task<Turno?> GetByIdAsync(int tenantId, int id);
    Task<List<TurnoDetalle>> GetByClienteAsync(int tenantId, int clienteId);
    Task<List<TurnoDetalle>> GetByEmpleadoRangoAsync(int tenantId, int empleadoId, DateTime desde, DateTime hasta);
    Task<List<TurnoDetalle>> GetByTenantRangoAsync(int tenantId, DateTime desde, DateTime hasta);
    Task ActualizarEstadoAsync(int tenantId, int id, EstadoTurno estado);
    Task ReprogramarAsync(int tenantId, int id, DateTime inicio, DateTime fin);
}

public class TurnoRepository : ITurnoRepository
{
    private readonly IDbConnectionFactory _db;
    public TurnoRepository(IDbConnectionFactory db) => _db = db;

    private const string DetalleSql = @"
        SELECT t.id, t.tenant_id, t.cliente_id, t.empleado_id, t.servicio_id,
               t.fecha_hora_inicio, t.fecha_hora_fin, t.estado, t.precio, t.notas, t.fecha_creacion,
               c.nombre   AS cliente_nombre,
               c.cedula   AS cliente_cedula,
               c.telefono AS cliente_telefono,
               c.email    AS cliente_email,
               e.nombre   AS empleado_nombre,
               e.email    AS empleado_email,
               s.nombre   AS servicio_nombre,
               s.duracion_minutos AS duracion_minutos
        FROM turnos t
        JOIN usuarios c  ON c.id = t.cliente_id
        JOIN usuarios e  ON e.id = t.empleado_id
        JOIN servicios s ON s.id = t.servicio_id ";

    public async Task<List<Turno>> GetActivosDelDiaAsync(int tenantId, int empleadoId, DateTime dia)
    {
        var d0 = dia.Date;
        var d1 = d0.AddDays(1);
        using var c = _db.Create();
        var r = await c.QueryAsync<Turno>(@"
            SELECT * FROM turnos
            WHERE tenant_id=@tenantId AND empleado_id=@empleadoId
              AND estado NOT IN (@cancelado, @noshow)
              AND fecha_hora_inicio >= @d0 AND fecha_hora_inicio < @d1
            ORDER BY fecha_hora_inicio",
            new { tenantId, empleadoId, d0, d1, cancelado = (int)EstadoTurno.Cancelado, noshow = (int)EstadoTurno.NoShow });
        return r.ToList();
    }

    public async Task<bool> HaySolapamientoAsync(int tenantId, int empleadoId, DateTime inicio, DateTime fin, int? excluirId = null)
    {
        using var c = _db.Create();
        var n = await c.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM turnos
            WHERE tenant_id=@tenantId AND empleado_id=@empleadoId
              AND estado NOT IN (@cancelado, @noshow)
              AND fecha_hora_inicio < @fin AND fecha_hora_fin > @inicio
              AND (@excluir IS NULL OR id <> @excluir)",
            new { tenantId, empleadoId, inicio, fin, excluir = excluirId,
                  cancelado = (int)EstadoTurno.Cancelado, noshow = (int)EstadoTurno.NoShow });
        return n > 0;
    }

    public async Task<int> CrearAsync(Turno t)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO turnos (tenant_id, cliente_id, empleado_id, servicio_id,
                                fecha_hora_inicio, fecha_hora_fin, estado, precio, notas, origen)
            VALUES (@TenantId, @ClienteId, @EmpleadoId, @ServicioId,
                    @FechaHoraInicio, @FechaHoraFin, @Estado, @Precio, @Notas, @Origen)
            RETURNING id",
            new { t.TenantId, t.ClienteId, t.EmpleadoId, t.ServicioId,
                  t.FechaHoraInicio, t.FechaHoraFin, Estado = (int)t.Estado, t.Precio, t.Notas, Origen = (int)t.Origen });
    }

    public async Task<Turno?> GetByIdAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Turno>(
            "SELECT * FROM turnos WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId });
    }

    public async Task<List<TurnoDetalle>> GetByClienteAsync(int tenantId, int clienteId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<TurnoDetalle>(
            DetalleSql + "WHERE t.tenant_id=@tenantId AND t.cliente_id=@clienteId ORDER BY t.fecha_hora_inicio DESC",
            new { tenantId, clienteId });
        return r.ToList();
    }

    public async Task<List<TurnoDetalle>> GetByEmpleadoRangoAsync(int tenantId, int empleadoId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<TurnoDetalle>(
            DetalleSql + @"WHERE t.tenant_id=@tenantId AND t.empleado_id=@empleadoId
                            AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta
                            ORDER BY t.fecha_hora_inicio",
            new { tenantId, empleadoId, desde, hasta });
        return r.ToList();
    }

    public async Task<List<TurnoDetalle>> GetByTenantRangoAsync(int tenantId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<TurnoDetalle>(
            DetalleSql + @"WHERE t.tenant_id=@tenantId
                            AND t.fecha_hora_inicio >= @desde AND t.fecha_hora_inicio < @hasta
                            ORDER BY t.fecha_hora_inicio",
            new { tenantId, desde, hasta });
        return r.ToList();
    }

    public async Task ActualizarEstadoAsync(int tenantId, int id, EstadoTurno estado)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "UPDATE turnos SET estado=@estado WHERE id=@id AND tenant_id=@tenantId",
            new { estado = (int)estado, id, tenantId });
    }

    public async Task ReprogramarAsync(int tenantId, int id, DateTime inicio, DateTime fin)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            UPDATE turnos SET fecha_hora_inicio=@inicio, fecha_hora_fin=@fin, estado=@pendiente
            WHERE id=@id AND tenant_id=@tenantId",
            new { inicio, fin, id, tenantId, pendiente = (int)EstadoTurno.Pendiente });
    }
}

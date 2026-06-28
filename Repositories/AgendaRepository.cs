using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

/// <summary>Horarios de trabajo recurrentes y bloqueos puntuales de los profesionales.</summary>
public interface IAgendaRepository
{
    Task<List<HorarioTrabajo>> GetHorariosAsync(int tenantId, int empleadoId);
    Task ReemplazarHorariosAsync(int tenantId, int empleadoId, IEnumerable<HorarioTrabajo> horarios);
    Task<List<Bloqueo>> GetBloqueosEnRangoAsync(int tenantId, int empleadoId, DateTime desde, DateTime hasta);
    Task<int> CrearBloqueoAsync(Bloqueo b);
    Task EliminarBloqueoAsync(int tenantId, int id);
    Task<List<Bloqueo>> GetBloqueosFuturosAsync(int tenantId, int empleadoId);
}

public class AgendaRepository : IAgendaRepository
{
    private readonly IDbConnectionFactory _db;
    public AgendaRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<HorarioTrabajo>> GetHorariosAsync(int tenantId, int empleadoId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<HorarioTrabajo>(
            "SELECT * FROM horarios_trabajo WHERE tenant_id=@tenantId AND empleado_id=@empleadoId ORDER BY dia_semana, hora_inicio",
            new { tenantId, empleadoId });
        return r.ToList();
    }

    public async Task ReemplazarHorariosAsync(int tenantId, int empleadoId, IEnumerable<HorarioTrabajo> horarios)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "DELETE FROM horarios_trabajo WHERE tenant_id=@tenantId AND empleado_id=@empleadoId",
            new { tenantId, empleadoId });
        foreach (var h in horarios)
            await c.ExecuteAsync(@"
                INSERT INTO horarios_trabajo (tenant_id, empleado_id, dia_semana, hora_inicio, hora_fin)
                VALUES (@tenantId, @empleadoId, @DiaSemana, @HoraInicio, @HoraFin)",
                new { tenantId, empleadoId, h.DiaSemana, h.HoraInicio, h.HoraFin });
    }

    public async Task<List<Bloqueo>> GetBloqueosEnRangoAsync(int tenantId, int empleadoId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Bloqueo>(@"
            SELECT * FROM bloqueos
            WHERE tenant_id=@tenantId AND empleado_id=@empleadoId
              AND fecha_hora_inicio < @hasta AND fecha_hora_fin > @desde",
            new { tenantId, empleadoId, desde, hasta });
        return r.ToList();
    }

    public async Task<int> CrearBloqueoAsync(Bloqueo b)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO bloqueos (tenant_id, empleado_id, fecha_hora_inicio, fecha_hora_fin, motivo)
            VALUES (@TenantId, @EmpleadoId, @FechaHoraInicio, @FechaHoraFin, @Motivo) RETURNING id", b);
    }

    public async Task EliminarBloqueoAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM bloqueos WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId });
    }

    public async Task<List<Bloqueo>> GetBloqueosFuturosAsync(int tenantId, int empleadoId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Bloqueo>(@"
            SELECT * FROM bloqueos
            WHERE tenant_id=@tenantId AND empleado_id=@empleadoId AND fecha_hora_fin >= now()
            ORDER BY fecha_hora_inicio", new { tenantId, empleadoId });
        return r.ToList();
    }
}

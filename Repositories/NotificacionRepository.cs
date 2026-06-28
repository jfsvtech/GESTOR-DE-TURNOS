using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface INotificacionRepository
{
    Task CrearAsync(Notificacion n);
    Task<List<Notificacion>> GetNoLeidasAsync(int tenantId, int usuarioId, int take = 8);
    Task MarcarLeidaAsync(int tenantId, int usuarioId, int id);
}

public class NotificacionRepository : INotificacionRepository
{
    private readonly IDbConnectionFactory _db;
    public NotificacionRepository(IDbConnectionFactory db) => _db = db;

    public async Task CrearAsync(Notificacion n)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            INSERT INTO notificaciones (tenant_id, usuario_id, turno_id, tipo, titulo, mensaje)
            VALUES (@TenantId, @UsuarioId, @TurnoId, @Tipo, @Titulo, @Mensaje)", n);
    }

    public async Task<List<Notificacion>> GetNoLeidasAsync(int tenantId, int usuarioId, int take = 8)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Notificacion>(@"
            SELECT * FROM notificaciones
            WHERE tenant_id=@tenantId AND usuario_id=@usuarioId AND leida=FALSE
            ORDER BY fecha_creacion DESC
            LIMIT @take", new { tenantId, usuarioId, take });
        return r.ToList();
    }

    public async Task MarcarLeidaAsync(int tenantId, int usuarioId, int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            UPDATE notificaciones SET leida=TRUE
            WHERE id=@id AND tenant_id=@tenantId AND usuario_id=@usuarioId",
            new { id, tenantId, usuarioId });
    }
}

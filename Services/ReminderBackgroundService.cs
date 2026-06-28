using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Services;

public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await EnviarRecordatoriosAsync(db, email);
                await EnviarAgendaDiariaAsync(db, email);
                await EnviarResumenDuenoAsync(db, email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando recordatorios automaticos.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private static async Task EnviarRecordatoriosAsync(IDbConnectionFactory db, IEmailSender email)
    {
        using var c = db.Create();
        var turnos = (await c.QueryAsync<TurnoDetalle>(@"
            SELECT t.id, t.tenant_id, t.cliente_id, t.empleado_id, t.servicio_id,
                   t.fecha_hora_inicio, t.fecha_hora_fin, t.estado, t.precio, t.notas, t.fecha_creacion,
                   cte.nombre AS cliente_nombre, cte.email AS cliente_email, cte.telefono AS cliente_telefono,
                   emp.nombre AS empleado_nombre, emp.email AS empleado_email,
                   s.nombre AS servicio_nombre, s.duracion_minutos AS duracion_minutos
            FROM turnos t
            JOIN usuarios cte ON cte.id = t.cliente_id
            JOIN usuarios emp ON emp.id = t.empleado_id
            JOIN servicios s ON s.id = t.servicio_id
            WHERE t.estado IN (@pendiente, @confirmado)
              AND t.fecha_hora_inicio BETWEEN now() + interval '55 minutes' AND now() + interval '125 minutes'
              AND (t.recordatorio_cliente_enviado = FALSE OR t.recordatorio_barbero_enviado = FALSE)",
            new { pendiente = (int)EstadoTurno.Pendiente, confirmado = (int)EstadoTurno.Confirmado })).ToList();

        foreach (var t in turnos)
        {
            var html = $@"<p>Recordatorio de cita</p>
                <p><strong>{t.ServicioNombre}</strong> con {t.EmpleadoNombre}<br>
                {t.FechaHoraInicio:dddd dd/MM/yyyy HH:mm}</p>";

            if (!string.IsNullOrWhiteSpace(t.ClienteEmail))
            {
                await email.SendAsync(t.ClienteEmail, "Recordatorio de tu cita", html);
                await c.ExecuteAsync("UPDATE turnos SET recordatorio_cliente_enviado=TRUE WHERE id=@id", new { t.Id });
            }
            if (!string.IsNullOrWhiteSpace(t.EmpleadoEmail))
            {
                await email.SendAsync(t.EmpleadoEmail, "Recordatorio: proxima cita", html);
                await c.ExecuteAsync("UPDATE turnos SET recordatorio_barbero_enviado=TRUE WHERE id=@id", new { t.Id });
            }
        }
    }

    private static async Task EnviarAgendaDiariaAsync(IDbConnectionFactory db, IEmailSender email)
    {
        var now = DateTime.Now;
        if (now.Hour < 7 || now.Hour > 10) return;

        using var c = db.Create();
        var barberos = (await c.QueryAsync<Usuario>(@"
            SELECT * FROM usuarios u
            WHERE u.email IS NOT NULL AND u.activo=TRUE AND u.atiende=TRUE
              AND NOT EXISTS (
                SELECT 1 FROM email_envios e
                WHERE e.tenant_id=u.tenant_id AND e.usuario_id=u.id AND e.tipo='agenda-dia' AND e.fecha=current_date
              )")).ToList();

        foreach (var b in barberos)
        {
            var turnos = (await c.QueryAsync<TurnoDetalle>(@"
                SELECT t.id, t.fecha_hora_inicio, t.fecha_hora_fin, t.precio,
                       cte.nombre AS cliente_nombre, cte.telefono AS cliente_telefono,
                       s.nombre AS servicio_nombre
                FROM turnos t
                JOIN usuarios cte ON cte.id=t.cliente_id
                JOIN servicios s ON s.id=t.servicio_id
                WHERE t.tenant_id=@tenantId AND t.empleado_id=@empleadoId
                  AND t.estado NOT IN (@cancelado, @noshow)
                  AND t.fecha_hora_inicio >= current_date AND t.fecha_hora_inicio < current_date + interval '1 day'
                ORDER BY t.fecha_hora_inicio",
                new { tenantId = b.TenantId, empleadoId = b.Id, cancelado = (int)EstadoTurno.Cancelado, noshow = (int)EstadoTurno.NoShow })).ToList();

            var items = turnos.Any()
                ? string.Join("", turnos.Select(t => $"<li>{t.FechaHoraInicio:HH:mm} - {t.ClienteNombre} - {t.ServicioNombre}</li>"))
                : "<li>No tienes turnos programados hoy.</li>";
            await email.SendAsync(b.Email!, "Tu agenda de hoy", $"<p>Hola {b.Nombre}, esta es tu agenda:</p><ul>{items}</ul>");
            await c.ExecuteAsync(@"
                INSERT INTO email_envios (tenant_id, usuario_id, tipo, fecha)
                VALUES (@tenantId, @usuarioId, 'agenda-dia', current_date)
                ON CONFLICT DO NOTHING", new { tenantId = b.TenantId, usuarioId = b.Id });
        }
    }

    private static async Task EnviarResumenDuenoAsync(IDbConnectionFactory db, IEmailSender email)
    {
        var now = DateTime.Now;
        if (now.Hour < 18 || now.Hour > 23) return;

        using var c = db.Create();
        var duenos = (await c.QueryAsync<Usuario>(@"
            SELECT * FROM usuarios u
            WHERE u.email IS NOT NULL AND u.activo=TRUE AND u.rol=@dueno
              AND NOT EXISTS (
                SELECT 1 FROM email_envios e
                WHERE e.tenant_id=u.tenant_id AND e.usuario_id=u.id AND e.tipo='resumen-dueno' AND e.fecha=current_date
              )", new { dueno = (int)Rol.Dueno })).ToList();

        foreach (var d in duenos)
        {
            var stats = await c.QuerySingleAsync<dynamic>(@"
                SELECT COUNT(*) FILTER (WHERE estado=@comp) AS completados,
                       COUNT(*) FILTER (WHERE estado=@canc) AS cancelados,
                       COALESCE(SUM(precio) FILTER (WHERE estado=@comp), 0) AS ventas
                FROM turnos
                WHERE tenant_id=@tenantId AND fecha_hora_inicio >= current_date
                  AND fecha_hora_inicio < current_date + interval '1 day'",
                new { tenantId = d.TenantId, comp = (int)EstadoTurno.Completado, canc = (int)EstadoTurno.Cancelado });

            await email.SendAsync(d.Email!, "Resumen diario del negocio",
                $"<p>Resumen de hoy:</p><ul><li>Completados: {stats.completados}</li><li>Cancelados: {stats.cancelados}</li><li>Ventas: ${stats.ventas:#,##0}</li></ul>");
            await c.ExecuteAsync(@"
                INSERT INTO email_envios (tenant_id, usuario_id, tipo, fecha)
                VALUES (@tenantId, @usuarioId, 'resumen-dueno', current_date)
                ON CONFLICT DO NOTHING", new { tenantId = d.TenantId, usuarioId = d.Id });
        }
    }
}

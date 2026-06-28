using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;

namespace GeneradorTurnos.Services;

public record OperacionResultado(bool Ok, string? Error = null, int TurnoId = 0)
{
    public static OperacionResultado Falla(string error) => new(false, error);
    public static OperacionResultado Exito(int id = 0) => new(true, null, id);
}

/// <summary>Reglas de negocio de reservas: validación, snapshot de precio y anti doble-reserva.</summary>
public class TurnoService
{
    private readonly ITurnoRepository _turnos;
    private readonly IServicioRepository _servicios;
    private readonly IUsuarioRepository _usuarios;
    private readonly DisponibilidadService _disponibilidad;
    private readonly INotificacionRepository _notificaciones;
    private readonly IEmailSender _email;

    public TurnoService(ITurnoRepository turnos, IServicioRepository servicios,
        IUsuarioRepository usuarios, DisponibilidadService disponibilidad, INotificacionRepository notificaciones,
        IEmailSender email)
    {
        _turnos = turnos;
        _servicios = servicios;
        _usuarios = usuarios;
        _disponibilidad = disponibilidad;
        _notificaciones = notificaciones;
        _email = email;
    }

    public async Task<OperacionResultado> ReservarAsync(int tenantId, int clienteId, int empleadoId,
        int servicioId, DateTime inicio, string? notas)
    {
        var empleado = await _usuarios.GetByIdInTenantAsync(tenantId, empleadoId);
        if (empleado is null || !empleado.Atiende || !empleado.Activo)
            return OperacionResultado.Falla("El profesional no está disponible.");

        var servicio = await _servicios.GetEfectivoAsync(tenantId, empleadoId, servicioId);
        if (servicio is null || !servicio.Activo)
            return OperacionResultado.Falla("Ese profesional no presta el servicio elegido.");

        if (inicio < DateTime.Now)
            return OperacionResultado.Falla("No puedes reservar en un horario pasado.");

        // El inicio debe coincidir con un hueco realmente disponible.
        var slots = await _disponibilidad.CalcularSlotsAsync(tenantId, empleadoId, servicio.DuracionMinutos, inicio.Date);
        if (!slots.Any(s => s.Inicio == inicio))
            return OperacionResultado.Falla("El horario seleccionado ya no está disponible.");

        var fin = inicio.AddMinutes(servicio.DuracionMinutos);

        // Revalidación final contra doble reserva (carrera entre dos clientes).
        if (await _turnos.HaySolapamientoAsync(tenantId, empleadoId, inicio, fin))
            return OperacionResultado.Falla("El horario acaba de ser tomado por otra persona.");

        var id = await _turnos.CrearAsync(new Turno
        {
            TenantId = tenantId,
            ClienteId = clienteId,
            EmpleadoId = empleadoId,
            ServicioId = servicioId,
            FechaHoraInicio = inicio,
            FechaHoraFin = fin,
            Estado = EstadoTurno.Confirmado, // auto-confirmado; el dinero suma al marcar "Realizado"
            Precio = servicio.Precio, // snapshot
            Notas = notas
        });

        await NotificarBarberoAsync(tenantId, empleadoId, clienteId, servicio.Nombre, id,
            "reserva", "Nuevo turno reservado",
            $"Reservaron {servicio.Nombre} para el {inicio:dd/MM/yyyy HH:mm}.");
        await EnviarCorreoCambioAsync(tenantId, empleadoId, clienteId, servicio.Nombre, inicio,
            "Nuevo turno reservado", "Se creo una nueva reserva.");

        return OperacionResultado.Exito(id);
    }

    public async Task<OperacionResultado> ReprogramarAsync(int tenantId, int turnoId, int actorClienteId, DateTime nuevoInicio)
    {
        var turno = await _turnos.GetByIdAsync(tenantId, turnoId);
        if (turno is null) return OperacionResultado.Falla("Turno no encontrado.");
        if (turno.ClienteId != actorClienteId) return OperacionResultado.Falla("No puedes modificar este turno.");
        if (turno.Estado is EstadoTurno.Completado or EstadoTurno.Cancelado)
            return OperacionResultado.Falla("Este turno ya no se puede modificar.");
        if (nuevoInicio < DateTime.Now) return OperacionResultado.Falla("No puedes mover el turno al pasado.");

        var servicio = await _servicios.GetEfectivoAsync(tenantId, turno.EmpleadoId, turno.ServicioId);
        if (servicio is null) return OperacionResultado.Falla("Servicio no encontrado.");

        var slots = await _disponibilidad.CalcularSlotsAsync(tenantId, turno.EmpleadoId, servicio.DuracionMinutos, nuevoInicio.Date);
        if (!slots.Any(s => s.Inicio == nuevoInicio))
            return OperacionResultado.Falla("El nuevo horario no está disponible.");

        var fin = nuevoInicio.AddMinutes(servicio.DuracionMinutos);
        if (await _turnos.HaySolapamientoAsync(tenantId, turno.EmpleadoId, nuevoInicio, fin, turnoId))
            return OperacionResultado.Falla("El nuevo horario ya está ocupado.");

        var anterior = turno.FechaHoraInicio;
        await _turnos.ReprogramarAsync(tenantId, turnoId, nuevoInicio, fin);
        await NotificarBarberoAsync(tenantId, turno.EmpleadoId, turno.ClienteId, servicio.Nombre, turnoId,
            "reprogramacion", "Turno reprogramado",
            $"Movieron {servicio.Nombre} del {anterior:dd/MM/yyyy HH:mm} al {nuevoInicio:dd/MM/yyyy HH:mm}.");
        await EnviarCorreoCambioAsync(tenantId, turno.EmpleadoId, turno.ClienteId, servicio.Nombre, nuevoInicio,
            "Turno reprogramado", $"La cita paso de {anterior:dd/MM/yyyy HH:mm} a {nuevoInicio:dd/MM/yyyy HH:mm}.");
        return OperacionResultado.Exito(turnoId);
    }

    /// <summary>Reserva creada por el staff (barbero/dueño). Puede ocupar espacios fuera del horario
    /// publicado; solo evita solaparse con otro turno activo del mismo profesional.</summary>
    public async Task<OperacionResultado> ReservarManualAsync(int tenantId, int empleadoId, int clienteId,
        int servicioId, DateTime inicio, int duracionMinutos, decimal precio, string? notas)
    {
        if (duracionMinutos <= 0) return OperacionResultado.Falla("Duración inválida.");
        var fin = inicio.AddMinutes(duracionMinutos);

        if (await _turnos.HaySolapamientoAsync(tenantId, empleadoId, inicio, fin))
            return OperacionResultado.Falla("Ese horario se cruza con otro turno del profesional.");

        var id = await _turnos.CrearAsync(new Turno
        {
            TenantId = tenantId,
            ClienteId = clienteId,
            EmpleadoId = empleadoId,
            ServicioId = servicioId,
            FechaHoraInicio = inicio,
            FechaHoraFin = fin,
            Estado = EstadoTurno.Confirmado,
            Precio = precio,
            Notas = notas,
            Origen = OrigenTurno.Manual
        });
        return OperacionResultado.Exito(id);
    }

    public async Task<OperacionResultado> CancelarPorClienteAsync(int tenantId, int turnoId, int actorClienteId)
    {
        var turno = await _turnos.GetByIdAsync(tenantId, turnoId);
        if (turno is null) return OperacionResultado.Falla("Turno no encontrado.");
        if (turno.ClienteId != actorClienteId) return OperacionResultado.Falla("No puedes cancelar este turno.");
        if (turno.Estado is EstadoTurno.Completado or EstadoTurno.Cancelado)
            return OperacionResultado.Falla("Este turno ya no se puede cancelar.");

        await _turnos.ActualizarEstadoAsync(tenantId, turnoId, EstadoTurno.Cancelado);
        var servicio = await _servicios.GetByIdAsync(tenantId, turno.ServicioId);
        await NotificarBarberoAsync(tenantId, turno.EmpleadoId, turno.ClienteId, servicio?.Nombre ?? "servicio", turnoId,
            "cancelacion", "Turno cancelado",
            $"Cancelaron el turno del {turno.FechaHoraInicio:dd/MM/yyyy HH:mm}.");
        await EnviarCorreoCambioAsync(tenantId, turno.EmpleadoId, turno.ClienteId, servicio?.Nombre ?? "servicio",
            turno.FechaHoraInicio, "Turno cancelado", "El cliente cancelo la cita.");
        return OperacionResultado.Exito(turnoId);
    }

    private async Task NotificarBarberoAsync(int tenantId, int empleadoId, int clienteId, string servicio,
        int turnoId, string tipo, string titulo, string mensaje)
    {
        var cliente = await _usuarios.GetByIdInTenantAsync(tenantId, clienteId);
        var nombre = cliente?.Nombre ?? "Cliente";
        await _notificaciones.CrearAsync(new Notificacion
        {
            TenantId = tenantId,
            UsuarioId = empleadoId,
            TurnoId = turnoId,
            Tipo = tipo,
            Titulo = titulo,
            Mensaje = $"{nombre}: {mensaje}"
        });
    }

    private async Task EnviarCorreoCambioAsync(int tenantId, int empleadoId, int clienteId, string servicio,
        DateTime inicio, string asunto, string detalle)
    {
        var empleado = await _usuarios.GetByIdInTenantAsync(tenantId, empleadoId);
        var cliente = await _usuarios.GetByIdInTenantAsync(tenantId, clienteId);
        var html = $@"<p><strong>{asunto}</strong></p>
            <p>{detalle}</p>
            <p><strong>Servicio:</strong> {servicio}<br>
            <strong>Fecha:</strong> {inicio:dddd dd/MM/yyyy HH:mm}<br>
            <strong>Cliente:</strong> {cliente?.Nombre}</p>";

        if (!string.IsNullOrWhiteSpace(empleado?.Email))
            await _email.SendAsync(empleado.Email, asunto, html);
        if (!string.IsNullOrWhiteSpace(cliente?.Email))
            await _email.SendAsync(cliente.Email, asunto, html);
    }
}

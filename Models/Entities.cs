namespace GeneradorTurnos.Models;

/// <summary>Una empresa/local cliente del SaaS (barbería, restaurante, salón...).</summary>
public class Tenant
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Plan { get; set; } = "Basico";
    public int MaxUsuarios { get; set; } = 10;
    public bool Activo { get; set; } = true;
    public string? FotoUrl { get; set; }
    public DateTime? SuscripcionInicio { get; set; }
    public DateTime? SuscripcionVencimiento { get; set; }
    public string EstadoSuscripcion { get; set; } = "Activo";
    public int RecordatorioPagoDias { get; set; } = 7;
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Usuario del sistema. Unifica dueños, profesionales y clientes vía <see cref="Rol"/>.
/// SuperAdmin tiene TenantId nulo.</summary>
public class Usuario
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public Rol Rol { get; set; }
    public string Nombre { get; set; } = "";
    public string? Cedula { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool Activo { get; set; } = true;
    public bool Atiende { get; set; }
    public string? FotoUrl { get; set; }
    public ComisionTipo ComisionTipo { get; set; } = ComisionTipo.Ninguna;
    public decimal ComisionValor { get; set; }
    public bool EmailVerificado { get; set; }
    public string? TokenVerificacion { get; set; }
    public DateTime? TokenExpira { get; set; }
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Foto de la galería de cortes (del negocio o de un profesional).</summary>
public class GaleriaFoto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? EmpleadoId { get; set; }
    public string FotoUrl { get; set; } = "";
    public string? Descripcion { get; set; }
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Gasto del negocio (luz, insumos, arriendo, etc.).</summary>
public class Gasto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Concepto { get; set; } = "";
    public string? Categoria { get; set; }
    public decimal Monto { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public class PagoSuscripcion
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public decimal Monto { get; set; }
    public DateTime PeriodoInicio { get; set; }
    public DateTime PeriodoFin { get; set; }
    public string? Metodo { get; set; }
    public string? Referencia { get; set; }
    public string? Nota { get; set; }
    public DateTime FechaPago { get; set; }
}

public class Auditoria
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public int? UsuarioId { get; set; }
    public string ActorNombre { get; set; } = "";
    public string Accion { get; set; } = "";
    public string Entidad { get; set; } = "";
    public int? EntidadId { get; set; }
    public string? Detalle { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public class Servicio
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Nombre { get; set; } = "";
    public int DuracionMinutos { get; set; }
    public decimal Precio { get; set; }
    public bool Activo { get; set; } = true;
}

public class EmpleadoServicio
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int EmpleadoId { get; set; }
    public int ServicioId { get; set; }
    public decimal? PrecioOverride { get; set; }
    public int? DuracionOverride { get; set; }
}

public class ServicioSolicitud
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int EmpleadoId { get; set; }
    public int ServicioId { get; set; }
    public bool Ofrecido { get; set; }
    public decimal? PrecioOverride { get; set; }
    public int? DuracionOverride { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaDecision { get; set; }
}

/// <summary>Horario laboral recurrente semanal de un profesional.</summary>
public class HorarioTrabajo
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int EmpleadoId { get; set; }
    public int DiaSemana { get; set; } // 0=Domingo .. 6=Sábado (System.DayOfWeek)
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFin { get; set; }
}

/// <summary>Excepción puntual (vacaciones, permiso) que bloquea la agenda.</summary>
public class Bloqueo
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int EmpleadoId { get; set; }
    public DateTime FechaHoraInicio { get; set; }
    public DateTime FechaHoraFin { get; set; }
    public string? Motivo { get; set; }
}

public class Turno
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ClienteId { get; set; }
    public int EmpleadoId { get; set; }
    public int ServicioId { get; set; }
    public DateTime FechaHoraInicio { get; set; }
    public DateTime FechaHoraFin { get; set; }
    public EstadoTurno Estado { get; set; } = EstadoTurno.Pendiente;
    public decimal Precio { get; set; }
    public string? Notas { get; set; }
    public OrigenTurno Origen { get; set; } = OrigenTurno.Cliente;
    public bool RecordatorioClienteEnviado { get; set; }
    public bool RecordatorioBarberoEnviado { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public class Notificacion
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int UsuarioId { get; set; }
    public int? TurnoId { get; set; }
    public string Tipo { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public bool Leida { get; set; }
    public DateTime FechaCreacion { get; set; }
}

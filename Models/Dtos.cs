namespace GeneradorTurnos.Models;

/// <summary>Turno con datos relacionados (cliente, profesional, servicio) ya resueltos.</summary>
public class TurnoDetalle
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ClienteId { get; set; }
    public int EmpleadoId { get; set; }
    public int ServicioId { get; set; }
    public DateTime FechaHoraInicio { get; set; }
    public DateTime FechaHoraFin { get; set; }
    public EstadoTurno Estado { get; set; }
    public decimal Precio { get; set; }
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; }

    public string ClienteNombre { get; set; } = "";
    public string? ClienteCedula { get; set; }
    public string? ClienteTelefono { get; set; }
    public string? ClienteEmail { get; set; }
    public string EmpleadoNombre { get; set; } = "";
    public string? EmpleadoEmail { get; set; }
    public string ServicioNombre { get; set; } = "";
    public int DuracionMinutos { get; set; }
}

/// <summary>Un hueco disponible para reservar.</summary>
public class Slot
{
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public string Hora => Inicio.ToString("HH:mm");
}

/// <summary>Profesional con su lista de servicios (para tarjetas de selección).</summary>
public class EmpleadoConServicios
{
    public Usuario Empleado { get; set; } = new();
    public List<Servicio> Servicios { get; set; } = new();
}

/// <summary>Profesional que presta un servicio, con su precio/duración efectivos.</summary>
public class ProfesionalDeServicio
{
    public int EmpleadoId { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public int DuracionMinutos { get; set; }
    public string? FotoUrl { get; set; }
    /// <summary>Próximo turno disponible (sugerencia). Null si no hay en el rango buscado.</summary>
    public DateTime? ProximoDisponible { get; set; }
}

// ---- DTOs de estadísticas ----

public class ResumenStats
{
    public int TurnosTotales { get; set; }
    public int TurnosCompletados { get; set; }
    public int TurnosCancelados { get; set; }
    public int TurnosPendientes { get; set; }
    public decimal IngresosTotales { get; set; }
    public decimal TicketPromedio { get; set; }
    public int ClientesUnicos { get; set; }
}

public class SerieFecha
{
    public DateTime Fecha { get; set; }
    public decimal Valor { get; set; }
    public int Cantidad { get; set; }
}

public class RankingItem
{
    public string Etiqueta { get; set; } = "";
    public decimal Valor { get; set; }
    public int Cantidad { get; set; }
}

/// <summary>Servicio del catálogo visto desde un profesional (si lo ofrece y con qué precio/duración).</summary>
public class ServicioBarbero
{
    public int ServicioId { get; set; }
    public string Nombre { get; set; } = "";
    public int DuracionBase { get; set; }
    public decimal PrecioBase { get; set; }
    public bool Activo { get; set; }
    public bool Ofrecido { get; set; }
    public decimal? PrecioOverride { get; set; }
    public int? DuracionOverride { get; set; }

    public decimal PrecioEfectivo => PrecioOverride ?? PrecioBase;
    public int DuracionEfectiva => DuracionOverride ?? DuracionBase;
}

public class ServicioSolicitudDetalle
{
    public int Id { get; set; }
    public int EmpleadoId { get; set; }
    public string EmpleadoNombre { get; set; } = "";
    public int ServicioId { get; set; }
    public string ServicioNombre { get; set; } = "";
    public bool Ofrecido { get; set; }
    public decimal? PrecioOverride { get; set; }
    public int? DuracionOverride { get; set; }
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Resumen de desempeño por profesional (panel del dueño).</summary>
public class ResumenBarbero
{
    public int EmpleadoId { get; set; }
    public string Nombre { get; set; } = "";
    public int Completados { get; set; }
    public int Cancelados { get; set; }
    public int NoShow { get; set; }
    public int Pendientes { get; set; }
    public int Confirmados { get; set; }
    public int Total { get; set; }
    public decimal Ingresos { get; set; }
    public decimal TicketPromedio { get; set; }
    public int ClientesUnicos { get; set; }

    public ComisionTipo ComisionTipo { get; set; }
    public decimal ComisionValor { get; set; }

    /// <summary>Lo que recibe el dueño por este barbero en el período.</summary>
    public decimal ComisionDueno => ComisionTipo switch
    {
        ComisionTipo.Porcentaje => Math.Round(Ingresos * ComisionValor / 100m, 0),
        ComisionTipo.FijoMensual => ComisionValor,
        _ => 0m
    };

    /// <summary>Lo que le queda al barbero tras la comisión del dueño.</summary>
    public decimal NetoBarbero => Ingresos - ComisionDueno;
    public decimal TasaFinalizacion => Total == 0 ? 0 : Math.Round(Completados * 100m / Total, 0);

    public string ComisionTexto => ComisionTipo switch
    {
        ComisionTipo.Porcentaje => $"{ComisionValor:0.##}%",
        ComisionTipo.FijoMensual => $"${ComisionValor:#,##0} fijo",
        _ => "—"
    };
}

public class SaasTenantResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Plan { get; set; } = "";
    public decimal ValorSuscripcion { get; set; }
    public int MaxUsuarios { get; set; }
    public bool Activo { get; set; }
    public DateTime? SuscripcionVencimiento { get; set; }
    public string EstadoSuscripcion { get; set; } = "Activo";
    public DateTime FechaCreacion { get; set; }
    public int Usuarios { get; set; }
    public int Trabajadores { get; set; }
    public int Clientes { get; set; }
    public int Turnos { get; set; }
    public decimal Ventas { get; set; }
    public int DiasVencimiento => SuscripcionVencimiento is null ? 9999 : (SuscripcionVencimiento.Value.Date - DateTime.Today).Days;
}

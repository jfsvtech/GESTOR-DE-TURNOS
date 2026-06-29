using System.Globalization;
using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeneradorTurnos.Controllers;

[Authorize(Roles = "Barbero,Dueno")]
[Route("{slug}/agenda")]
public class BarberoController : TenantBaseController
{
    private readonly ITurnoRepository _turnos;
    private readonly IStatsRepository _stats;
    private readonly IServicioRepository _servicios;
    private readonly IAgendaRepository _agenda;
    private readonly IUsuarioRepository _usuarios;
    private readonly TurnoService _turnoService;
    private readonly IGaleriaRepository _galeria;
    private readonly IFileStorage _files;
    private readonly INotificacionRepository _notificaciones;

    public BarberoController(ITenantContext tenant, ITurnoRepository turnos, IStatsRepository stats,
        IServicioRepository servicios, IAgendaRepository agenda, IUsuarioRepository usuarios, TurnoService turnoService,
        IGaleriaRepository galeria, IFileStorage files, INotificacionRepository notificaciones)
        : base(tenant)
    {
        _turnos = turnos;
        _stats = stats;
        _servicios = servicios;
        _agenda = agenda;
        _usuarios = usuarios;
        _turnoService = turnoService;
        _galeria = galeria;
        _files = files;
        _notificaciones = notificaciones;
    }

    private IActionResult? GuardTenant()
        => UsuarioPerteneceAlTenant() ? null : RedirectToAction("Login", "Account", new { slug = Slug });

    [HttpGet("hoy")]
    public async Task<IActionResult> Hoy()
    {
        if (GuardTenant() is { } r) return r;
        var hoy = DateTime.Today;
        var agenda = await _turnos.GetByEmpleadoRangoAsync(TenantId, CurrentUserId, hoy, hoy.AddDays(1));
        var proximo = agenda
            .Where(t => t.FechaHoraInicio >= DateTime.Now && t.Estado is not EstadoTurno.Cancelado and not EstadoTurno.Completado)
            .OrderBy(t => t.FechaHoraInicio)
            .FirstOrDefault();
        var historial = proximo is null
            ? new List<TurnoDetalle>()
            : (await _turnos.GetByClienteAsync(TenantId, proximo.ClienteId)).Where(t => t.Id != proximo.Id).Take(8).ToList();

        return View(new HoyBarberoVm { Fecha = hoy, Agenda = agenda, Proximo = proximo, HistorialCliente = historial });
    }

    // ---------------- Agenda del día ----------------
    [HttpGet("")]
    public async Task<IActionResult> Agenda(string? fecha)
    {
        if (GuardTenant() is { } r) return r;

        var dia = DateTime.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date : DateTime.Today;

        var turnos = await _turnos.GetByEmpleadoRangoAsync(TenantId, CurrentUserId, dia, dia.AddDays(1));
        var bloqueos = await _agenda.GetBloqueosEnRangoAsync(TenantId, CurrentUserId, dia, dia.AddDays(1));
        ViewData["Fecha"] = dia;
        ViewBag.Bloqueos = bloqueos;
        ViewBag.Notificaciones = await _notificaciones.GetNoLeidasAsync(TenantId, CurrentUserId);
        return View(turnos);
    }

    [HttpPost("notificaciones/{id:int}/leer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LeerNotificacion(int id, string? fecha)
    {
        if (GuardTenant() is { } r) return r;
        await _notificaciones.MarcarLeidaAsync(TenantId, CurrentUserId, id);
        return RedirectToAction(nameof(Agenda), new { slug = Slug, fecha });
    }

    [HttpPost("turno/{id:int}/estado")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(int id, EstadoTurno estado, string? fecha)
    {
        var turno = await _turnos.GetByIdAsync(TenantId, id);
        if (turno is null || turno.EmpleadoId != CurrentUserId)
            TempData["Error"] = "Turno no encontrado.";
        else
        {
            await _turnos.ActualizarEstadoAsync(TenantId, id, estado);
            TempData["Ok"] = $"Turno marcado como {estado.Display()}.";
        }
        return RedirectToAction(nameof(Agenda), new { slug = Slug, fecha });
    }

    // ---------------- Historial ----------------
    [HttpGet("historial")]
    public async Task<IActionResult> Historial(DateTime? desde, DateTime? hasta)
    {
        if (GuardTenant() is { } r) return r;
        var (d, h) = Rango(desde, hasta);
        ViewData["Desde"] = d; ViewData["Hasta"] = h.AddDays(-1);
        ViewBag.Resumen = await _stats.ResumenAsync(TenantId, d, h, CurrentUserId);
        var turnos = await _turnos.GetByEmpleadoRangoAsync(TenantId, CurrentUserId, d, h);
        return View(turnos);
    }

    // ---------------- Estadísticas ----------------
    [HttpGet("estadisticas")]
    public async Task<IActionResult> Estadisticas(DateTime? desde, DateTime? hasta)
    {
        if (GuardTenant() is { } r) return r;
        var (d, h) = Rango(desde, hasta);
        var emp = CurrentUserId;
        return View(new DashboardBarberoVm
        {
            Desde = d, Hasta = h.AddDays(-1),
            Resumen = await _stats.ResumenAsync(TenantId, d, h, emp),
            TurnosPorDia = await _stats.SeriePorDiaAsync(TenantId, d, h, emp),
            ServiciosTop = await _stats.ServiciosTopAsync(TenantId, d, h, emp)
        });
    }

    // ---------------- Mis servicios / precios ----------------
    [HttpGet("servicios")]
    public async Task<IActionResult> MisServicios()
    {
        if (GuardTenant() is { } r) return r;
        return View(await _servicios.GetCatalogoConOverridesAsync(TenantId, CurrentUserId));
    }

    [HttpPost("servicios")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MisServicios(List<int> ofrecidos, Dictionary<int, decimal> precios, Dictionary<int, int> duraciones)
    {
        if (GuardTenant() is { } r) return r;
        ofrecidos ??= new();
        var catalogo = await _servicios.GetCatalogoConOverridesAsync(TenantId, CurrentUserId);
        foreach (var s in catalogo)
        {
            var ofrece = ofrecidos.Contains(s.ServicioId);
            decimal? precio = precios != null && precios.TryGetValue(s.ServicioId, out var p) && p > 0 ? p : null;
            int? dur = duraciones != null && duraciones.TryGetValue(s.ServicioId, out var du) && du > 0 ? du : null;
            await _servicios.SolicitarCambioAsync(new ServicioSolicitud
            {
                TenantId = TenantId,
                EmpleadoId = CurrentUserId,
                ServicioId = s.ServicioId,
                Ofrecido = ofrece,
                PrecioOverride = precio,
                DuracionOverride = dur
            });
        }
        TempData["Ok"] = "Tus cambios quedaron pendientes de aprobacion del dueno.";
        return RedirectToAction(nameof(MisServicios), new { slug = Slug });
    }

    // ---------------- Bloquear agenda ----------------
    [HttpGet("bloquear")]
    public async Task<IActionResult> Bloquear()
    {
        if (GuardTenant() is { } r) return r;
        ViewBag.Futuros = await _agenda.GetBloqueosFuturosAsync(TenantId, CurrentUserId);
        return View(new BloqueoVm());
    }

    [HttpPost("bloquear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bloquear(BloqueoVm vm)
    {
        if (GuardTenant() is { } r) return r;
        if (TimeOnly.TryParse(vm.HoraInicio, out var hi) && TimeOnly.TryParse(vm.HoraFin, out var hf) && hf > hi)
        {
            await _agenda.CrearBloqueoAsync(new Bloqueo
            {
                TenantId = TenantId, EmpleadoId = CurrentUserId,
                FechaHoraInicio = vm.Fecha.Date + hi.ToTimeSpan(),
                FechaHoraFin = vm.Fecha.Date + hf.ToTimeSpan(),
                Motivo = vm.Motivo
            });
            TempData["Ok"] = "Bloqueo creado.";
        }
        else TempData["Error"] = "Rango horario inválido.";
        return RedirectToAction(nameof(Bloquear), new { slug = Slug });
    }

    [HttpPost("bloquear/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarBloqueo(int id, string? volverFecha)
    {
        await _agenda.EliminarBloqueoAsync(TenantId, id);
        TempData["Ok"] = "Bloqueo eliminado.";
        return string.IsNullOrEmpty(volverFecha)
            ? RedirectToAction(nameof(Bloquear), new { slug = Slug })
            : RedirectToAction(nameof(Agenda), new { slug = Slug, fecha = volverFecha });
    }

    // Bloqueo rápido desde la agenda del día (almuerzo, etc.). Vuelve a la agenda.
    [HttpPost("bloquear-dia")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BloquearDia(string fecha, string horaInicio, string horaFin, string? motivo)
    {
        if (GuardTenant() is { } r) return r;
        if (DateTime.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            && TimeOnly.TryParse(horaInicio, out var hi) && TimeOnly.TryParse(horaFin, out var hf) && hf > hi)
        {
            await _agenda.CrearBloqueoAsync(new Bloqueo
            {
                TenantId = TenantId, EmpleadoId = CurrentUserId,
                FechaHoraInicio = dia.Date + hi.ToTimeSpan(),
                FechaHoraFin = dia.Date + hf.ToTimeSpan(),
                Motivo = string.IsNullOrWhiteSpace(motivo) ? "No disponible" : motivo
            });
            TempData["Ok"] = "Bloqueo creado.";
        }
        else TempData["Error"] = "Rango horario inválido.";
        return RedirectToAction(nameof(Agenda), new { slug = Slug, fecha });
    }

    // ---------------- Reserva manual (walk-in) ----------------
    [HttpGet("nuevo-turno")]
    public async Task<IActionResult> NuevoTurno()
    {
        if (GuardTenant() is { } r) return r;
        ViewBag.Servicios = await _servicios.GetByEmpleadoAsync(TenantId, CurrentUserId);
        return View(new ReservaManualVm());
    }

    [HttpPost("nuevo-turno")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoTurno(ReservaManualVm vm)
    {
        if (GuardTenant() is { } r) return r;

        var servicio = await _servicios.GetEfectivoAsync(TenantId, CurrentUserId, vm.ServicioId);
        if (servicio is null || !TimeOnly.TryParse(vm.Hora, out var hora))
        {
            ModelState.AddModelError("", "Revisa el servicio y la hora.");
            ViewBag.Servicios = await _servicios.GetByEmpleadoAsync(TenantId, CurrentUserId);
            return View(vm);
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Servicios = await _servicios.GetByEmpleadoAsync(TenantId, CurrentUserId);
            return View(vm);
        }

        var cliente = await _usuarios.UpsertClienteAsync(TenantId, vm.ClienteCedula.Trim(), vm.ClienteNombre.Trim(),
            vm.ClienteTelefono.Trim());

        var inicio = vm.Fecha.Date + hora.ToTimeSpan();
        var res = await _turnoService.ReservarManualAsync(TenantId, CurrentUserId, cliente.Id, vm.ServicioId,
            inicio, servicio.DuracionMinutos, servicio.Precio, vm.Notas);

        if (!res.Ok)
        {
            ModelState.AddModelError("", res.Error!);
            ViewBag.Servicios = await _servicios.GetByEmpleadoAsync(TenantId, CurrentUserId);
            return View(vm);
        }

        TempData["Ok"] = "Turno agendado.";
        return RedirectToAction(nameof(Agenda), new { slug = Slug, fecha = vm.Fecha.ToString("yyyy-MM-dd") });
    }

    // ---------------- Mis horarios de trabajo ----------------
    private static readonly string[] DiasNombre =
        { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };

    [HttpGet("horarios")]
    public async Task<IActionResult> Horarios()
    {
        if (GuardTenant() is { } r) return r;
        ViewBag.Dias = DiasNombre;
        return View(await _agenda.GetHorariosAsync(TenantId, CurrentUserId));
    }

    [HttpPost("horarios")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Horarios(int[] dia, string[] inicio, string[] fin)
    {
        if (GuardTenant() is { } r) return r;

        var horarios = new List<HorarioTrabajo>();
        for (int i = 0; i < (dia?.Length ?? 0); i++)
        {
            if (i >= inicio.Length || i >= fin.Length) break;
            if (string.IsNullOrWhiteSpace(inicio[i]) || string.IsNullOrWhiteSpace(fin[i])) continue;
            if (!TimeOnly.TryParse(inicio[i], out var hi) || !TimeOnly.TryParse(fin[i], out var hf)) continue;
            if (hf <= hi) continue;
            horarios.Add(new HorarioTrabajo
            {
                TenantId = TenantId, EmpleadoId = CurrentUserId, DiaSemana = dia![i], HoraInicio = hi, HoraFin = hf
            });
        }
        await _agenda.ReemplazarHorariosAsync(TenantId, CurrentUserId, horarios);
        TempData["Ok"] = "Tus horarios fueron actualizados.";
        return RedirectToAction(nameof(Horarios), new { slug = Slug });
    }

    // ---------------- Calendario (día / semana / mes) ----------------
    [HttpGet("calendario")]
    public IActionResult Calendario()
    {
        if (GuardTenant() is { } r) return r;
        return View();
    }

    [HttpGet("calendario/eventos")]
    public async Task<IActionResult> Eventos(string? start, string? end)
    {
        if (!UsuarioPerteneceAlTenant()) return Json(Array.Empty<object>());
        var desde = DateTime.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ds)
            ? ds.Date : DateTime.Today;
        var hasta = DateTime.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var he)
            ? he.Date : DateTime.Today.AddMonths(1);
        var turnos = await _turnos.GetByEmpleadoRangoAsync(TenantId, CurrentUserId, desde, hasta);

        string Color(EstadoTurno e) => e switch
        {
            EstadoTurno.Completado => "#10b981",
            EstadoTurno.Confirmado => "#2563eb",
            EstadoTurno.Pendiente => "#f59e0b",
            EstadoTurno.NoShow => "#ef4444",
            _ => "#94a3b8"
        };

        return Json(turnos.Select(t => new
        {
            id = t.Id,
            title = $"{t.ClienteNombre} · {t.ServicioNombre}",
            start = t.FechaHoraInicio.ToString("yyyy-MM-ddTHH:mm:ss"),
            end = t.FechaHoraFin.ToString("yyyy-MM-ddTHH:mm:ss"),
            color = Color(t.Estado),
            extendedProps = new
            {
                cliente = t.ClienteNombre,
                telefono = t.ClienteTelefono ?? "—",
                servicio = t.ServicioNombre,
                estado = t.Estado.Display(),
                precio = t.Precio
            }
        }));
    }

    // ---------------- Galería de cortes ----------------
    [HttpGet("galeria")]
    public async Task<IActionResult> Galeria()
    {
        if (GuardTenant() is { } r) return r;
        return View(await _galeria.GetByEmpleadoAsync(TenantId, CurrentUserId));
    }

    [HttpPost("galeria")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GaleriaSubir(IFormFile? foto, string? descripcion)
    {
        if (GuardTenant() is { } r) return r;
        var url = await _files.GuardarImagenAsync(foto, $"galeria/{TenantId}");
        if (url != null)
        {
            await _galeria.CrearAsync(new GaleriaFoto
            {
                TenantId = TenantId, EmpleadoId = CurrentUserId, FotoUrl = url, Descripcion = descripcion
            });
            TempData["Ok"] = "Foto agregada a tu galería.";
        }
        else TempData["Error"] = "Sube una imagen válida (jpg, png, webp; máx 8 MB).";
        return RedirectToAction(nameof(Galeria), new { slug = Slug });
    }

    [HttpPost("galeria/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GaleriaEliminar(int id)
    {
        if (GuardTenant() is { } r) return r;
        await _galeria.EliminarAsync(TenantId, id);
        TempData["Ok"] = "Foto eliminada.";
        return RedirectToAction(nameof(Galeria), new { slug = Slug });
    }

    [HttpPost("mi-foto")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MiFoto(IFormFile? foto)
    {
        if (GuardTenant() is { } r) return r;
        var url = await _files.GuardarImagenAsync(foto, "profesionales");
        if (url != null) { await _usuarios.SetFotoAsync(CurrentUserId, url); TempData["Ok"] = "Foto de perfil actualizada."; }
        else TempData["Error"] = "Sube una imagen válida.";
        return RedirectToAction(nameof(Galeria), new { slug = Slug });
    }

    private static (DateTime desde, DateTime hasta) Rango(DateTime? desde, DateTime? hasta)
    {
        var d = (desde ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var h = (hasta ?? DateTime.Today).Date.AddDays(1);
        if (h <= d) h = d.AddDays(1);
        return (d, h);
    }
}

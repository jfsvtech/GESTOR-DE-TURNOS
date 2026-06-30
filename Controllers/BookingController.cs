using System.Globalization;
using GeneradorTurnos.Auth;
using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeneradorTurnos.Controllers;

[Route("{slug}")]
public class BookingController : TenantBaseController
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IServicioRepository _servicios;
    private readonly ITurnoRepository _turnos;
    private readonly DisponibilidadService _disponibilidad;
    private readonly TurnoService _turnoService;
    private readonly IGaleriaRepository _galeria;

    public BookingController(ITenantContext tenant, IUsuarioRepository usuarios,
        IServicioRepository servicios, ITurnoRepository turnos,
        DisponibilidadService disponibilidad, TurnoService turnoService, IGaleriaRepository galeria) : base(tenant)
    {
        _usuarios = usuarios;
        _servicios = servicios;
        _turnos = turnos;
        _disponibilidad = disponibilidad;
        _turnoService = turnoService;
        _galeria = galeria;
    }

    // Paso 1: elegir SERVICIO (catálogo del negocio).
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var servicios = await _servicios.GetByTenantAsync(TenantId, soloActivos: true);
        ViewBag.Galeria = await _galeria.GetByTenantAsync(TenantId, 12);
        return View(servicios);
    }

    // Paso 2: profesionales que prestan el servicio elegido.
    [HttpGet("servicio/{servicioId:int}")]
    public async Task<IActionResult> Servicio(int servicioId)
    {
        var servicio = await _servicios.GetByIdAsync(TenantId, servicioId);
        if (servicio is null || !servicio.Activo) return NotFound();

        ViewBag.Servicio = servicio;
        var profesionales = await _servicios.GetProfesionalesPorServicioAsync(TenantId, servicioId);

        // Sugerencia: próximo turno disponible con cada profesional.
        foreach (var p in profesionales)
            p.ProximoDisponible = await _disponibilidad.ProximoSlotAsync(TenantId, p.EmpleadoId, p.DuracionMinutos);

        // Ordenar: primero quienes tienen disponibilidad más cercana.
        profesionales = profesionales
            .OrderBy(p => p.ProximoDisponible ?? DateTime.MaxValue)
            .ToList();

        return View(profesionales);
    }

    // Paso 3: disponibilidad del profesional (servicio preseleccionado).
    [HttpGet("reservar/{empleadoId:int}")]
    public async Task<IActionResult> Reservar(int empleadoId, int? servicioId, string? fecha)
    {
        var empleado = await _usuarios.GetByIdInTenantAsync(TenantId, empleadoId);
        if (empleado is null || !empleado.Atiende || !empleado.Activo)
            return NotFound();

        var servicios = await _servicios.GetByEmpleadoAsync(TenantId, empleadoId);
        var sel = servicioId.HasValue && servicios.Any(s => s.Id == servicioId.Value)
            ? servicioId.Value : servicios.FirstOrDefault()?.Id ?? 0;
        var hoyTenant = TenantTime.Today(Tenant.Current);
        var dia = DateTime.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
            && f.Date >= hoyTenant ? f.Date : hoyTenant;
        return View(new ReservarViewModel
        {
            EmpleadoId = empleadoId,
            EmpleadoNombre = empleado.Nombre,
            Servicios = servicios,
            ServicioId = sel,
            Fecha = dia
        });
    }

    // API: huecos disponibles (consumida por JS).
    [HttpGet("api/slots")]
    [EnableRateLimiting("slots")]
    public async Task<IActionResult> Slots(int empleadoId, int servicioId, string fecha)
    {
        if (!DateTime.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
            dia = TenantTime.Today(Tenant.Current);

        var empleado = await _usuarios.GetByIdInTenantAsync(TenantId, empleadoId);
        if (empleado is null || !empleado.Activo || !empleado.Atiende) return Json(Array.Empty<object>());

        var servicio = await _servicios.GetEfectivoAsync(TenantId, empleadoId, servicioId);
        if (servicio is null || !servicio.Activo) return Json(Array.Empty<object>());

        var slots = await _disponibilidad.CalcularSlotsAsync(TenantId, empleadoId, servicio.DuracionMinutos, dia);
        return Json(slots.Select(s => new
        {
            inicio = s.Inicio.ToString("yyyy-MM-ddTHH:mm:ss"),
            hora = s.Hora
        }));
    }

    private IActionResult? RequireCliente()
    {
        if (UsuarioPerteneceAlTenant() && CurrentRol == Rol.Cliente) return null;
        var returnUrl = Request.Path + Request.QueryString;
        return RedirectToAction("Login", "Account", new { slug = Slug, returnUrl });
    }

    // Ruta propia (evita colisión con GET reservar/{empleadoId} que causaba 405).
    [HttpPost("reservar/confirmar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(int empleadoId, int servicioId, string inicio, string? notas)
    {
        // Si no hay sesión de cliente: guardamos la reserva y la completamos tras iniciar sesión.
        if (!(UsuarioPerteneceAlTenant() && CurrentRol == Rol.Cliente))
        {
            TempData["PendingBooking"] = $"{empleadoId}|{servicioId}|{inicio}|{notas}";
            TempData["Error"] = "Inicia sesión o crea tu cuenta para confirmar tu turno.";
            var volver = Url.Action(nameof(ReanudarReserva), "Booking", new { slug = Slug });
            return RedirectToAction("Login", "Account", new { slug = Slug, returnUrl = volver });
        }

        var res = await IntentarReservarAsync(empleadoId, servicioId, inicio, notas);
        if (!res.Ok)
        {
            TempData["Error"] = res.Error;
            return RedirectToAction(nameof(Reservar), new { slug = Slug, empleadoId, servicioId });
        }
        TempData["Ok"] = "¡Turno reservado correctamente!";
        return RedirectToAction(nameof(MisTurnos), new { slug = Slug });
    }

    // Tras iniciar sesión, completa la reserva que quedó pendiente.
    [HttpGet("reanudar-reserva")]
    public async Task<IActionResult> ReanudarReserva()
    {
        if (RequireCliente() is { } g) return g;

        var data = TempData["PendingBooking"] as string;
        if (string.IsNullOrEmpty(data)) return RedirectToAction(nameof(Index), new { slug = Slug });

        var p = data.Split('|');
        if (p.Length < 3 || !int.TryParse(p[0], out var empId) || !int.TryParse(p[1], out var srvId))
            return RedirectToAction(nameof(Index), new { slug = Slug });

        var res = await IntentarReservarAsync(empId, srvId, p[2], p.Length > 3 ? p[3] : null);
        TempData[res.Ok ? "Ok" : "Error"] = res.Ok ? "¡Turno reservado correctamente!" : res.Error;
        return res.Ok
            ? RedirectToAction(nameof(MisTurnos), new { slug = Slug })
            : RedirectToAction(nameof(Reservar), new { slug = Slug, empleadoId = empId, servicioId = srvId });
    }

    private async Task<OperacionResultado> IntentarReservarAsync(int empleadoId, int servicioId, string inicio, string? notas)
    {
        if (!DateTime.TryParseExact(inicio, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var inicioDt))
            return OperacionResultado.Falla("Horario inválido. Intenta de nuevo.");

        return await _turnoService.ReservarAsync(TenantId, CurrentUserId, empleadoId, servicioId, inicioDt, notas);
    }

    [HttpGet("mis-turnos")]
    public async Task<IActionResult> MisTurnos()
    {
        if (RequireCliente() is { } g) return g;
        var turnos = await _turnos.GetByClienteAsync(TenantId, CurrentUserId);
        return View(turnos);
    }

    [HttpPost("mis-turnos/{id:int}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(int id)
    {
        if (RequireCliente() is { } g) return g;
        var res = await _turnoService.CancelarPorClienteAsync(TenantId, id, CurrentUserId);
        TempData[res.Ok ? "Ok" : "Error"] = res.Ok ? "Turno cancelado." : res.Error;
        return RedirectToAction(nameof(MisTurnos), new { slug = Slug });
    }

    [HttpGet("mis-turnos/{id:int}/reprogramar")]
    public async Task<IActionResult> Reprogramar(int id)
    {
        if (RequireCliente() is { } g) return g;
        var turno = await _turnos.GetByIdAsync(TenantId, id);
        if (turno is null || turno.ClienteId != CurrentUserId) return NotFound();

        var empleado = await _usuarios.GetByIdInTenantAsync(TenantId, turno.EmpleadoId);
        var servicio = await _servicios.GetByIdAsync(TenantId, turno.ServicioId);
        ViewData["TurnoId"] = id;
        return View(new ReservarViewModel
        {
            EmpleadoId = turno.EmpleadoId,
            EmpleadoNombre = empleado?.Nombre ?? "",
            ServicioId = turno.ServicioId,
            Servicios = servicio is null ? new() : new List<Servicio> { servicio },
            Fecha = TenantTime.Today(Tenant.Current)
        });
    }

    [HttpPost("mis-turnos/{id:int}/reprogramar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprogramar(int id, string inicio)
    {
        if (RequireCliente() is { } g) return g;
        if (!DateTime.TryParseExact(inicio, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var inicioDt))
        {
            TempData["Error"] = "Horario inválido.";
            return RedirectToAction(nameof(Reprogramar), new { slug = Slug, id });
        }

        var res = await _turnoService.ReprogramarAsync(TenantId, id, CurrentUserId, inicioDt);
        TempData[res.Ok ? "Ok" : "Error"] = res.Ok ? "Turno reprogramado." : res.Error;
        return res.Ok
            ? RedirectToAction(nameof(MisTurnos), new { slug = Slug })
            : RedirectToAction(nameof(Reprogramar), new { slug = Slug, id });
    }
}

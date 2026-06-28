using System.Globalization;
using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeneradorTurnos.Controllers;

[Authorize(Roles = "Dueno")]
[Route("{slug}/admin")]
public class DuenoController : TenantBaseController
{
    private readonly IStatsRepository _stats;
    private readonly IServicioRepository _servicios;
    private readonly IUsuarioRepository _usuarios;
    private readonly IAgendaRepository _agenda;
    private readonly ITurnoRepository _turnos;
    private readonly ExcelService _excel;
    private readonly IEmailSender _email;
    private readonly IGastoRepository _gastos;
    private readonly ITenantRepository _tenants;
    private readonly IFileStorage _files;

    public DuenoController(ITenantContext tenant, IStatsRepository stats, IServicioRepository servicios,
        IUsuarioRepository usuarios, IAgendaRepository agenda, ITurnoRepository turnos, ExcelService excel,
        IEmailSender email, IGastoRepository gastos, ITenantRepository tenants, IFileStorage files) : base(tenant)
    {
        _stats = stats;
        _servicios = servicios;
        _usuarios = usuarios;
        _agenda = agenda;
        _turnos = turnos;
        _excel = excel;
        _email = email;
        _gastos = gastos;
        _tenants = tenants;
        _files = files;
    }

    private IActionResult? GuardTenant()
        => UsuarioPerteneceAlTenant() ? null : RedirectToAction("Login", "Account", new { slug = Slug });

    // ---------------- Dashboard ----------------
    [HttpGet("")]
    public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta)
    {
        if (GuardTenant() is { } r) return r;
        var (d, h) = Rango(desde, hasta);

        var turnosRango = await _turnos.GetByTenantRangoAsync(TenantId, d, h);
        var barberosResumen = await _stats.ResumenPorBarberoAsync(TenantId, d, h);
        var dias = Math.Max(1, (h.Date - d.Date).Days);
        var capacidadHoras = Math.Max(1, barberosResumen.Count) * dias * 8m;
        var horasOcupadas = turnosRango
            .Where(t => t.Estado != EstadoTurno.Cancelado && t.Estado != EstadoTurno.NoShow)
            .Sum(t => (decimal)(t.FechaHoraFin - t.FechaHoraInicio).TotalHours);

        var vm = new DashboardDuenoVm
        {
            Desde = d,
            Hasta = h.AddDays(-1),
            Resumen = await _stats.ResumenAsync(TenantId, d, h),
            IngresosPorDia = await _stats.SeriePorDiaAsync(TenantId, d, h),
            ServiciosTop = await _stats.ServiciosTopAsync(TenantId, d, h),
            IngresosPorBarbero = await _stats.IngresosPorBarberoAsync(TenantId, d, h),
            Barberos = barberosResumen,
            Gastos = await _gastos.TotalEnRangoAsync(TenantId, d, h),
            HorasMuertasEstimadas = Math.Max(0, capacidadHoras - horasOcupadas),
            OcupacionPorBarbero = barberosResumen.Select(b => new RankingItem
            {
                Etiqueta = b.Nombre,
                Cantidad = b.Total,
                Valor = b.Total == 0 ? 0 : Math.Round(b.Completados * 100m / b.Total, 0)
            }).ToList(),
            ClientesRecurrentes = turnosRango
                .GroupBy(t => t.ClienteNombre)
                .Select(g => new RankingItem { Etiqueta = g.Key, Cantidad = g.Count(), Valor = g.Sum(x => x.Precio) })
                .Where(x => x.Cantidad > 1)
                .OrderByDescending(x => x.Cantidad)
                .Take(8).ToList(),
            CancelacionesPorDia = turnosRango
                .Where(t => t.Estado == EstadoTurno.Cancelado)
                .GroupBy(t => t.FechaHoraInicio.Date)
                .Select(g => new SerieFecha { Fecha = g.Key, Cantidad = g.Count(), Valor = g.Count() })
                .OrderBy(x => x.Fecha).ToList(),
            ProductividadPorSemana = turnosRango
                .Where(t => t.Estado == EstadoTurno.Completado)
                .GroupBy(t => System.Globalization.ISOWeek.GetWeekOfYear(t.FechaHoraInicio))
                .Select(g => new SerieFecha { Fecha = d.AddDays((g.Key - System.Globalization.ISOWeek.GetWeekOfYear(d)) * 7), Cantidad = g.Count(), Valor = g.Sum(x => x.Precio) })
                .OrderBy(x => x.Fecha).ToList()
        };
        return View(vm);
    }

    // ---------------- Gastos del negocio ----------------
    [HttpGet("onboarding")]
    public async Task<IActionResult> Onboarding()
    {
        if (GuardTenant() is { } r) return r;
        var servicios = await _servicios.GetByTenantAsync(TenantId);
        var profesionales = await _usuarios.GetByRolAsync(TenantId, Rol.Barbero, soloActivos: false);
        var publicUrl = $"{Request.Scheme}://{Request.Host}/{Slug}";
        ViewBag.PublicUrl = publicUrl;
        ViewBag.QrUrl = "https://api.qrserver.com/v1/create-qr-code/?size=320x320&data=" + Uri.EscapeDataString(publicUrl);
        ViewBag.TieneServicios = servicios.Any();
        ViewBag.TieneTrabajadores = profesionales.Any();
        ViewBag.TieneFoto = !string.IsNullOrWhiteSpace(Tenant.Current?.FotoUrl);
        ViewBag.TieneHorarios = profesionales.Any(); // se marca como siguiente paso operativo.
        return View();
    }

    [HttpGet("gastos")]
    public async Task<IActionResult> Gastos(DateTime? desde, DateTime? hasta)
    {
        if (GuardTenant() is { } r) return r;
        var (d, h) = Rango(desde, hasta);
        return View(new GastosVm
        {
            Desde = d, Hasta = h.AddDays(-1),
            Gastos = await _gastos.GetEnRangoAsync(TenantId, d, h),
            Total = await _gastos.TotalEnRangoAsync(TenantId, d, h)
        });
    }

    [HttpPost("gastos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GastoCrear(GastoFormVm vm)
    {
        if (GuardTenant() is { } r) return r;
        if (ModelState.IsValid)
        {
            await _gastos.CrearAsync(new Gasto
            {
                TenantId = TenantId, Concepto = vm.Concepto.Trim(),
                Categoria = string.IsNullOrWhiteSpace(vm.Categoria) ? null : vm.Categoria.Trim(),
                Monto = vm.Monto, Fecha = vm.Fecha
            });
            TempData["Ok"] = "Gasto registrado.";
        }
        else TempData["Error"] = "Revisa los datos del gasto.";
        return RedirectToAction(nameof(Gastos), new { slug = Slug });
    }

    [HttpPost("gastos/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GastoEliminar(int id)
    {
        if (GuardTenant() is { } r) return r;
        await _gastos.EliminarAsync(TenantId, id);
        TempData["Ok"] = "Gasto eliminado.";
        return RedirectToAction(nameof(Gastos), new { slug = Slug });
    }

    // ---------------- "Yo también atiendo" (dueño como profesional) ----------------
    [HttpPost("perfil/atiende")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAtiende(bool atiende)
    {
        if (GuardTenant() is { } r) return r;
        await _usuarios.SetAtiendeAsync(CurrentUserId, atiende);
        TempData["Ok"] = atiende
            ? "Ahora apareces como profesional. Configura tus servicios y horarios."
            : "Ya no apareces como profesional reservable.";
        return RedirectToAction(nameof(Profesionales), new { slug = Slug });
    }

    // ---------------- Foto / portada del negocio ----------------
    [HttpGet("negocio")]
    public IActionResult Negocio()
    {
        if (GuardTenant() is { } r) return r;
        return View(Tenant.Current);
    }

    [HttpPost("negocio/foto")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NegocioFoto(IFormFile? foto)
    {
        if (GuardTenant() is { } r) return r;
        var url = await _files.GuardarImagenAsync(foto, "negocios");
        if (url != null) { await _tenants.SetFotoAsync(TenantId, url); TempData["Ok"] = "Foto del negocio actualizada."; }
        else TempData["Error"] = "Sube una imagen válida (jpg, png, webp; máx 8 MB).";
        return RedirectToAction(nameof(Negocio), new { slug = Slug });
    }

    // ---------------- Turnos ----------------
    [HttpGet("turnos")]
    public async Task<IActionResult> Turnos(DateTime? desde, DateTime? hasta)
    {
        if (GuardTenant() is { } r) return r;
        var (d, h) = Rango(desde, hasta);
        ViewData["Desde"] = d;
        ViewData["Hasta"] = h.AddDays(-1);
        var turnos = await _turnos.GetByTenantRangoAsync(TenantId, d, h);
        return View(turnos);
    }

    [HttpGet("turnos/exportar")]
    public async Task<IActionResult> Exportar(DateTime? desde, DateTime? hasta)
    {
        if (GuardTenant() is { } r) return r;
        var (d, h) = Rango(desde, hasta);
        var turnos = await _turnos.GetByTenantRangoAsync(TenantId, d, h);
        var bytes = _excel.ExportarTurnos(Tenant.Current!.Nombre, d, h, turnos);
        var nombre = $"turnos_{Slug}_{d:yyyyMMdd}_{h.AddDays(-1):yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
    }

    // ---------------- Servicios ----------------
    [HttpGet("servicios")]
    public async Task<IActionResult> Servicios()
    {
        if (GuardTenant() is { } r) return r;
        ViewBag.Solicitudes = await _servicios.GetSolicitudesPendientesAsync(TenantId);
        return View(await _servicios.GetByTenantAsync(TenantId));
    }

    [HttpPost("servicios/solicitudes/{id:int}/aprobar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AprobarSolicitudServicio(int id)
    {
        if (GuardTenant() is { } r) return r;
        var solicitud = await _servicios.GetSolicitudAsync(TenantId, id);
        if (solicitud is null || solicitud.Estado != "Pendiente") return NotFound();

        await _servicios.UpsertOverrideAsync(TenantId, solicitud.EmpleadoId, solicitud.ServicioId,
            solicitud.Ofrecido, solicitud.PrecioOverride, solicitud.DuracionOverride);
        await _servicios.ResolverSolicitudAsync(TenantId, id, aprobada: true);
        TempData["Ok"] = "Cambio aprobado y aplicado.";
        return RedirectToAction(nameof(Servicios), new { slug = Slug });
    }

    [HttpPost("servicios/solicitudes/{id:int}/rechazar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RechazarSolicitudServicio(int id)
    {
        if (GuardTenant() is { } r) return r;
        await _servicios.ResolverSolicitudAsync(TenantId, id, aprobada: false);
        TempData["Ok"] = "Solicitud rechazada.";
        return RedirectToAction(nameof(Servicios), new { slug = Slug });
    }

    [HttpGet("servicios/nuevo")]
    public IActionResult ServicioNuevo()
    {
        if (GuardTenant() is { } r) return r;
        return View("ServicioForm", new ServicioFormVm());
    }

    [HttpGet("servicios/{id:int}/editar")]
    public async Task<IActionResult> ServicioEditar(int id)
    {
        if (GuardTenant() is { } r) return r;
        var s = await _servicios.GetByIdAsync(TenantId, id);
        if (s is null) return NotFound();
        return View("ServicioForm", new ServicioFormVm
        {
            Id = s.Id, Nombre = s.Nombre, DuracionMinutos = s.DuracionMinutos, Precio = s.Precio, Activo = s.Activo
        });
    }

    [HttpPost("servicios/guardar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ServicioGuardar(ServicioFormVm vm)
    {
        if (GuardTenant() is { } r) return r;
        if (!ModelState.IsValid) return View("ServicioForm", vm);

        if (vm.Id == 0)
            await _servicios.CreateAsync(new Servicio
            {
                TenantId = TenantId, Nombre = vm.Nombre, DuracionMinutos = vm.DuracionMinutos,
                Precio = vm.Precio, Activo = vm.Activo
            });
        else
            await _servicios.UpdateAsync(new Servicio
            {
                Id = vm.Id, TenantId = TenantId, Nombre = vm.Nombre, DuracionMinutos = vm.DuracionMinutos,
                Precio = vm.Precio, Activo = vm.Activo
            });

        TempData["Ok"] = "Servicio guardado.";
        return RedirectToAction(nameof(Servicios), new { slug = Slug });
    }

    // ---------------- Profesionales ----------------
    [HttpGet("profesionales")]
    public async Task<IActionResult> Profesionales()
    {
        if (GuardTenant() is { } r) return r;
        var yo = await _usuarios.GetByIdInTenantAsync(TenantId, CurrentUserId);
        ViewBag.YoAtiendo = yo?.Atiende ?? false;
        return View(await _usuarios.GetByRolAsync(TenantId, Rol.Barbero, soloActivos: false));
    }

    [HttpGet("profesionales/nuevo")]
    public async Task<IActionResult> ProfesionalNuevo()
    {
        if (GuardTenant() is { } r) return r;
        ViewBag.Servicios = await _servicios.GetByTenantAsync(TenantId, soloActivos: true);
        return View("ProfesionalForm", new BarberoFormVm());
    }

    [HttpGet("profesionales/{id:int}/editar")]
    public async Task<IActionResult> ProfesionalEditar(int id)
    {
        if (GuardTenant() is { } r) return r;
        var u = await _usuarios.GetByIdInTenantAsync(TenantId, id);
        if (u is null || u.Rol != Rol.Barbero) return NotFound();
        ViewBag.Servicios = await _servicios.GetByTenantAsync(TenantId, soloActivos: true);
        ViewBag.FotoActual = u.FotoUrl;
        return View("ProfesionalForm", new BarberoFormVm
        {
            Id = u.Id, Nombre = u.Nombre, Cedula = u.Cedula ?? "", Telefono = u.Telefono,
            Email = u.Email, Activo = u.Activo,
            ComisionTipo = u.ComisionTipo, ComisionValor = u.ComisionValor,
            ServicioIds = await _servicios.GetServicioIdsByEmpleadoAsync(TenantId, id)
        });
    }

    [HttpPost("profesionales/guardar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProfesionalGuardar(BarberoFormVm vm, IFormFile? foto)
    {
        if (GuardTenant() is { } r) return r;

        async Task<IActionResult> Recargar()
        {
            ViewBag.Servicios = await _servicios.GetByTenantAsync(TenantId, soloActivos: true);
            return View("ProfesionalForm", vm);
        }

        if (!ModelState.IsValid) return await Recargar();
        if (vm.Id == 0 && string.IsNullOrWhiteSpace(vm.Password))
        {
            ModelState.AddModelError(nameof(vm.Password), "La contraseña es obligatoria al crear.");
            return await Recargar();
        }
        if (vm.Id == 0 && string.IsNullOrWhiteSpace(vm.Email))
        {
            ModelState.AddModelError(nameof(vm.Email), "El correo es obligatorio (inicio de sesión con verificación).");
            return await Recargar();
        }

        int empleadoId;
        if (vm.Id == 0)
        {
            if (await _usuarios.CedulaExistsAsync(TenantId, vm.Cedula.Trim()))
            {
                ModelState.AddModelError(nameof(vm.Cedula), "Ya existe un usuario con esa cédula.");
                return await Recargar();
            }
            if (await _usuarios.EmailExistsAsync(TenantId, vm.Email!.Trim()))
            {
                ModelState.AddModelError(nameof(vm.Email), "Ya existe un usuario con ese correo.");
                return await Recargar();
            }
            if (await _usuarios.CountTrabajadoresAsync(TenantId) >= Tenant.Current!.MaxUsuarios)
            {
                ModelState.AddModelError("", "La empresa alcanzo el limite de trabajadores del plan.");
                return await Recargar();
            }

            var token = Guid.NewGuid().ToString("N");
            empleadoId = await _usuarios.CreateAsync(new Usuario
            {
                TenantId = TenantId, Rol = Rol.Barbero, Nombre = vm.Nombre.Trim(), Cedula = vm.Cedula.Trim(),
                Telefono = vm.Telefono, Email = vm.Email!.Trim(), Activo = vm.Activo, Atiende = true,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password!),
                EmailVerificado = false, TokenVerificacion = token, TokenExpira = DateTime.Now.AddDays(7)
            });
            await EnviarVerificacionEquipoAsync(vm.Nombre.Trim(), vm.Email!.Trim(), token);
        }
        else
        {
            var u = await _usuarios.GetByIdInTenantAsync(TenantId, vm.Id);
            if (u is null) return NotFound();
            u.Nombre = vm.Nombre.Trim(); u.Telefono = vm.Telefono; u.Email = vm.Email; u.Activo = vm.Activo;
            await _usuarios.UpdateAsync(u);
            if (!string.IsNullOrWhiteSpace(vm.Password))
                await _usuarios.UpdatePasswordAsync(u.Id, BCrypt.Net.BCrypt.HashPassword(vm.Password));
            empleadoId = u.Id;
        }

        await _servicios.SetServiciosEmpleadoAsync(TenantId, empleadoId, vm.ServicioIds ?? new());
        await _usuarios.SetComisionAsync(empleadoId, vm.ComisionTipo, vm.ComisionValor);
        var fotoUrl = await _files.GuardarImagenAsync(foto, "profesionales");
        if (fotoUrl != null) await _usuarios.SetFotoAsync(empleadoId, fotoUrl);

        if (vm.Id == 0)
            // Nuevo: debe verificar su correo antes de poder ingresar.
            return RedirectToAction("VerificacionPendiente", "Account", new { slug = Slug });

        TempData["Ok"] = "Profesional guardado.";
        return RedirectToAction(nameof(Profesionales), new { slug = Slug });
    }

    private async Task EnviarVerificacionEquipoAsync(string nombre, string email, string token)
    {
        var link = Url.Action("Verificar", "Account", new { slug = Slug, token }, Request.Scheme)!;
        var html = $@"<p>Hola {nombre},</p>
            <p>Te crearon una cuenta de profesional en <strong>{Tenant.Current!.Nombre}</strong>.
            Verifica tu correo para poder ingresar:</p>
            <p><a href='{link}'>Verificar mi correo</a></p>";
        await _email.SendAsync(email, "Verifica tu cuenta", html);
        TempData["PendingEmail"] = email;
        if (!_email.Enabled) TempData["VerifyLink"] = link;
    }

    // ---------------- Horarios ----------------
    private static readonly string[] DiasNombre =
        { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };

    [HttpGet("profesionales/{id:int}/horarios")]
    public async Task<IActionResult> Horarios(int id)
    {
        if (GuardTenant() is { } r) return r;
        var u = await _usuarios.GetByIdInTenantAsync(TenantId, id);
        if (u is null || u.Rol != Rol.Barbero) return NotFound();
        ViewBag.Empleado = u;
        ViewBag.Dias = DiasNombre;
        return View(await _agenda.GetHorariosAsync(TenantId, id));
    }

    [HttpPost("profesionales/{id:int}/horarios")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Horarios(int id, int[] dia, string[] inicio, string[] fin)
    {
        if (GuardTenant() is { } r) return r;
        var u = await _usuarios.GetByIdInTenantAsync(TenantId, id);
        if (u is null || u.Rol != Rol.Barbero) return NotFound();

        var horarios = new List<HorarioTrabajo>();
        for (int i = 0; i < (dia?.Length ?? 0); i++)
        {
            if (i >= inicio.Length || i >= fin.Length) break;
            if (string.IsNullOrWhiteSpace(inicio[i]) || string.IsNullOrWhiteSpace(fin[i])) continue;
            if (!TimeOnly.TryParse(inicio[i], out var hi) || !TimeOnly.TryParse(fin[i], out var hf)) continue;
            if (hf <= hi) continue;
            horarios.Add(new HorarioTrabajo
            {
                TenantId = TenantId, EmpleadoId = id, DiaSemana = dia![i], HoraInicio = hi, HoraFin = hf
            });
        }

        await _agenda.ReemplazarHorariosAsync(TenantId, id, horarios);
        TempData["Ok"] = "Horarios actualizados.";
        return RedirectToAction(nameof(Profesionales), new { slug = Slug });
    }

    private static (DateTime desde, DateTime hasta) Rango(DateTime? desde, DateTime? hasta)
    {
        var d = (desde ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var h = (hasta ?? DateTime.Today).Date.AddDays(1);
        if (h <= d) h = d.AddDays(1);
        return (d, h);
    }
}

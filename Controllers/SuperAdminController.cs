using System.Security.Claims;
using System.Text.RegularExpressions;
using GeneradorTurnos.Auth;
using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeneradorTurnos.Controllers;

[Route("super")]
[EnableRateLimiting("auth")]
public class SuperAdminController : Controller
{
    private readonly ITenantRepository _tenants;
    private readonly IUsuarioRepository _usuarios;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public SuperAdminController(ITenantRepository tenants, IUsuarioRepository usuarios, IEmailSender email, IConfiguration config)
    {
        _tenants = tenants;
        _usuarios = usuarios;
        _email = email;
        _config = config;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginEmailVm { ReturnUrl = returnUrl });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginEmailVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await _usuarios.GetGlobalByEmailAsync(vm.Email.Trim());
        if (u is null || u.Rol != Rol.SuperAdmin || !BCrypt.Net.BCrypt.Verify(vm.Password, u.PasswordHash))
        {
            ModelState.AddModelError("", "Credenciales incorrectas.");
            return View(vm);
        }
        if (!u.EmailVerificado)
        {
            ModelState.AddModelError("", "Tu correo aún no está verificado.");
            return View(vm);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nombre),
            new(ClaimTypes.Role, Rol.SuperAdmin.ToString()),
            new(AppClaims.TenantId, "0")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("salir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salir()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var empresas = await _tenants.GetDashboardAsync();
        return View(new SuperAdminDashboardVm
        {
            Empresas = empresas,
            VentasTotales = empresas.Sum(e => e.Ventas),
            EmpresasActivas = empresas.Count(e => e.Activo),
            UsuariosTotales = empresas.Sum(e => e.Trabajadores),
            TurnosTotales = empresas.Sum(e => e.Turnos)
        });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("configuracion/correo")]
    public IActionResult ConfiguracionCorreo()
    {
        var names = new[]
        {
            "Email:Enabled",
            "Email:Provider",
            "GmailApi:ClientId",
            "GmailApi:ClientSecret",
            "GmailApi:RefreshToken",
            "GmailApi:From",
            "GmailApi:FromName"
        };

        return View(new EmailConfigStatusVm
        {
            Enabled = _config.GetValue("Email:Enabled", false),
            Provider = _config["Email:Provider"] ?? "Smtp",
            From = Mask(_config["GmailApi:From"] ?? _config["Email:From"]),
            FromName = _config["GmailApi:FromName"] ?? "",
            Variables = names.Select(n => new ConfigSecretStatus
            {
                Name = n.Replace(':', '_'),
                Present = !string.IsNullOrWhiteSpace(_config[n]),
                MaskedValue = Mask(_config[n])
            }).ToList()
        });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("empresa/{id:int}")]
    public async Task<IActionResult> Empresa(int id)
    {
        var empresa = await _tenants.GetByIdAsync(id);
        if (empresa is null) return NotFound();
        return View(new SuperAdminEmpresaDetalleVm
        {
            Empresa = empresa,
            Usuarios = await _usuarios.GetByTenantAsync(id),
            Pagos = await _tenants.GetPagosAsync(id),
            Auditoria = await _tenants.GetAuditoriaAsync(id),
            NuevoUsuario = new UsuarioInternoFormVm { TenantId = id },
            NuevoPago = new PagoSuscripcionFormVm { TenantId = id }
        });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("empresa/{id:int}/suscripcion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarSuscripcion(int id, SuscripcionFormVm vm)
    {
        var empresa = await _tenants.GetByIdAsync(id);
        if (empresa is null) return NotFound();

        empresa.Plan = vm.Plan;
        empresa.EstadoSuscripcion = vm.EstadoSuscripcion;
        empresa.SuscripcionInicio = vm.SuscripcionInicio;
        empresa.SuscripcionVencimiento = vm.SuscripcionVencimiento;
        empresa.RecordatorioPagoDias = vm.RecordatorioPagoDias;
        await _tenants.UpdateSuscripcionAsync(empresa);
        await Auditar(id, "Actualizar suscripcion", "Tenant", id,
            $"Plan {vm.Plan}, estado {vm.EstadoSuscripcion}, vence {vm.SuscripcionVencimiento:yyyy-MM-dd}");
        TempData["Ok"] = "Suscripcion actualizada.";
        return RedirectToAction(nameof(Empresa), new { id });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("empresa/{id:int}/pago")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarPago(int id, PagoSuscripcionFormVm vm)
    {
        if (await _tenants.GetByIdAsync(id) is null) return NotFound();
        if (vm.PeriodoFin < vm.PeriodoInicio) vm.PeriodoFin = vm.PeriodoInicio;
        await _tenants.RegistrarPagoAsync(new PagoSuscripcion
        {
            TenantId = id,
            Monto = vm.Monto,
            PeriodoInicio = vm.PeriodoInicio,
            PeriodoFin = vm.PeriodoFin,
            Metodo = vm.Metodo,
            Referencia = vm.Referencia,
            Nota = vm.Nota
        });
        await Auditar(id, "Registrar pago", "PagoSuscripcion", null,
            $"Pago ${vm.Monto:#,##0} periodo {vm.PeriodoInicio:yyyy-MM-dd} a {vm.PeriodoFin:yyyy-MM-dd}");
        TempData["Ok"] = "Pago registrado.";
        return RedirectToAction(nameof(Empresa), new { id });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("empresa/{id:int}/usuarios")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearUsuario(int id, UsuarioInternoFormVm vm)
    {
        if (await _tenants.GetByIdAsync(id) is null) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisa nombre, correo y rol del usuario.";
            return RedirectToAction(nameof(Empresa), new { id });
        }

        if (string.IsNullOrWhiteSpace(vm.Password)) vm.Password = Guid.NewGuid().ToString("N")[..10] + "aA1!";
        if (!string.IsNullOrWhiteSpace(vm.Email) && await _usuarios.EmailExistsAsync(id, vm.Email))
        {
            TempData["Error"] = "Ya existe un usuario con ese correo.";
            return RedirectToAction(nameof(Empresa), new { id });
        }

        var empresa = await _tenants.GetByIdAsync(id);
        if (empresa is null) return NotFound();
        var token = Guid.NewGuid().ToString("N");
        var userId = await _usuarios.CreateAsync(new Usuario
        {
            TenantId = id,
            Rol = vm.Rol,
            Nombre = vm.Nombre.Trim(),
            Cedula = string.IsNullOrWhiteSpace(vm.Cedula) ? null : vm.Cedula.Trim(),
            Email = vm.Email.Trim(),
            Telefono = vm.Telefono,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
            Activo = vm.Activo,
            Atiende = vm.Rol == Rol.Barbero,
            EmailVerificado = false,
            TokenVerificacion = token,
            TokenExpira = DateTime.Now.AddDays(7)
        });
        var link = Url.Action("Verificar", "Account", new { slug = empresa.Slug, token }, Request.Scheme)!;
        var html = $@"<p>Hola {vm.Nombre.Trim()},</p>
            <p>Te crearon una cuenta en <strong>{empresa.Nombre}</strong>.</p>
            <p>Para activar el acceso debes verificar tu correo:</p>
            <p><a href='{link}'>Verificar mi correo</a></p>";
        await _email.SendAsync(vm.Email.Trim(), "Verifica tu cuenta", html);

        await Auditar(id, "Crear usuario", "Usuario", userId, $"{vm.Nombre} ({vm.Rol})");
        TempData["Ok"] = _email.Enabled
            ? "Usuario creado. Enviamos el correo de verificacion."
            : $"Usuario creado. SMTP esta desactivado; enlace de verificacion: {link}";
        return RedirectToAction(nameof(Empresa), new { id });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("empresa/{tenantId:int}/usuarios/{userId:int}/estado")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActualizarUsuario(int tenantId, int userId, Rol rol, bool activo)
    {
        await _usuarios.SetRolActivoAsync(tenantId, userId, rol, activo);
        await Auditar(tenantId, "Actualizar usuario", "Usuario", userId, $"Rol {rol}, activo {activo}");
        TempData["Ok"] = "Usuario actualizado.";
        return RedirectToAction(nameof(Empresa), new { id = tenantId });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("empresa/{tenantId:int}/usuarios/{userId:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarUsuario(int tenantId, int userId)
    {
        await _usuarios.DeleteAsync(tenantId, userId);
        await Auditar(tenantId, "Eliminar usuario", "Usuario", userId, "Eliminado desde panel SaaS");
        TempData["Ok"] = "Usuario eliminado.";
        return RedirectToAction(nameof(Empresa), new { id = tenantId });
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("nuevo")]
    public IActionResult Nuevo() => View(new TenantFormVm());

    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("produccion/limpiar")]
    public IActionResult LimpiarProduccion() => View(new ResetProduccionVm());

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("produccion/limpiar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LimpiarProduccion(ResetProduccionVm vm)
    {
        const string frase = "LIMPIAR PRODUCCION";
        if (!string.Equals(vm.Confirmacion?.Trim(), frase, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(vm.Confirmacion), $"Escribe exactamente: {frase}");
            return View(vm);
        }

        var eliminadas = await _tenants.ResetProductionDataAsync(User.Identity?.Name ?? "SuperAdmin");
        TempData["Ok"] = $"Produccion limpiada. Empresas eliminadas: {eliminadas}. Los superadmins globales se conservaron.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("nuevo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Nuevo(TenantFormVm vm)
    {
        var slug = Slugify(vm.Slug);
        if (string.IsNullOrWhiteSpace(slug))
            ModelState.AddModelError(nameof(vm.Slug), "Identificador inválido.");
        else if (await _tenants.SlugExistsAsync(slug))
            ModelState.AddModelError(nameof(vm.Slug), "Ese identificador ya está en uso.");

        if (!ModelState.IsValid) return View(vm);

        var tenantId = await _tenants.CreateAsync(new Tenant
        {
            Nombre = vm.Nombre.Trim(), Slug = slug, Plan = vm.Plan, MaxUsuarios = vm.MaxUsuarios, Activo = true
        });

        var token = Guid.NewGuid().ToString("N");
        await _usuarios.CreateAsync(new Usuario
        {
            TenantId = tenantId, Rol = Rol.Dueno, Nombre = vm.DuenoNombre.Trim(), Cedula = vm.DuenoCedula.Trim(),
            Email = vm.DuenoEmail.Trim(), PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.DuenoPassword), Activo = true,
            EmailVerificado = false, TokenVerificacion = token, TokenExpira = DateTime.Now.AddDays(7)
        });

        var link = Url.Action("Verificar", "Account", new { slug, token }, Request.Scheme)!;
        var html = $"<p>Hola {vm.DuenoNombre},</p><p>Tu negocio <strong>{vm.Nombre}</strong> fue creado. " +
                   $"Verifica tu correo para ingresar:</p><p><a href='{link}'>Verificar mi correo</a></p>";
        await _email.SendAsync(vm.DuenoEmail.Trim(), "Verifica tu cuenta", html);

        TempData["Ok"] = _email.Enabled
            ? $"Negocio '{vm.Nombre}' creado. Enviamos verificación a {vm.DuenoEmail}."
            : $"Negocio '{vm.Nombre}' creado. Enlace de verificación del dueño: {link}";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{id:int}/estado")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(int id, bool activo)
    {
        await _tenants.SetActivoAsync(id, activo);
        await Auditar(id, activo ? "Activar empresa" : "Suspender empresa", "Tenant", id, null);
        TempData["Ok"] = activo ? "Negocio activado." : "Negocio desactivado.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{id:int}/limite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarLimite(int id, int maxUsuarios)
    {
        if (maxUsuarios < 1) maxUsuarios = 1;
        await _tenants.UpdateMaxUsuariosAsync(id, maxUsuarios);
        await Auditar(id, "Actualizar limite trabajadores", "Tenant", id, $"Limite {maxUsuarios}");
        TempData["Ok"] = "Limite de trabajadores actualizado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task Auditar(int? tenantId, string accion, string entidad, int? entidadId, string? detalle)
    {
        await _tenants.AddAuditoriaAsync(new Auditoria
        {
            TenantId = tenantId,
            ActorNombre = User.Identity?.Name ?? "SuperAdmin",
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId,
            Detalle = detalle
        });
    }

    private static string Slugify(string input)
    {
        input = (input ?? "").Trim().ToLowerInvariant();
        input = input.Replace("á", "a").Replace("é", "e").Replace("í", "i")
                     .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
        input = Regex.Replace(input, @"[^a-z0-9]+", "-").Trim('-');
        return input;
    }

    private static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "No configurado";
        if (value.Length <= 8) return "********";
        return $"{value[..4]}...{value[^4..]}";
    }
}

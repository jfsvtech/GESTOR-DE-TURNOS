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
public class SuperAdminController : Controller
{
    private readonly ITenantRepository _tenants;
    private readonly IUsuarioRepository _usuarios;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<SuperAdminController> _logger;

    public SuperAdminController(ITenantRepository tenants, IUsuarioRepository usuarios, IEmailSender email, IConfiguration config, ILogger<SuperAdminController> logger)
    {
        _tenants = tenants;
        _usuarios = usuarios;
        _email = email;
        _config = config;
        _logger = logger;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginEmailVm { ReturnUrl = returnUrl });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
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

    [HttpGet("recuperar")]
    public IActionResult Recuperar() => View(new ForgotPasswordVm());

    [HttpPost("recuperar")]
    [EnableRateLimiting("auth")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recuperar(ForgotPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await _usuarios.GetGlobalByEmailAsync(vm.Email.Trim());
        if (u is not null && u.Rol == Rol.SuperAdmin && u.Activo)
        {
            var token = Guid.NewGuid().ToString("N");
            await _usuarios.SetTokenAsync(u.Id, token, DateTime.Now.AddHours(2));
            var link = Url.Action(nameof(Restablecer), "SuperAdmin", new { token }, Request.Scheme)!;
            var html = $@"<p>Hola {u.Nombre},</p>
                <p>Recibimos una solicitud para restablecer tu contrasena del panel SaaS.</p>
                <p><a href='{link}'>Crear nueva contrasena</a></p>
                <p>Este enlace vence en 2 horas.</p>";
            await _email.SendAsync(u.Email!, "Restablecer contrasena SaaS", html);
            if (!_email.Enabled) TempData["VerifyLink"] = link;
        }

        TempData["Ok"] = "Si el correo existe, enviamos un enlace para restablecer la contrasena.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("restablecer")]
    public async Task<IActionResult> Restablecer(string token)
    {
        var u = await _usuarios.GetByTokenAsync(token);
        if (u is null || u.TenantId is not null || u.Rol != Rol.SuperAdmin)
        {
            TempData["Error"] = "El enlace no es valido o expiro.";
            return RedirectToAction(nameof(Login));
        }

        return View(new ResetPasswordVm { Token = token });
    }

    [HttpPost("restablecer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restablecer(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await _usuarios.GetByTokenAsync(vm.Token);
        if (u is null || u.TenantId is not null || u.Rol != Rol.SuperAdmin)
        {
            ModelState.AddModelError("", "El enlace no es valido o expiro.");
            return View(vm);
        }

        await _usuarios.UpdatePasswordAsync(u.Id, BCrypt.Net.BCrypt.HashPassword(vm.Password));
        await _usuarios.MarcarEmailVerificadoAsync(u.Id);
        TempData["Ok"] = "Contrasena actualizada. Ya puedes iniciar sesion.";
        return RedirectToAction(nameof(Login));
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
        empresa.CicloSuscripcion = vm.CicloSuscripcion;
        empresa.ValorSuscripcion = vm.ValorSuscripcion;
        empresa.EstadoSuscripcion = vm.EstadoSuscripcion;
        empresa.SuscripcionInicio = vm.SuscripcionInicio;
        empresa.SuscripcionVencimiento = vm.SuscripcionVencimiento;
        empresa.RecordatorioPagoDias = vm.RecordatorioPagoDias;
        await _tenants.UpdateSuscripcionAsync(empresa);
        await Auditar(id, "Actualizar suscripcion", "Tenant", id,
            $"Plan {vm.Plan}, ciclo {vm.CicloSuscripcion}, valor ${vm.ValorSuscripcion:#,##0}, estado {vm.EstadoSuscripcion}, vence {vm.SuscripcionVencimiento:yyyy-MM-dd}");
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

        try
        {
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
                Telefono = vm.Telefono.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
                Activo = vm.Activo,
                Atiende = vm.Rol == Rol.Barbero,
                EmailVerificado = false,
                TokenVerificacion = token,
                TokenExpira = DateTime.Now.AddDays(7)
            });
            var link = TenantUrl(empresa.Slug, $"/cuenta/verificar?token={Uri.EscapeDataString(token)}");
            var html = $@"<p>Hola {vm.Nombre.Trim()},</p>
                <p>Te crearon una cuenta en <strong>{empresa.Nombre}</strong>.</p>
                <p>Para activar el acceso debes verificar tu correo:</p>
                <p><a href='{link}'>Verificar mi correo</a></p>";
            await _email.SendAsync(vm.Email.Trim(), "Verifica tu cuenta", html);

            await Auditar(id, "Crear usuario", "Usuario", userId, $"{vm.Nombre} ({vm.Rol})");
            TempData["Ok"] = _email.Enabled
                ? "Usuario creado. Enviamos el correo de verificacion."
                : $"Usuario creado. SMTP esta desactivado; enlace de verificacion: {link}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando usuario interno para tenant {TenantId}", id);
            TempData["Error"] = "No se pudo crear el usuario. Verifica que el correo no exista y que la base este sincronizada.";
        }
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
    [HttpPost("empresa/{tenantId:int}/usuarios/{userId:int}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarUsuario(int tenantId, int userId, UsuarioInternoFormVm vm)
    {
        var usuario = await _usuarios.GetByIdInTenantAsync(tenantId, userId);
        if (usuario is null) return NotFound();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisa nombre, correo, telefono y rol.";
            return RedirectToAction(nameof(Empresa), new { id = tenantId });
        }

        var email = vm.Email.Trim();
        var emailOwner = await _usuarios.GetByEmailAsync(tenantId, email);
        if (emailOwner is not null && emailOwner.Id != userId)
        {
            TempData["Error"] = "Ese correo ya pertenece a otro usuario de la empresa.";
            return RedirectToAction(nameof(Empresa), new { id = tenantId });
        }

        await _usuarios.UpdateInternalAsync(new Usuario
        {
            Id = userId,
            TenantId = tenantId,
            Nombre = vm.Nombre.Trim(),
            Cedula = string.IsNullOrWhiteSpace(vm.Cedula) ? null : vm.Cedula.Trim(),
            Email = email,
            Telefono = vm.Telefono.Trim(),
            Rol = vm.Rol,
            Activo = vm.Activo,
            Atiende = vm.Rol == Rol.Barbero || (vm.Rol == Rol.Dueno && usuario.Atiende)
        });

        if (!string.IsNullOrWhiteSpace(vm.Password))
            await _usuarios.UpdatePasswordAsync(userId, BCrypt.Net.BCrypt.HashPassword(vm.Password));

        await Auditar(tenantId, "Editar usuario", "Usuario", userId, $"{vm.Nombre} ({vm.Rol}), activo {vm.Activo}");
        TempData["Ok"] = "Usuario editado.";
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

        int eliminadas;
        try
        {
            eliminadas = await _tenants.ResetProductionDataAsync(User.Identity?.Name ?? "SuperAdmin");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Confirmacion), ex.Message);
            return View(vm);
        }
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

        int? tenantId = null;
        try
        {
        tenantId = await _tenants.CreateAsync(new Tenant
        {
            Nombre = vm.Nombre.Trim(), Slug = slug, Plan = vm.Plan, CicloSuscripcion = vm.CicloSuscripcion,
            ValorSuscripcion = vm.ValorSuscripcion,
            SuscripcionInicio = DateTime.Today,
            SuscripcionVencimiento = DateTime.Today.AddMonths(MesesPorCiclo(vm.CicloSuscripcion)),
            EstadoSuscripcion = "Activo",
            MaxUsuarios = vm.MaxUsuarios, Activo = true
        });

        var token = Guid.NewGuid().ToString("N");
        await _usuarios.CreateAsync(new Usuario
        {
            TenantId = tenantId, Rol = Rol.Dueno, Nombre = vm.DuenoNombre.Trim(), Cedula = vm.DuenoCedula.Trim(),
            Email = vm.DuenoEmail.Trim(), Telefono = vm.DuenoTelefono.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.DuenoPassword), Activo = true,
            EmailVerificado = false, TokenVerificacion = token, TokenExpira = DateTime.Now.AddDays(7)
        });

        var link = TenantUrl(slug, $"/cuenta/verificar?token={Uri.EscapeDataString(token)}");
        var html = $"<p>Hola {vm.DuenoNombre},</p><p>Tu negocio <strong>{vm.Nombre}</strong> fue creado. " +
                   $"Verifica tu correo para ingresar:</p><p><a href='{link}'>Verificar mi correo</a></p>";
        await _email.SendAsync(vm.DuenoEmail.Trim(), "Verifica tu cuenta", html);

        TempData["Ok"] = _email.Enabled
            ? $"Negocio '{vm.Nombre}' creado. {vm.DuenoNombre.Trim()} quedó registrado como dueño; enviamos la verificación a {vm.DuenoEmail}."
            : $"Negocio '{vm.Nombre}' creado. {vm.DuenoNombre.Trim()} quedó registrado como dueño. Enlace de verificación: {link}";
        return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando negocio {Slug}", slug);
            if (tenantId.HasValue)
            {
                await _tenants.DeleteAsync(tenantId.Value);
            }

            ModelState.AddModelError("", "No se pudo crear el negocio completo. Revisa correo del dueno, slug y secuencias de la base.");
            return View(vm);
        }
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

    private static int MesesPorCiclo(string? ciclo) => (ciclo ?? "").Trim().ToLowerInvariant() switch
    {
        "trimestral" => 3,
        "anual" => 12,
        _ => 1
    };

    private string TenantUrl(string slug, string path = "/")
    {
        path = string.IsNullOrWhiteSpace(path) ? "/" : path;
        if (!path.StartsWith('/')) path = "/" + path;

        var baseDomain = (_config["App:BaseDomain"] ?? "").Trim().Trim('.');
        if (!string.IsNullOrWhiteSpace(baseDomain))
            return $"https://{slug}.{baseDomain}{path}";

        return $"{Request.Scheme}://{Request.Host}/{slug}{path}";
    }
}

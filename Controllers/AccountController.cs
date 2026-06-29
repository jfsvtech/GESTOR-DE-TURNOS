using System.Security.Claims;
using GeneradorTurnos.Auth;
using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeneradorTurnos.Controllers;

[Route("{slug}/cuenta")]
[EnableRateLimiting("auth")]
public class AccountController : TenantBaseController
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IEmailSender _email;

    public AccountController(ITenantContext tenant, IUsuarioRepository usuarios, IEmailSender email) : base(tenant)
    {
        _usuarios = usuarios;
        _email = email;
    }

    // ============ Registro de CLIENTE (autónomo, con correo) ============
    [HttpGet("registro")]
    public IActionResult Registro(string? returnUrl = null)
        => View(new RegistroClienteVm { ReturnUrl = returnUrl });

    [HttpPost("registro")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registro(RegistroClienteVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        if (await _usuarios.EmailExistsAsync(TenantId, vm.Email.Trim()))
        {
            ModelState.AddModelError(nameof(vm.Email), "Ya existe una cuenta con este correo. Inicia sesión.");
            return View(vm);
        }

        var token = Guid.NewGuid().ToString("N");
        var u = new Usuario
        {
            TenantId = TenantId,
            Rol = Rol.Cliente,
            Nombre = vm.Nombre.Trim(),
            Email = vm.Email.Trim(),
            Telefono = vm.Telefono.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
            Activo = true,
            EmailVerificado = false,
            TokenVerificacion = token,
            TokenExpira = DateTime.Now.AddDays(1)
        };
        u.Id = await _usuarios.CreateAsync(u);

        await EnviarVerificacionAsync(u, token);

        // En desarrollo (sin SMTP) mostramos el enlace para poder verificar.
        if (!_email.Enabled)
            TempData["VerifyLink"] = Url.Action(nameof(Verificar), "Account",
                new { slug = Slug, token }, Request.Scheme);

        TempData["PendingEmail"] = u.Email;
        return RedirectToAction(nameof(VerificacionPendiente), new { slug = Slug });
    }

    [HttpGet("verificacion-pendiente")]
    public IActionResult VerificacionPendiente() => View();

    [HttpGet("verificar")]
    public async Task<IActionResult> Verificar(string token)
    {
        var u = await _usuarios.GetByTokenAsync(token);
        if (u is null || u.TenantId != TenantId)
        {
            TempData["Error"] = "El enlace de verificación no es válido o expiró.";
            return RedirectToAction(nameof(Login), new { slug = Slug });
        }

        await _usuarios.MarcarEmailVerificadoAsync(u.Id);
        u.EmailVerificado = true;
        await FirmarAsync(u);
        TempData["Ok"] = "¡Tu correo fue verificado! Bienvenido.";
        return RedirectSegunRol(u.Rol);
    }

    [HttpPost("reenviar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reenviar(string email)
    {
        var u = await _usuarios.GetByEmailAsync(TenantId, email);
        if (u is not null && !u.EmailVerificado)
        {
            var token = Guid.NewGuid().ToString("N");
            await _usuarios.SetTokenAsync(u.Id, token, DateTime.Now.AddDays(1));
            await EnviarVerificacionAsync(u, token);
            if (!_email.Enabled)
                TempData["VerifyLink"] = Url.Action(nameof(Verificar), "Account",
                    new { slug = Slug, token }, Request.Scheme);
        }
        TempData["PendingEmail"] = email;
        TempData["Ok"] = "Te reenviamos el correo de verificación.";
        return RedirectToAction(nameof(VerificacionPendiente), new { slug = Slug });
    }

    // ============ Login de CLIENTE (correo + contraseña) ============
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginEmailVm { ReturnUrl = returnUrl });

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginEmailVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // Login unificado por correo para TODOS los roles del negocio (cliente, barbero, dueño).
        var u = await _usuarios.GetByEmailAsync(TenantId, vm.Email.Trim());
        if (u is null)
        {
            var super = await _usuarios.GetGlobalByEmailAsync(vm.Email.Trim());
            if (super is not null && super.Rol == Rol.SuperAdmin && super.Activo
                && BCrypt.Net.BCrypt.Verify(vm.Password, super.PasswordHash))
            {
                if (!super.EmailVerificado)
                {
                    ModelState.AddModelError("", "Tu correo aun no esta verificado.");
                    return View(vm);
                }
                await FirmarAsync(super);
                return RedirectToAction("Index", "SuperAdmin");
            }
        }
        if (u is null || !u.Activo || !BCrypt.Net.BCrypt.Verify(vm.Password, u.PasswordHash))
        {
            ModelState.AddModelError("", "Correo o contraseña incorrecta.");
            return View(vm);
        }
        if (!u.EmailVerificado)
        {
            TempData["PendingEmail"] = u.Email;
            TempData["Error"] = "Debes verificar tu correo antes de ingresar.";
            return RedirectToAction(nameof(VerificacionPendiente), new { slug = Slug });
        }

        await FirmarAsync(u);
        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectSegunRol(u.Rol);
    }

    [HttpGet("recuperar")]
    public IActionResult Recuperar() => View(new ForgotPasswordVm());

    [HttpPost("recuperar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recuperar(ForgotPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await _usuarios.GetByEmailAsync(TenantId, vm.Email.Trim());
        if (u is not null && u.Activo)
        {
            var token = Guid.NewGuid().ToString("N");
            await _usuarios.SetTokenAsync(u.Id, token, DateTime.Now.AddHours(2));
            await EnviarResetAsync(u, token);
            if (!_email.Enabled)
                TempData["VerifyLink"] = Url.Action(nameof(Restablecer), "Account",
                    new { slug = Slug, token }, Request.Scheme);
        }

        TempData["Ok"] = "Si el correo existe, enviamos un enlace para restablecer la contrasena.";
        return RedirectToAction(nameof(Login), new { slug = Slug });
    }

    [HttpGet("restablecer")]
    public async Task<IActionResult> Restablecer(string token)
    {
        var u = await _usuarios.GetByTokenAsync(token);
        if (u is null || u.TenantId != TenantId)
        {
            TempData["Error"] = "El enlace no es valido o expiro.";
            return RedirectToAction(nameof(Login), new { slug = Slug });
        }

        return View(new ResetPasswordVm { Token = token });
    }

    [HttpPost("restablecer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restablecer(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await _usuarios.GetByTokenAsync(vm.Token);
        if (u is null || u.TenantId != TenantId)
        {
            ModelState.AddModelError("", "El enlace no es valido o expiro.");
            return View(vm);
        }

        await _usuarios.UpdatePasswordAsync(u.Id, BCrypt.Net.BCrypt.HashPassword(vm.Password));
        await _usuarios.MarcarEmailVerificadoAsync(u.Id);
        TempData["Ok"] = "Contrasena actualizada. Ya puedes iniciar sesion.";
        return RedirectToAction(nameof(Login), new { slug = Slug });
    }

    private IActionResult RedirectSegunRol(Rol rol) => rol switch
    {
        Rol.Dueno => RedirectToAction("Index", "Dueno", new { slug = Slug }),
        Rol.Barbero => RedirectToAction("Agenda", "Barbero", new { slug = Slug }),
        _ => RedirectToAction("MisTurnos", "Booking", new { slug = Slug })
    };

    // El equipo ahora también entra por correo: redirigimos al login unificado.
    [HttpGet("equipo")]
    public IActionResult Equipo(string? returnUrl = null)
        => RedirectToAction(nameof(Login), new { slug = Slug, returnUrl });

    [HttpPost("salir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salir()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Booking", new { slug = Slug });
    }

    private async Task EnviarVerificacionAsync(Usuario u, string token)
    {
        var link = Url.Action(nameof(Verificar), "Account", new { slug = Slug, token }, Request.Scheme)!;
        var html = $@"<p>Hola {u.Nombre},</p>
            <p>Confirma tu correo para activar tu cuenta en <strong>{Tenant.Current!.Nombre}</strong>:</p>
            <p><a href='{link}' style='background:#6d28d9;color:#fff;padding:10px 18px;border-radius:10px;text-decoration:none'>Verificar mi correo</a></p>
            <p>O copia este enlace: {link}</p>";
        await _email.SendAsync(u.Email!, "Verifica tu correo", html);
    }

    private async Task EnviarResetAsync(Usuario u, string token)
    {
        var link = Url.Action(nameof(Restablecer), "Account", new { slug = Slug, token }, Request.Scheme)!;
        var html = $@"<p>Hola {u.Nombre},</p>
            <p>Recibimos una solicitud para restablecer tu contrasena en <strong>{Tenant.Current!.Nombre}</strong>.</p>
            <p><a href='{link}' style='background:#059669;color:#fff;padding:10px 18px;border-radius:10px;text-decoration:none'>Crear nueva contrasena</a></p>
            <p>Este enlace vence en 2 horas. Si no fuiste tu, ignora este correo.</p>";
        await _email.SendAsync(u.Email!, "Restablecer contrasena", html);
    }

    private async Task FirmarAsync(Usuario u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nombre),
            new(ClaimTypes.Role, u.Rol.ToString()),
            new(AppClaims.TenantId, (u.TenantId ?? 0).ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}

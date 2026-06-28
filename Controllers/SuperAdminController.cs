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

namespace GeneradorTurnos.Controllers;

[Route("super")]
public class SuperAdminController : Controller
{
    private readonly ITenantRepository _tenants;
    private readonly IUsuarioRepository _usuarios;
    private readonly IEmailSender _email;

    public SuperAdminController(ITenantRepository tenants, IUsuarioRepository usuarios, IEmailSender email)
    {
        _tenants = tenants;
        _usuarios = usuarios;
        _email = email;
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
    [HttpGet("nuevo")]
    public IActionResult Nuevo() => View(new TenantFormVm());

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
        TempData["Ok"] = "Limite de trabajadores actualizado.";
        return RedirectToAction(nameof(Index));
    }

    private static string Slugify(string input)
    {
        input = (input ?? "").Trim().ToLowerInvariant();
        input = input.Replace("á", "a").Replace("é", "e").Replace("í", "i")
                     .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
        input = Regex.Replace(input, @"[^a-z0-9]+", "-").Trim('-');
        return input;
    }
}

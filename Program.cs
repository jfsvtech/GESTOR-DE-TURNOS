using System.Globalization;
using GeneradorTurnos.Data;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;

// Cultura es-CO: miles con punto (150.000) y decimales con coma.
var culturaCo = new CultureInfo("es-CO");
CultureInfo.DefaultThreadCurrentCulture = culturaCo;
CultureInfo.DefaultThreadCurrentUICulture = culturaCo;

var builder = WebApplication.CreateBuilder(args);

// ---- MVC + filtro global de resolución de tenant ----
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<TenantResolutionFilter>();
    options.ModelBinderProviders.Insert(0, new InvariantDecimalModelBinderProvider());
});

// ---- Datos / repositorios ----
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IServicioRepository, ServicioRepository>();
builder.Services.AddScoped<IAgendaRepository, AgendaRepository>();
builder.Services.AddScoped<ITurnoRepository, TurnoRepository>();
builder.Services.AddScoped<IStatsRepository, StatsRepository>();
builder.Services.AddScoped<IGastoRepository, GastoRepository>();
builder.Services.AddScoped<IGaleriaRepository, GaleriaRepository>();
builder.Services.AddScoped<INotificacionRepository, NotificacionRepository>();
builder.Services.AddScoped<IFileStorage, FileStorage>();

// ---- Servicios de dominio ----
builder.Services.AddScoped<DisponibilidadService>();
builder.Services.AddScoped<TurnoService>();
builder.Services.AddScoped<ExcelService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddHostedService<ReminderBackgroundService>();

// ---- Tenant context (por petición) ----
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantResolutionFilter>();

// ---- DataProtection (claves persistidas para que las cookies sobrevivan reinicios) ----
var keysDir = Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("GeneradorTurnos");

// ---- Autenticación por cookie ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "GeneradorTurnos.Auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/";

        // Login es por empresa (/{slug}/cuenta/login); construimos la URL según la ruta.
        options.Events.OnRedirectToLogin = ctx =>
        {
            var path = ctx.Request.Path.Value ?? "/";
            var first = path.Trim('/').Split('/').FirstOrDefault() ?? "";
            string login = first.Equals("super", StringComparison.OrdinalIgnoreCase)
                ? "/super/login"
                : string.IsNullOrEmpty(first) ? "/" : $"/{first}/cuenta/login";
            var returnUrl = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString);
            ctx.Response.Redirect($"{login}?returnUrl={returnUrl}");
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ---- Inicialización de BD (crea base, esquema y datos demo) ----
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var locOptions = new Microsoft.AspNetCore.Builder.RequestLocalizationOptions()
    .SetDefaultCulture("es-CO")
    .AddSupportedCultures("es-CO")
    .AddSupportedUICultures("es-CO");
app.UseRequestLocalization(locOptions);

app.UseHttpsRedirection();
var staticFileProvider = new FileExtensionContentTypeProvider();
staticFileProvider.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileProvider
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Atributos de ruta en los controladores + ruta por defecto para Home.
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

using System.Globalization;
using GeneradorTurnos.Data;
using GeneradorTurnos.Repositories;
using GeneradorTurnos.Services;
using GeneradorTurnos.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using System.Threading.RateLimiting;

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
builder.Services.AddHttpClient<IEmailSender, EmailSender>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddHostedService<ReminderBackgroundService>();

// ---- Tenant context (por petición) ----
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantResolutionFilter>();

// ---- CORS: cerrado por defecto; solo habilita origenes declarados en App:AllowedOrigins ----
var allowedOrigins = builder.Configuration.GetSection("App:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// ---- Rate limiting: protege login, registro y endpoints publicos de disponibilidad ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 8;
        limiter.Window = TimeSpan.FromMinutes(5);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("slots", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

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
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
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
if (builder.Configuration.GetValue("Security:EnableSecurityHeaders", true))
{
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        headers.TryAdd("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
            "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
            "img-src 'self' data: blob: https://images.unsplash.com https://api.qrserver.com; " +
            "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
        await next();
    });
}
var staticFileProvider = new FileExtensionContentTypeProvider();
staticFileProvider.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileProvider,
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.EndsWith("/service-worker.js", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
            return;
        }

        if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
        }
    }
});
app.UseRouting();
if (allowedOrigins.Length > 0) app.UseCors("ConfiguredOrigins");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Atributos de ruta en los controladores + ruta por defecto para Home.
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

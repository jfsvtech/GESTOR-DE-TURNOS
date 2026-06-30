using System.ComponentModel.DataAnnotations;

namespace GeneradorTurnos.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Ingresa tu cédula")]
    [Display(Name = "Cédula")]
    public string Cedula { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu contraseña")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class RegistroViewModel
{
    [Required(ErrorMessage = "Ingresa tu nombre")]
    [Display(Name = "Nombre completo")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu cédula")]
    [Display(Name = "Cédula")]
    public string Cedula { get; set; } = "";

    [Required(ErrorMessage = "Ingresa el telefono")]
    [Display(Name = "Teléfono")]
    public string Telefono { get; set; } = "";

    [EmailAddress(ErrorMessage = "Correo no válido")]
    [Display(Name = "Correo (opcional)")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Crea una contraseña")]
    [MinLength(6, ErrorMessage = "Mínimo 6 caracteres")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = "";
}

public class RegistroClienteVm
{
    [Required(ErrorMessage = "Ingresa tu nombre")]
    [Display(Name = "Nombre completo")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu correo")]
    [EmailAddress(ErrorMessage = "Correo no válido")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Ingresa el telefono")]
    [Display(Name = "Teléfono")]
    public string Telefono { get; set; } = "";

    [Required(ErrorMessage = "Crea una contraseña")]
    [MinLength(6, ErrorMessage = "Mínimo 6 caracteres")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class LoginEmailVm
{
    [Required(ErrorMessage = "Ingresa tu correo")]
    [EmailAddress(ErrorMessage = "Correo no válido")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu contraseña")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ForgotPasswordVm
{
    [Required(ErrorMessage = "Ingresa tu correo")]
    [EmailAddress(ErrorMessage = "Correo no valido")]
    [Display(Name = "Correo electronico")]
    public string Email { get; set; } = "";
}

public class ResetPasswordVm
{
    [Required]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Crea una contrasena")]
    [MinLength(6, ErrorMessage = "Minimo 6 caracteres")]
    [DataType(DataType.Password)]
    [Display(Name = "Nueva contrasena")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contrasenas no coinciden")]
    [Display(Name = "Confirmar contrasena")]
    public string ConfirmPassword { get; set; } = "";
}

public class IngresarClienteViewModel
{
    [Required(ErrorMessage = "Ingresa tu cédula")]
    [Display(Name = "Cédula")]
    public string Cedula { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu nombre")]
    [Display(Name = "Nombre completo")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu teléfono")]
    [Display(Name = "Teléfono")]
    public string Telefono { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ReservaManualVm
{
    public int ServicioId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    [Display(Name = "Hora")] public string Hora { get; set; } = "10:00";
    [Required] [Display(Name = "Cédula del cliente")] public string ClienteCedula { get; set; } = "";
    [Required] [Display(Name = "Nombre del cliente")] public string ClienteNombre { get; set; } = "";
    [Required(ErrorMessage = "Ingresa el telefono del cliente")]
    [Display(Name = "Teléfono")] public string ClienteTelefono { get; set; } = "";
    [Display(Name = "Notas")] public string? Notas { get; set; }
}

public class BloqueoVm
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    [Display(Name = "Desde")] public string HoraInicio { get; set; } = "12:00";
    [Display(Name = "Hasta")] public string HoraFin { get; set; } = "13:00";
    [Display(Name = "Motivo")] public string? Motivo { get; set; }
}

public class ReservarViewModel
{
    public int EmpleadoId { get; set; }
    public string EmpleadoNombre { get; set; } = "";
    public int ServicioId { get; set; }
    public List<Servicio> Servicios { get; set; } = new();
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string? Notas { get; set; }
}

public class ServicioFormVm
{
    public int Id { get; set; }
    [Required] [Display(Name = "Nombre del servicio")]
    public string Nombre { get; set; } = "";
    [Range(5, 600)] [Display(Name = "Duración (minutos)")]
    public int DuracionMinutos { get; set; } = 30;
    [Range(0, 100000000)] [Display(Name = "Precio")]
    public decimal Precio { get; set; }
    public bool Activo { get; set; } = true;
}

public class BarberoFormVm
{
    public int Id { get; set; }
    [Required] [Display(Name = "Nombre")]
    public string Nombre { get; set; } = "";
    [Required] [Display(Name = "Cédula")]
    public string Cedula { get; set; } = "";
    [Required(ErrorMessage = "Ingresa el telefono")]
    [Display(Name = "Teléfono")]
    public string Telefono { get; set; } = "";
    [Display(Name = "Correo")]
    public string? Email { get; set; }
    [Display(Name = "Contraseña (dejar vacío para no cambiar)")]
    public string? Password { get; set; }
    public bool Activo { get; set; } = true;
    public List<int> ServicioIds { get; set; } = new();

    [Display(Name = "Comisión del dueño")]
    public ComisionTipo ComisionTipo { get; set; } = ComisionTipo.Ninguna;
    [Display(Name = "Valor (% o monto fijo)")]
    public decimal ComisionValor { get; set; }
}

public class TenantFormVm
{
    [Required] [Display(Name = "Nombre del negocio")]
    public string Nombre { get; set; } = "";
    [Required] [Display(Name = "Identificador URL (slug)")]
    public string Slug { get; set; } = "";
    [Display(Name = "Plan")]
    public string Plan { get; set; } = "Basico";
    [Display(Name = "Ciclo de contrato")]
    public string CicloSuscripcion { get; set; } = "Mensual";
    [Range(0, 999999999, ErrorMessage = "Valor de suscripcion invalido")]
    [Display(Name = "Valor mensual de suscripcion")]
    public decimal ValorSuscripcion { get; set; }
    [Range(1, 10000)]
    [Display(Name = "Trabajadores maximos")]
    public int MaxUsuarios { get; set; } = 10;

    [Required] [Display(Name = "Nombre del dueño")]
    public string DuenoNombre { get; set; } = "";
    [Required] [Display(Name = "Cédula del dueño")]
    public string DuenoCedula { get; set; } = "";
    [Required] [EmailAddress] [Display(Name = "Correo del dueño")]
    public string DuenoEmail { get; set; } = "";
    [Required(ErrorMessage = "Ingresa el telefono del dueno")]
    [Display(Name = "Telefono del dueno")]
    public string DuenoTelefono { get; set; } = "";
    [Required] [Display(Name = "Contraseña del dueño")]
    public string DuenoPassword { get; set; } = "";
}

public class GastoFormVm
{
    [Required(ErrorMessage = "Indica el concepto")]
    [Display(Name = "Concepto")]
    public string Concepto { get; set; } = "";

    [Display(Name = "Categoría")]
    public string? Categoria { get; set; }

    [Range(1, 1000000000, ErrorMessage = "Monto inválido")]
    [Display(Name = "Monto")]
    public decimal Monto { get; set; }

    [Display(Name = "Fecha")]
    public DateTime Fecha { get; set; } = DateTime.Today;
}

public class GastosVm
{
    public List<Gasto> Gastos { get; set; } = new();
    public decimal Total { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public GastoFormVm Nuevo { get; set; } = new();
}

public class DashboardDuenoVm
{
    public ResumenStats Resumen { get; set; } = new();
    public List<SerieFecha> IngresosPorDia { get; set; } = new();
    public List<RankingItem> ServiciosTop { get; set; } = new();
    public List<RankingItem> IngresosPorBarbero { get; set; } = new();
    public List<ResumenBarbero> Barberos { get; set; } = new();
    public List<RankingItem> OcupacionPorBarbero { get; set; } = new();
    public List<RankingItem> ClientesRecurrentes { get; set; } = new();
    public List<RankingItem> ClientesPorIngresos { get; set; } = new();
    public List<RankingItem> ClientesConCancelaciones { get; set; } = new();
    public decimal TasaRecurrencia { get; set; }
    public List<SerieFecha> CancelacionesPorDia { get; set; } = new();
    public List<SerieFecha> ProductividadPorSemana { get; set; } = new();
    public decimal HorasMuertasEstimadas { get; set; }
    public string ServicioMasRentable => ServiciosTop.OrderByDescending(x => x.Valor).FirstOrDefault()?.Etiqueta ?? "Sin datos";
    public decimal Gastos { get; set; }
    public decimal Utilidad => Resumen.IngresosTotales - Gastos;
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
}

public class DashboardBarberoVm
{
    public ResumenStats Resumen { get; set; } = new();
    public List<SerieFecha> TurnosPorDia { get; set; } = new();
    public List<RankingItem> ServiciosTop { get; set; } = new();
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
}

public class SuperAdminEmpresaDetalleVm
{
    public Tenant Empresa { get; set; } = new();
    public List<Usuario> Usuarios { get; set; } = new();
    public List<PagoSuscripcion> Pagos { get; set; } = new();
    public List<Auditoria> Auditoria { get; set; } = new();
    public UsuarioInternoFormVm NuevoUsuario { get; set; } = new();
    public PagoSuscripcionFormVm NuevoPago { get; set; } = new();
}

public class UsuarioInternoFormVm
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    [Required] public string Nombre { get; set; } = "";
    public string? Cedula { get; set; }
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Telefono { get; set; } = "";
    [Required] public Rol Rol { get; set; } = Rol.Barbero;
    public string? Password { get; set; }
    public bool Activo { get; set; } = true;
}

public class SuscripcionFormVm
{
    public int TenantId { get; set; }
    [Required] public string Plan { get; set; } = "Basico";
    [Required] public string CicloSuscripcion { get; set; } = "Mensual";
    [Range(0, 999999999)] public decimal ValorSuscripcion { get; set; }
    [Required] public string EstadoSuscripcion { get; set; } = "Activo";
    public DateTime? SuscripcionInicio { get; set; }
    public DateTime? SuscripcionVencimiento { get; set; }
    [Range(1, 60)] public int RecordatorioPagoDias { get; set; } = 7;
}

public class PagoSuscripcionFormVm
{
    public int TenantId { get; set; }
    [Range(0.01, 999999999)] public decimal Monto { get; set; }
    [Required] public DateTime PeriodoInicio { get; set; } = DateTime.Today;
    [Required] public DateTime PeriodoFin { get; set; } = DateTime.Today.AddMonths(1);
    public string? Metodo { get; set; }
    public string? Referencia { get; set; }
    public string? Nota { get; set; }
}

public class HoyBarberoVm
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    public TurnoDetalle? Proximo { get; set; }
    public List<TurnoDetalle> Agenda { get; set; } = new();
    public List<TurnoDetalle> HistorialCliente { get; set; } = new();
}

public class SuperAdminDashboardVm
{
    public List<SaasTenantResumen> Empresas { get; set; } = new();
    public decimal VentasTotales { get; set; }
    public int EmpresasActivas { get; set; }
    public int UsuariosTotales { get; set; }
    public int TurnosTotales { get; set; }
}

public class ResetProduccionVm
{
    [Required]
    [Display(Name = "Frase de confirmacion")]
    public string Confirmacion { get; set; } = "";
}

public class EmailConfigStatusVm
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Smtp";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "";
    public List<ConfigSecretStatus> Variables { get; set; } = new();
}

public class ConfigSecretStatus
{
    public string Name { get; set; } = "";
    public bool Present { get; set; }
    public string MaskedValue { get; set; } = "";
}

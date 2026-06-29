namespace GeneradorTurnos.Models;

public enum Rol
{
    SuperAdmin = 1,
    Dueno = 2,
    Barbero = 3,
    Cliente = 4
}

public enum EstadoTurno
{
    Pendiente = 1,
    Confirmado = 2,
    Completado = 3,
    Cancelado = 4,
    NoShow = 5
}

public enum OrigenTurno
{
    Cliente = 1,
    Manual = 2
}

public enum ComisionTipo
{
    Ninguna = 0,
    Porcentaje = 1,
    FijoMensual = 2
}

public static class RolExtensions
{
    public static string ToRoleName(this Rol rol) => rol.ToString();

    public static string Display(this Rol rol) => rol switch
    {
        Rol.SuperAdmin => "Super Administrador",
        Rol.Dueno => "Dueño",
        Rol.Barbero => "Profesional",
        Rol.Cliente => "Cliente",
        _ => rol.ToString()
    };
}

public static class EstadoTurnoExtensions
{
    public static string Display(this EstadoTurno e) => e switch
    {
        EstadoTurno.Pendiente => "Pendiente",
        EstadoTurno.Confirmado => "Confirmado",
        EstadoTurno.Completado => "Completado",
        EstadoTurno.Cancelado => "Cancelado",
        EstadoTurno.NoShow => "No asistió",
        _ => e.ToString()
    };

    public static string Badge(this EstadoTurno e) => e switch
    {
        EstadoTurno.Pendiente => "bg-warning text-dark status-pendiente",
        EstadoTurno.Confirmado => "bg-success status-confirmada",
        EstadoTurno.Completado => "bg-secondary status-finalizada",
        EstadoTurno.Cancelado => "bg-danger status-cancelada",
        EstadoTurno.NoShow => "bg-danger",
        _ => "bg-light text-dark"
    };
}

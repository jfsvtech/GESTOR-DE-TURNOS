# Generador de Turnos — SaaS multiempresa

Sistema web (responsive, también para celular) para **gestionar la disponibilidad y reserva
de turnos** en distintos negocios: barberías, restaurantes, salones de belleza, etc.
Arquitectura **multiempresa (SaaS)**: una sola base de datos compartida, aislada por `tenant_id`.

## Tecnologías
- ASP.NET Core MVC (.NET 10) + Razor Views
- PostgreSQL + Dapper + Npgsql
- BCrypt.Net-Next (hash de contraseñas)
- ClosedXML (exportar a Excel)
- Bootstrap 5 + Bootstrap Icons + Chart.js
- DataProtection de ASP.NET Core (cookies de sesión)

## Puesta en marcha (1 sola vez)

> Requisitos: .NET 10 SDK y PostgreSQL en `localhost:5432` (ya detectados en tu equipo).

1. **Configura la conexión** (te pedirá la contraseña del usuario `postgres`):
   ```powershell
   ./setup-db.ps1
   ```
   Esto escribe las cadenas de conexión en `appsettings.json`.

   > Alternativa manual: edita `appsettings.json` y reemplaza `__SET_ME__` por tu contraseña
   > en `AdminConnection` y `DefaultConnection`.

2. **Ejecuta la aplicación**:
   ```powershell
   dotnet run
   ```
   En el primer arranque crea **automáticamente** la base de datos `generadorturnos`,
   el esquema y **datos demo**.

3. Abre el navegador en la URL que muestra la consola (p. ej. `http://localhost:5221`).

## Credenciales demo

Empresa demo: **Barbería Centro** → `http://localhost:5221/barberia-centro`

**Todos inician sesión con correo + contraseña (y verificación de correo).**

| Rol          | Acceso                            | Correo                  | Contraseña   |
|--------------|-----------------------------------|-------------------------|--------------|
| SuperAdmin   | `/super/login`                    | `admin@saas.com`        | `admin123`   |
| Dueño        | `/barberia-centro/cuenta/login`   | `dueno@barberia.com`    | `dueno123`   |
| Profesional  | `/barberia-centro/cuenta/login`   | `carlos@barberia.com`   | `barbero123` |
| Profesional  | `/barberia-centro/cuenta/login`   | `andres@barberia.com`   | `barbero123` |
| Cliente      | `/barberia-centro/cuenta/login`   | `juan@cliente.com`      | `cliente123` |

> **Cliente autónomo**: se registra solo en `/barberia-centro/cuenta/registro` (nombre + correo +
> contraseña), recibe un **correo de verificación** y al confirmarlo queda activo. El **equipo**
> (barberos/dueños) y el **SuperAdmin** también entran con su correo. Al crear un profesional o un
> negocio, se genera un enlace de verificación para esa cuenta (en modo dev se muestra en pantalla).

## Roles y funcionalidades

- **Cliente** (cuenta propia, correo + contraseña + verificación): rol por defecto **Cliente**;
  solo **ver, crear y eliminar** sus citas. Flujo de reserva **por servicio**: elige el servicio →
  ve los profesionales que lo prestan → disponibilidad → reserva, reprograma o cancela.
- **Profesional (barbero)**: agenda por día, marca estados, **historial de cortes y ganancias**,
  **edita sus propios servicios, precios y duración**, **gestiona sus propios horarios**,
  **bloquea** la agenda y crea **turnos manuales** (walk-in, incluso fuera de horario), estadísticas Chart.js.
- **Dueño**: dashboard con Chart.js, **resumen por profesional** (cortes, ticket, ingresos),
  CRUD de servicios y profesionales, listado de turnos y **exportación a Excel**.
- **SuperAdmin (SaaS)**: alta/baja de negocios (tenants) y creación de su dueño.

## Correo / verificación
Por defecto el envío de correos está **desactivado** (`Email:Enabled=false` en `appsettings.json`):
en ese modo la pantalla de "verificación pendiente" **muestra el enlace** para confirmar la cuenta
(útil en desarrollo). Para enviar correos reales, configura la sección `Email` con tu SMTP
(Host, Port, User, Password, From) y pon `Enabled: true`.

## Cómo vender a un nuevo local (SaaS)
Entra como SuperAdmin → **Nuevo negocio** → defines nombre, identificador URL (slug), plan y la cuenta del dueño.
El nuevo local queda disponible en `/{slug}` con sus datos completamente aislados.

## Arquitectura (resumen)
```
Controllers ─ Services (Disponibilidad, Turnos, Stats, Excel) ─ Repositories (Dapper) ─ PostgreSQL
       │
       └─ TenantResolutionFilter: resuelve {slug} de la URL → ITenantContext (tenant_id)
          Toda consulta de datos filtra por tenant_id (aislamiento multiempresa).
Autenticación por cookie (DataProtection) + roles. Contraseñas con BCrypt.
```
- **Núcleo de disponibilidad** (`Services/DisponibilidadService.cs`): horario laboral − bloqueos − turnos,
  en rejilla de 15 min, con revalidación anti doble-reserva al confirmar.
- **Precio**: se guarda un *snapshot* en el turno al reservar, para no alterar el histórico.

## Configuración (`appsettings.json`)
- `App:AutoMigrate` (true): crea BD + esquema al arrancar.
- `App:SeedDemoData` (true): siembra datos demo si la BD está vacía.
  Ponlo en `false` para producción tras el primer arranque.

# Generador de Turnos - SaaS multiempresa

Sistema web responsive para gestionar disponibilidad, reservas, clientes, trabajadores, servicios, pagos y reportes para barberias, spa y negocios por cita.

La arquitectura es multiempresa: una sola aplicacion y una base compartida con aislamiento por `tenant_id`.

## Tecnologias

- ASP.NET Core MVC (.NET 10) + Razor Views.
- PostgreSQL + Dapper + Npgsql.
- BCrypt.Net-Next para hash de contrasenas.
- Bootstrap 5, Bootstrap Icons y Chart.js.
- PWA con manifest y service worker.
- DataProtection para cookies persistentes.

## Puesta en marcha local

Requisitos: .NET 10 SDK y PostgreSQL.

1. Configura la base:

   ```powershell
   ./setup-db.ps1
   ```

2. Configura una clave temporal para crear los superadmins:

   ```powershell
   $env:SuperAdmin__BootstrapPassword="UNA_CLAVE_SEGURA_TEMPORAL"
   ```

3. Ejecuta:

   ```powershell
   dotnet run
   ```

4. Entra a:

   ```txt
   http://localhost:5221/super/login
   ```

## Acceso inicial SaaS

Al arrancar, si existe `SuperAdmin__BootstrapPassword`, la app crea o repara estos usuarios globales:

- `jfsvtech@gmail.com`
- `juliansernavasco@gmail.com`

Ambos quedan como:

```txt
tenant_id = NULL
rol = SuperAdmin
activo = true
email_verificado = true
```

En produccion, el panel vive en:

```txt
https://admin.jfsvtech.com/login
```

No se crean empresas, servicios, turnos ni usuarios demo.

## Flujo SaaS

1. El SuperAdmin entra al panel SaaS.
2. Crea un nuevo negocio.
3. Define el dueno inicial del negocio.
4. El dueno entra a su empresa y configura servicios, trabajadores, horarios, precios y foto.
5. Clientes y trabajadores usan un login unificado por correo dentro de cada empresa.

## Roles

- SuperAdmin: administra empresas, limites, suscripciones, pagos y configuracion SaaS.
- Dueno: administra su negocio, trabajadores, servicios, horarios, reportes y turnos.
- Empleado/barbero: gestiona su agenda, vista de hoy, historial de clientes, servicios y horarios.
- Cliente: ve historial, reserva, cancela o reprograma segun disponibilidad real.

## Configuracion principal

- `DB_URL` o `DATABASE_URL`: conexion principal PostgreSQL.
- `ADMIN_DB_URL`: conexion con permisos para crear/aplicar esquema.
- `App__AutoMigrate=true`: aplica esquema al iniciar.
- `App__SeedDemoData=false`: no se siembran datos demo.
- `App__BaseDomain=jfsvtech.com`: habilita subdominios por empresa y `admin.jfsvtech.com`.
- `SuperAdmin__BootstrapPassword`: clave temporal para crear/reparar superadmins.
- `Email__Provider=GmailApi`: envio real de verificacion por correo.

## Disponibilidad

La disponibilidad se calcula con:

- Horario laboral del trabajador.
- Bloqueos.
- Duracion real del servicio.
- Turnos existentes.
- Validacion anti-solape:

```txt
inicio < turno.fin AND fin > turno.inicio
```

La base tambien intenta crear una restriccion anti-solape con PostgreSQL cuando `btree_gist` esta disponible.

# Variables de produccion

Configura estos valores en Railway o en el proveedor de deploy. No subas `.env` al repositorio.

## Base de datos

- `DB_URL` o `DATABASE_URL`: conexion principal de PostgreSQL.
- `ADMIN_DB_URL` o `DATABASE_ADMIN_URL`: conexion administrativa para crear la base si `App:AutoMigrate=true`.

Tambien puedes usar el formato nativo de ASP.NET Core:

- `ConnectionStrings__DefaultConnection`
- `ConnectionStrings__AdminConnection`

## Seguridad

- `ASPNETCORE_ENVIRONMENT=Production`
- `Security__RequireHttpsCookies=true`
- `Security__EnableSecurityHeaders=true`

## Email

- `Email__Enabled=true`
- `Email__Provider=GmailApi`
- `GmailApi__ClientId`
- `GmailApi__ClientSecret`
- `GmailApi__RefreshToken`
- `GmailApi__From`
- `GmailApi__FromName=Turnos`

El panel `/super/configuracion/correo` permite al SuperAdmin ver el estado enmascarado de estas variables.

## Storage

Actualmente `Storage__Provider=Local` guarda en `wwwroot/uploads`, valido solo para desarrollo.
Para produccion usar Cloudinary, S3, Firebase Storage o volumen persistente. En base de datos se debe guardar solo la URL final.

## App

- `App__AutoMigrate=true` solo si el usuario de base tiene permisos para migraciones.
- `App__SeedDemoData=false` en produccion.
- `App__AllowedOrigins__0=https://tu-dominio.com` si vas a consumir la app desde otro origen.

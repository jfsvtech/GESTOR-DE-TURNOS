# Ejecutar ultima prueba local

Como ya no hay credenciales en `appsettings.json`, debes cargar variables de entorno antes de iniciar.
No guardes estos valores en archivos del repositorio.

## PowerShell temporal

Abre una terminal nueva y ejecuta:

```powershell
$env:DB_URL="Host=localhost;Port=5432;Database=generadorturnos;Username=postgres;Password=TU_PASSWORD"
$env:ADMIN_DB_URL="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=TU_PASSWORD"
$env:Email__Enabled="true"
$env:Email__Provider="GmailApi"
$env:GmailApi__ClientId="TU_CLIENT_ID"
$env:GmailApi__ClientSecret="TU_CLIENT_SECRET"
$env:GmailApi__RefreshToken="TU_REFRESH_TOKEN"
$env:GmailApi__From="tu-correo@gmail.com"
$env:GmailApi__FromName="Turnos"
dotnet run --urls http://localhost:5221
```

Si solo quieres probar sin correos reales:

```powershell
$env:Email__Enabled="false"
dotnet run --urls http://localhost:5221
```

Con `Email__Enabled=false`, la app no enviara correos reales. Los usuarios quedaran pendientes de verificacion hasta usar un enlace generado por el flujo de desarrollo.

## Probar

1. Abre `http://localhost:5221`.
2. Entra como superadmin.
3. Ve a `/super/configuracion/correo`.
4. Verifica que todas las variables Gmail API aparezcan como configuradas.
5. Crea un negocio o usuario interno.
6. Confirma que llega correo de verificacion.

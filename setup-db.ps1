<#
    setup-db.ps1
    Configura la conexión a PostgreSQL para GeneradorTurnos.
    Ejecuta esto UNA sola vez (te pedirá la contraseña del usuario 'postgres').

    Uso:
        ./setup-db.ps1
        ./setup-db.ps1 -PgUser postgres -PgHost localhost -PgPort 5432
#>
param(
    [string]$PgUser = "postgres",
    [string]$PgHost = "localhost",
    [int]$PgPort = 5432,
    [string]$DbName = "generadorturnos"
)

$ErrorActionPreference = "Stop"
$appsettings = Join-Path $PSScriptRoot "appsettings.json"

Write-Host "== Configuración de base de datos GeneradorTurnos ==" -ForegroundColor Cyan

# 1) Pedir contraseña
$secure = Read-Host "Contraseña del usuario '$PgUser' en PostgreSQL" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$pwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

if ([string]::IsNullOrWhiteSpace($pwd)) { Write-Error "Contraseña vacía."; exit 1 }

# 2) Probar conexión con psql si está disponible
$psql = "C:\Program Files\PostgreSQL\17\bin\psql.exe"
if (Test-Path $psql) {
    $env:PGPASSWORD = $pwd
    & $psql -U $PgUser -h $PgHost -p $PgPort -d postgres -tAc "SELECT 1;" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "No se pudo conectar. Revisa la contraseña."; exit 1 }
    Write-Host "Conexión verificada." -ForegroundColor Green
} else {
    Write-Host "psql no encontrado; se omite la verificación previa (la app validará al arrancar)." -ForegroundColor Yellow
}

# 3) Escribir las cadenas de conexión en appsettings.json
$json = Get-Content $appsettings -Raw | ConvertFrom-Json
$admin = "Host=$PgHost;Port=$PgPort;Database=postgres;Username=$PgUser;Password=$pwd"
$def   = "Host=$PgHost;Port=$PgPort;Database=$DbName;Username=$PgUser;Password=$pwd"
$json.ConnectionStrings.AdminConnection = $admin
$json.ConnectionStrings.DefaultConnection = $def
$json | ConvertTo-Json -Depth 10 | Set-Content $appsettings -Encoding utf8

Write-Host "appsettings.json actualizado." -ForegroundColor Green
Write-Host ""
Write-Host "Listo. Ahora ejecuta:" -ForegroundColor Cyan
Write-Host "    dotnet run" -ForegroundColor White
Write-Host "La app creará la base '$DbName', el esquema y los datos demo automáticamente." -ForegroundColor Gray

# Publicacion en Railway

Este proyecto queda preparado para Railway usando Docker. No subas secretos al repositorio.

## 1. Preparar repositorio

1. Verifica que compile local:
   ```bash
   dotnet build
   ```
2. Sube el codigo a GitHub.
3. Confirma que no estas subiendo:
   - `.env`
   - `appsettings.Development.json`
   - `keys/`
   - `wwwroot/uploads/`
   - `artifacts/`

## 2. Crear proyecto en Railway

1. En Railway, crea un nuevo proyecto.
2. Selecciona **Deploy from GitHub repo**.
3. Conecta el repositorio.
4. Railway detectara el `Dockerfile` y construira la app.

## 3. Crear PostgreSQL

1. Dentro del proyecto de Railway, agrega un servicio **PostgreSQL**.
2. Copia la variable `DATABASE_URL` generada por Railway.
3. En el servicio de la app, configura:
   - `DB_URL=${{Postgres.DATABASE_URL}}`
   - `DATABASE_URL=${{Postgres.DATABASE_URL}}`

Si quieres que la app cree/aplique esquema automaticamente, usa tambien:

- `ADMIN_DB_URL=${{Postgres.DATABASE_URL}}`
- `App__AutoMigrate=true`

Si prefieres migracion manual:

- `App__AutoMigrate=false`
- Ejecuta `Data/schema.sql` contra PostgreSQL desde un cliente seguro.

## 4. Variables obligatorias

Configura estas variables en el servicio web:

```text
ASPNETCORE_ENVIRONMENT=Production
App__AutoMigrate=true
App__SeedDemoData=false
Security__RequireHttpsCookies=true
Security__EnableSecurityHeaders=true
Storage__Provider=Local
Storage__MaxImageMb=5
App__BaseDomain=jfsvtech.com
AllowedHosts=*
SuperAdmin__BootstrapPassword=UNA_CLAVE_SEGURA_TEMPORAL
```

Railway provee `PORT`; el Dockerfile lo usa automaticamente.

Con `SuperAdmin__BootstrapPassword`, el arranque crea/verifica estos superadmins globales:

- `jfsvtech@gmail.com`
- `juliansernavasco@gmail.com`

Ambos quedan con `tenant_id=NULL`, rol `SuperAdmin`, activos y con correo verificado. Despues del primer ingreso, cambia la clave desde la base o implementa flujo de cambio de password antes de entregar accesos a terceros.

## 5. Email real con Gmail API

Para que la verificacion de correo funcione en produccion, configura Gmail API con OAuth en Railway:

```text
Email__Enabled=true
Email__Provider=GmailApi
GmailApi__ClientId=...
GmailApi__ClientSecret=...
GmailApi__RefreshToken=...
GmailApi__From=correo-remitente@gmail.com
GmailApi__FromName=Turnos
```

Sin Gmail API completo, los usuarios quedaran pendientes de verificacion y no podran entrar.
El SuperAdmin puede revisar el estado en `/super/configuracion/correo`; los secretos se muestran enmascarados.

## 6. Dominio y HTTPS

1. En Railway, abre **Settings > Networking**.
2. Genera dominio Railway o conecta dominio propio.
3. Railway entrega HTTPS automaticamente.
4. Si usas dominio propio, agrega el CNAME que Railway indique.
5. Para negocios por subdominio, configura tambien el wildcard `*.jfsvtech.com` en DNS y Railway.
6. Con `App__BaseDomain=jfsvtech.com`, la app resuelve automaticamente:
   - `https://nenebarber.jfsvtech.com`
   - `https://q2.jfsvtech.com`

Ver mas detalles en `docs/subdomain-tenancy.md`.

## 7. Storage de imagenes

Actualmente `Storage__Provider=Local` guarda en `wwwroot/uploads`.
Eso sirve para desarrollo, pero en Railway el filesystem no debe considerarse persistente.

Antes de produccion comercial, migra imagenes a:

- Cloudinary
- AWS S3
- Firebase Storage
- Supabase Storage

La base de datos debe guardar solo la URL publica/segura de la imagen.

## 8. Primera subida

1. Haz commit de los cambios.
2. Push a GitHub.
3. Railway ejecutara build Docker.
4. La base de datos se crea/aplica al arrancar si configuraste:
   - `DB_URL=${{Postgres.DATABASE_URL}}`
   - `ADMIN_DB_URL=${{Postgres.DATABASE_URL}}`
   - `App__AutoMigrate=true`
5. Revisa logs:
   - `Esquema aplicado correctamente.`
   - `Now listening on: http://0.0.0.0:<PORT>`
6. Entra al dominio Railway.
7. Crea/verifica usuarios usando correo real.

## 9. Checklist antes de abrir a clientes

- [ ] `DB_URL` configurada.
- [ ] Gmail API real configurado.
- [ ] `App__SeedDemoData=false`.
- [ ] Dominio HTTPS activo.
- [ ] `App__BaseDomain=jfsvtech.com` configurado.
- [ ] Wildcard `*.jfsvtech.com` activo en DNS/Railway.
- [ ] `SuperAdmin__BootstrapPassword` configurado con una clave fuerte.
- [ ] Entrar a `/super/login` con `jfsvtech@gmail.com` y `juliansernavasco@gmail.com`.
- [ ] Usuarios nuevos no pueden entrar sin verificar correo.
- [ ] `Data/schema.sql` aplicado.
- [ ] Revisar si `ex_turnos_no_solape_activos` fue creado.
- [ ] Storage cloud definido para imagenes.
- [ ] Backups PostgreSQL activos en Railway.

## 10. Verificar constraint anti-solape

En PostgreSQL:

```sql
SELECT conname
FROM pg_constraint
WHERE conname = 'ex_turnos_no_solape_activos';
```

Si no aparece, revisa:

- permisos para `CREATE EXTENSION btree_gist`
- turnos existentes solapados
- logs de migracion

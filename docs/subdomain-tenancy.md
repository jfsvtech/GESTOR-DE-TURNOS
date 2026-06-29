# Multi-tenant por subdominio

La aplicacion soporta dos formas de acceso:

- Portal general: `https://jfsvtech.com`
- Panel SaaS: `https://admin.jfsvtech.com`
- Negocio por subdominio: `https://{slug}.jfsvtech.com`
- Compatibilidad antigua: `https://jfsvtech.com/{slug}`

## Variables requeridas

En Railway:

```env
App__BaseDomain=jfsvtech.com
AllowedHosts=*
```

Mantener tambien las variables de base de datos ya usadas por produccion:

```env
DB_URL=...
ADMIN_DB_URL=...
```

## Comportamiento

Si el host es `jfsvtech.com` o `www.jfsvtech.com`, la app muestra el portal general.

Si el host es `admin.jfsvtech.com`, la app abre el panel SaaS:

```txt
https://admin.jfsvtech.com       -> /super
https://admin.jfsvtech.com/login -> /super/login
```

Si el host termina en `.jfsvtech.com`, la app toma el primer subdominio como `slug`:

```txt
nenebarber.jfsvtech.com -> slug nenebarber
q2.jfsvtech.com         -> slug q2
```

Luego busca el tenant activo en `tenants`. Si no existe, responde:

```txt
404 Negocio no encontrado.
```

Para mantener la URL limpia, el middleware reescribe internamente:

```txt
https://nenebarber.jfsvtech.com
```

como si fuera:

```txt
/nenebarber
```

sin cambiar la URL visible.

Tambien normaliza duplicados:

```txt
https://nenebarber.jfsvtech.com/nenebarber -> https://nenebarber.jfsvtech.com/
```

En formularios `POST`, no redirige para no perder el metodo HTTP.

## Prueba local con Host header

Con la app corriendo en `http://localhost:5221` y `App__BaseDomain=jfsvtech.com`:

```powershell
curl.exe -H "Host: jfsvtech.com" http://localhost:5221/
curl.exe -H "Host: admin.jfsvtech.com" http://localhost:5221/
curl.exe -H "Host: admin.jfsvtech.com" http://localhost:5221/login
curl.exe -H "Host: nenebarber.jfsvtech.com" http://localhost:5221/
curl.exe -H "Host: q2.jfsvtech.com" http://localhost:5221/
curl.exe -H "Host: empresaquenoexiste.jfsvtech.com" http://localhost:5221/
```

Para probar login por subdominio:

```powershell
curl.exe -H "Host: nenebarber.jfsvtech.com" http://localhost:5221/login
curl.exe -H "Host: q2.jfsvtech.com" http://localhost:5221/login
```

## Prueba en Railway

1. Confirmar DNS wildcard: `*.jfsvtech.com`.
2. Confirmar dominio wildcard en Railway.
3. Configurar `App__BaseDomain=jfsvtech.com`.
4. Configurar `AllowedHosts=*`.
5. Abrir:

```txt
https://jfsvtech.com
https://admin.jfsvtech.com
https://admin.jfsvtech.com/login
https://nenebarber.jfsvtech.com
https://q2.jfsvtech.com
https://nenebarber.jfsvtech.com/login
https://q2.jfsvtech.com/login
https://empresaquenoexiste.jfsvtech.com
```

## Notas

Los archivos estaticos no pasan por la resolucion de tenant:

```txt
/css
/js
/lib
/images
/img
/uploads
/favicon.ico
/manifest.json
/manifest.webmanifest
/service-worker.js
/sw.js
/_framework
/health
```

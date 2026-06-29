# Administracion SaaS

## Superadmins globales

El panel SaaS vive en:

```txt
/super/login
```

Los superadmins no pertenecen a ninguna empresa. En base de datos quedan como:

```txt
usuarios.tenant_id = NULL
usuarios.rol = SuperAdmin
```

En cada arranque, si existe la variable `SuperAdmin__BootstrapPassword`, la app crea o actualiza estos correos como superadmins:

```txt
jfsvtech@gmail.com
juliansernavasco@gmail.com
```

Variables:

```env
SuperAdmin__BootstrapPassword=UNA_CLAVE_SEGURA_TEMPORAL
```

Opcionalmente puedes reemplazar la lista:

```env
SuperAdmin__Emails__0=jfsvtech@gmail.com
SuperAdmin__Emails__1=juliansernavasco@gmail.com
```

## Crear empresa y dueno

Desde `/super` usa **Nuevo negocio**.

Ese formulario crea:

- Tenant/empresa.
- Usuario dueno dentro de esa empresa.
- Token de verificacion de correo.
- Correo de activacion si Gmail API esta configurado.

## Crear usuarios internos

Desde `/super/empresa/{id}` puedes crear usuarios internos:

- Dueno.
- Empleado.

Los clientes no consumen limite comercial de trabajadores.

## Limpiar para produccion real

Desde `/super/produccion/limpiar` puedes borrar todos los datos de empresas para arrancar limpio.

La accion elimina:

- Empresas.
- Usuarios por empresa.
- Clientes.
- Turnos.
- Servicios.
- Horarios.
- Gastos.
- Pagos.
- Notificaciones.
- Galeria.
- Auditoria asociada a empresas.

Se conservan:

- Usuarios globales `SuperAdmin`.

Requiere escribir exactamente:

```txt
LIMPIAR PRODUCCION
```

No ejecutes esta accion en una base con datos reales de clientes salvo que tengas backup confirmado.

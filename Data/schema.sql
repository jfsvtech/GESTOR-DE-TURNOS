-- ============================================================
--  GeneradorTurnos - Esquema multiempresa (Shared DB + tenant_id)
--  Idempotente: se puede ejecutar en cada arranque.
-- ============================================================

CREATE TABLE IF NOT EXISTS tenants (
    id              SERIAL PRIMARY KEY,
    nombre          TEXT        NOT NULL,
    slug            TEXT        NOT NULL UNIQUE,
    plan            TEXT        NOT NULL DEFAULT 'Basico',
    valor_suscripcion NUMERIC(12,2) NOT NULL DEFAULT 0,
    max_usuarios    INT         NOT NULL DEFAULT 10,
    activo          BOOLEAN     NOT NULL DEFAULT TRUE,
    fecha_creacion  TIMESTAMP   NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS usuarios (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT         NULL REFERENCES tenants(id) ON DELETE CASCADE,
    rol             INT         NOT NULL,
    nombre          TEXT        NOT NULL,
    cedula          TEXT        NOT NULL,
    email           TEXT        NULL,
    telefono        TEXT        NULL,
    password_hash   TEXT        NOT NULL,
    activo          BOOLEAN     NOT NULL DEFAULT TRUE,
    fecha_creacion  TIMESTAMP   NOT NULL DEFAULT now()
);
-- Cédula única por empresa (un cliente puede existir en varias empresas).
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_tenant_cedula
    ON usuarios(tenant_id, cedula) WHERE tenant_id IS NOT NULL;
-- SuperAdmins (tenant nulo): cédula única global.
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_global_cedula
    ON usuarios(cedula) WHERE tenant_id IS NULL;

CREATE TABLE IF NOT EXISTS servicios (
    id                SERIAL PRIMARY KEY,
    tenant_id         INT          NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    nombre            TEXT         NOT NULL,
    duracion_minutos  INT          NOT NULL,
    precio            NUMERIC(12,2) NOT NULL,
    activo            BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS empleado_servicios (
    id           SERIAL PRIMARY KEY,
    tenant_id    INT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    empleado_id  INT NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    servicio_id  INT NOT NULL REFERENCES servicios(id) ON DELETE CASCADE,
    UNIQUE(empleado_id, servicio_id)
);

CREATE TABLE IF NOT EXISTS horarios_trabajo (
    id           SERIAL PRIMARY KEY,
    tenant_id    INT  NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    empleado_id  INT  NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    dia_semana   INT  NOT NULL,           -- 0=Domingo .. 6=Sábado
    hora_inicio  TIME NOT NULL,
    hora_fin     TIME NOT NULL
);

CREATE TABLE IF NOT EXISTS bloqueos (
    id                 SERIAL PRIMARY KEY,
    tenant_id          INT       NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    empleado_id        INT       NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    fecha_hora_inicio  TIMESTAMP NOT NULL,
    fecha_hora_fin     TIMESTAMP NOT NULL,
    motivo             TEXT      NULL
);

CREATE TABLE IF NOT EXISTS turnos (
    id                 SERIAL PRIMARY KEY,
    tenant_id          INT          NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    cliente_id         INT          NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    empleado_id        INT          NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    servicio_id        INT          NOT NULL REFERENCES servicios(id),
    fecha_hora_inicio  TIMESTAMP    NOT NULL,
    fecha_hora_fin     TIMESTAMP    NOT NULL,
    estado             INT          NOT NULL DEFAULT 1,
    precio             NUMERIC(12,2) NOT NULL,
    notas              TEXT         NULL,
    fecha_creacion     TIMESTAMP    NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_turnos_emp_fecha
    ON turnos(tenant_id, empleado_id, fecha_hora_inicio);
CREATE INDEX IF NOT EXISTS ix_turnos_cliente
    ON turnos(tenant_id, cliente_id);
CREATE INDEX IF NOT EXISTS ix_turnos_tenant_fecha
    ON turnos(tenant_id, fecha_hora_inicio);
CREATE INDEX IF NOT EXISTS ix_turnos_emp_rango
    ON turnos(tenant_id, empleado_id, fecha_hora_inicio, fecha_hora_fin);

-- ============================================================
--  Migraciones idempotentes (se aplican en cada arranque)
-- ============================================================

-- Limite comercial de usuarios por empresa.
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS max_usuarios INT NOT NULL DEFAULT 10;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS suscripcion_inicio DATE NULL;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS suscripcion_vencimiento DATE NULL;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS valor_suscripcion NUMERIC(12,2) NOT NULL DEFAULT 0;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS estado_suscripcion TEXT NOT NULL DEFAULT 'Activo';
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS recordatorio_pago_dias INT NOT NULL DEFAULT 7;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP NULL;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP NULL;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS created_by INT NULL;
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS updated_by INT NULL;

-- Clientes sin contraseña (se identifican con cédula + nombre + teléfono).
ALTER TABLE usuarios ALTER COLUMN password_hash DROP NOT NULL;

-- Precio/duración propios por profesional (override sobre el catálogo del negocio).
ALTER TABLE empleado_servicios ADD COLUMN IF NOT EXISTS precio_override   NUMERIC(12,2) NULL;
ALTER TABLE empleado_servicios ADD COLUMN IF NOT EXISTS duracion_override INT           NULL;

-- Origen del turno: 1 = reservado por el cliente, 2 = creado manualmente por el staff.
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS origen INT NOT NULL DEFAULT 1;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS recordatorio_cliente_enviado BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS recordatorio_barbero_enviado BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP NULL;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP NULL;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS created_by INT NULL;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS updated_by INT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_turnos_rango_valido') THEN
        ALTER TABLE turnos ADD CONSTRAINT ck_turnos_rango_valido CHECK (fecha_hora_fin > fecha_hora_inicio);
    END IF;
END $$;

-- Clientes autónomos: registro con correo + contraseña + verificación.
ALTER TABLE usuarios ALTER COLUMN cedula DROP NOT NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS email_verificado   BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS token_verificacion TEXT      NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS token_expira        TIMESTAMP NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS updated_at          TIMESTAMP NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS deleted_at          TIMESTAMP NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS created_by          INT NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS updated_by          INT NULL;

-- Permitir múltiples clientes sin cédula (NULL); cédula única solo cuando existe.
DROP INDEX IF EXISTS ux_usuarios_tenant_cedula;
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_tenant_cedula
    ON usuarios(tenant_id, cedula) WHERE tenant_id IS NOT NULL AND cedula IS NOT NULL;
-- Email único por empresa (login de clientes).
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_tenant_email
    ON usuarios(tenant_id, lower(email)) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_global_email
    ON usuarios(lower(email)) WHERE tenant_id IS NULL AND email IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_usuarios_tenant_rol_activo
    ON usuarios(tenant_id, rol, activo);

-- El staff existente no requiere verificacion al migrar desde versiones anteriores.
UPDATE usuarios SET email_verificado = TRUE
    WHERE rol IN (1,2,3) AND email_verificado = FALSE;


-- El dueño también puede atender como profesional. 'atiende' marca a quienes son reservables.
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS atiende BOOLEAN NOT NULL DEFAULT FALSE;
UPDATE usuarios SET atiende = TRUE WHERE rol = 3 AND atiende = FALSE;  -- barberos siempre atienden

-- Fotos: logo/portada del negocio, foto del profesional y galería de cortes.
ALTER TABLE tenants  ADD COLUMN IF NOT EXISTS foto_url TEXT NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS foto_url TEXT NULL;

-- Comisión del dueño sobre cada barbero: 0=ninguna, 1=porcentaje, 2=fijo mensual.
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS comision_tipo  INT           NOT NULL DEFAULT 0;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS comision_valor NUMERIC(12,2) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS galeria (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT       NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    empleado_id     INT       NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    foto_url        TEXT      NOT NULL,
    descripcion     TEXT      NULL,
    fecha_creacion  TIMESTAMP NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_galeria_tenant ON galeria(tenant_id);
ALTER TABLE galeria ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP NULL;
ALTER TABLE galeria ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP NULL;
ALTER TABLE galeria ADD COLUMN IF NOT EXISTS created_by INT NULL;
ALTER TABLE galeria ADD COLUMN IF NOT EXISTS updated_by INT NULL;

-- Gastos del negocio (luz, insumos, etc.).
CREATE TABLE IF NOT EXISTS gastos (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT           NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    concepto        TEXT          NOT NULL,
    categoria       TEXT          NULL,
    monto           NUMERIC(12,2) NOT NULL,
    fecha           DATE          NOT NULL,
    fecha_creacion  TIMESTAMP     NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_gastos_tenant_fecha ON gastos(tenant_id, fecha);
ALTER TABLE gastos ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP NULL;
ALTER TABLE gastos ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP NULL;
ALTER TABLE gastos ADD COLUMN IF NOT EXISTS created_by INT NULL;
ALTER TABLE gastos ADD COLUMN IF NOT EXISTS updated_by INT NULL;

-- Notificaciones internas para avisar al profesional cambios hechos por clientes.
CREATE TABLE IF NOT EXISTS notificaciones (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT       NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    usuario_id      INT       NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    turno_id        INT       NULL REFERENCES turnos(id) ON DELETE CASCADE,
    tipo            TEXT      NOT NULL,
    titulo          TEXT      NOT NULL,
    mensaje         TEXT      NOT NULL,
    leida           BOOLEAN   NOT NULL DEFAULT FALSE,
    fecha_creacion  TIMESTAMP NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_notificaciones_usuario
    ON notificaciones(tenant_id, usuario_id, leida, fecha_creacion DESC);

-- Cambios de servicios/precios solicitados por profesionales y aprobados por el dueno.
CREATE TABLE IF NOT EXISTS servicio_solicitudes (
    id                 SERIAL PRIMARY KEY,
    tenant_id          INT           NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    empleado_id        INT           NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    servicio_id        INT           NOT NULL REFERENCES servicios(id) ON DELETE CASCADE,
    ofrecido           BOOLEAN       NOT NULL,
    precio_override    NUMERIC(12,2) NULL,
    duracion_override  INT           NULL,
    estado             TEXT          NOT NULL DEFAULT 'Pendiente',
    fecha_creacion     TIMESTAMP     NOT NULL DEFAULT now(),
    fecha_decision     TIMESTAMP     NULL
);
CREATE INDEX IF NOT EXISTS ix_servicio_solicitudes_pendientes
    ON servicio_solicitudes(tenant_id, estado, fecha_creacion DESC);
CREATE INDEX IF NOT EXISTS ix_servicios_tenant_activo
    ON servicios(tenant_id, activo);
CREATE INDEX IF NOT EXISTS ix_empleado_servicios_tenant_emp
    ON empleado_servicios(tenant_id, empleado_id);
CREATE INDEX IF NOT EXISTS ix_empleado_servicios_tenant_servicio
    ON empleado_servicios(tenant_id, servicio_id);
CREATE INDEX IF NOT EXISTS ix_horarios_tenant_emp
    ON horarios_trabajo(tenant_id, empleado_id, dia_semana);
CREATE INDEX IF NOT EXISTS ix_bloqueos_emp_rango
    ON bloqueos(tenant_id, empleado_id, fecha_hora_inicio, fecha_hora_fin);

-- Control de correos automaticos diarios.
CREATE TABLE IF NOT EXISTS email_envios (
    id             SERIAL PRIMARY KEY,
    tenant_id      INT       NULL REFERENCES tenants(id) ON DELETE CASCADE,
    usuario_id     INT       NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    tipo           TEXT      NOT NULL,
    fecha          DATE      NOT NULL,
    fecha_envio    TIMESTAMP NOT NULL DEFAULT now(),
    UNIQUE(tenant_id, usuario_id, tipo, fecha)
);

-- Control SaaS: pagos, suscripciones y auditoria por empresa.
CREATE TABLE IF NOT EXISTS pagos_suscripcion (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT           NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    monto           NUMERIC(12,2) NOT NULL,
    periodo_inicio  DATE          NOT NULL,
    periodo_fin     DATE          NOT NULL,
    metodo          TEXT          NULL,
    referencia      TEXT          NULL,
    nota            TEXT          NULL,
    fecha_pago      TIMESTAMP     NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_pagos_suscripcion_tenant
    ON pagos_suscripcion(tenant_id, fecha_pago DESC);

CREATE TABLE IF NOT EXISTS auditoria (
    id              SERIAL PRIMARY KEY,
    tenant_id       INT       NULL REFERENCES tenants(id) ON DELETE CASCADE,
    usuario_id      INT       NULL REFERENCES usuarios(id) ON DELETE SET NULL,
    actor_nombre    TEXT      NOT NULL,
    accion          TEXT      NOT NULL,
    entidad         TEXT      NOT NULL,
    entidad_id      INT       NULL,
    detalle         TEXT      NULL,
    fecha_creacion  TIMESTAMP NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_auditoria_tenant_fecha
    ON auditoria(tenant_id, fecha_creacion DESC);

-- Blindaje anti doble reserva a nivel base de datos.
-- Requiere btree_gist. Si el usuario de BD no puede crear extensiones o si ya existen solapes,
-- la app mantiene la validacion de dominio y deja pendiente la restriccion operativa.
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS btree_gist;
EXCEPTION
    WHEN insufficient_privilege THEN
        RAISE NOTICE 'No hay permisos para crear btree_gist; se omite exclusion constraint anti-solape.';
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')
       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_turnos_no_solape_activos')
       AND NOT EXISTS (
            SELECT 1
            FROM turnos a
            JOIN turnos b ON b.id > a.id
             AND b.tenant_id = a.tenant_id
             AND b.empleado_id = a.empleado_id
             AND b.estado NOT IN (4,5)
             AND a.estado NOT IN (4,5)
             AND a.fecha_hora_inicio < b.fecha_hora_fin
             AND a.fecha_hora_fin > b.fecha_hora_inicio
       )
    THEN
        ALTER TABLE turnos
            ADD CONSTRAINT ex_turnos_no_solape_activos
            EXCLUDE USING gist (
                tenant_id WITH =,
                empleado_id WITH =,
                tsrange(fecha_hora_inicio, fecha_hora_fin, '[)') WITH &&
            )
            WHERE (estado NOT IN (4,5));
    END IF;
END $$;

-- Sincroniza secuencias SERIAL tras restauraciones/importaciones.
-- Evita errores 23505 en *_pkey cuando la secuencia queda por debajo del max(id).
SELECT setval(pg_get_serial_sequence('tenants', 'id'), COALESCE((SELECT MAX(id) FROM tenants), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('usuarios', 'id'), COALESCE((SELECT MAX(id) FROM usuarios), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('servicios', 'id'), COALESCE((SELECT MAX(id) FROM servicios), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('empleado_servicios', 'id'), COALESCE((SELECT MAX(id) FROM empleado_servicios), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('horarios_trabajo', 'id'), COALESCE((SELECT MAX(id) FROM horarios_trabajo), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('bloqueos', 'id'), COALESCE((SELECT MAX(id) FROM bloqueos), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('turnos', 'id'), COALESCE((SELECT MAX(id) FROM turnos), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('galeria', 'id'), COALESCE((SELECT MAX(id) FROM galeria), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('gastos', 'id'), COALESCE((SELECT MAX(id) FROM gastos), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('notificaciones', 'id'), COALESCE((SELECT MAX(id) FROM notificaciones), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('servicio_solicitudes', 'id'), COALESCE((SELECT MAX(id) FROM servicio_solicitudes), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('email_envios', 'id'), COALESCE((SELECT MAX(id) FROM email_envios), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('pagos_suscripcion', 'id'), COALESCE((SELECT MAX(id) FROM pagos_suscripcion), 0) + 1, false);
SELECT setval(pg_get_serial_sequence('auditoria', 'id'), COALESCE((SELECT MAX(id) FROM auditoria), 0) + 1, false);

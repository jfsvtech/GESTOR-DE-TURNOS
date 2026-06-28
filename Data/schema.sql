-- ============================================================
--  GeneradorTurnos - Esquema multiempresa (Shared DB + tenant_id)
--  Idempotente: se puede ejecutar en cada arranque.
-- ============================================================

CREATE TABLE IF NOT EXISTS tenants (
    id              SERIAL PRIMARY KEY,
    nombre          TEXT        NOT NULL,
    slug            TEXT        NOT NULL UNIQUE,
    plan            TEXT        NOT NULL DEFAULT 'Basico',
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

-- ============================================================
--  Migraciones idempotentes (se aplican en cada arranque)
-- ============================================================

-- Limite comercial de usuarios por empresa.
ALTER TABLE tenants ADD COLUMN IF NOT EXISTS max_usuarios INT NOT NULL DEFAULT 10;

-- Clientes sin contraseña (se identifican con cédula + nombre + teléfono).
ALTER TABLE usuarios ALTER COLUMN password_hash DROP NOT NULL;

-- Precio/duración propios por profesional (override sobre el catálogo del negocio).
ALTER TABLE empleado_servicios ADD COLUMN IF NOT EXISTS precio_override   NUMERIC(12,2) NULL;
ALTER TABLE empleado_servicios ADD COLUMN IF NOT EXISTS duracion_override INT           NULL;

-- Origen del turno: 1 = reservado por el cliente, 2 = creado manualmente por el staff.
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS origen INT NOT NULL DEFAULT 1;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS recordatorio_cliente_enviado BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS recordatorio_barbero_enviado BOOLEAN NOT NULL DEFAULT FALSE;

-- Clientes autónomos: registro con correo + contraseña + verificación.
ALTER TABLE usuarios ALTER COLUMN cedula DROP NOT NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS email_verificado   BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS token_verificacion TEXT      NULL;
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS token_expira        TIMESTAMP NULL;

-- Permitir múltiples clientes sin cédula (NULL); cédula única solo cuando existe.
DROP INDEX IF EXISTS ux_usuarios_tenant_cedula;
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_tenant_cedula
    ON usuarios(tenant_id, cedula) WHERE tenant_id IS NOT NULL AND cedula IS NOT NULL;
-- Email único por empresa (login de clientes).
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_tenant_email
    ON usuarios(tenant_id, lower(email)) WHERE email IS NOT NULL;

-- El staff existente no requiere verificación; el cliente demo queda verificado.
UPDATE usuarios SET email_verificado = TRUE
    WHERE rol IN (1,2,3) AND email_verificado = FALSE;
UPDATE usuarios SET email = 'juan@cliente.com', email_verificado = TRUE
    WHERE cedula = '3001' AND email IS NULL;

-- Correos para el staff demo (ahora todos inician sesión por correo).
UPDATE usuarios SET email = 'carlos@barberia.com', email_verificado = TRUE
    WHERE cedula = '2001' AND (email IS NULL OR email = '');
UPDATE usuarios SET email = 'andres@barberia.com', email_verificado = TRUE
    WHERE cedula = '2002' AND (email IS NULL OR email = '');

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

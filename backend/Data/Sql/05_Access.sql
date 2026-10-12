--
-- Users, groups, sessions, API tokens, settings, and directory sign-in.
--

CREATE TABLE IF NOT EXISTS public.app_user (
    id            serial PRIMARY KEY,
    uuid          uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    username      text NOT NULL,
    password_hash text NOT NULL,
    is_active     boolean NOT NULL DEFAULT true,
    source        text NOT NULL DEFAULT 'local',
    directory_id  text,
    display_name  text NOT NULL DEFAULT '',
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT app_user_source_check CHECK (source IN ('local', 'directory'))
);

ALTER TABLE public.app_user OWNER TO {{OWNER}};

CREATE UNIQUE INDEX IF NOT EXISTS app_user_username_lower_idx ON public.app_user (lower(username));

CREATE UNIQUE INDEX IF NOT EXISTS app_user_directory_id_idx
    ON public.app_user (lower(directory_id))
    WHERE directory_id IS NOT NULL AND directory_id <> '';

CREATE TABLE IF NOT EXISTS public.app_group (
    id                  serial PRIMARY KEY,
    uuid                uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    name                text NOT NULL,
    description         text NOT NULL DEFAULT '',
    can_read            boolean NOT NULL DEFAULT true,
    can_write           boolean NOT NULL DEFAULT false,
    can_admin           boolean NOT NULL DEFAULT false,
    can_read_warehouse  boolean NOT NULL DEFAULT false,
    can_write_warehouse boolean NOT NULL DEFAULT false,
    is_system           boolean NOT NULL DEFAULT false,
    source              text NOT NULL DEFAULT 'local',
    directory_id        text,
    directory_dn        text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT app_group_source_check CHECK (source IN ('local', 'directory'))
);

ALTER TABLE public.app_group OWNER TO {{OWNER}};

CREATE UNIQUE INDEX IF NOT EXISTS app_group_name_lower_idx ON public.app_group (lower(name));

CREATE UNIQUE INDEX IF NOT EXISTS app_group_directory_id_idx
    ON public.app_group (lower(directory_id))
    WHERE directory_id IS NOT NULL AND directory_id <> '';

CREATE UNIQUE INDEX IF NOT EXISTS app_group_directory_dn_idx
    ON public.app_group (lower(directory_dn))
    WHERE directory_dn IS NOT NULL AND directory_dn <> '';

CREATE TABLE IF NOT EXISTS public.user_group (
    user_id  integer NOT NULL REFERENCES public.app_user(id) ON DELETE CASCADE,
    group_id integer NOT NULL REFERENCES public.app_group(id) ON DELETE CASCADE,
    origin   text NOT NULL DEFAULT 'manual',
    PRIMARY KEY (user_id, group_id),
    CONSTRAINT user_group_origin_check CHECK (origin IN ('manual', 'directory'))
);

ALTER TABLE public.user_group OWNER TO {{OWNER}};

CREATE TABLE IF NOT EXISTS public.api_token (
    id           serial PRIMARY KEY,
    uuid         uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    user_id      integer NOT NULL REFERENCES public.app_user(id) ON DELETE CASCADE,
    name         text NOT NULL,
    token_hash   text NOT NULL UNIQUE,
    token_prefix text NOT NULL,
    can_write    boolean NOT NULL DEFAULT false,
    created_at   timestamptz NOT NULL DEFAULT now(),
    expires_at   timestamptz NOT NULL DEFAULT now() + interval '90 days',
    last_used_at timestamptz,
    revoked_at   timestamptz
);

ALTER TABLE public.api_token OWNER TO {{OWNER}};

CREATE TABLE IF NOT EXISTS public.user_session (
    id           serial PRIMARY KEY,
    user_id      integer NOT NULL REFERENCES public.app_user(id) ON DELETE CASCADE,
    token_hash   text NOT NULL UNIQUE,
    created_at   timestamptz NOT NULL DEFAULT now(),
    expires_at   timestamptz NOT NULL,
    last_used_at timestamptz
);

ALTER TABLE public.user_session OWNER TO {{OWNER}};

DROP TRIGGER IF EXISTS set_timestamp ON public.app_user;
CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.app_user FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

DROP TRIGGER IF EXISTS set_timestamp ON public.app_group;
CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.app_group FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

INSERT INTO public.app_group (name, description, can_read, can_write, can_admin, is_system)
SELECT 'Read and write', 'May view and change the library.', true, true, false, true
WHERE NOT EXISTS (SELECT 1 FROM public.app_group WHERE lower(name) = lower('Read and write'));

INSERT INTO public.app_group (name, description, can_read, can_write, can_admin, is_system)
SELECT 'Read only', 'May view the library, but not change it.', true, false, false, true
WHERE NOT EXISTS (SELECT 1 FROM public.app_group WHERE lower(name) = lower('Read only'));

INSERT INTO public.app_group (name, description, can_read, can_write, can_admin, can_read_warehouse, can_write_warehouse, is_system)
SELECT 'Admins', 'May view and change the library, and manage users, groups and settings.', true, true, true, true, true, true
WHERE NOT EXISTS (SELECT 1 FROM public.app_group WHERE lower(name) = lower('Admins'));

-- Default account admin / kicomlib. Hash is PBKDF2-HMAC-SHA256, 210000 iterations.
INSERT INTO public.app_user (username, password_hash, is_active)
SELECT 'admin',
       'pbkdf2-sha256$210000$S2lDb21MaWJEZWZhdWx0MDEh$YmQ8zYnT28KrSBN7lde4pCY4uSzWLd/93RTsEKXv4k8=',
       true
WHERE NOT EXISTS (SELECT 1 FROM public.app_user WHERE lower(username) = 'admin');

INSERT INTO public.user_group (user_id, group_id)
SELECT u.id, g.id
FROM public.app_user u
JOIN public.app_group g ON lower(g.name) = lower('Admins')
WHERE lower(u.username) = 'admin'
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS public.app_setting (
    key        text PRIMARY KEY,
    value      text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.app_setting OWNER TO {{OWNER}};

INSERT INTO public.app_setting (key, value)
VALUES ('session_lifetime_days', '1')
ON CONFLICT (key) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.directory_connection (
    id                          integer PRIMARY KEY DEFAULT 1,
    enabled                     boolean NOT NULL DEFAULT false,
    server_type                 text NOT NULL DEFAULT 'ldap',
    host                        text NOT NULL DEFAULT '',
    port                        integer NOT NULL DEFAULT 389,
    encryption                  text NOT NULL DEFAULT 'none',
    allow_untrusted_certificate boolean NOT NULL DEFAULT false,
    bind_dn                     text NOT NULL DEFAULT '',
    bind_password_protected     text NOT NULL DEFAULT '',
    user_base_dn                text NOT NULL DEFAULT '',
    user_filter                 text NOT NULL DEFAULT '(&(objectClass=inetOrgPerson)(uid={username}))',
    username_attribute          text NOT NULL DEFAULT 'uid',
    display_name_attribute      text NOT NULL DEFAULT 'displayName',
    user_id_attribute           text NOT NULL DEFAULT 'entryUUID',
    group_base_dn               text NOT NULL DEFAULT '',
    group_filter                text NOT NULL DEFAULT '(objectClass=groupOfNames)',
    group_name_attribute        text NOT NULL DEFAULT 'cn',
    group_id_attribute          text NOT NULL DEFAULT 'entryUUID',
    membership_attribute        text NOT NULL DEFAULT 'member',
    provision_on_login          boolean NOT NULL DEFAULT true,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT directory_connection_singleton CHECK (id = 1),
    CONSTRAINT directory_connection_server_type CHECK (server_type IN ('ldap', 'ad')),
    CONSTRAINT directory_connection_encryption CHECK (encryption IN ('none', 'ssl', 'starttls'))
);

ALTER TABLE public.directory_connection OWNER TO {{OWNER}};

INSERT INTO public.directory_connection (id) VALUES (1) ON CONFLICT (id) DO NOTHING;

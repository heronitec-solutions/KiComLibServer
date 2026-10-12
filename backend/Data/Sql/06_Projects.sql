--
-- Git hosts, mirrored repositories, KiCad projects, and the project job queue.
--

CREATE TABLE public.git_host (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    kind             text NOT NULL CHECK (kind IN ('gitlab', 'github', 'generic')),
    name             text NOT NULL UNIQUE,
    web_url          text NOT NULL,
    ssh_host         text NULL,
    ca_certificate   text NULL,
    ssh_known_hosts  text NULL,
    allow_http       boolean NOT NULL DEFAULT false,
    is_system        boolean NOT NULL DEFAULT false,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.git_host OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.git_host
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

CREATE TABLE public.git_credential (
    id                serial PRIMARY KEY,
    uuid              uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    host_id           integer NOT NULL REFERENCES public.git_host(id),
    name              text NOT NULL,
    kind              text NOT NULL CHECK (kind IN ('https_token', 'ssh_key')),
    username          text NULL,
    secret_protected  text NOT NULL,
    secret_hint       text NOT NULL,
    public_key        text NULL,
    expires_at        timestamptz NULL,
    last_used_at      timestamptz NULL,
    last_error        text NULL,
    created_by        integer NULL REFERENCES public.app_user(id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    UNIQUE (host_id, name)
);

ALTER TABLE public.git_credential OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.git_credential
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

CREATE TABLE public.git_repository (
    id                        serial PRIMARY KEY,
    uuid                      uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    host_id                   integer NOT NULL REFERENCES public.git_host(id),
    credential_id             integer NULL REFERENCES public.git_credential(id) ON DELETE SET NULL,
    clone_url                 text NOT NULL,
    web_url                   text NULL,
    provider_path             text NOT NULL,
    default_branch            text NULL,
    tracked_branches          text[] NOT NULL DEFAULT '{}',
    tag_pattern               text NOT NULL DEFAULT '*',
    sync_mode                 text NOT NULL DEFAULT 'poll' CHECK (sync_mode IN ('webhook', 'poll')),
    poll_interval_minutes     integer NULL,
    webhook_secret_protected  text NULL,
    lfs_enabled               boolean NOT NULL DEFAULT false,
    status                    text NOT NULL DEFAULT 'new' CHECK (status IN ('new', 'ok', 'error', 'disabled')),
    last_fetch_at             timestamptz NULL,
    last_error                text NULL,
    created_by                integer NULL REFERENCES public.app_user(id) ON DELETE SET NULL,
    created_at                timestamptz NOT NULL DEFAULT now(),
    updated_at                timestamptz NOT NULL DEFAULT now(),
    UNIQUE (host_id, provider_path)
);

ALTER TABLE public.git_repository OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.git_repository
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

CREATE TABLE public.git_ref (
    repository_id  integer NOT NULL REFERENCES public.git_repository(id) ON DELETE CASCADE,
    name           text NOT NULL,
    sha            varchar(64) NOT NULL,
    updated_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (repository_id, name)
);

ALTER TABLE public.git_ref OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.git_ref
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

CREATE TABLE public.project (
    id                serial PRIMARY KEY,
    uuid              uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    repository_id     integer NOT NULL REFERENCES public.git_repository(id),
    project_file      text NOT NULL,
    export_scope      text NOT NULL DEFAULT 'project_dir' CHECK (export_scope IN ('project_dir', 'repository')),
    name              text NOT NULL,
    description       text NULL,
    status            text NOT NULL DEFAULT 'development' CHECK (status IN ('development', 'released', 'obsolete')),
    customer          text NULL,
    owner_user_id     integer NULL REFERENCES public.app_user(id) ON DELETE SET NULL,
    head_revision_id  integer NULL,
    is_archived       boolean NOT NULL DEFAULT false,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    UNIQUE (repository_id, project_file)
);

ALTER TABLE public.project OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.project
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

CREATE TABLE public.project_group_access (
    project_id  integer NOT NULL REFERENCES public.project(id) ON DELETE CASCADE,
    group_id    integer NOT NULL REFERENCES public.app_group(id) ON DELETE CASCADE,
    can_write   boolean NOT NULL DEFAULT false,
    PRIMARY KEY (project_id, group_id)
);

ALTER TABLE public.project_group_access OWNER TO {{OWNER}};

CREATE TABLE public.project_blob (
    hash          character(64) PRIMARY KEY,
    size          bigint NOT NULL,
    media_type    text NOT NULL,
    data          bytea NULL,
    storage       smallint NOT NULL DEFAULT 0,
    external_ref  text NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.project_blob OWNER TO {{OWNER}};

CREATE TABLE public.project_revision (
    id                 serial PRIMARY KEY,
    project_id         integer NOT NULL REFERENCES public.project(id) ON DELETE CASCADE,
    commit_sha         varchar(64) NOT NULL,
    parent_shas        text[] NOT NULL DEFAULT '{}',
    committed_at       timestamptz NOT NULL,
    author_name        text NOT NULL,
    author_email       text NULL,
    message            text NOT NULL,
    refs               text[] NOT NULL DEFAULT '{}',
    reachable          boolean NOT NULL DEFAULT true,
    kicad_version      text NULL,
    analysis_status    text NOT NULL DEFAULT 'pending'
                       CHECK (analysis_status IN ('pending', 'running', 'done', 'failed', 'unsupported')),
    analysis_error     text NULL,
    netlist_blob_hash  character(64) NULL REFERENCES public.project_blob(hash),
    catalog_commit_id  integer NULL REFERENCES public.commit(id),
    analyzed_at        timestamptz NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    UNIQUE (project_id, commit_sha)
);

ALTER TABLE public.project_revision OWNER TO {{OWNER}};

ALTER TABLE public.project
    ADD CONSTRAINT project_head_revision_fk
    FOREIGN KEY (head_revision_id) REFERENCES public.project_revision(id) ON DELETE SET NULL;

CREATE TABLE public.project_component (
    id                  bigserial PRIMARY KEY,
    revision_id         integer NOT NULL REFERENCES public.project_revision(id) ON DELETE CASCADE,
    variant             text NOT NULL DEFAULT '',
    symbol_path         text NOT NULL,
    symbol_uuids        uuid[] NOT NULL,
    sheet_names         text NOT NULL DEFAULT '/',
    reference           text NOT NULL,
    value               text NULL,
    footprint           text NULL,
    lib_id              text NULL,
    kicomlib_uuid         uuid NULL,
    mpn                 text NULL,
    manufacturer        text NULL,
    fields              jsonb NOT NULL DEFAULT '{}',
    dnp                 boolean NOT NULL DEFAULT false,
    exclude_from_bom    boolean NOT NULL DEFAULT false,
    exclude_from_board  boolean NOT NULL DEFAULT false,
    part_id             integer NULL REFERENCES public.part(id) ON DELETE SET NULL,
    match_method        text NOT NULL DEFAULT 'none' CHECK (match_method IN ('uuid', 'name', 'mpn', 'none')),
    UNIQUE (revision_id, variant, symbol_path)
);

ALTER TABLE public.project_component OWNER TO {{OWNER}};

CREATE INDEX project_component_part_idx ON public.project_component (part_id) WHERE part_id IS NOT NULL;

CREATE TABLE public.project_release (
    id           serial PRIMARY KEY,
    uuid         uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    project_id   integer NOT NULL REFERENCES public.project(id) ON DELETE CASCADE,
    revision_id  integer NOT NULL REFERENCES public.project_revision(id),
    tag          text NULL,
    name         text NOT NULL,
    notes        text NULL,
    state        text NOT NULL DEFAULT 'released' CHECK (state IN ('released', 'withdrawn')),
    released_by  integer NULL REFERENCES public.app_user(id) ON DELETE SET NULL,
    released_at  timestamptz NOT NULL DEFAULT now(),
    UNIQUE (project_id, name)
);

ALTER TABLE public.project_release OWNER TO {{OWNER}};

CREATE TABLE public.project_artifact (
    id             serial PRIMARY KEY,
    revision_id    integer NOT NULL REFERENCES public.project_revision(id) ON DELETE CASCADE,
    kind           text NOT NULL,
    variant        text NOT NULL DEFAULT '',
    file_name      text NOT NULL,
    blob_hash      character(64) NOT NULL REFERENCES public.project_blob(hash),
    kicad_version  text NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    UNIQUE (revision_id, kind, variant, file_name)
);

ALTER TABLE public.project_artifact OWNER TO {{OWNER}};

CREATE TABLE public.project_diff (
    id                  serial PRIMARY KEY,
    uuid                uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    project_id          integer NOT NULL REFERENCES public.project(id) ON DELETE CASCADE,
    base_revision_id    integer NOT NULL REFERENCES public.project_revision(id) ON DELETE CASCADE,
    target_revision_id  integer NOT NULL REFERENCES public.project_revision(id) ON DELETE CASCADE,
    status              text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'running', 'done', 'failed')),
    summary             jsonb NULL,
    kicad_version       text NULL,
    error               text NULL,
    last_viewed_at      timestamptz NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    UNIQUE (base_revision_id, target_revision_id)
);

ALTER TABLE public.project_diff OWNER TO {{OWNER}};

CREATE TABLE public.project_job (
    id             bigserial PRIMARY KEY,
    kind           text NOT NULL CHECK (kind IN ('fetch', 'discover', 'analyze', 'rematch', 'render', 'visual_diff', 'outputs')),
    dedup_key      text NULL,
    repository_id  integer NULL REFERENCES public.git_repository(id) ON DELETE CASCADE,
    project_id     integer NULL REFERENCES public.project(id) ON DELETE CASCADE,
    payload        jsonb NOT NULL DEFAULT '{}',
    status         text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'running', 'done', 'failed', 'cancelled')),
    attempts       integer NOT NULL DEFAULT 0,
    run_after      timestamptz NOT NULL DEFAULT now(),
    started_at     timestamptz NULL,
    finished_at    timestamptz NULL,
    error          text NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.project_job OWNER TO {{OWNER}};

CREATE UNIQUE INDEX project_job_dedup_idx ON public.project_job (dedup_key) WHERE status = 'queued';
CREATE INDEX project_job_queue_idx ON public.project_job (run_after, id) WHERE status = 'queued';

INSERT INTO public.git_host (kind, name, web_url, ssh_host, is_system) VALUES
    ('gitlab', 'GitLab.com', 'https://gitlab.com', 'git@gitlab.com', true),
    ('github', 'GitHub.com', 'https://github.com', 'git@github.com', true);

INSERT INTO public.app_setting (key, value) VALUES
    ('git_poll_interval_minutes', '5'),
    ('git_webhook_poll_interval_minutes', '60'),
    ('project_max_commits_per_sync', '20'),
    ('project_initial_tags', '50'),
    ('kicomlib_library_nicknames', 'KiComLib');

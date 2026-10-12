--
-- KiComLib database schema, version 1.
--

-- Creates functions, tables, triggers and views of an empty database.
-- {{OWNER}} is replaced by the backend with the configured database owner.


--------------------------------------------------------------------------------------------------
-- CREATE EXTENSIONS / FUNCTIONS
--------------------------------------------------------------------------------------------------

CREATE EXTENSION IF NOT EXISTS pgcrypto;

--
-- update timestamp function
--

CREATE FUNCTION public.trigger_set_timestamp() RETURNS trigger
    LANGUAGE plpgsql
    AS
    $$
      BEGIN
         NEW.updated_at = NOW();
         RETURN NEW;
      END;
    $$;


ALTER FUNCTION public.trigger_set_timestamp() OWNER TO {{OWNER}};



--------------------------------------------------------------------------------------------------
-- CREATE TABLES
--------------------------------------------------------------------------------------------------

--
-- Table: distributor
--

CREATE TABLE public.distributor (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    name character varying(100) NOT NULL,
    url character varying(200) NULL,
    trustedparts_id character varying(100) NULL,
    is_favorite boolean DEFAULT false NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.distributor OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.distributor FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


--
-- Table: manufacturer
--

CREATE TABLE public.manufacturer (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    name character varying(100) NOT NULL,
    url character varying(200) NULL,
    trustedparts_id character varying(100) NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.manufacturer OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.manufacturer FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


--
-- Table: library_file
--

CREATE TABLE public.library_file (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    type smallint NOT NULL,
    name character varying(100) NOT NULL,
    comment text NULL,
    filename character varying(100) NOT NULL UNIQUE,
    description text NULL,
    version character varying(8) NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.library_file OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.library_file FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


--
-- Table: library
--

CREATE TABLE public.library (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    name character varying(100) NOT NULL,
    filename character varying(100) NOT NULL UNIQUE,
    description text NULL,
    special_columns text[] NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.library OWNER TO {{OWNER}};

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.library FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


--------------------------------------------------------------------------------------------------
-- VERSIONING CORE
--------------------------------------------------------------------------------------------------

--
-- Table: blob
--

CREATE TABLE public.blob (
    hash         character(64) PRIMARY KEY,
    size         bigint NOT NULL,
    media_type   text NOT NULL,
    data         bytea NULL,
    storage      smallint NOT NULL DEFAULT 0,
    external_ref text NULL,
    created_at   timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.blob OWNER TO {{OWNER}};


--
-- Table: commit
--

CREATE TABLE public.commit (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    parent_commit_id integer NULL REFERENCES public.commit(id),
    message          text NOT NULL,
    author           text NOT NULL,
    client           text NULL,
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.commit OWNER TO {{OWNER}};


--
-- Table: schema_version
--

CREATE TABLE public.schema_version (
    version    integer PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.schema_version OWNER TO {{OWNER}};


--------------------------------------------------------------------------------------------------
-- VERSIONED ENTITIES – HEAD + REVISION
--------------------------------------------------------------------------------------------------

--
-- Table: symbol (head)
--

CREATE TABLE public.symbol (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    library_file_id  integer NOT NULL REFERENCES public.library_file(id) ON UPDATE CASCADE ON DELETE CASCADE,
    name             varchar(100) NOT NULL,
    head_revision_id integer NULL,
    deleted_at       timestamptz NULL,
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.symbol OWNER TO {{OWNER}};

CREATE UNIQUE INDEX symbol_lib_name_live ON public.symbol(library_file_id, name) WHERE deleted_at IS NULL;

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.symbol FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


CREATE TABLE public.symbol_revision (
    id                 serial PRIMARY KEY,
    symbol_id          integer NOT NULL REFERENCES public.symbol(id) ON DELETE CASCADE,
    commit_id          integer NOT NULL REFERENCES public.commit(id),
    revision_no        integer NOT NULL,
    is_deleted         boolean NOT NULL DEFAULT false,
    library_file_id    integer NOT NULL REFERENCES public.library_file(id),
    name               varchar(100) NOT NULL,
    blob_hash          character(64) NULL REFERENCES public.blob(hash),
    preview_blob_hash  character(64) NULL REFERENCES public.blob(hash),
    reference          varchar(50) NULL,
    in_bom             boolean NOT NULL DEFAULT true,
    on_board           boolean NOT NULL DEFAULT true,
    pin_numbers_hidden boolean NOT NULL DEFAULT false,
    pin_names_hidden   boolean NOT NULL DEFAULT false,
    pin_names_offset   real NULL,
    description        text NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    UNIQUE (symbol_id, revision_no)
);

ALTER TABLE public.symbol_revision OWNER TO {{OWNER}};

ALTER TABLE public.symbol
    ADD CONSTRAINT symbol_head_fk FOREIGN KEY (head_revision_id) REFERENCES public.symbol_revision(id) DEFERRABLE INITIALLY DEFERRED;


--
-- Table: model3d (head) – created before footprint for FK ordering of junction later
--

CREATE TABLE public.model3d (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    directory        text NOT NULL DEFAULT '',
    filename         text NOT NULL,
    head_revision_id integer NULL,
    deleted_at       timestamptz NULL,
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.model3d OWNER TO {{OWNER}};

CREATE UNIQUE INDEX model3d_path_live ON public.model3d(directory, filename) WHERE deleted_at IS NULL;

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.model3d FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


CREATE TABLE public.model3d_revision (
    id          serial PRIMARY KEY,
    model3d_id  integer NOT NULL REFERENCES public.model3d(id) ON DELETE CASCADE,
    commit_id   integer NOT NULL REFERENCES public.commit(id),
    revision_no integer NOT NULL,
    is_deleted  boolean NOT NULL DEFAULT false,
    directory   text NOT NULL DEFAULT '',
    filename    text NOT NULL,
    comment     text NULL,
    blob_hash   character(64) NULL REFERENCES public.blob(hash),
    size        bigint NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    UNIQUE (model3d_id, revision_no)
);

ALTER TABLE public.model3d_revision OWNER TO {{OWNER}};

ALTER TABLE public.model3d
    ADD CONSTRAINT model3d_head_fk FOREIGN KEY (head_revision_id) REFERENCES public.model3d_revision(id) DEFERRABLE INITIALLY DEFERRED;


--
-- Table: datasheet (head)
--

CREATE TABLE public.datasheet (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    directory        text NOT NULL DEFAULT '',
    filename         text NOT NULL,
    head_revision_id integer NULL,
    deleted_at       timestamptz NULL,
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.datasheet OWNER TO {{OWNER}};

CREATE UNIQUE INDEX datasheet_path_live ON public.datasheet(directory, filename) WHERE deleted_at IS NULL;

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.datasheet FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


CREATE TABLE public.datasheet_revision (
    id           serial PRIMARY KEY,
    datasheet_id integer NOT NULL REFERENCES public.datasheet(id) ON DELETE CASCADE,
    commit_id    integer NOT NULL REFERENCES public.commit(id),
    revision_no  integer NOT NULL,
    is_deleted   boolean NOT NULL DEFAULT false,
    directory    text NOT NULL DEFAULT '',
    filename     text NOT NULL,
    comment      text NULL,
    blob_hash    character(64) NULL REFERENCES public.blob(hash),
    size         bigint NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    UNIQUE (datasheet_id, revision_no)
);

ALTER TABLE public.datasheet_revision OWNER TO {{OWNER}};

ALTER TABLE public.datasheet
    ADD CONSTRAINT datasheet_head_fk FOREIGN KEY (head_revision_id) REFERENCES public.datasheet_revision(id) DEFERRABLE INITIALLY DEFERRED;


--
-- Table: template (head)
--

CREATE TABLE public.template (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    directory        text NOT NULL DEFAULT '',
    filename         text NOT NULL,
    head_revision_id integer NULL,
    deleted_at       timestamptz NULL,
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.template OWNER TO {{OWNER}};

CREATE UNIQUE INDEX template_path_live ON public.template(directory, filename) WHERE deleted_at IS NULL;

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.template FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


CREATE TABLE public.template_revision (
    id          serial PRIMARY KEY,
    template_id integer NOT NULL REFERENCES public.template(id) ON DELETE CASCADE,
    commit_id   integer NOT NULL REFERENCES public.commit(id),
    revision_no integer NOT NULL,
    is_deleted  boolean NOT NULL DEFAULT false,
    directory   text NOT NULL DEFAULT '',
    filename    text NOT NULL,
    comment     text NULL,
    blob_hash   character(64) NULL REFERENCES public.blob(hash),
    size        bigint NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    UNIQUE (template_id, revision_no)
);

ALTER TABLE public.template_revision OWNER TO {{OWNER}};

ALTER TABLE public.template
    ADD CONSTRAINT template_head_fk FOREIGN KEY (head_revision_id) REFERENCES public.template_revision(id) DEFERRABLE INITIALLY DEFERRED;


--
-- Table: footprint (head)
--

CREATE TABLE public.footprint (
    id               serial PRIMARY KEY,
    uuid             uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    library_file_id  integer NOT NULL REFERENCES public.library_file(id) ON UPDATE CASCADE ON DELETE CASCADE,
    name             varchar(100) NOT NULL,
    head_revision_id integer NULL,
    deleted_at       timestamptz NULL,
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE public.footprint OWNER TO {{OWNER}};

CREATE UNIQUE INDEX footprint_lib_name_live ON public.footprint(library_file_id, name) WHERE deleted_at IS NULL;

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.footprint FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


CREATE TABLE public.footprint_revision (
    id                     serial PRIMARY KEY,
    footprint_id           integer NOT NULL REFERENCES public.footprint(id) ON DELETE CASCADE,
    commit_id              integer NOT NULL REFERENCES public.commit(id),
    revision_no            integer NOT NULL,
    is_deleted             boolean NOT NULL DEFAULT false,
    library_file_id        integer NOT NULL REFERENCES public.library_file(id),
    name                   varchar(100) NOT NULL,
    blob_hash              character(64) NULL REFERENCES public.blob(hash),
    preview_blob_hash      character(64) NULL REFERENCES public.blob(hash),
    type                   smallint NOT NULL,
    board_only             boolean NOT NULL DEFAULT false,
    exclude_from_pos_files boolean NOT NULL DEFAULT false,
    exclude_from_bom       boolean NOT NULL DEFAULT false,
    height                 real NOT NULL DEFAULT 0.0,
    layer                  varchar(10) NOT NULL,
    description            text NULL,
    tags                   text NULL,
    property               text NULL,
    created_at             timestamptz NOT NULL DEFAULT now(),
    UNIQUE (footprint_id, revision_no)
);

ALTER TABLE public.footprint_revision OWNER TO {{OWNER}};

ALTER TABLE public.footprint
    ADD CONSTRAINT footprint_head_fk FOREIGN KEY (head_revision_id) REFERENCES public.footprint_revision(id) DEFERRABLE INITIALLY DEFERRED;


CREATE TABLE public.footprint_revision_model3d (
    footprint_revision_id integer NOT NULL REFERENCES public.footprint_revision(id) ON DELETE CASCADE,
    position              smallint NOT NULL,
    model3d_id            integer NOT NULL REFERENCES public.model3d(id),
    offset_x real NOT NULL DEFAULT 0,
    offset_y real NOT NULL DEFAULT 0,
    offset_z real NOT NULL DEFAULT 0,
    scale_x  real NOT NULL DEFAULT 1,
    scale_y  real NOT NULL DEFAULT 1,
    scale_z  real NOT NULL DEFAULT 1,
    rotate_x real NOT NULL DEFAULT 0,
    rotate_y real NOT NULL DEFAULT 0,
    rotate_z real NOT NULL DEFAULT 0,
    opacity  real NULL,
    hide     boolean NOT NULL DEFAULT false,
    PRIMARY KEY (footprint_revision_id, position)
);

ALTER TABLE public.footprint_revision_model3d OWNER TO {{OWNER}};


--------------------------------------------------------------------------------------------------
-- CATALOG HEADS (part / part_number / order_number / manufacturer / distributor)
-- Live rows stay mutable. Every change is also stored in catalog_revision.
--------------------------------------------------------------------------------------------------

--
-- Table: part
--

CREATE TABLE public.part (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    name character varying(100) NOT NULL UNIQUE,
    comment text NULL,
    datasheet_id integer NULL,
    datasheet character varying(100) NULL,
    datasheet_ids json NOT NULL DEFAULT '[]'::json,
    library_id integer NULL,
    symbol_id integer NULL,
    footprint_id integer NULL,
    lifecycle_status character varying(16) NOT NULL DEFAULT 'active',
    keywords text NULL,
    value character varying(100) NULL,
    properties json NULL,
    alternatives json NOT NULL DEFAULT '[]'::json,
    links json NOT NULL DEFAULT '[]'::json,
    image_hash character(64) NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.part OWNER TO {{OWNER}};

ALTER TABLE ONLY public.part
    ADD CONSTRAINT part_library_id_foreign FOREIGN KEY (library_id) REFERENCES public.library(id) ON UPDATE CASCADE ON DELETE CASCADE;

ALTER TABLE ONLY public.part
    ADD CONSTRAINT part_symbol_id_foreign FOREIGN KEY (symbol_id) REFERENCES public.symbol(id) ON UPDATE CASCADE ON DELETE CASCADE;

ALTER TABLE ONLY public.part
    ADD CONSTRAINT part_footprint_id_foreign FOREIGN KEY (footprint_id) REFERENCES public.footprint(id) ON UPDATE CASCADE ON DELETE CASCADE;

ALTER TABLE ONLY public.part
    ADD CONSTRAINT part_datasheet_id_foreign FOREIGN KEY (datasheet_id) REFERENCES public.datasheet(id) ON UPDATE CASCADE ON DELETE SET NULL;

ALTER TABLE ONLY public.part
    ADD CONSTRAINT part_image_hash_foreign FOREIGN KEY (image_hash) REFERENCES public.blob(hash);

CREATE TRIGGER set_timestamp BEFORE UPDATE ON public.part FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();


--
-- Table: part numbers
--

CREATE TABLE public.part_number (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    part_id integer NULL,
    manufacturer_id integer NULL,
    manufacturer_part_number character varying(100) NULL,
    quantity integer DEFAULT 1 NOT NULL,
    package_type character varying(50) NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.part_number OWNER TO {{OWNER}};

ALTER TABLE ONLY public.part_number
    ADD CONSTRAINT part_manufacturer_id_foreign FOREIGN KEY (manufacturer_id) REFERENCES public.manufacturer(id) ON UPDATE CASCADE ON DELETE CASCADE;

ALTER TABLE ONLY public.part_number
    ADD CONSTRAINT part_id_foreign FOREIGN KEY (part_id) REFERENCES public.part(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Table: order numbers
--

CREATE TABLE public.order_number (
    id SERIAL NOT NULL UNIQUE PRIMARY KEY,
    uuid uuid DEFAULT gen_random_uuid() NOT NULL UNIQUE,
    order_number character varying(100) NOT NULL,
    part_number_id integer NULL,
    distributor_id integer NULL,
    quantity_min integer DEFAULT 1 NOT NULL,
    quantity_steps integer DEFAULT 1 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.order_number OWNER TO {{OWNER}};

ALTER TABLE ONLY public.order_number
    ADD CONSTRAINT part_number_id_foreign FOREIGN KEY (part_number_id) REFERENCES public.part_number(id) ON UPDATE CASCADE ON DELETE CASCADE;

ALTER TABLE ONLY public.order_number
    ADD CONSTRAINT distributor_id_foreign FOREIGN KEY (distributor_id) REFERENCES public.distributor(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Table: catalog_revision
-- Immutable snapshots of part, manufacturer, distributor, part number, and order number.
--

CREATE TABLE public.catalog_revision (
    id            serial PRIMARY KEY,
    entity        text NOT NULL,
    entity_id     integer NOT NULL,
    entity_uuid   uuid NOT NULL,
    commit_id     integer NOT NULL REFERENCES public.commit(id),
    revision_no   integer NOT NULL,
    is_deleted    boolean NOT NULL DEFAULT false,
    name          text NOT NULL,
    snapshot      text NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    UNIQUE (entity, entity_id, revision_no)
);

ALTER TABLE public.catalog_revision OWNER TO {{OWNER}};

CREATE INDEX catalog_revision_commit_id_idx ON public.catalog_revision (commit_id);

CREATE OR REPLACE FUNCTION public.backfill_catalog_revisions()
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    cid integer;
BEGIN
    IF EXISTS (SELECT 1 FROM public.catalog_revision LIMIT 1) THEN
        RETURN;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM public.manufacturer
        UNION ALL SELECT 1 FROM public.distributor
        UNION ALL SELECT 1 FROM public.part
        UNION ALL SELECT 1 FROM public.part_number
        UNION ALL SELECT 1 FROM public.order_number
    ) THEN
        RETURN;
    END IF;

    INSERT INTO public.commit (message, author, client)
    VALUES ('Initial catalog import', 'system', 'init')
    RETURNING id INTO cid;

    INSERT INTO public.catalog_revision (entity, entity_id, entity_uuid, commit_id, revision_no, is_deleted, name, snapshot)
    SELECT 'manufacturer', m.id, m.uuid, cid, 1, false, m.name,
           (to_jsonb(m) - 'id' - 'uuid' - 'created_at' - 'updated_at')::text
    FROM public.manufacturer m;

    INSERT INTO public.catalog_revision (entity, entity_id, entity_uuid, commit_id, revision_no, is_deleted, name, snapshot)
    SELECT 'distributor', d.id, d.uuid, cid, 1, false, d.name,
           (to_jsonb(d) - 'id' - 'uuid' - 'created_at' - 'updated_at')::text
    FROM public.distributor d;

    INSERT INTO public.catalog_revision (entity, entity_id, entity_uuid, commit_id, revision_no, is_deleted, name, snapshot)
    SELECT 'part', p.id, p.uuid, cid, 1, false, p.name,
           (to_jsonb(p) - 'id' - 'uuid' - 'created_at' - 'updated_at')::text
    FROM public.part p;

    INSERT INTO public.catalog_revision (entity, entity_id, entity_uuid, commit_id, revision_no, is_deleted, name, snapshot)
    SELECT 'partnumber', n.id, n.uuid, cid, 1, false,
           COALESCE(NULLIF(n.manufacturer_part_number, ''), 'part number ' || n.id::text),
           (to_jsonb(n) - 'id' - 'uuid' - 'created_at' - 'updated_at')::text
    FROM public.part_number n;

    INSERT INTO public.catalog_revision (entity, entity_id, entity_uuid, commit_id, revision_no, is_deleted, name, snapshot)
    SELECT 'ordernumber', o.id, o.uuid, cid, 1, false, o.order_number,
           (to_jsonb(o) - 'id' - 'uuid' - 'created_at' - 'updated_at')::text
    FROM public.order_number o;
END;
$$;

ALTER FUNCTION public.backfill_catalog_revisions() OWNER TO {{OWNER}};


--------------------------------------------------------------------------------------------------
-- HEAD VIEWS
--------------------------------------------------------------------------------------------------

CREATE OR REPLACE VIEW public.symbol_head_v AS
    SELECT
        s.id,
        s.uuid,
        s.library_file_id,
        s.name,
        s.head_revision_id,
        s.deleted_at,
        s.updated_at,
        s.created_at,
        r.revision_no,
        r.blob_hash AS hash,
        r.blob_hash AS file_hash,
        r.preview_blob_hash,
        r.reference,
        r.in_bom,
        r.on_board,
        r.pin_numbers_hidden,
        r.pin_names_hidden,
        r.pin_names_offset,
        r.description,
        r.commit_id,
        lf.name AS library_file_name,
        lf.filename AS library_file_filename,
        lf.version AS library_file_version
    FROM public.symbol s
    LEFT JOIN public.symbol_revision r ON r.id = s.head_revision_id
    INNER JOIN public.library_file lf ON lf.id = s.library_file_id
    WHERE s.deleted_at IS NULL;

ALTER VIEW public.symbol_head_v OWNER TO {{OWNER}};


CREATE OR REPLACE VIEW public.footprint_head_v AS
    SELECT
        f.id,
        f.uuid,
        f.library_file_id,
        f.name,
        f.head_revision_id,
        f.deleted_at,
        f.updated_at,
        f.created_at,
        r.revision_no,
        r.blob_hash AS hash,
        r.blob_hash AS file_hash,
        r.preview_blob_hash,
        r.type,
        r.board_only,
        r.exclude_from_pos_files,
        r.exclude_from_bom,
        r.height,
        r.layer,
        r.description,
        r.tags,
        r.property,
        r.commit_id,
        (SELECT frm.model3d_id FROM public.footprint_revision_model3d frm
         WHERE frm.footprint_revision_id = r.id AND frm.position = 0 LIMIT 1) AS model3d_id,
        lf.name AS library_file_name,
        lf.filename AS library_file_filename,
        lf.version AS library_file_version
    FROM public.footprint f
    LEFT JOIN public.footprint_revision r ON r.id = f.head_revision_id
    INNER JOIN public.library_file lf ON lf.id = f.library_file_id
    WHERE f.deleted_at IS NULL;

ALTER VIEW public.footprint_head_v OWNER TO {{OWNER}};


CREATE OR REPLACE VIEW public.model3d_head_v AS
    SELECT
        m.id,
        m.uuid,
        m.directory,
        m.filename,
        m.head_revision_id,
        m.deleted_at,
        m.updated_at,
        m.created_at,
        r.revision_no,
        r.blob_hash AS hash,
        r.blob_hash AS file_hash,
        r.comment,
        r.size AS file_size,
        r.commit_id
    FROM public.model3d m
    LEFT JOIN public.model3d_revision r ON r.id = m.head_revision_id
    WHERE m.deleted_at IS NULL;

ALTER VIEW public.model3d_head_v OWNER TO {{OWNER}};


CREATE OR REPLACE VIEW public.datasheet_head_v AS
    SELECT
        d.id,
        d.uuid,
        d.directory,
        d.filename,
        d.head_revision_id,
        d.deleted_at,
        d.updated_at,
        d.created_at,
        r.revision_no,
        r.blob_hash AS hash,
        r.blob_hash AS file_hash,
        r.comment,
        r.size AS file_size,
        r.commit_id
    FROM public.datasheet d
    LEFT JOIN public.datasheet_revision r ON r.id = d.head_revision_id
    WHERE d.deleted_at IS NULL;

ALTER VIEW public.datasheet_head_v OWNER TO {{OWNER}};


CREATE OR REPLACE VIEW public.template_head_v AS
    SELECT
        t.id,
        t.uuid,
        t.directory,
        t.filename,
        t.head_revision_id,
        t.deleted_at,
        t.updated_at,
        t.created_at,
        r.revision_no,
        r.blob_hash AS hash,
        r.blob_hash AS file_hash,
        r.comment,
        r.size AS file_size,
        r.commit_id
    FROM public.template t
    LEFT JOIN public.template_revision r ON r.id = t.head_revision_id
    WHERE t.deleted_at IS NULL;

ALTER VIEW public.template_head_v OWNER TO {{OWNER}};

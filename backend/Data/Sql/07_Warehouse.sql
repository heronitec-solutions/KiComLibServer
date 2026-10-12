--
-- Warehouse stock. Read and write are group rights of their own and do not
-- follow the catalog rights. Those rights are columns on app_group.
--

CREATE TABLE public.warehouse_item (
    id         serial PRIMARY KEY,
    uuid       uuid NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    part_id    integer REFERENCES public.part(id) ON DELETE SET NULL,
    name       text NOT NULL DEFAULT '',
    value      text NOT NULL DEFAULT '',
    comment    text NOT NULL DEFAULT '',
    image_hash character(64) NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT warehouse_item_identity CHECK (part_id IS NOT NULL OR length(btrim(name)) > 0),
    CONSTRAINT warehouse_item_image_hash_foreign FOREIGN KEY (image_hash) REFERENCES public.blob(hash)
);

ALTER TABLE public.warehouse_item OWNER TO {{OWNER}};

CREATE UNIQUE INDEX warehouse_item_part_id_idx
    ON public.warehouse_item (part_id)
    WHERE part_id IS NOT NULL;

CREATE TABLE public.warehouse_batch (
    id         serial PRIMARY KEY,
    item_id    integer NOT NULL REFERENCES public.warehouse_item(id) ON DELETE CASCADE,
    name       text NOT NULL,
    quantity   integer NOT NULL,
    location   text NOT NULL DEFAULT '',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT warehouse_batch_quantity_nonnegative CHECK (quantity >= 0),
    CONSTRAINT warehouse_batch_empty_rule CHECK (
        (quantity = 0 AND name = 'Empty') OR (quantity > 0 AND name <> 'Empty')
    )
);

ALTER TABLE public.warehouse_batch OWNER TO {{OWNER}};

CREATE INDEX warehouse_batch_item_id_idx ON public.warehouse_batch (item_id);

CREATE UNIQUE INDEX warehouse_batch_one_empty_idx
    ON public.warehouse_batch (item_id)
    WHERE name = 'Empty';

CREATE TRIGGER set_timestamp
    BEFORE UPDATE ON public.warehouse_item
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

CREATE TRIGGER set_timestamp
    BEFORE UPDATE ON public.warehouse_batch
    FOR EACH ROW EXECUTE FUNCTION public.trigger_set_timestamp();

INSERT INTO public.schema_version (version) VALUES (1);

--
-- Record revision 1 for catalog rows inserted by the seed scripts.
-- No-op when catalog_revision already has rows.
--

SELECT public.backfill_catalog_revisions();

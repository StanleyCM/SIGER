-- SIGER / Entregable 3. MANUAL: pending explicit approval before execution on Supabase.
-- PostgreSQL >= 12. Run this entire file as one transaction, during a maintenance window.
-- No data deletion, schema recreation, migration runner or modification of Auth/RLS/grants.
-- The new enum label is compared as text in CHECKs so it need not be used before COMMIT.
-- https://www.postgresql.org/docs/current/sql-altertype.html
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SET LOCAL search_path = public, pg_catalog;

-- Fail before modifying anything if the expected base schema is absent.
DO $$
BEGIN
    IF to_regclass('public.reserva') IS NULL OR to_regclass('public.orden') IS NULL
       OR to_regtype('public.estado_orden') IS NULL THEN
        RAISE EXCEPTION 'Expected SIGER tables and enum are missing';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public'
        AND table_name = 'reserva' AND column_name = 'id_mesa' AND is_nullable = 'NO') THEN
        RAISE EXCEPTION 'reserva.id_mesa must remain NOT NULL';
    END IF;
END $$;

LOCK TABLE public.reserva, public.orden IN ACCESS EXCLUSIVE MODE;
ALTER TYPE public.estado_orden ADD VALUE IF NOT EXISTS 'Preordenada';

ALTER TABLE public.reserva
    ALTER COLUMN id_usuario DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS nombre_contacto varchar(150),
    ADD COLUMN IF NOT EXISTS telefono_contacto varchar(30),
    ADD COLUMN IF NOT EXISTS email_contacto varchar(150),
    ADD COLUMN IF NOT EXISTS token_acceso_hash bytea,
    ADD COLUMN IF NOT EXISTS token_acceso_expira timestamptz;
ALTER TABLE public.orden
    ALTER COLUMN id_usuario DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS id_reserva bigint NULL;

-- IF NOT EXISTS must not silently accept incompatible pre-existing columns.
DO $$
DECLARE expected record;
BEGIN
    FOR expected IN SELECT * FROM (VALUES
        ('reserva','nombre_contacto','character varying',150),
        ('reserva','telefono_contacto','character varying',30),
        ('reserva','email_contacto','character varying',150),
        ('reserva','token_acceso_hash','bytea',NULL),
        ('reserva','token_acceso_expira','timestamp with time zone',NULL),
        ('orden','id_reserva','bigint',NULL)
    ) AS v(tbl,col,typ,len) LOOP
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema = 'public'
            AND c.table_name = expected.tbl AND c.column_name = expected.col AND c.data_type = expected.typ
            AND c.character_maximum_length IS NOT DISTINCT FROM expected.len AND c.is_nullable = 'YES') THEN
            RAISE EXCEPTION 'Incompatible column %.%', expected.tbl, expected.col;
        END IF;
    END LOOP;
END $$;

-- Add and validate constraints without touching any pre-existing foreign key.
-- Named constraints already present must match exactly; unexpected definitions abort.
DO $$
DECLARE item record; existing text;
BEGIN
    FOR item IN SELECT * FROM (VALUES
        ('reserva', 'ck_reserva_contacto_invitado',
         'CHECK (id_usuario IS NOT NULL OR (COALESCE(length(btrim(nombre_contacto)), 0) > 0 AND COALESCE(length(btrim(telefono_contacto)), 0) > 0))'),
        ('reserva', 'ck_reserva_credencial',
         'CHECK ((token_acceso_hash IS NULL) = (token_acceso_expira IS NULL) AND (token_acceso_hash IS NULL OR octet_length(token_acceso_hash) = 32) AND (id_usuario IS NOT NULL OR token_acceso_hash IS NOT NULL))'),
        ('orden', 'ck_orden_responsable',
         'CHECK (id_usuario IS NOT NULL OR (origen::text = ''Web'' AND tipo::text = ''Mesa'' AND id_reserva IS NOT NULL AND id_mesa IS NOT NULL AND id_cliente IS NULL AND estado::text IN (''Preordenada'', ''Cancelada'') AND NOT cuenta_solicitada))'),
        ('orden', 'ck_orden_preorden',
         'CHECK (estado::text <> ''Preordenada'' OR (origen::text = ''Web'' AND tipo::text = ''Mesa'' AND id_reserva IS NOT NULL AND id_mesa IS NOT NULL AND id_usuario IS NULL AND id_cliente IS NULL AND NOT cuenta_solicitada))'),
        ('orden', 'fk_orden_reserva',
         'FOREIGN KEY (id_reserva) REFERENCES public.reserva(id_reserva) ON DELETE RESTRICT')
    ) AS v(tbl,con,definition) LOOP
        -- Compare server-normalized definitions through a temporary comparison constraint.
        -- It is removed before COMMIT; no existing constraint is removed or replaced.
        SELECT pg_get_constraintdef(oid) INTO existing FROM pg_constraint
            WHERE conrelid = format('public.%I', item.tbl)::regclass AND conname = item.con;
        IF existing IS NULL THEN
            EXECUTE format('ALTER TABLE public.%I ADD CONSTRAINT %I %s', item.tbl, item.con, item.definition);
        ELSE
            EXECUTE format('ALTER TABLE public.%I ADD CONSTRAINT %I %s', item.tbl, item.con || '_003_compare', item.definition);
            IF existing <> (SELECT pg_get_constraintdef(oid) FROM pg_constraint
                WHERE conrelid = format('public.%I', item.tbl)::regclass AND conname = item.con || '_003_compare') THEN
                RAISE EXCEPTION 'Unexpected existing constraint %', item.con;
            END IF;
            EXECUTE format('ALTER TABLE public.%I DROP CONSTRAINT %I', item.tbl, item.con || '_003_compare');
        END IF;
    END LOOP;
END $$;

-- Reuse valid equivalent btree indexes (including an unfiltered unique index).
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT * FROM (VALUES
        ('orden', 'ux_orden_id_reserva', ARRAY['id_reserva'], true, 'id_reserva IS NOT NULL'),
        ('reserva', 'ux_reserva_token_acceso_hash', ARRAY['token_acceso_hash'], true, 'token_acceso_hash IS NOT NULL'),
        ('reserva', 'ix_reserva_mesa_fecha_disponibilidad', ARRAY['id_mesa','fecha_hora'], false, NULL)
    ) AS v(tbl,idx,cols,uniq,predicate) LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_index i JOIN pg_class ix ON ix.oid = i.indexrelid JOIN pg_am am ON am.oid = ix.relam
            WHERE i.indrelid = format('public.%I', item.tbl)::regclass AND i.indisvalid AND i.indisready
              AND am.amname = 'btree' AND (NOT item.uniq OR i.indisunique) AND i.indexprs IS NULL
              AND (SELECT array_agg(a.attname::text ORDER BY k.ord)
                   FROM unnest(i.indkey) WITH ORDINALITY k(attnum,ord)
                   JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                   WHERE k.ord <= i.indnkeyatts) = item.cols
              AND (i.indpred IS NULL OR pg_get_expr(i.indpred, i.indrelid) = '(' || item.predicate || ')')
        ) THEN
            -- A conflicting name intentionally causes an error; never drop an existing index.
            EXECUTE format('CREATE %s INDEX %I ON public.%I (%s)%s',
                CASE WHEN item.uniq THEN 'UNIQUE' ELSE '' END, item.idx, item.tbl,
                (SELECT string_agg(quote_ident(c), ',') FROM unnest(item.cols) c),
                CASE WHEN item.predicate IS NULL THEN '' ELSE ' WHERE ' || item.predicate END);
        END IF;
    END LOOP;
END $$;
COMMIT;

CREATE TABLE IF NOT EXISTS public.controle_posicao_eventos (
    id smallint PRIMARY KEY CHECK (id = 1),
    ultima_posicao bigint NOT NULL CHECK (ultima_posicao >= 0)
);

INSERT INTO public.controle_posicao_eventos (id, ultima_posicao)
VALUES (1, 0)
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.eventos (
    posicao_global bigint PRIMARY KEY,
    evento_id uuid NOT NULL UNIQUE,
    stream_id text NOT NULL,
    versao_stream bigint NOT NULL CHECK (versao_stream > 0),
    tipo_evento text NOT NULL,
    versao_schema integer NOT NULL CHECK (versao_schema > 0),
    dados jsonb NOT NULL CHECK (jsonb_typeof(dados) = 'object'),
    metadados jsonb NOT NULL DEFAULT '{{}}'::jsonb CHECK (jsonb_typeof(metadados) = 'object'),
    ocorrido_em timestamptz NOT NULL,
    registrado_em timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_eventos_stream_versao UNIQUE (stream_id, versao_stream)
);

CREATE TABLE IF NOT EXISTS public.checkpoints_publicacao (
    publicador text PRIMARY KEY,
    ultima_posicao bigint NOT NULL DEFAULT 0 CHECK (ultima_posicao >= 0),
    atualizado_em timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS public.chaves_idempotencia (
    chave text PRIMARY KEY,
    evento_id uuid NOT NULL UNIQUE,
    hash_requisicao text NOT NULL,
    resposta jsonb NOT NULL CHECK (jsonb_typeof(resposta) = 'object'),
    criado_em timestamptz NOT NULL DEFAULT clock_timestamp()
);

INSERT INTO public.checkpoints_publicacao (publicador, ultima_posicao)
VALUES ('lancamentos-kafka', 0)
ON CONFLICT (publicador) DO NOTHING;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.eventos) THEN
        INSERT INTO public.eventos (
            posicao_global,
            evento_id,
            stream_id,
            versao_stream,
            tipo_evento,
            versao_schema,
            dados,
            metadados,
            ocorrido_em
        )
        SELECT
            ROW_NUMBER() OVER (ORDER BY l.criado_em, l.id),
            l.id,
            'lancamento:' || l.id::text,
            1,
            'lancamento.registrado.v1',
            1,
            jsonb_build_object(
                'EventId', l.id,
                'OcorridoEm', l.criado_em,
                'EventType', 'lancamento.registrado.v1',
                'LancamentoId', l.id,
                'Tipo', l.tipo,
                'Valor', l.valor,
                'Data', l.data,
                'Descricao', l.descricao
            ),
            jsonb_build_object('origem', 'backfill-lancamentos'),
            l.criado_em
        FROM public.lancamentos l
        ORDER BY l.criado_em, l.id;
    END IF;
END
$$;

UPDATE public.controle_posicao_eventos
SET ultima_posicao = GREATEST(
    ultima_posicao,
    COALESCE((SELECT MAX(posicao_global) FROM public.eventos), 0)
)
WHERE id = 1;

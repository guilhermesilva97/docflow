# DocFlow

![CI](https://github.com/guilhermesilva97/docflow/actions/workflows/ci.yml/badge.svg)

Pipeline de notas fiscais orientado a eventos: o PDF chega, um LLM extrai os campos com saída estruturada, regras de negócio validam, e o que a máquina não garante vai para uma pessoa revisar, com o PDF ao lado dos campos e a tela atualizando ao vivo.

Construído em **.NET 10** com **Clean Architecture + DDD**, **MassTransit** (saga, outbox, retry, DLQ), **RabbitMQ**, **Kafka**, **SQL Server**, **Azure Blob** (Azurite) e **Angular 22**.

```
PDF ──▶ API ──outbox──▶ RabbitMQ ──▶ Saga ──▶ Extraction (LLM | heurística)
                                      │  ◀── DocumentExtracted
                                      ├──▶ Validation (CNPJ, totais, datas)
                                      │  ◀── DocumentValidated
                                      ▼
                    Approved · NeedsReview (humano) · Rejected (duplicada)
                                      │
                         DocumentStatusChanged ──▶ SSE (tela) + Kafka (auditoria)
```

## Destaques técnicos

- **Saga orquestrada** (state machine do MassTransit) persistida no SQL Server, com tratamento explícito de eventos fora de ordem.
- **Transactional outbox + inbox**: o documento e as mensagens são gravados na mesma transação, e duplicatas são descartadas.
- **Idempotência em camadas**: inbox, `Ignore` por estado na saga, transições validadas no agregado e dedupe por offset na auditoria.
- **IA com rede de segurança**:
  - LLM com JSON Schema estrito e fallback heurístico;
  - limite de confiança;
  - revisão humana.
- **Evals**: 3 layouts de nota × gabarito conhecido, medindo a acurácia por campo do LLM contra o baseline.
- **RabbitMQ para o workflow e Kafka para a trilha de auditoria imutável.**
- **Workers stateless**: só a API escreve no agregado.
- **Tudo via Docker**: rodar, testar, formatar, avaliar e gerar migrations.

## Como rodar

Pré-requisitos: Docker com Compose v2 e cerca de 4 GB livres para o Docker (o SQL Server sozinho usa 2 GB).

```bash
cp .env.example .env
docker compose up -d --build --wait
```

| Endereço | O quê |
|---|---|
| http://localhost:8280 | Aplicação |
| http://localhost:8282 | RabbitMQ Management (guest/guest) |

Na tela, use **Gerar nota fictícia** para criar PDFs em 3 layouts e 3 cenários (válida, CNPJ inválido, total errado), ou envie seus próprios PDFs.

### Com LLM

Sem LLM, o sistema usa o extrator heurístico, que entende bem só o layout clássico. Para ligar a IA:

**Ollama local (sem custo)**

```env
LLM_BASE_URL=http://ollama:11434/v1
LLM_MODEL=qwen2.5:7b
```

```bash
docker compose --profile local-llm up -d --build --wait
docker compose --profile local-llm run --rm ollama-pull
```

**OpenAI ou Azure OpenAI**

```env
LLM_BASE_URL=https://api.openai.com/v1
LLM_API_KEY=sk-...
LLM_MODEL=gpt-4o-mini
LLM_TEMPERATURE=0
LLM_REASONING_EFFORT=
```

Modelos de raciocínio (família `gpt-5`, `o3`, `o4`) não aceitam `temperature`: deixe `LLM_TEMPERATURE` vazio e use, por exemplo, `LLM_REASONING_EFFORT=low`.

## Testes e evals

```bash
docker compose --profile test run --rm --build dotnet-test
docker compose --profile test run --rm --build --no-deps web-test
docker compose --profile evals run --rm --build evals
```

`dotnet-test` sobe SQL Server e RabbitMQ reais e roda `dotnet format --verify-no-changes`, os testes unitários e os de integração.

## Estrutura

```
docflow/
├── src/
│   ├── DocFlow.Domain              agregado Document, regras, CNPJ
│   ├── DocFlow.Contracts           mensagens, filas e tópicos
│   ├── DocFlow.Application         casos de uso e portas
│   ├── DocFlow.Infrastructure      EF Core, MassTransit, Blob, PdfPig, OpenAI
│   ├── DocFlow.Samples             gerador de notas fictícias (QuestPDF)
│   ├── DocFlow.Api                 ingestão, revisão, saga, SSE
│   ├── DocFlow.Extraction.Worker
│   ├── DocFlow.Validation.Worker
│   ├── DocFlow.Audit.Worker        Kafka → DocFlowAudit
│   └── DocFlow.Evals
├── tests/
│   ├── DocFlow.UnitTests
│   └── DocFlow.IntegrationTests
├── web/                            Angular 22 + nginx
├── scripts/smoke.sh
├── docs/
├── Dockerfile                      multi-stage para todos os serviços .NET
└── docker-compose.yml
```

## Documentação

| Documento | Conteúdo |
|---|---|
| [01 - Ideia](docs/01-ideia.md) | Problema, objetivos, escopo e requisitos |
| [02 - Arquitetura](docs/02-arquitetura.md) | C4, máquina de estados, sequência e camadas |
| [03 - Execução](docs/03-execucao.md) | Como foi construído e os problemas encontrados |
| [04 - Pipeline](docs/04-pipeline.md) | CI/CD passo a passo e deploy no Azure |
| [ADRs](docs/adr) | Decisões de arquitetura |

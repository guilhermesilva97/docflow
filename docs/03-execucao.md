# 03 - Execução

Como o DocFlow foi construído, na ordem em que aconteceu: o que decidi, por quê, e o que deu errado no caminho.

## Etapas

| # | Etapa | Entregável |
|---|---|---|
| 1 | Versões e licenças | Pacotes escolhidos e fixados com Central Package Management |
| 2 | Domínio | Agregado `Document`, `InvoiceValidator`, `Cnpj` e testes |
| 3 | Aplicação | Casos de uso e portas |
| 4 | Gerador de notas | 3 layouts × 3 cenários com gabarito (QuestPDF) |
| 5 | Extração | PdfPig, heurística, LLM com schema estrito e fallback |
| 6 | Mensageria | Saga, outbox, consumers, retry, DLQ e ponte para Kafka |
| 7 | Persistência | EF Core, JSON columns, migrations e concorrência otimista |
| 8 | API e SSE | Minimal API, revisão e eventos ao vivo |
| 9 | Front-end | Angular 22 zoneless com signals |
| 10 | Containers e pipeline | Dockerfile único, compose com profiles e CI |
| 11 | Validação de ponta a ponta | Smoke test com a stack real |
| 12 | Teste com LLM real | Correção de parâmetros do modelo e evals com OpenAI |

---

## 1. Versões e licenças

Antes de escrever código, consultei as versões atuais no NuGet.

> **Problema:** a versão mais recente do MassTransit (9.x) está publicada com licença comercial. Usar a última versão sem olhar a licença colocaria um projeto público em situação irregular.
>
> **Decisão:** fixei a **8.5.12**, que é Apache 2.0 e já tem build para `net10.0` ([ADR 0006](adr/0006-masstransit-8-e-docker.md)).

Todas as versões ficam em `Directory.Packages.props` (Central Package Management), e os `.csproj` só dizem *quais* pacotes usam.

## 2. Domínio primeiro

Comecei pelo que não depende de nada:

- **`Cnpj`**: validação dos dois dígitos verificadores (pesos 5–2/9–2 e 6–2/9–2), rejeitando sequências repetidas como `11111111111111`, que passam no cálculo mas não são CNPJs válidos.
- **`InvoiceValidator`**: devolve uma *lista* de problemas em vez de parar no primeiro. O revisor precisa ver tudo de uma vez.
- **`Document`**: a máquina de estados é um dicionário `AllowedTransitions`. Cada método de negócio (`RecordExtraction`, `CompleteValidation`, `Approve`, `Reject`) passa por `TransitionTo`, que valida e registra no histórico.

Uma regra importante ficou no agregado, não na saga: **a ordem das decisões em `CompleteValidation`**.

1. Tem problema de validação → revisão.
2. A confiança está abaixo do limite → revisão.
3. É duplicada → rejeitada.
4. Senão → aprovada.

A duplicidade só é decidida automaticamente quando os dados são confiáveis. Uma nota com CNPJ mal extraído não pode ser rejeitada como "duplicada" de outra.

## 3. Aplicação

Três casos de uso, cada um com uma responsabilidade:

- `DocumentIngestion`: salva o PDF, cria o agregado e publica `DocumentReceived` e `DocumentStatusChanged` **antes** do `SaveChanges`. Com o outbox, publicar é só gravar uma linha na mesma transação.
- `DocumentWorkflow`: chamado pelas activities da saga. Não chama `SaveChanges`, porque quem confirma a transação é o repositório da saga, junto com o estado dela e o outbox.
- `DocumentReview`: aprovação e rejeição humanas, com checagem de duplicidade.

## 4. Gerador de notas com gabarito

Para medir a IA, eu precisava de documentos com resposta conhecida. O `InvoiceSampleGenerator` cria notas com QuestPDF a partir de uma *seed*.

Os três layouts foram desenhados para serem diferentes de propósito:

- **Clássico**: rótulos explícitos ("CNPJ do emitente:", "Valor total: R$ ..."). É o caso fácil.
- **Moderno**: tabela, CNPJ sem pontuação, datas por extenso ("12 de março de 2026"), "FORNECEDOR/CLIENTE" em vez de emitente/destinatário.
- **Compacto**: tudo em linha, datas ISO, valores com ponto decimal, "Prestador/Tomador".

Os cenários `InvalidCnpj` e `WrongTotal` imprimem dados que quebram uma regra específica. O gabarito é o que está *impresso*, porque a extração deve ler o que está no papel, e a validação é que deve reprovar.

## 5. Extração

- **Texto:** `ContentOrderTextExtractor` do PdfPig preserva a ordem de leitura e as quebras de linha, diferente do `page.Text`, que cola tudo.
- **Heurística:** regex sobre rótulos do layout clássico, mais um fallback genérico para CNPJ (o primeiro achado é o emitente, o segundo o destinatário). A confiança é a cobertura de campos encontrados, com teto de 0,9. Uma regex nunca deveria parecer mais confiável que um humano.
- **LLM:** `ChatResponseFormat.CreateJsonSchemaFormat` com `strict: true`, campos anuláveis (`["string","null"]`) e a instrução explícita de nunca inventar valores. O texto é truncado em 12 mil caracteres.
- **Fallback:** se o LLM lançar qualquer exceção (rede, timeout, JSON inválido), a heurística assume e o método fica registrado como `heuristic (fallback)`.
- **PDF sem texto ou corrompido:** `ExtractionFailedException` vira o evento `DocumentExtractionFailed`. É um caminho de negócio, que leva o documento para revisão, e não uma exceção que esgotaria os retries e cairia na DLQ.

## 6. Mensageria

**Saga.** A state machine tem três estados intermediários e trata explicitamente o que pode chegar fora de hora. Por exemplo, um `DocumentExtracted` repetido durante `Validating` é ignorado em vez de virar falha.

**Activities.** Cada transição é uma classe `IStateMachineActivity` resolvida pelo container, com o `DocumentWorkflow` injetado. A saga não conhece EF Core nem regras de negócio.

**Workers.** Recebem comandos com tudo de que precisam e devolvem eventos. O worker de validação nem tem banco.

**Ponte para o Kafka.** `DocumentStatusChanged` sai pelo outbox para o RabbitMQ, e o `KafkaAuditForwarder` reencaminha para o tópico. Assim o Kafka só recebe o que foi commitado.

**Eventos ao vivo.** Cada instância da API assina `DocumentStatusChanged` numa **fila temporária** própria, em vez de competir numa fila compartilhada. Se houver duas réplicas, as duas recebem o evento e notificam seus próprios clientes SSE.

## 7. Persistência

- `InvoiceData`, a lista de problemas e o histórico são gravados como JSON (`InvoiceJson`, `IssuesJson`, `HistoryJson`), com um `ValueComparer` que compara pelo JSON serializado. Sem ele, o EF Core não perceberia mudanças dentro de listas.
- `DuplicateKey` é uma coluna real com índice, para a busca de duplicidade não depender do JSON.
- `RowVersion` como *shadow property* no documento e como propriedade na saga dá concorrência otimista nos dois lados.

> **Problema:** as migrations geradas pelo `dotnet ef` vêm com `// <auto-generated />` e `/// <inheritdoc />`, o que viola a regra de zero comentários do projeto.
>
> **Correção:** removi as linhas de comentário depois de gerar, e marquei a pasta `Migrations` como `generated_code = true` no `.editorconfig`, para o `dotnet format` não exigir estilo em código gerado.

> **Problema:** o build acusou `NU1903` (vulnerabilidade alta) em `System.Security.Cryptography.Pkcs 9.0.11`. Eu não usava esse pacote diretamente: `dotnet nuget why` mostrou que ele vinha de `Microsoft.EntityFrameworkCore.SqlServer → Microsoft.Data.SqlClient 6.1.7`.
>
> **Correção:** habilitei `CentralPackageTransitivePinningEnabled` e fixei `System.Security.Cryptography.Pkcs` em 10.0.14. O build voltou a ter zero warnings sem esperar uma nova versão do SqlClient.

## 8. API

- Minimal API com `TypedResults` e `Results<...>`, o que deixa os tipos de resposta explícitos e testáveis.
- SSE com `TypedResults.ServerSentEvents`, nativo do .NET 10, alimentado por um `Channel` limitado por cliente (`DropOldest`, para um cliente lento não segurar memória).
- Erros de domínio viram HTTP: `InvoiceRejectedException` → 400 com `ValidationProblem` (o front mostra cada problema), duplicada ou transição inválida → 409.
- `POST /api/samples` gera uma nota fictícia e a injeta no mesmo fluxo de um upload real. É o que torna a demo possível sem PDFs reais.

## 9. Front-end

> **Problema:** o scaffold do Angular 22 gera `tsconfig.json`, `.editorconfig` e `.gitignore` com comentários, além de `.vscode/` e um teste de exemplo que deixaria de passar.
>
> **Correção:** removi os comentários e os arquivos desnecessários, e liguei `strict` e `strictTemplates`, que o scaffold não deixava explícitos.

- Zoneless com signals: `LiveStatus` mantém um `EventSource` e expõe `lastChange`. As páginas reagem com `effect`, usando `untracked` para não criar dependências acidentais.
- A tela de revisão mostra o PDF original num `iframe` ao lado do formulário e recalcula a soma dos itens enquanto o revisor edita. Os problemas que voltam da API aparecem no formulário.
- As funções puras (`format.ts`) concentram rótulos, formatação pt-BR e a normalização da nota antes do envio, e são o que os testes Vitest cobrem.

## 10. Containers e pipeline

- Um único Dockerfile para os quatro serviços .NET (`ARG PROJECT`), com o stage de restore copiando só os `.csproj` para aproveitar o cache.
- O compose tem limites de memória por serviço. O SQL Server tem `MSSQL_MEMORY_LIMIT_MB` e `mem_limit: 2g`, e o Kafka roda com heap de 256 MB.
- Healthchecks sem instalar nada nas imagens: `sqlcmd` (SQL Server), `kafka-broker-api-versions.sh` (Kafka), `nc` (Azurite) e `/dev/tcp` do bash (RabbitMQ e API, já que a imagem `aspnet` não tem curl).

> **Problema:** o `dotnet format --verify-no-changes` da imagem de teste falhou em centenas de linhas com `ENDOFLINE`. Os arquivos foram criados no Windows com CRLF, e o `.editorconfig` exige LF.
>
> **Correção:** rodei `dotnet format` no container para normalizar, e criei um `.gitattributes` com `* text=auto eol=lf`, para o Git nunca reintroduzir CRLF num checkout no Windows.

> **Problema:** na primeira subida da stack de testes, o RabbitMQ morreu com `Error when reading /var/lib/rabbitmq/.erlang.cookie: eacces`. O healthcheck `rabbitmq-diagnostics ping` roda como root e, se dispara antes do broker terminar o boot, cria o cookie com dono root. O broker roda como usuário `rabbitmq` e não consegue mais ler o próprio cookie.
>
> **Correção:** troquei o healthcheck por um teste de porta TCP (`bash -c 'exec 3<>/dev/tcp/localhost/5672'`), que não toca no cookie, e adicionei `start_period`. É o tipo de erro que só aparece de vez em quando, numa corrida de inicialização.

> **Problema:** o Azurite ficava `unhealthy` mesmo funcionando. No Alpine, `localhost` resolve primeiro para `::1` (IPv6), e o Azurite só escuta em IPv4.
>
> **Correção:** `nc -z 127.0.0.1 10000`.

## 11. Validação de ponta a ponta

> **Problema:** o smoke test passou em todo o fluxo de negócio, mas o check do SSE devolveu `000`. Testando direto na API, os headers da resposta **não eram enviados** até o primeiro evento. `TypedResults.ServerSentEvents` só faz flush quando escreve um item. Na prática, o `EventSource` do navegador ficaria em "conectando" até alguém mudar um documento.
>
> **Correção:** o stream agora começa com um evento `ready` (`SseItem` com `eventType`) e só depois repassa os eventos `status`. O front já escutava só `status`, então não precisou mudar.

> **Problema:** a mensagem de total divergente apareceu como `Soma dos itens (25,164.15)...`, porque o container roda com cultura invariante.
>
> **Correção:** as mensagens de validação usam `string.Create(CultureInfo pt-BR, ...)` explicitamente. Um domínio brasileiro não deve depender da cultura do servidor.

## 12. Primeiro teste com um LLM real

Até aqui o LLM só tinha sido exercitado por testes de parsing e pelo caminho de falha. O primeiro teste com a OpenAI (`gpt-5.6-luna`) começou com um problema que o próprio desenho escondia.

> **Problema:** o extrator enviava `Temperature = 0` fixo. Modelos de raciocínio (família `gpt-5`, `o3`, `o4`) recusam `temperature`, então toda chamada falharia. Só que o `FallbackInvoiceExtractor` captura a falha, registra um *warning* e usa a heurística. O sistema **pareceria funcionar com IA** enquanto rodava 100% sem ela; o único sinal seria o `extractionMethod` como `heuristic (fallback)` e a queda de acurácia nos layouts Modern e Compact.
>
> **Correção:** `Temperature` e `ReasoningEffort` passaram a ser opcionais em `LlmOptions` (`LLM_TEMPERATURE` e `LLM_REASONING_EFFORT`) e só são enviados quando configurados. `BuildOptions` virou um método testável, com 2 testes novos.
>
> **Lição:** fallback silencioso protege o usuário, mas pode esconder um defeito de configuração por muito tempo. O `extractionMethod` gravado em cada documento e o warning no log são o que torna a troca auditável; no futuro, isso merece uma métrica e um alerta.

> **Problema:** no SDK `OpenAI` 2.14 para .NET, `ChatCompletionOptions.ReasoningEffortLevel` é marcado como experimental (`OPENAI001`), e o build falha com esse diagnóstico.
>
> **Decisão:** suprimir **somente** o `OPENAI001`, via `<NoWarn>`, nos dois projetos que usam a API (Infrastructure e testes unitários). Aceito o risco de a API mudar: o uso fica concentrado em um único método (`BuildOptions`), coberto por teste, e uma quebra futura aparece no build.

Resultado com `gpt-5.6-luna` e `reasoning_effort=low`:

| Extrator | Classic | Modern | Compact | Geral |
|---|---|---|---|---|
| Heurístico | 100% | só CNPJs | só CNPJs | 48% |
| **LLM** | **100%** | **100%** | **100%** | **100%** |

Na stack completa, enviei uma nota válida e uma com total errado em cada layout. As 3 válidas foram aprovadas sozinhas com `extractionMethod = llm`, inclusive Modern e Compact, que antes iam para revisão. As 3 com total errado foram para `NeedsReview` com a diferença exata na mensagem.

---

## Validações realizadas

| Verificação | Resultado |
|---|---|
| `dotnet build` | 0 warnings e 0 erros |
| `dotnet format --verify-no-changes` | Sem alterações |
| Testes unitários (domínio, saga com `ITestHarness`, consumers, extratores, gerador, opções do LLM) | 39 aprovados |
| Testes de integração com SQL Server 2022 e RabbitMQ 4 reais (migrations, JSON columns, outbox na mesma transação, duplicidade, ida e volta pelo broker) | 4 aprovados |
| Front: prettier, Vitest e `ng build` de produção | 3 testes aprovados; bundle inicial de 76,75 kB transferidos |
| Smoke na stack completa, sem LLM: nota válida | `Approved` sozinha via heurística (confiança 90%) |
| Smoke: total errado e CNPJ inválido | `NeedsReview` |
| Smoke: reenvio do mesmo PDF já aprovado | `Rejected` como duplicada |
| Smoke: rejeição manual | `Rejected` |
| Revisão: aprovar sem corrigir | HTTP 400 com o problema `total_mismatch` |
| Revisão: corrigir o total e aprovar | HTTP 200, `Approved`, revisor no histórico |
| PDF corrompido | Falha controlada → `NeedsReview` ("O arquivo não é um PDF legível.") |
| Layout moderno sem LLM | Confiança 0,2 → `NeedsReview` |
| SSE pelo nginx | HTTP 200 imediato com evento `ready` |
| Trilha de auditoria (RabbitMQ → forwarder → Kafka → Audit Worker → `DocFlowAudit`) | 34 eventos gravados após o smoke |
| Filas `_error` (DLQ) após o smoke | Todas vazias |
| Evals, baseline heurístico (18 notas) | 100% no clássico; nos outros layouts só os CNPJs; 48% no geral |
| Workflow validado com actionlint | Sem erros |
| Evals com LLM real (`gpt-5.6-luna`, 18 notas) | 100% em todos os campos e layouts |
| Stack completa com LLM real: 3 notas válidas e 3 com total errado | Válidas `Approved` via `llm` nos 3 layouts; erradas em `NeedsReview` |

O número que justifica o LLM é a diferença para o baseline: a heurística é perfeita no layout para o qual foi escrita e inútil nos outros, e o LLM chegou a 100% nos três. O job `evals` existe para medir isso de novo antes de qualquer troca de modelo ou de prompt.

## Próximos passos

- OCR (Azure Document Intelligence ou Tesseract) para PDFs digitalizados, hoje enviados para revisão.
- Agendar um timeout na saga para documentos parados em `Extracting`.
- Métricas OpenTelemetry por etapa (tempo de extração, taxa de revisão, custo de tokens).
- Usar as correções dos revisores como novos casos de eval.
- Métrica e alerta para a taxa de uso do fallback, que hoje só aparece no log e no `extractionMethod`.

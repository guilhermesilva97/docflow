# ADR 0001: saga orquestrada com state machine do MassTransit

**Status:** aceita

## Contexto

Um documento passa por várias etapas assíncronas: extração, validação e, às vezes, revisão humana. Cada etapa roda em um serviço diferente e pode falhar, demorar ou ser entregue duas vezes.

Há dois estilos possíveis:

- **Coreografia:** cada serviço reage ao evento anterior e publica o próximo.
- **Orquestração:** um coordenador central decide o próximo passo.

## Decisão

Usei **orquestração** com uma `MassTransitStateMachine<DocumentState>`.

- A saga reage a `DocumentReceived`, `DocumentExtracted`, `DocumentExtractionFailed`, `DocumentValidated` e `DocumentReviewed`.
- Ela envia comandos (`ExtractDocument`, `ValidateDocument`) para filas específicas.
- O estado da saga (`Extracting`, `Validating`, `AwaitingReview`) fica persistido no SQL Server, na tabela `DocumentWorkflows`.

Cada transição chama uma *activity* (`StartExtractionActivity`, `RecordExtractionActivity`...) que delega ao caso de uso `DocumentWorkflow`. Toda regra de negócio fica no agregado `Document`, e a saga só decide **o que acontece depois**.

## Consequências

**Positivas**

- O fluxo inteiro está num único arquivo legível ([DocumentStateMachine.cs](../../src/DocFlow.Infrastructure/Messaging/DocumentStateMachine.cs)).
- Eventos fora de ordem ou repetidos são tratados de forma explícita, com `Ignore(...)` por estado.
- Os workers não sabem que existe um fluxo: recebem um comando e devolvem um evento. Isso os torna triviais de testar e escalar.
- A saga é testável em memória com o `ITestHarness` do MassTransit.

**Negativas**

- O orquestrador é um ponto central: se cair, o fluxo para. Como o estado fica persistido e as mensagens esperam na fila, o fluxo retoma quando ele volta.
- Existem dois "estados": o da saga (onde está o processo) e o do agregado (o que o documento é). Mantive essa separação de propósito, e a saga guarda só `LastStatus` para decidir o próximo passo.

## Alternativas consideradas

- **Coreografia pura:** menos acoplamento, mas o fluxo fica espalhado entre quatro serviços e é difícil responder "em que etapa está esse documento?".
- **Routing slip (MassTransit Courier):** bom para sequências fixas com compensação. Aqui o fluxo tem bifurcação humana, que se encaixa melhor numa state machine.

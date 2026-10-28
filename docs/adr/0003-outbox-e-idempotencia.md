# ADR 0003: transactional outbox e idempotência em camadas

**Status:** aceita

## Contexto

O problema clássico do dual write: gravar o documento no banco e publicar `DocumentReceived` no RabbitMQ são duas operações. Se o processo cair entre elas, ou o documento existe sem evento (fica parado para sempre) ou o evento existe sem documento.

Além disso, RabbitMQ entrega **pelo menos uma vez**, então toda mensagem pode chegar duplicada.

## Decisão

**Outbox (EF Core + MassTransit)**

- `AddEntityFrameworkOutbox<DocFlowDbContext>` com `UseBusOutbox()`: na API, `Publish` grava em `OutboxMessage` dentro da mesma transação do `SaveChanges`. Um *delivery service* em background envia para o broker depois do commit.
- Nos endpoints que consomem mensagens (saga e forwarder), `UseEntityFrameworkOutbox` faz o mesmo para mensagens publicadas durante o consumo e registra a mensagem recebida em `InboxState`.

**Idempotência em camadas**

| Camada | Mecanismo |
|---|---|
| Inbox | `InboxState` deduplica por `MessageId` dentro de `DuplicateDetectionWindow` (30 min) |
| Saga | `Ignore(evento)` nos estados em que ele não faz sentido, como um segundo `DocumentExtracted` durante `Validating` |
| Agregado | `AllowedTransitions` recusa transições inválidas com `InvalidTransitionException` |
| Workers | São funções puras do comando: reprocessar produz o mesmo evento |
| Auditoria | Índice único em `(Partition, Offset)` do Kafka |

**Retry e DLQ**

- Retry exponencial (5 tentativas, de 200 ms a 10 s) em todos os endpoints, configurado antes do outbox, para que cada tentativa use uma transação nova.
- `DomainException` e `DocumentNotFoundException` **não** são retentadas: repetir não muda o resultado.
- Esgotadas as tentativas, o MassTransit move a mensagem para `<fila>_error`, que é a DLQ. A pipeline verifica que essas filas terminam vazias.

## Consequências

- Nenhuma mensagem é perdida nem publicada sem o dado correspondente. Isso é comprovado no teste de integração `Ingestion_writes_document_and_outbox_messages_in_the_same_transaction`.
- Há latência extra, porque o delivery service consulta o outbox a cada 1 s (`QueryDelay`).
- O banco ganha quatro tabelas de infraestrutura: `OutboxMessage`, `OutboxState`, `InboxState` e `DocumentWorkflows`.

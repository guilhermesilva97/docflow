# ADR 0004: RabbitMQ para o workflow, Kafka para a trilha de auditoria

**Status:** aceita

## Contexto

Há dois tipos de tráfego de mensagens:

1. **Comandos e eventos do workflow:** precisam de roteamento por fila, retry, DLQ e consumo competitivo entre réplicas.
2. **Histórico de tudo que aconteceu com cada documento:** precisa ser imutável, reprocessável desde o início e consumível por vários leitores independentes (auditoria hoje, analytics amanhã).

## Decisão

- **RabbitMQ** (via MassTransit) para o workflow.
- **Kafka** (KRaft, 1 nó) para o tópico `docflow.document-events`.
- A ponte é o `KafkaAuditForwarder`: um consumidor RabbitMQ de `DocumentStatusChanged` que produz no Kafka via *MassTransit Kafka Rider*. Como `DocumentStatusChanged` sai pelo outbox, o Kafka recebe exatamente o que foi commitado.
- O `DocFlow.Audit.Worker` consome o tópico com o group `docflow-audit` e grava em `DocFlowAudit.dbo.AuditEntries`.

## Consequências

- Um consumidor novo (por exemplo, um dashboard de produtividade da revisão) pode ler o tópico desde o offset zero sem tocar no workflow.
- A auditoria fica fisicamente separada do banco operacional.
- São dois brokers para operar. Num ambiente gerenciado, isso vira Azure Service Bus + Event Hubs (que fala o protocolo Kafka), e o MassTransit suporta os dois.
- A entrega no Kafka é *at-least-once*. A deduplicação fica no consumidor, pelo par partição/offset.

## Alternativas consideradas

- **Só RabbitMQ, com uma fila de auditoria:** funciona, mas mensagens consumidas somem. Reprocessar o histórico exigiria guardar tudo em outro lugar.
- **Só Kafka:** perde as filas com retry e DLQ por consumidor, que o MassTransit dá de graça sobre RabbitMQ.

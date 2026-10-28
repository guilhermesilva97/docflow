# ADR 0002: a API é dona do agregado e os workers são stateless

**Status:** aceita

## Contexto

O escopo pedia uma Ingestion API, uma Review API, um Extraction Worker e um Validation Worker. A pergunta era: quem pode escrever no documento?

## Decisão

- **`DocFlow.Api` concentra ingestão, revisão e orquestração (saga).** As três operam sobre o mesmo agregado `Document`, o mesmo bounded context, e precisam do mesmo banco e da mesma transação de outbox. Separá-las em três deployables significaria três serviços escrevendo na mesma tabela, que é o anti-padrão de "banco compartilhado".
- **Extraction e Validation são workers sem estado e sem banco.** Recebem um comando com tudo que precisam (o nome do blob, ou os campos extraídos) e respondem com um evento. Quem grava o resultado no agregado é a saga, na API.
- **Audit tem o próprio banco** (`DocFlowAudit`) e só lê do Kafka.

## Consequências

- Há um único lugar que muda o estado do documento. Concorrência é resolvida com `RowVersion` (otimista) no agregado e na saga.
- Os workers escalam horizontalmente sem coordenação. O worker de extração, que chama o LLM, é o candidato natural a ter mais réplicas. `Extraction__ConcurrentMessageLimit` controla quantas chamadas simultâneas cada réplica faz.
- Se o volume de revisão crescer, dá para separar a Review API depois, desde que ela continue falando com o agregado por mensagens.

## Alternativas consideradas

- **Cada worker grava no banco:** menos mensagens, mas espalha regra de negócio e transações pelos serviços.
- **Três APIs separadas:** mais "microserviços" no papel, mas com acoplamento pelo banco. É pior que um serviço coeso.

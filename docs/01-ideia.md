# 01 - Ideia

## O problema

Toda empresa que paga fornecedores recebe notas fiscais em PDF, e alguém precisa digitar os dados no sistema financeiro: emitente, CNPJ, número, datas, itens e total. É um trabalho repetitivo, sujeito a erro de digitação, e os erros caros (CNPJ errado, total que não bate, nota paga duas vezes) só aparecem depois.

Já trabalhei com integrações e automações de backoffice em que o gargalo era exatamente esse: o sistema estava pronto, mas a entrada de dados era manual. O DocFlow é a minha resposta arquitetural para isso, com dados fictícios e sem nenhum código de cliente.

## O que eu queria provar

1. Que um pipeline de documentos pode ser **orientado a eventos e confiável**: nada se perde, nada é processado duas vezes e qualquer etapa pode cair e voltar.
2. Que IA entra como **uma etapa do fluxo**, não como o fluxo inteiro: o LLM extrai, as regras de negócio conferem e uma pessoa decide quando os dois não bastam.
3. Que dá para **medir** a qualidade da extração, comparando o LLM com um baseline, em vez de confiar na demo.
4. Que DDD e Clean Architecture cabem em .NET moderno sem cerimônia: agregado pequeno, casos de uso explícitos e infraestrutura plugável.

## Usuários

| Persona | Necessidade |
|---|---|
| Analista do contas a pagar | Enviar PDFs e só olhar os que precisam de atenção |
| Revisor | Ver o PDF ao lado dos campos, corrigir e aprovar em segundos |
| Auditoria / compliance | Saber quem aprovou o quê, quando, e por qual caminho |
| Time de engenharia | Trocar o modelo de IA com segurança, guiado por métricas |

## Escopo

**Dentro**

- Upload de PDF e geração de notas fictícias com gabarito.
- Extração por LLM (saída estruturada) com fallback heurístico.
- Validação:
  - dígito verificador de CNPJ;
  - soma dos itens igual ao total;
  - quantidade × preço igual ao subtotal;
  - datas coerentes;
  - campos obrigatórios.
- Detecção de nota duplicada (mesmo emitente e número já aprovados).
- Revisão humana com correção dos campos.
- Atualização da tela ao vivo.
- Trilha de auditoria imutável.
- Evals de acurácia por campo e por layout.

**Fora (por enquanto)**

- OCR de PDFs digitalizados (hoje viram revisão manual com motivo claro).
- Autenticação e perfis de acesso.
- Integração com ERP ou pagamento.
- Leitura do XML da NF-e: o foco é o PDF, que é onde a IA faz diferença.

## Requisitos não funcionais

| Requisito | Como é atendido |
|---|---|
| Confiabilidade | Transactional outbox, inbox, retry exponencial e DLQ |
| Idempotência | Inbox por `MessageId`, `Ignore` na saga, transições validadas no agregado, dedupe por offset na auditoria |
| Escalabilidade | Workers stateless e com concorrência configurável |
| Rastreabilidade | Histórico no agregado e eventos em Kafka gravados num banco de auditoria separado |
| Resiliência da IA | Fallback heurístico e humano no loop por confiança |
| Portabilidade | Tudo em containers; LLM configurável por variável de ambiente |

## Critérios de sucesso

- Uma nota válida no layout conhecido é aprovada sozinha, sem LLM, em poucos segundos.
- Notas com total errado, CNPJ inválido ou duplicadas nunca são aprovadas automaticamente.
- A pipeline prova o fluxo de ponta a ponta com a stack real (SQL Server, RabbitMQ, Kafka, Azurite).
- Os evals mostram, por layout, onde o LLM supera a heurística.

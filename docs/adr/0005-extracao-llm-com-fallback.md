# ADR 0005: extração por LLM com saída estruturada, fallback heurístico e humano no loop

**Status:** aceita

## Contexto

Notas fiscais chegam com layouts diferentes. Regras fixas (regex) funcionam bem para um layout conhecido e mal para o resto. Um LLM generaliza, mas pode errar, ficar indisponível ou custar caro.

## Decisão

1. **LLM com JSON Schema estrito** (`ChatResponseFormat.CreateJsonSchemaFormat`, `strict: true`) via SDK oficial da OpenAI para .NET, com `Endpoint` configurável. Funciona com OpenAI, Azure OpenAI, Ollama e qualquer API compatível. O modelo devolve os campos e um `confidence` de 0 a 1.
2. **Extrator heurístico** (regex sobre o texto do PdfPig) como baseline e fallback. A confiança dele é a cobertura de campos encontrados, com teto de 0,9.
3. **`FallbackInvoiceExtractor`**: se o LLM falhar (rede, timeout, JSON fora do schema), usa a heurística e marca o método como `heuristic (fallback)`.
4. **Sem LLM configurado** (`Llm__BaseUrl` vazio), o sistema roda só com a heurística. É assim que a pipeline de CI funciona, sem custo.
5. **O humano decide quando a máquina não tem certeza:** documento com problema de validação ou confiança abaixo de `Workflow__ConfidenceThreshold` (0,8) vai para `NeedsReview`.
6. **PDF sem texto** (imagem digitalizada) ou arquivo corrompido vira `DocumentExtractionFailed`, um caminho controlado que leva para revisão, não para a DLQ.

## Consequências

- O sistema nunca para por causa do LLM: no pior caso, mais documentos caem na fila de revisão.
- A confiança autodeclarada pelo modelo é mal calibrada. Por isso ela nunca aprova nada sozinha: as regras de validação (CNPJ, soma dos itens, datas) também precisam passar.
- Os evals (`DocFlow.Evals`) medem a acurácia por campo e por layout, comparando a heurística e o LLM contra o gabarito do gerador. É assim que se decide, com número, se vale trocar de modelo.

## Alternativas consideradas

- **OCR + template por fornecedor:** alto custo de manutenção a cada fornecedor novo.
- **Só LLM, sem fallback:** a indisponibilidade do provedor pararia o fluxo inteiro.
- **Microsoft.Extensions.AI:** abstração boa, mas aqui a porta `IInvoiceExtractor` já isola o fornecedor, e o SDK direto deixa explícito o uso de *structured outputs*.

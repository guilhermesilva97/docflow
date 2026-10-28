using System.Globalization;
using System.Text.Json;
using DocFlow.Application;
using DocFlow.Domain;
using OpenAI.Chat;

namespace DocFlow.Infrastructure.Extraction;

public sealed class LlmOptions
{
    public const string Section = "Llm";

    public string? BaseUrl { get; set; }
    public string ApiKey { get; set; } = "ollama";
    public string Model { get; set; } = "qwen2.5:7b";
    public int TimeoutSeconds { get; set; } = 120;
    public float? Temperature { get; set; }
    public string? ReasoningEffort { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl);
}

public sealed class OpenAIInvoiceExtractor(ChatClient client, LlmOptions settings) : IInvoiceExtractor
{
    private const int MaxInputCharacters = 12_000;

    private const string SystemPrompt = """
        Você extrai dados de notas fiscais de serviço brasileiras a partir do texto de um PDF.
        Regras:
        - Emitente é quem presta o serviço (também chamado de fornecedor ou prestador).
        - Destinatário é quem contrata (também chamado de cliente ou tomador).
        - CNPJ: devolva apenas os 14 dígitos, sem pontuação.
        - Datas no formato YYYY-MM-DD.
        - Valores numéricos com ponto como separador decimal, sem símbolo de moeda.
        - Copie o número da nota exatamente como aparece, sem prefixos como "NF" ou "Nº".
        - Use null quando um campo não existir no texto. Nunca invente valores.
        - confidence: de 0 a 1, o quanto você tem certeza de que todos os campos estão corretos.
        """;

    private static readonly BinaryData Schema = BinaryData.FromString("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["issuer_name", "issuer_cnpj", "recipient_name", "recipient_cnpj", "number",
                       "issue_date", "due_date", "items", "total", "confidence"],
          "properties": {
            "issuer_name": { "type": ["string", "null"] },
            "issuer_cnpj": { "type": ["string", "null"] },
            "recipient_name": { "type": ["string", "null"] },
            "recipient_cnpj": { "type": ["string", "null"] },
            "number": { "type": ["string", "null"] },
            "issue_date": { "type": ["string", "null"] },
            "due_date": { "type": ["string", "null"] },
            "total": { "type": ["number", "null"] },
            "confidence": { "type": "number" },
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["description", "quantity", "unit_price", "total"],
                "properties": {
                  "description": { "type": "string" },
                  "quantity": { "type": "number" },
                  "unit_price": { "type": "number" },
                  "total": { "type": "number" }
                }
              }
            }
          }
        }
        """);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Name => "llm";

    public async Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken)
    {
        var options = BuildOptions(settings);
        ChatMessage[] messages =
        [
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(text.Length > MaxInputCharacters ? text[..MaxInputCharacters] : text),
        ];

        ChatCompletion completion = await client.CompleteChatAsync(messages, options, cancellationToken);
        var json = completion.Content.FirstOrDefault()?.Text
            ?? throw new ExtractionFailedException("O modelo não devolveu conteúdo.");

        LlmInvoice parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LlmInvoice>(json, Json)
                ?? throw new ExtractionFailedException("Resposta vazia do modelo.");
        }
        catch (JsonException error)
        {
            throw new ExtractionFailedException("O modelo devolveu JSON fora do schema.", error);
        }

        return new ExtractionResult(parsed.ToDomain(), Math.Clamp(parsed.Confidence, 0, 1), Name);
    }

    public static ChatCompletionOptions BuildOptions(LlmOptions settings)
    {
        var options = new ChatCompletionOptions
        {
            Temperature = settings.Temperature,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("invoice_extraction", Schema, jsonSchemaIsStrict: true),
        };
        if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort))
        {
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(settings.ReasoningEffort);
        }

        return options;
    }

    private sealed record LlmItem(string Description, decimal Quantity, decimal UnitPrice, decimal Total);

    private sealed record LlmInvoice(
        string? IssuerName,
        string? IssuerCnpj,
        string? RecipientName,
        string? RecipientCnpj,
        string? Number,
        string? IssueDate,
        string? DueDate,
        IReadOnlyList<LlmItem>? Items,
        decimal? Total,
        double Confidence)
    {
        public InvoiceData ToDomain() => new(
            IssuerName,
            IssuerCnpj is null ? null : Cnpj.Digits(IssuerCnpj),
            RecipientName,
            RecipientCnpj is null ? null : Cnpj.Digits(RecipientCnpj),
            Number,
            ParseDate(IssueDate),
            ParseDate(DueDate),
            [.. (Items ?? []).Select(i => new InvoiceItem(i.Description, i.Quantity, i.UnitPrice, i.Total))],
            Total);

        private static DateOnly? ParseDate(string? value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }
}

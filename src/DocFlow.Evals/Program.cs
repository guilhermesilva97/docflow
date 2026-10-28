using System.Globalization;
using DocFlow.Application;
using DocFlow.Evals;
using DocFlow.Infrastructure;
using DocFlow.Infrastructure.Extraction;
using DocFlow.Samples;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var perTemplate = configuration.GetValue("EVALS_PER_TEMPLATE", 6);
var minAccuracy = configuration.GetValue("EVALS_MIN_ACCURACY", 0.85);

var services = new ServiceCollection()
    .AddLogging(logging => logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning))
    .AddDocFlowExtraction(configuration)
    .BuildServiceProvider();

var reader = services.GetRequiredService<IPdfTextReader>();
var extractors = new List<IInvoiceExtractor> { services.GetRequiredService<HeuristicInvoiceExtractor>() };
var llm = services.GetService<OpenAIInvoiceExtractor>();
if (llm is not null)
{
    extractors.Add(llm);
}

var samples = new InvoiceSampleGenerator(2026, new DateOnly(2026, 6, 30)).GenerateSet(perTemplate).ToList();
var accuracyByExtractor = new Dictionary<string, double>();

foreach (var extractor in extractors)
{
    var scores = new List<(SampleTemplate Template, IReadOnlyDictionary<string, bool> Fields)>();
    foreach (var sample in samples)
    {
        var text = reader.ReadText(new MemoryStream(sample.Pdf));
        try
        {
            var result = await extractor.ExtractAsync(text, CancellationToken.None);
            scores.Add((sample.Template, FieldScorer.Score(sample.Truth, result.Invoice)));
        }
        catch (Exception error)
        {
            Console.WriteLine($"  [{extractor.Name}] {sample.FileName}: {error.Message}");
            scores.Add((sample.Template, FieldScorer.Score(sample.Truth, null)));
        }
    }

    var accuracy = scores.SelectMany(s => s.Fields.Values).Average(ok => ok ? 1.0 : 0.0);
    accuracyByExtractor[extractor.Name] = accuracy;

    Console.WriteLine();
    Console.WriteLine($"Extrator: {extractor.Name} ({samples.Count} notas)");
    Console.WriteLine($"{"campo",-16}{string.Join(string.Empty, Enum.GetNames<SampleTemplate>().Select(t => $"{t,10}"))}{"geral",10}");
    foreach (var field in FieldScorer.Fields)
    {
        var perTemplateAccuracy = Enum.GetValues<SampleTemplate>()
            .Select(t => Percent(scores.Where(s => s.Template == t).Average(s => s.Fields[field] ? 1.0 : 0.0)));
        var overall = Percent(scores.Average(s => s.Fields[field] ? 1.0 : 0.0));
        Console.WriteLine($"{field,-16}{string.Join(string.Empty, perTemplateAccuracy.Select(p => $"{p,10}"))}{overall,10}");
    }

    Console.WriteLine($"Acurácia por campo: {Percent(accuracy)}");
}

var gated = llm is not null ? "llm" : "heuristic";
var gatedAccuracy = accuracyByExtractor[gated];
Console.WriteLine();
Console.WriteLine($"Gate: {gated} = {Percent(gatedAccuracy)} (mínimo {Percent(minAccuracy)})");
if (llm is null)
{
    Console.WriteLine("Llm__BaseUrl não configurado: apenas o baseline heurístico foi avaliado.");
}

return gatedAccuracy >= minAccuracy ? 0 : 1;

static string Percent(double value) => value.ToString("P0", CultureInfo.GetCultureInfo("pt-BR"));

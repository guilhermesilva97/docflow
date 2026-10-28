using DocFlow.Application;
using DocFlow.Domain;

namespace DocFlow.UnitTests;

public static class TestData
{
    public static readonly DateOnly Today = new(2026, 6, 30);
    public static readonly DateTimeOffset Now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);

    public static InvoiceData ValidInvoice() => new(
        "Aurora Tecnologia Ltda",
        "11.222.333/0001-81",
        "Lumen Logística Ltda",
        Cnpj.WithCheckDigits("123456780001"),
        "000123",
        new DateOnly(2026, 6, 1),
        new DateOnly(2026, 7, 1),
        [
            new InvoiceItem("Consultoria", 10, 150m, 1500m),
            new InvoiceItem("Suporte", 2, 350.50m, 701m),
        ],
        2201m);
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class InMemoryDocuments : IDocumentRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Document> _documents = [];

    public int SaveCount { get; private set; }

    public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.GetValueOrDefault(id));

    public Task<IReadOnlyList<Document>> ListAsync(DocumentStatus? status, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Document>>(
            [.. _documents.Values.Where(d => status is null || d.Status == status).Take(take)]);

    public Task<bool> ExistsApprovedWithKeyAsync(string duplicateKey, Guid exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.Values.Any(d =>
            d.DuplicateKey == duplicateKey && d.Status == DocumentStatus.Approved && d.Id != exceptId));

    public void Add(Document document) => _documents[document.Id] = document;

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

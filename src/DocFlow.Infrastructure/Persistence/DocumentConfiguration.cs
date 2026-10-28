using System.Text.Json;
using DocFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DocFlow.Infrastructure.Persistence;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.FileName).HasMaxLength(260);
        builder.Property(d => d.BlobName).HasMaxLength(300);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ExtractionMethod).HasMaxLength(40);
        builder.Property(d => d.DuplicateKey).HasMaxLength(60);
        builder.Property(d => d.ReviewedBy).HasMaxLength(120);
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.Property(d => d.Invoice).HasColumnName("InvoiceJson").AsJson();
        builder.Property(d => d.Issues).HasColumnName("IssuesJson").AsJson();
        builder.Property(d => d.History).HasColumnName("HistoryJson").AsJson();

        builder.Ignore(d => d.IsFinal);
        builder.Ignore(d => d.LastChange);

        builder.HasIndex(d => new { d.DuplicateKey, d.Status });
        builder.HasIndex(d => d.ReceivedAt);
    }
}

internal static class JsonColumns
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> AsJson<T>(this PropertyBuilder<T> property) =>
        property.HasConversion(
            new ValueConverter<T, string>(
                value => JsonSerializer.Serialize(value, Options),
                json => JsonSerializer.Deserialize<T>(json, Options)!),
            new ValueComparer<T>(
                (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
                value => JsonSerializer.Serialize(value, Options).GetHashCode(),
                value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!));
}

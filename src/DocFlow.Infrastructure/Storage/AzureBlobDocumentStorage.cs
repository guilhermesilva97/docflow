using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DocFlow.Application;

namespace DocFlow.Infrastructure.Storage;

public sealed class BlobStorageOptions
{
    public const string Section = "BlobStorage";

    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string Container { get; set; } = "documents";
}

public sealed class AzureBlobDocumentStorage(BlobContainerClient container) : IDocumentStorage
{
    public async Task SaveAsync(string blobName, Stream content, CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" } },
            cancellationToken);
    }

    public async Task<Stream> OpenAsync(string blobName, CancellationToken cancellationToken)
    {
        var download = await container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
        var buffer = new MemoryStream();
        await download.Value.Content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    public Task EnsureContainerAsync(CancellationToken cancellationToken) =>
        container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
}

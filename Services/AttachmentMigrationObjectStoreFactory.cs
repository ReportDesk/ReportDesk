using System.Security.Cryptography;

namespace ReportSystem.Services;

public static class AttachmentMigrationObjectStoreFactory
{
    public static IAttachmentMigrationObjectStore Create(AttachmentDeliveryCompatibilityOptions options)
    {
        if (!options.EnableMigrationExecution)
            return new DisabledAttachmentMigrationObjectStore();

        if (string.IsNullOrWhiteSpace(options.MigrationLocalObjectStorePath))
        {
            throw new InvalidOperationException(
                "AttachmentDelivery:MigrationLocalObjectStorePath is required when migration execution is enabled.");
        }

        return new LocalAttachmentMigrationObjectStore(options.MigrationLocalObjectStorePath);
    }
}

/// <summary>
/// A migration-only local object-store adapter. It copies and hashes files without
/// deleting the historical source and never opens a live R2 client.
/// </summary>
public sealed class LocalAttachmentMigrationObjectStore : IAttachmentMigrationObjectStore
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    public LocalAttachmentMigrationObjectStore(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
    }

    public async Task<string> CopyFromLocalAsync(
        string sourcePath,
        string destinationKey,
        string expectedSha256,
        CancellationToken ct)
    {
        var destinationPath = ResolveDestinationPath(destinationKey);
        if (File.Exists(destinationPath))
            return await ComputeChecksumAsync(destinationPath, ct);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, ct);
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            // A concurrent/resumed migration may have written the same key.
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return await ComputeChecksumAsync(destinationPath, ct);
    }

    private string ResolveDestinationPath(string destinationKey)
    {
        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new InvalidOperationException("Migration destination key is required.");

        var relativePath = destinationKey.Replace('/', Path.DirectorySeparatorChar);
        var destinationPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        if (!destinationPath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Migration destination key escaped the configured local object-store root.");

        return destinationPath;
    }

    private static async Task<string> ComputeChecksumAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
}

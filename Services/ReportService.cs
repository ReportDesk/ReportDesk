using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

namespace ReportSystem.Services;

public sealed class ReportService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",
        ".bmp"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/bmp"
    };

    private const long MaxImageSizeBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly IImageVirusScanner _scanner;

    public ReportService(AppDbContext db, IWebHostEnvironment environment, IImageVirusScanner scanner)
    {
        _db = db;
        _environment = environment;
        _scanner = scanner;
    }

    public async Task<int> CreateReportAsync(ReportSubmissionRequest request, CancellationToken cancellationToken)
    {
        if (!ReportTypes.IsValid(request.ReportType))
        {
            throw new InvalidOperationException("Invalid report type.");
        }

        var reportType = ReportTypes.Normalize(request.ReportType);
        var now = DateTime.Now;
        var report = new MasterReport
        {
            ReportType = reportType,
            ReportTitle = request.Title.Trim(),
            ReportedBy = request.Name.Trim(),
            Description = request.Description.Trim(),
            SubmissionDate = now,
            Area = request.Area?.Trim() ?? string.Empty,
            Status = "Open",
            UpdatedDate = now,
            UpdatedBy = 0,
            MachineId = reportType == ReportTypes.Machine ? request.MachineId ?? 0 : 0
        };

        _db.MasterReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);

        await SaveAttachmentIfPresentAsync(report.ReportId, null, request.Photo, request.Name, "report", cancellationToken);
        await SaveAttachmentIfPresentAsync(report.ReportId, null, request.BeforePhoto, request.Name, "before", cancellationToken);
        await SaveAttachmentIfPresentAsync(report.ReportId, null, request.AfterPhoto, request.Name, "after", cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return report.ReportId;
    }

    public async Task UpdateFollowUpAsync(int reportId, FollowUpRequest request, CancellationToken cancellationToken)
    {
        var report = await _db.MasterReports.FirstOrDefaultAsync(x => x.ReportId == reportId, cancellationToken);
        if (report is null)
        {
            throw new KeyNotFoundException("Report was not found.");
        }

        var now = DateTime.Now;
        report.Status = request.Status.Trim();
        report.UpdatedDate = now;

        var followUp = new MasterFollowUp
        {
            ReportId = reportId,
            FollowUpDate = now,
            ResponsiblePerson = request.Name.Trim(),
            MaterialsUsed = request.MaterialsUsed?.Trim() ?? string.Empty,
            Description = request.Description.Trim(),
            UpdatedBy = 0
        };

        _db.MasterFollowUps.Add(followUp);
        await _db.SaveChangesAsync(cancellationToken);

        await SaveAttachmentIfPresentAsync(reportId, followUp.FollowUpId, request.Photo, request.Name, "follow-up", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteReportAsync(int reportId, CancellationToken cancellationToken)
    {
        var report = await _db.MasterReports.FirstOrDefaultAsync(x => x.ReportId == reportId, cancellationToken);
        if (report is null)
        {
            return;
        }

        var followUps = await _db.MasterFollowUps.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken);
        var attachments = await _db.Attachments.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken);

        _db.Attachments.RemoveRange(attachments);
        _db.MasterFollowUps.RemoveRange(followUps);
        _db.MasterReports.Remove(report);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveAttachmentIfPresentAsync(int reportId, int? followUpId, IFormFile? file, string uploadedBy, string purpose, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return;
        }

        await ValidateImageAsync(file, cancellationToken);

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "reports", reportId.ToString());
        Directory.CreateDirectory(uploadRoot);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safeFileName = $"{purpose}-{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, safeFileName);

        await using (var stream = File.Create(physicalPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        _db.Attachments.Add(new Attachment
        {
            ReportId = reportId,
            FollowUpId = followUpId ?? 0,
            FileName = Path.GetFileName(file.FileName),
            FilePath = $"/uploads/reports/{reportId}/{safeFileName}",
            FileSize = checked((int)file.Length),
            FileType = file.ContentType,
            UploadDate = DateTime.Now,
            UploadedBy = uploadedBy
        });
    }

    private async Task ValidateImageAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > MaxImageSizeBytes)
        {
            throw new InvalidOperationException("Image size must be 5 MB or smaller.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension) || !AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("Only JPG, PNG, GIF, WebP, or BMP images are allowed.");
        }

        var scanResult = await _scanner.ScanAsync(file, cancellationToken);
        if (!scanResult.IsClean)
        {
            throw new InvalidOperationException(scanResult.Message ?? "Image failed virus scan.");
        }
    }
}

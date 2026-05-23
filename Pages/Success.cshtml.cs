using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

namespace ReportSystem.Pages
{
    public class SuccessModel : PageModel
    {
        private readonly AppDbContext _db;

        public SuccessModel(AppDbContext db)
        {
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public int? ReportId { get; set; }

        public string ReportedBy { get; private set; } = string.Empty;
        public string SubmissionDateText { get; private set; } = string.Empty;
        public string ThankYouMessage { get; private set; } = string.Empty;
        public string ContextLabel { get; private set; } = string.Empty;
        public string ContextValue { get; private set; } = string.Empty;
        public string DescriptionLabel { get; private set; } = "Description";
        public string Description { get; private set; } = string.Empty;
        public string Description2Label { get; private set; } = string.Empty;
        public string Description2 { get; private set; } = string.Empty;
        public bool HasDescription2 => !string.IsNullOrWhiteSpace(Description2);

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!ReportId.HasValue || ReportId.Value <= 0)
                return NotFound();

            var report = await _db.MasterReports
                .AsNoTracking()
                .Include(r => r.Machine)
                .FirstOrDefaultAsync(r => r.ReportId == ReportId.Value, ct);

            if (report == null)
                return NotFound();

            MapReport(report);
            return Page();
        }

        private void MapReport(MasterReport report)
        {
            ReportedBy = report.ReportedBy;
            SubmissionDateText = report.SubmissionDate.ToString("dd MMM yyyy, HH:mm");
            Description = report.Description;

            switch (report.ReportType?.Trim().ToLowerInvariant())
            {
                case "machine":
                    ThankYouMessage = "Terima kasih sudah melaporkan kerusakan mesih.";
                    ContextLabel = "Name Mesin";
                    ContextValue = FormatMachine(report.Machine);
                    DescriptionLabel = "Kondisi Mesin";
                    break;

                case "nearmiss":
                    ThankYouMessage = "Terimakasih sudah melaporkan kondisi potensi bahaya";
                    ContextLabel = "Area";
                    ContextValue = report.Area;
                    DescriptionLabel = "Kondisi Bahaya";
                    break;

                case "suggestion":
                    ThankYouMessage = "Terimakasih sudah memberikan sumbang saran";
                    ContextLabel = "Perihal";
                    ContextValue = !string.IsNullOrWhiteSpace(report.About)
                        ? report.About
                        : report.Area;
                    DescriptionLabel = "Saran";
                    break;

                case "kaizen":
                    ThankYouMessage = "Terimakasih sudah berkontribusi dalam perbaikan berkesinambungan";
                    ContextLabel = "Area";
                    ContextValue = report.Area;
                    DescriptionLabel = "Kondisi Sebelum";
                    Description2Label = "Kondisi Sesudah";
                    Description2 = report.Description2 ?? string.Empty;
                    break;

                default:
                    ThankYouMessage = "Terimakasih sudah mengirimkan laporan.";
                    break;
            }
        }

        private static string FormatMachine(MasterMachine? machine)
        {
            if (machine == null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(machine.MachineArea)
                ? machine.MachineName
                : $"{machine.MachineName} - {machine.MachineArea}";
        }
    }
}

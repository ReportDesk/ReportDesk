using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Services;

namespace ReportSystem.Pages.Admin
{
    public class ReportDetailModel : PageModel
    {
        private readonly AppDbContext _db;

        public ReportDetailModel(AppDbContext db)
        {
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public int ReportId { get; set; }

        public string ReportType { get; private set; } = string.Empty;
        public string ReportTypeFriendly { get; private set; } = string.Empty;
        public string TypeBadgeClass { get; private set; } = "bg-secondary";

        public string ReportTitle { get; private set; } = string.Empty;
        public string ReportedBy { get; private set; } = string.Empty;
        public string Area { get; private set; } = string.Empty;
        public string MachineName { get; private set; } = string.Empty;
        public string ReportDateText { get; private set; } = string.Empty;
        public string ReportArea { get; private set; } = string.Empty;
        public string ReportDescription { get; private set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Status is required.")]
        public string SelectedStatus { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Name is required.")]
        public string FollowUpName { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Follow up description is required.")]
        public string FollowUpDescription { get; set; } = string.Empty;

        [BindProperty]
        public IFormFile? FollowUpPhoto { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            //var redirect = RedirectIfNotAdmin();
            //if (redirect is not null)
            //{
            //    return redirect;
            //}

            //return await LoadReportAsync(cancellationToken) ? Page() : NotFound();
            return Page();
        }

        //public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        //{
        //    var redirect = RedirectIfNotAdmin();
        //    if (redirect is not null)
        //    {
        //        return redirect;
        //    }

        //    if (!await LoadReportAsync(cancellationToken))
        //    {
        //        return NotFound();
        //    }

        //    if (!ModelState.IsValid)
        //    {
        //        return Page();
        //    }

        //    try
        //    {
        //        await _reportService.UpdateFollowUpAsync(ReportId, new FollowUpRequest
        //        {
        //            Status = SelectedStatus,
        //            Name = FollowUpName,
        //            Description = FollowUpDescription,
        //            Photo = FollowUpPhoto
        //        }, cancellationToken);
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        ModelState.AddModelError(string.Empty, ex.Message);
        //        return Page();
        //    }

        //    SuccessMessage = "Follow up saved.";
        //    return RedirectToPage("/Admin/ReportDetail", new { reportId = ReportId });
        //}

        //private async Task<bool> LoadReportAsync(CancellationToken cancellationToken)
        //{
        //    var report = await _db.MasterReports
        //        .AsNoTracking()
        //        .FirstOrDefaultAsync(x => x.ReportId == ReportId, cancellationToken);

        //    if (report is null)
        //    {
        //        return false;
        //    }

        //    ReportDateText = report.SubmissionDate.ToString("yyyy-MM-dd HH:mm");
        //    ReportTitle = report.ReportTitle;
        //    ReportType = report.ReportType;
        //    ReportedBy = report.ReportedBy;
        //    ReportArea = report.Area;
        //    ReportDescription = report.Description;
        //    SelectedStatus = string.IsNullOrWhiteSpace(SelectedStatus) ? report.Status : SelectedStatus;

        //    return true;
        //}
    }
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages.Admin
{
    public class ReportDetailModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ReportDetailModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // -- Route Parameter

        [BindProperty(SupportsGet = true)]
        public int ReportId { get; set; }

        // -- Report Display Properties

        public string ReportType { get; private set; } = string.Empty;
        public string ReportTypeFriendly { get; private set; } = string.Empty;
        public string TypeBadgeClass { get; private set; } = "bg-secondary";

        public string ReportTitle { get; private set; } = string.Empty;
        public string ReportedBy { get; private set; } = string.Empty;
        public string Area { get; private set; } = string.Empty;
        public string MachineName { get; private set; } = string.Empty;
        public string ReportDateText { get; private set; } = string.Empty;
        public string ReportDescription { get; private set; } = string.Empty;
        public string CurrentStatus { get; private set; } = string.Empty;

        // -- Follow up History

        public List<FollowUpViewModel> FollowUps { get; private set; } = new();

        // -- Feedback

        [TempData]
        public string? SuccessMessage { get; set; }

        // -- Handler

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("InternalApi");
                var response = await client.GetAsync($"/api/maangement/reports/{ReportId}");

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return NotFound();

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var detail = JsonSerializer.Deserialize<ReportDetailDto>(json, options);

                if (detail is null)
                    return NotFound();

                ReportType          = detail.ReportType ?? string.Empty;
                ReportTitle         = detail.Title ?? string.Empty;
                ReportedBy          = detail.ReportedBy ?? string.Empty;
                Area                = detail.Area ?? string.Empty;
                MachineName         = detail.MachineName ?? string.Empty;
                ReportDateText      = detail.SubmissionDate;
                ReportDescription   = detail.Description ?? string.Empty;
                CurrentStatus       = detail.Status ?? string.Empty;
                FollowUps           = detail.FollowUps ?? new();

                ReportTypeFriendly = ReportType switch
                {
                    "Machine"       => "Machine Report",
                    "NearMiss"      => "Near-Miss Report",
                    "Suggestion"    => "Suggestion",
                    "Kaizen"        => "Kaizen Reprot",
                    _               => ReportType
                };

                TypeBadgeClass = ReportType switch
                {
                    "Machine"       => "bg-success",
                    "NearMiss"      => "bg-warning text-dark",
                    "Suggestion"    => "bg-info text-dark",
                    "Kaizen"        => "bg-primary",
                    _               => "bg-secondary"
                };
            }
            catch(Exception ex)
            {
                return StatusCode(500);
            }

            return Page();
        }

        private sealed class ReportDetailDto
        {
            public int ReportId { get; set; }
            public string? ReportType { get; set; }
            public string? Title { get; set; }
            public string? ReportedBy { get; set; }
            public string? Area { get; set; }
            public string? MachineName { get; set; }
            public string SubmissionDate { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? Status { get; set; }
            public List<FollowUpViewModel>? Followups { get; set; }
        }

        public sealed class FollowUpViewModel
        {
            public int FollowUpId { get; set; }
            public string ResponsiblePerson { get; set; } = string.Empty;
            public string FollowUpDateText { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string MaterialsUsed { get; set; } = string.Empty;
            public List<AttachmentViewModel> Attachments { get; set; } = new();
        }

        public sealed class AttachmentViewModel
        {
            public int AttachmentId { get; set; }
            public string Filename { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
        }
    }
}

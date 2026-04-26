using ReportSystem.Services;

namespace ReportSystem.Pages.Report;

public sealed class SuggestionModel : ReportPageModel
{
    public SuggestionModel(AppDbContext db, ReportService reportService) : base(db, reportService)
    {
    }

    public override string ReportType => ReportTypes.Suggestion;
    public override string PageHeading => "Suggestion";
    public override string PageDescription => "Melaporkan ide perbaikan di segala bidang.";
}

using ReportSystem.Services;

namespace ReportSystem.Pages.Report;

public sealed class KaizenModel : ReportPageModel
{
    public KaizenModel(AppDbContext db, ReportService reportService) : base(db, reportService)
    {
    }

    public override string ReportType => ReportTypes.Kaizen;
    public override string PageHeading => "Kaizen Report";
    public override string PageDescription => "Melaporkan kaizen yang sudah selesai dilakukan.";
}

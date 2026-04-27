using ReportSystem.Services;

namespace ReportSystem.Pages.Report;

public sealed class NearMissModel : ReportPageModel
{
    public NearMissModel(AppDbContext db, ReportService reportService) : base(db, reportService)
    {
    }

    public override string ReportType => ReportTypes.NearMiss;
    public override string PageHeading => "Near-Miss Report";
    public override string PageDescription => "Melaporkan temuan kondisi yang berpotensi bahaya.";
}

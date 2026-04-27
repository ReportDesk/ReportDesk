using ReportSystem.Services;

namespace ReportSystem.Pages.Report;

public sealed class MachineModel : ReportPageModel
{
    public MachineModel(AppDbContext db, ReportService reportService) : base(db, reportService)
    {
    }

    public override string ReportType => ReportTypes.Machine;
    public override string PageHeading => "Machine Report";
    public override string PageDescription => "Melaporkan mesin atau peralatan yang rusak.";
}

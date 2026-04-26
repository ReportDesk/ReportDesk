using Microsoft.AspNetCore.Mvc;
using ReportSystem.Services;

namespace ReportSystem.Data.API;

[ApiController]
[Route("api/reports")]
public sealed class ReportController : ControllerBase
{
    private readonly ReportService _reportService;

    public ReportController(ReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] ReportSubmissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var reportId = await _reportService.CreateReportAsync(request, cancellationToken);
            return Created($"/Admin/ReportDetail/{reportId}", new { reportId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{reportId:int}/follow-up")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> FollowUp(int reportId, [FromForm] FollowUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _reportService.UpdateFollowUpAsync(reportId, request, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{reportId:int}")]
    public async Task<IActionResult> Delete(int reportId, CancellationToken cancellationToken)
    {
        await _reportService.DeleteReportAsync(reportId, cancellationToken);
        return NoContent();
    }
}

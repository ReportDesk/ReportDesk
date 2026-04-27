using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ReportSystem.Services;

public sealed class ReportSubmissionRequest
{
    [Required]
    public string ReportType { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Area { get; set; }

    public int? MachineId { get; set; }

    [Required]
    [StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    public IFormFile? Photo { get; set; }
    public IFormFile? BeforePhoto { get; set; }
    public IFormFile? AfterPhoto { get; set; }
}

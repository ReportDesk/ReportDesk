using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ReportSystem.Services;

public sealed class FollowUpRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? MaterialsUsed { get; set; }

    public IFormFile? Photo { get; set; }
}

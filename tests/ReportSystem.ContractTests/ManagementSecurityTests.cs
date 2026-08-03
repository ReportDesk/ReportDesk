using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using ReportSystem.Controllers.API;
using ReportSystem.Filters;
using ReportSystem.Models;
using Xunit;

namespace ReportSystem.ContractTests;

public sealed class ManagementSecurityTests : IClassFixture<ManagementSecurityApplicationFactory>
{
    private readonly ManagementSecurityApplicationFactory _factory;
    public ManagementSecurityTests(ManagementSecurityApplicationFactory factory) { _factory = factory; _factory.SeedAsync().GetAwaiter().GetResult(); }

    [Fact]
    public void ManagementBoundaryRequiresAdminSessionAndMutationAntiforgery()
    {
        var authorization = Assert.Single(typeof(ManagementController).GetCustomAttributes<SessionAuthorizeAttribute>());
        Assert.Equal(["Admin"], authorization.AllowedRoles);
        Assert.NotNull(GetAction(nameof(ManagementController.SaveFollowUp)).GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(GetAction(nameof(ManagementController.UpdateStatus)).GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void PrivateAttachmentReadMatchesAuthorizedReportPageRoles()
    {
        var authorization = Assert.Single(typeof(AttachmentController).GetCustomAttributes<SessionAuthorizeAttribute>());
        Assert.Equal(["Admin", "Machine", "NearMiss", "Suggestion", "Kaizen"], authorization.AllowedRoles);
    }

    [Theory]
    [InlineData("viewer", 731)]
    [InlineData("machine", 742)]
    public async Task PrivateAttachmentRead_DeniesSessionRoleForForeignReportType(string username, int attachmentId)
    {
        await SeedAttachmentDeliveryFixturesAsync();

        using var browser = _factory.CreateBrowser();
        await browser.LoginAsync(username);
        using var response = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{attachmentId}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("machine", 731)]
    [InlineData("viewer", 742)]
    [InlineData("admin", 731)]
    public async Task PrivateAttachmentRead_AllowsRoleAuthorizedForAttachmentReport(string username, int attachmentId)
    {
        await SeedAttachmentDeliveryFixturesAsync();

        using var browser = _factory.CreateBrowser();
        await browser.LoginAsync(username);
        using var response = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{attachmentId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task HistoricalLocalAttachmentPath_RemainsRedirectedWithoutPrivateScopeLookup()
    {
        await SeedAttachmentDeliveryFixturesAsync();

        using var browser = _factory.CreateBrowser();
        await browser.LoginAsync("viewer");
        using var response = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/attachments/751"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/uploads/followups/historical.webp", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AnonymousManagementMutationReturnsRepositoryUnauthorizedResponse()
    {
        using var browser = _factory.CreateBrowser(); using var response = await browser.SendAsync(UpdateStatusRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.Contains("Unauthorized. Please log in.", body); Assert.Contains("/Login", body);
    }

    [Fact]
    public async Task DisallowedManagementRoleReturnsForbidden()
    {
        using var browser = _factory.CreateBrowser(); await browser.LoginAsync("viewer"); using var response = await browser.SendAsync(UpdateStatusRequest());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Contains("Forbidden. You do not have permission.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminMutationWithoutAntiforgeryTokenReturnsBadRequest()
    {
        using var browser = _factory.CreateBrowser(); await browser.LoginAsync("admin"); using var response = await browser.SendAsync(UpdateStatusRequest()); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminMutationWithTokenSucceedsForJsonAndMultipartRequests()
    {
        using var browser = _factory.CreateBrowser(); await browser.LoginAsync("admin"); var token = await browser.GetAntiforgeryTokenAsync();
        using var jsonRequest = UpdateStatusRequest(); jsonRequest.Headers.Add("X-CSRF-TOKEN", token); using var jsonResponse = await browser.SendAsync(jsonRequest); Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
        token = await browser.GetAntiforgeryTokenAsync();
        using var multipart = new MultipartFormDataContent { { new StringContent("73"), "reportId" }, { new StringContent("On Progress"), "status" }, { new StringContent("Fixture owner"), "responsiblePerson" }, { new StringContent("Fixture follow-up"), "description" }, { new StringContent("Fixture material"), "materialsUsed" }, { new StringContent(token), "__RequestVerificationToken" } };
        using var multipartResponse = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/management/followup/save") { Content = multipart }); Assert.Equal(HttpStatusCode.OK, multipartResponse.StatusCode);
    }

    [Fact]
    public async Task TokenFromAnotherBrowserSessionIsRejected()
    {
        using var admin = _factory.CreateBrowser(); await admin.LoginAsync("admin"); using var forger = _factory.CreateBrowser(); var forgedToken = await forger.GetAntiforgeryTokenAsync();
        using var request = UpdateStatusRequest(); request.Headers.Add("X-CSRF-TOKEN", forgedToken); using var response = await admin.SendAsync(request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage UpdateStatusRequest() => new(HttpMethod.Post, "/api/management/UpdateStatus?id=73") { Content = JsonContent.Create(new { status = "On Progress" }) };
    private static MethodInfo GetAction(string methodName) => typeof(ManagementController).GetMethod(methodName) ?? throw new InvalidOperationException(methodName);

    private async Task SeedAttachmentDeliveryFixturesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.MasterUsers.AnyAsync(user => user.Username == "machine"))
            db.MasterUsers.Add(new MasterUser { UserId = 3, Username = "machine", Fullname = "machine", Email = "machine@test.invalid", Password = BCrypt.Net.BCrypt.HashPassword("Test-password-1!"), Role = "Machine", IsActive = 1, CreatedDate = new DateTime(2026, 1, 1) });

        if (!await db.MasterReports.AnyAsync(report => report.ReportId == 74))
            db.MasterReports.Add(new MasterReport { ReportId = 74, ReportType = "Suggestion", ReportedBy = "Suggestion Fixture", Area = "Fixture Area", Description = "Suggestion fixture", Status = "Open", SubmissionDate = new DateTime(2026, 1, 2) });

        if (!await db.MasterFollowUps.AnyAsync(followUp => followUp.FollowUpId == 174))
            db.MasterFollowUps.Add(new MasterFollowUp { FollowUpId = 174, ReportId = 74, FollowUpDate = new DateTime(2026, 1, 3), ResponsiblePerson = "Fixture", Description = "Fixture follow-up", UpdatedBy = 1 });

        if (!await db.Attachments.AnyAsync(attachment => attachment.AttachmentId == 731))
        {
            db.Attachments.AddRange(
                PrivateAttachment(731, 73),
                new Attachment { AttachmentId = 742, FollowUpId = 174, FileName = "suggestion.webp", FilePath = "r2://reports/74/followups/suggestion.webp", FileType = "image/webp", UploadDate = new DateTime(2026, 1, 3) },
                new Attachment { AttachmentId = 751, ReportId = 73, FileName = "historical.webp", FilePath = "/uploads/followups/historical.webp", FileType = "image/webp", UploadDate = new DateTime(2026, 1, 3) });
        }

        await db.SaveChangesAsync();
    }

    private static Attachment PrivateAttachment(int attachmentId, int reportId) => new()
    {
        AttachmentId = attachmentId,
        ReportId = reportId,
        FileName = "machine.webp",
        FilePath = $"r2://reports/{reportId}/machine.webp",
        FileType = "image/webp",
        UploadDate = new DateTime(2026, 1, 3)
    };
}

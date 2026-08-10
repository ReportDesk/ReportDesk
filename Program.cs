using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Helpers;
using ReportSystem.Services;
using Supabase;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Railway terminates TLS and forwards requests through its reverse proxy. Trust its
// forwarded headers so rate limits are partitioned by the actual client IP address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SupabaseDb")));
builder.Services.AddScoped<Client>(_ =>
    new Client(
        builder.Configuration["Supabase:Url"]!,
        builder.Configuration["Supabase:Key"]!,
        new SupabaseOptions { AutoRefreshToken = true }
    ));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("upload", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
                 ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    options.AddPolicy("reportSubmit", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
                 ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var accountId = builder.Configuration["R2:AccountId"]!;
    var accessKey = builder.Configuration["R2:AccessKeyId"]!;
    var secretKey = builder.Configuration["R2:SecretAccessKey"]!;

    var credentials = new BasicAWSCredentials(accessKey, secretKey);

    var config = new AmazonS3Config
    {
        ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
        ForcePathStyle = true,   // Required for R2
        AuthenticationRegion = "auto",

        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    };

    return new AmazonS3Client(credentials, config);
});

builder.Services.Configure<R2Options>(builder.Configuration.GetSection("R2"));

builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<ReportSystem.Helpers.AppDbConnection>();
builder.Services.AddHttpClient<IEmailService, EmailService>();
builder.Services.AddScoped<IR2StorageService, R2StorageService>();
builder.Services.AddScoped<IFileValidationService, FileValidationService>();
builder.Services.Configure<PhotoValidationOptions>(builder.Configuration.GetSection("PhotoValidation"));
builder.Services.AddScoped<IPhotoCaptureValidationService, PhotoCaptureValidationService>();
builder.Services.AddSingleton<IMachinePhotoCaptureSessionService, MachinePhotoCaptureSessionService>();
builder.Services.AddScoped<IImageProcessingService, ImageProcessingService>();
builder.Services.AddHostedService<TempFileCleanupService>();

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.MapRazorPages();
app.MapControllers();

app.Run();

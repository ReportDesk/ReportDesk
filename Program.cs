using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Helpers;
using Supabase;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
    });

//builder.Services.AddDbContext<AppDbContext>(options =>
//    options.(builder.Configuration.GetConnectionString("SupabaseDb")));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SupabaseDb")));
builder.Services.AddScoped<ReportSystem.Services.ReportService>();
builder.Services.AddScoped<ReportSystem.Services.IImageVirusScanner, ReportSystem.Services.NoOpImageVirusScanner>();
builder.Services.AddScoped<Client>(_ =>
    new Client(
        builder.Configuration["Supabase:Url"]!,
        builder.Configuration["Supabase:Key"]!,
        new SupabaseOptions { AutoRefreshToken = true }
    ));

builder.Services.AddHttpContextAccessor();
var connStr = builder.Configuration.GetConnectionString("SupabaseDb");
Console.WriteLine($"CONNECTION STRING: {connStr}"); // check terminal output

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<ReportSystem.Helpers.AppDbConnection>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseSession();

app.MapRazorPages();
app.MapControllers();

app.Run();

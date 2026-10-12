# Report System

A web-based reporting platform built with ASP.NET Core 8.0, featuring file uploads, image processing, and email notifications.

## Tech Stack

- **Framework:** ASP.NET Core 8.0 (Razor Pages + Web API)
- **Database:** PostgreSQL via [Supabase](https://supabase.com) (Entity Framework Core)
- **File Storage:** Cloudflare R2 (AWS S3-compatible)
- **Email:** [Resend](https://resend.com) via HTTP
- **Image Processing:** SixLabors.ImageSharp
- **Auth:** Cookie-based authentication
- **Excel Export:** ClosedXML

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A [Supabase](https://supabase.com) project (PostgreSQL database)
- A [Cloudflare R2](https://developers.cloudflare.com/r2/) bucket
- A [Resend](https://resend.com) account and API key

## Configuration

Copy `appsettings.json` and fill in your credentials. **Never commit secrets to version control.**

```json
{
  "ConnectionStrings": {
    "SupabaseDb": "Host=db.<ref>.supabase.co;Database=postgres;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true"
  },
  "Supabase": {
    "Url": "https://<ref>.supabase.co",
    "Key": "<anon-or-service-role-key>"
  },
  "Email": {
    "Resend": {
      "ApiKey": "<resend-api-key>"
    },
    "From": {
      "Address": "noreply@yourdomain.com",
      "Name": "Report Desk"
    }
  },
  "R2": {
    "AccountId": "<cloudflare-account-id>",
    "AccessKeyId": "<r2-access-key-id>",
    "SecretAccessKey": "<r2-secret-access-key>",
    "BucketName": "<your-bucket-name>",
    "PublicBaseUrl": "https://<your-r2-public-url>"
  }
}
```

> **Tip:** Use [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for local development:
> ```bash
> dotnet user-secrets set "R2:SecretAccessKey" "your-secret"
> ```

## Running Locally

```bash
# Clone the repository
git clone <repo-url>
cd ReportSystem

# Restore dependencies
dotnet restore

# Apply database migrations
dotnet ef database update

# Run the application
dotnet run
```

The app will be available at `https://localhost:5001` (or the port shown in the console).

## Project Structure

```
ReportSystem/
├── Controllers/
│   └── Api/          # REST API endpoints
├── Data/             # DbContext and database configuration
├── Models/           # Entity and view models
├── Pages/            # Razor Pages (UI)
│   └── Shared/       # Layouts and partials
├── Properties/       # Launch settings
├── wwwroot/          # Static assets (CSS, JS, Bootstrap)
├── Program.cs        # App entry point and service registration
├── appsettings.json  # Configuration (do not commit secrets)
└── Report_System.csproj
```

## Key NuGet Packages

| Package | Purpose |
|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL via EF Core |
| `Supabase` | Supabase client |
| `AWSSDK.S3` | Cloudflare R2 (S3-compatible) storage |
| `SixLabors.ImageSharp` | Server-side image processing |
| `ClosedXML` | Excel report generation |
| `BCrypt.Net-Next` | Password hashing |
| `Resend` | Email support |

## Notes

- File uploads are rate-limited to **10 requests per minute** per client.
- Sessions expire after **8 hours** of inactivity; auth cookies after **24 hours**.
- A background service (`TempFileCleanupService`) automatically cleans up temporary files.
- R2 requires `ForcePathStyle = true` and `AuthenticationRegion = "auto"`.

## License

Report Desk is proprietary software. All rights are reserved by the copyright holder.

The source code is publicly available for viewing, but permission is required before reusing, copying, modifying, or redistributing the project's original materials, except where applicable law or the repository hosting platform's terms provide otherwise.

See the [LICENSE](./LICENSE) file for the full terms.

For permission to use this project, please contact the copyright holder.


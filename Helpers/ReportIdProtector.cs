using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace ReportSystem.Helpers
{
    public static class ReportIdProtector
    {
        private const string Purpose = "ReportSystem.ReportDetail.ReportId.v1";

        public static string Protect(IDataProtectionProvider provider, int reportId)
        {
            return provider.CreateProtector(Purpose)
                .Protect(reportId.ToString(CultureInfo.InvariantCulture));
        }

        public static bool TryUnprotect(IDataProtectionProvider provider, string protectedReportId, out int reportId)
        {
            reportId = 0;

            if (string.IsNullOrWhiteSpace(protectedReportId))
                return false;

            try
            {
                var value = provider.CreateProtector(Purpose).Unprotect(protectedReportId);
                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out reportId)
                    && reportId > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}

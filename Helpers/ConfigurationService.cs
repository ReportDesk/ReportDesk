namespace ReportSystem.Helpers
{
    public interface IConfigurationService
    {
        string GetConnectionString(string connectionName);
        string GetKeyString(string keyDirectory);
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetConnectionString(string connectionName)
        {
            return _configuration.GetConnectionString(connectionName);
        }

        public string GetKeyString(string keyDirectory)
        {
            return _configuration[keyDirectory];
        }
    }
}

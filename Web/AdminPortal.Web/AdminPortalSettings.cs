using System.Configuration;

namespace AdminPortal.Web;

public static class AdminPortalSettings
{
    public static string? SiteTitle => ConfigurationManager.AppSettings["SiteTitle"];

    public static string? AdminDbConnectionString =>
        ConfigurationManager.ConnectionStrings["AdminDb"]?.ConnectionString;
}

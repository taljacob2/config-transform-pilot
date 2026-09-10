using System.Configuration;

namespace AdminPortal.Web;

public static class AdminPortalSettings
{
    public static string? SiteTitle => ConfigurationManager.AppSettings["SiteTitle"];

    public static string? AdminDbConnectionString =>
        ConfigurationManager.ConnectionStrings["AdminDb"]?.ConnectionString;

    // Load-balanced Production servers each talk to their own cache node (docs/HOST_LAYER_DESIGN.md
    // in config-transform) -- overridden per-host under .configtransform/Clients/*/Hosts/<Host>/.
    public static string? CacheNodeEndpoint => ConfigurationManager.AppSettings["CacheNodeEndpoint"];
}

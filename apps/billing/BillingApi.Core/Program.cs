using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var apiUrl = configuration["Billing:ApiUrl"];
Console.WriteLine($"BillingApi starting against {apiUrl}");

using Billing.Application;
using Billing.Infrastructure.Jobs;
using Billing.Infrastructure.Pdf;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<BillingDbContext>(o => o.UseSqlServer(config.GetConnectionString("Billing")));
        services.AddScoped<IBillingDbContext>(sp => sp.GetRequiredService<BillingDbContext>());
        services.AddScoped<IInvoiceNumberGenerator, SqlInvoiceNumberGenerator>();
        services.AddSingleton<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();
        services.AddScoped<BillingService>();
        services.AddScoped<BillingRunService>();
        services.AddSingleton(TimeProvider.System);
        services.Configure<BillingOptions>(config.GetSection(BillingOptions.Section));

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        if (config.GetValue("Billing:EnableScheduledRun", true))
            services.AddHostedService<BillingRunWorker>();

        return services;
    }
}

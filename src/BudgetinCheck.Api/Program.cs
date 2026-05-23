using BudgetinCheck.Api.Features.BudgetPlans;
using BudgetinCheck.Api.Features.ExpenseSummary;
using BudgetinCheck.Api.Features.BudgetSummary;
using BudgetinCheck.Api.Features.IncomeMonth;
using BudgetinCheck.Api.Features.IncomeSacrifice;
using BudgetinCheck.Api.Features.Logo;
using BudgetinCheck.Api.Features.Proxy;
using BudgetinCheck.Api.Features.Settings;
using BudgetinCheck.Api.Features.Subscription;
using BudgetinCheck.Api.Infrastructure.Auth;
using BudgetinCheck.Api.Infrastructure.Configuration;
using BudgetinCheck.Api.Infrastructure.Data;
using BudgetinCheck.Api.Infrastructure.Development;
using BudgetinCheck.Api.Infrastructure.Legacy;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

DevelopmentEnvironmentConfiguration.ApplyBudgetAppDatabaseFallback(
    builder.Configuration,
    builder.Environment);

builder.Services.Configure<LegacyNextJsOptions>(builder.Configuration.GetSection(LegacyNextJsOptions.SectionName));
builder.Services.Configure<BudgetDataOptions>(builder.Configuration.GetSection(BudgetDataOptions.SectionName));

builder.Services.AddHttpClient<LegacyBffClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<LegacyNextJsOptions>>()
        .Value;

    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    }

    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 100 : options.TimeoutSeconds);
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<BudgetDbConnectionFactory>();
builder.Services.AddScoped<CurrentSessionResolver>();
builder.Services.AddScoped<BudgetSummaryService>();
builder.Services.AddScoped<ExpenseSummaryService>();
builder.Services.AddScoped<IncomeMonthService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "budgetin-check-api",
    mode = "staged-bff-migration",
}));

// Mobile auth still lives in the legacy Next.js server during the staged migration.
// Keep the .NET host contract-compatible so local backend switching can reuse the
// same register_check/register_complete flow and logout endpoint.
app.MapPost("/api/mobile-auth", LegacyProxyEndpoints.ProxyAsync)
    .WithName("PostMobileAuthProxy")
    .WithOpenApi();

app.MapPost("/api/mobile-auth/logout", LegacyProxyEndpoints.ProxyAsync)
    .WithName("PostMobileAuthLogoutProxy")
    .WithOpenApi();

var bff = app.MapGroup("/api/bff");

bff.MapGet("/subscription", SubscriptionEndpoints.GetAsync)
    .WithName("GetSubscription")
    .WithOpenApi();

bff.MapGet("/logo", LogoEndpoints.GetAsync)
    .WithName("GetLogo")
    .WithOpenApi();

bff.MapGet("/budget-plans", BudgetPlansEndpoints.GetAsync)
    .WithName("GetBudgetPlans")
    .WithOpenApi();

bff.MapGet("/budget-summary", BudgetSummaryEndpoints.GetAsync)
    .WithName("GetBudgetSummary")
    .WithOpenApi();

bff.MapGet("/income-sacrifice", IncomeSacrificeEndpoints.GetAsync)
    .WithName("GetIncomeSacrifice")
    .WithOpenApi();

bff.MapPatch("/income-sacrifice", IncomeSacrificeEndpoints.PatchAsync)
    .WithName("PatchIncomeSacrifice")
    .WithOpenApi();

bff.MapPost("/income-sacrifice/custom", IncomeSacrificeEndpoints.CreateCustomAsync)
    .WithName("CreateIncomeSacrificeCustomItem")
    .WithOpenApi();

bff.MapDelete("/income-sacrifice/custom/{id}", IncomeSacrificeEndpoints.DeleteCustomAsync)
    .WithName("DeleteIncomeSacrificeCustomItem")
    .WithOpenApi();

bff.MapGet("/income-sacrifice/goals", IncomeSacrificeEndpoints.GetGoalsAsync)
    .WithName("GetIncomeSacrificeGoals")
    .WithOpenApi();

bff.MapPatch("/income-sacrifice/goals", IncomeSacrificeEndpoints.PatchGoalsAsync)
    .WithName("PatchIncomeSacrificeGoals")
    .WithOpenApi();

bff.MapPost("/income-sacrifice/goals", IncomeSacrificeEndpoints.PostGoalsAsync)
    .WithName("PostIncomeSacrificeGoals")
    .WithOpenApi();

// Keep settings behavior explicit and implemented natively during migration.
bff.MapGet("/settings", SettingsEndpoints.GetAsync)
    .WithName("GetSettings")
    .WithOpenApi();

bff.MapPatch("/settings", SettingsEndpoints.PatchAsync)
    .WithName("PatchSettings")
    .WithOpenApi();

// Keep the highest-risk period-sensitive BFF routes explicit in the .NET repo
// even while they still proxy to the legacy Next.js implementation.
bff.MapGet("/dashboard", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetDashboardProxy")
    .WithOpenApi();

bff.MapGet("/income-month", IncomeMonthEndpoints.GetAsync)
    .WithName("GetIncomeMonth")
    .WithOpenApi();

bff.MapGet("/income-summary", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetIncomeSummaryProxy")
    .WithOpenApi();

bff.MapGet("/expenses", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetExpensesProxy")
    .WithOpenApi();

bff.MapPost("/expenses", LegacyProxyEndpoints.ProxyAsync)
    .WithName("PostExpensesProxy")
    .WithOpenApi();

bff.MapGet("/expenses/months", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetExpenseMonthsProxy")
    .WithOpenApi();

bff.MapGet("/expenses/summary", ExpenseSummaryEndpoints.GetAsync)
    .WithName("GetExpenseSummary")
    .WithOpenApi();

bff.MapGet("/expenses/pay-period-months", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetExpensePayPeriodMonthsProxy")
    .WithOpenApi();

bff.MapGet("/expense-insights", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetExpenseInsightsProxy")
    .WithOpenApi();

bff.MapGet("/debt-summary", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetDebtSummaryProxy")
    .WithOpenApi();

bff.MapGet("/debts/{id}", LegacyProxyEndpoints.ProxyAsync)
    .WithName("GetDebtDetailProxy")
    .WithOpenApi();

bff.MapPatch("/debts/{id}", LegacyProxyEndpoints.ProxyAsync)
    .WithName("PatchDebtDetailProxy")
    .WithOpenApi();

app.MapMethods(
        "/api/bff/{**catchAll}",
        new[]
        {
            HttpMethods.Get,
            HttpMethods.Post,
            HttpMethods.Put,
            HttpMethods.Patch,
            HttpMethods.Delete,
            HttpMethods.Options,
        },
        LegacyProxyEndpoints.ProxyAsync)
    .ExcludeFromDescription();

app.Run();

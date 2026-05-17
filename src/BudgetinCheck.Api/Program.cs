using BudgetinCheck.Api.Features.BudgetPlans;
using BudgetinCheck.Api.Features.BudgetSummary;
using BudgetinCheck.Api.Features.Logo;
using BudgetinCheck.Api.Features.Proxy;
using BudgetinCheck.Api.Features.Subscription;
using BudgetinCheck.Api.Infrastructure.Auth;
using BudgetinCheck.Api.Infrastructure.Configuration;
using BudgetinCheck.Api.Infrastructure.Data;
using BudgetinCheck.Api.Infrastructure.Legacy;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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

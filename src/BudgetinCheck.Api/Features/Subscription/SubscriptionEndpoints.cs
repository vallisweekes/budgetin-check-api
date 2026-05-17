using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Auth;

namespace BudgetinCheck.Api.Features.Subscription;

internal static class SubscriptionEndpoints
{
    public static async Task<IResult> GetAsync(HttpRequest request, CurrentSessionResolver sessionResolver, CancellationToken cancellationToken)
    {
        var sessionResolution = await sessionResolver.ResolveAsync(request, cancellationToken);
        if (!sessionResolution.IsSuccess)
        {
            return sessionResolution.ToResult();
        }

        return Results.Json(new
        {
            current = new
            {
                status = "free",
                planKey = "free",
                planLabel = "Free",
                billingLabel = "All features are currently free during the launch phase.",
                renewalLabel = (string?)null,
                manageLabel = "Billing is not live yet.",
            },
            offers = new object[]
            {
                new
                {
                    id = "pro_monthly",
                    title = "Pro Monthly",
                    priceLabel = "GBP 4.99",
                    billingLabel = "per month",
                    highlight = true,
                    bullets = new[]
                    {
                        "Keep the full budgeting experience",
                        "Priority access to new tools",
                        "Early premium rollout pricing",
                    },
                },
                new
                {
                    id = "pro_yearly",
                    title = "Pro Yearly",
                    priceLabel = "GBP 49.99",
                    billingLabel = "per year",
                    highlight = false,
                    bullets = new[]
                    {
                        "Everything in Pro Monthly",
                        "Lower annual price",
                        "Best value for long-term users",
                    },
                },
            },
            launchState = new
            {
                mode = "soft_launch",
                canPurchase = false,
                message = "Subscriptions are being prepared. Upgrade entry points are visible, but billing is not live yet.",
            },
        });
    }
}
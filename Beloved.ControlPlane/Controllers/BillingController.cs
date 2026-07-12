using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Controllers;

/// <summary>
/// Stripe billing endpoints: checkout, customer portal, and webhook handler.
/// Written to the Fowler/Edwards standard — thin controller, all business logic
/// delegated to Stripe SDK and QuotaService.
/// </summary>
[ApiController]
[Route("api/billing")]
public sealed class BillingController : ControllerBase
{
    private readonly BelovedDbContext _db;
    private readonly IConfiguration _config;

    // Stripe price IDs — set these in appsettings.json -> Stripe section
    private string ProPriceId      => _config["Stripe:ProPriceId"]      ?? "price_pro_placeholder";
    private string WebhookSecret   => _config["Stripe:WebhookSecret"]   ?? string.Empty;
    private string SuccessUrl      => _config["Stripe:SuccessUrl"]      ?? "http://localhost:3000?billing=success";
    private string CancelUrl       => _config["Stripe:CancelUrl"]       ?? "http://localhost:3000?billing=cancelled";

    private readonly Services.IEmailSender _emailSender;

    public BillingController(BelovedDbContext db, IConfiguration config, Services.IEmailSender emailSender)
    {
        _db = db;
        _config = config;
        _emailSender = emailSender;
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
    }

    // ── GET /api/billing/plan ──────────────────────────────────────────────────

    /// <summary>Returns the authenticated tenant's current plan and monthly usage.</summary>
    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan()
    {
        var tenant = await ResolveTenantAsync();
        if (tenant == null) return Unauthorized();

        var period = DateTime.UtcNow.ToString("yyyy-MM");
        var usedThisMonth = await _db.AssemblyUsages
            .CountAsync(u => u.TenantId == tenant.Id && u.PeriodMonth == period);

        return Ok(new
        {
            plan          = tenant.Plan.ToString(),
            monthlyQuota  = tenant.Plan == TenantPlan.Enterprise ? (int?)null : tenant.MonthlyQuota,
            usedThisMonth,
            remaining     = tenant.Plan == TenantPlan.Enterprise ? (int?)null : Math.Max(0, tenant.MonthlyQuota - usedThisMonth),
            stripeCustomerId = tenant.StripeCustomerId
        });
    }

    // ── POST /api/billing/checkout ─────────────────────────────────────────────

    /// <summary>
    /// Creates a Stripe Checkout session for upgrading to Pro.
    /// Returns the Checkout session URL for the client to redirect to.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout()
    {
        var tenant = await ResolveTenantAsync();
        if (tenant == null) return Unauthorized();

        // Create or reuse Stripe Customer
        string customerId;
        if (!string.IsNullOrEmpty(tenant.StripeCustomerId))
        {
            customerId = tenant.StripeCustomerId;
        }
        else
        {
            var customerService = new CustomerService();
            var customer = await customerService.CreateAsync(new CustomerCreateOptions
            {
                Name     = tenant.Name,
                Metadata = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["beloved_tenant_id"] = tenant.Id.ToString()
                }
            });
            tenant.StripeCustomerId = customer.Id;
            await _db.SaveChangesAsync();
            customerId = customer.Id;
        }

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Customer   = customerId,
            Mode       = "subscription",
            LineItems  = new System.Collections.Generic.List<SessionLineItemOptions>
            {
                new() { Price = ProPriceId, Quantity = 1 }
            },
            SuccessUrl = SuccessUrl,
            CancelUrl  = CancelUrl,
            Metadata   = new System.Collections.Generic.Dictionary<string, string>
            {
                ["beloved_tenant_id"] = tenant.Id.ToString()
            }
        });

        return Ok(new { checkoutUrl = session.Url });
    }

    // ── GET /api/billing/portal ────────────────────────────────────────────────

    /// <summary>
    /// Creates a Stripe Customer Portal session so the tenant can manage
    /// their subscription, download invoices, or cancel — all self-serve.
    /// </summary>
    [HttpGet("portal")]
    public async Task<IActionResult> GetPortal()
    {
        var tenant = await ResolveTenantAsync();
        if (tenant == null) return Unauthorized();

        if (string.IsNullOrEmpty(tenant.StripeCustomerId))
            return BadRequest("No Stripe customer found for this tenant. Complete checkout first.");

        var portalService = new Stripe.BillingPortal.SessionService();
        var portal = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer   = tenant.StripeCustomerId,
            ReturnUrl  = SuccessUrl
        });

        return Redirect(portal.Url);
    }

    // ── POST /api/billing/webhook ──────────────────────────────────────────────

    /// <summary>
    /// Stripe webhook receiver. Handles:
    ///   - checkout.session.completed  → upgrade tenant to Pro
    ///   - customer.subscription.deleted → downgrade tenant to Free
    ///   - invoice.payment_failed → log warning (could notify tenant)
    /// Stripe signature is verified against WebhookSecret before processing.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> StripeWebhook()
    {
        var payload  = await new StreamReader(Request.Body).ReadToEndAsync();
        var sigHeader = Request.Headers["Stripe-Signature"].ToString();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, sigHeader, WebhookSecret);
        }
        catch (StripeException ex)
        {
            return BadRequest($"Webhook signature verification failed: {ex.Message}");
        }

        switch (stripeEvent.Type)
        {
            case Stripe.EventTypes.CheckoutSessionCompleted:
            {
                var session = (Session)stripeEvent.Data.Object;
                if (session.Metadata.TryGetValue("beloved_tenant_id", out var idStr)
                    && Guid.TryParse(idStr, out var tenantId))
                {
                    var t = await _db.Tenants.FindAsync(tenantId);
                    if (t != null)
                    {
                        t.Plan = TenantPlan.Pro;
                        t.StripeCustomerId = session.CustomerId;
                        t.StripeSubscriptionId = session.SubscriptionId;
                        await _db.SaveChangesAsync();
                    }
                }
                break;
            }

            case Stripe.EventTypes.CustomerSubscriptionDeleted:
            {
                var sub = (Subscription)stripeEvent.Data.Object;
                var t = await _db.Tenants.FirstOrDefaultAsync(
                    t => t.StripeSubscriptionId == sub.Id);
                if (t != null)
                {
                    t.Plan = TenantPlan.Free;
                    t.StripeSubscriptionId = null;
                    await _db.SaveChangesAsync();
                }
                break;
            }

            case Stripe.EventTypes.InvoicePaymentFailed:
            {
                var invoice = (Invoice)stripeEvent.Data.Object;
                var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
                if (tenant != null)
                {
                    // Resolve tenant owner email from User Org memberships
                    var owner = await _db.OrganisationMembers
                        .Include(m => m.User)
                        .Where(m => m.OrganisationId == tenant.OrganisationId && m.Role == OrgRole.Owner)
                        .Select(m => m.User)
                        .FirstOrDefaultAsync();

                    var recipientEmail = owner?.Email ?? "billing-contact@beloved.build";
                    var amountDueDecimal = (decimal)(invoice.AmountDue / 100.0);
                    var updateUrl = $"{Request.Scheme}://{Request.Host}/billing?tenantId={tenant.Id}";

                    await _emailSender.SendPaymentFailedEmailAsync(recipientEmail, tenant.Name, amountDueDecimal, updateUrl);
                }
                break;
            }
        }

        return Ok();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<Tenant?> ResolveTenantAsync()
    {
        var apiKey = Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        return await _db.Tenants.FirstOrDefaultAsync(t => t.ApiKey == apiKey);
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PersonalFinance.Api.Observability;

// The app's own spans and metrics, alongside the ASP.NET Core, HttpClient
// (Plaid) and Npgsql instrumentation (#121). Names follow OpenTelemetry's
// conventions: dotted, lower case, the unit in the description.
public static class Telemetry
{
    public const string Name = "PersonalFinance.Api";

    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> TransactionsSynced = Meter.CreateCounter<long>(
        "personalfinance.plaid.transactions_synced", unit: "{transaction}",
        description: "Transactions applied by Plaid syncs, by change (added, modified, removed)");

    public static readonly Counter<long> ItemSyncs = Meter.CreateCounter<long>(
        "personalfinance.plaid.item_syncs", unit: "{sync}",
        description: "Plaid Item syncs, by outcome (ok, error)");

    public static readonly Counter<long> Webhooks = Meter.CreateCounter<long>(
        "personalfinance.plaid.webhooks", unit: "{webhook}",
        description: "Plaid webhooks received, by verification result");

    public static readonly Counter<long> AuditEvents = Meter.CreateCounter<long>(
        "personalfinance.audit.events", unit: "{event}",
        description: "Security audit events recorded, by type - e.g. alert on auth.refresh.reuse_detected");
}

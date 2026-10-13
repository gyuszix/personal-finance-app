using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.Insights.Cards.NetWorth;

// Today's net worth with its assets/liabilities split. Balances aren't
// per month, so this ignores the selected month.
public partial class NetWorthCard(ApiService api) : InsightCardViewModel
{
    public override string Title => "Net worth";
    public override InsightGroup Group => InsightGroup.NetWorth;
    public override string EmptyMessage => "No accounts linked yet.";

    [ObservableProperty]
    private string netWorthText = Format.None;

    [ObservableProperty]
    private string assetsText = Format.None;

    [ObservableProperty]
    private string liabilitiesText = Format.None;

    protected override async Task<bool?> LoadCoreAsync(DateTime month)
    {
        var summary = await api.GetAccountsSummaryAsync();
        if (summary == null) return null;

        NetWorthText = Format.Money(summary.NetWorth);
        AssetsText = Format.Money(summary.TotalAssets);
        LiabilitiesText = Format.Money(summary.TotalLiabilities);
        return summary.Accounts.Count > 0;
    }
}

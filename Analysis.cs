namespace SubscriptionTracker;

public record CategorySpend(string Category, decimal Monthly, int Count);
public record Renewal(int Id, string Name, decimal Cost, DateOnly Date, int DaysAway);
public record OverlapGroup(string Category, List<SubscriptionDto> Subscriptions, int KeepId, decimal MonthlySavings);
public record IdleItem(SubscriptionDto Subscription, string Reason);

public record Analysis(
    decimal MonthlyTotal, decimal AnnualTotal,
    decimal OverlapMonthly, decimal IdleMonthly, decimal PotentialMonthlySavings,
    List<CategorySpend> ByCategory, List<OverlapGroup> Overlaps,
    List<IdleItem> Idle, List<Renewal> Renewals);

public record Snapshot(List<SubscriptionDto> Subscriptions, Analysis Analysis);

public static class Analyzer
{
    public const int IdleDays = 30;      // no use logged for this long = idle
    public const int MinUses = 1;        // this many uses (or fewer) in 30 days = idle
    public const int RenewalWindow = 14; // days ahead to list upcoming renewals

    public static decimal Monthly(decimal cost, BillingCycle c) => Math.Round(c switch
    {
        BillingCycle.Weekly => cost * 52m / 12m,
        BillingCycle.Quarterly => cost / 3m,
        BillingCycle.Yearly => cost / 12m,
        _ => cost
    }, 2);

    static DateOnly Roll(DateOnly d, BillingCycle c, DateOnly today)
    {
        while (d < today)
            d = c switch
            {
                BillingCycle.Weekly => d.AddDays(7),
                BillingCycle.Monthly => d.AddMonths(1),
                BillingCycle.Quarterly => d.AddMonths(3),
                _ => d.AddYears(1)
            };
        return d;
    }

    public static Snapshot Build(IEnumerable<Subscription> subs, DateOnly today)
    {
        var since = today.AddDays(-30);
        var all = subs.Select(s => new SubscriptionDto(
            s.Id, s.Name, s.Category, s.Cost, s.Cycle, Monthly(s.Cost, s.Cycle),
            s.Active ? Roll(s.NextRenewal, s.Cycle, today) : s.NextRenewal, s.Active, s.CancelledOn, s.Notes,
            s.Usage.Count(u => u.Date > since && u.Date <= today),
            s.Usage.Count > 0 ? s.Usage.Max(u => u.Date) : null, [])).ToList();

        var active = all.Where(s => s.Active).ToList();

        // Overlap: 2+ active subscriptions in one category. Keep the most used; the rest are extras.
        var overlaps = active
            .GroupBy(s => s.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var keep = g.OrderByDescending(s => s.UsesLast30).ThenByDescending(s => s.LastUsed).First();
                return new OverlapGroup(g.Key, g.ToList(), keep.Id, g.Where(s => s.Id != keep.Id).Sum(s => s.MonthlyCost));
            }).ToList();

        // Idle: nothing logged in 30+ days, or barely used.
        var idle = active.Select(s =>
        {
            var reason =
                s.LastUsed is null ? "No use logged yet"
                : today.DayNumber - s.LastUsed.Value.DayNumber > IdleDays ? $"Last used {today.DayNumber - s.LastUsed.Value.DayNumber} days ago"
                : s.UsesLast30 <= MinUses ? $"Used {s.UsesLast30}× in 30 days"
                : null;
            return reason is null ? null : new IdleItem(s, reason);
        }).OfType<IdleItem>().ToList();

        // A subscription that is both overlapping and idle is counted once.
        var extraIds = overlaps.SelectMany(o => o.Subscriptions.Where(s => s.Id != o.KeepId)).Select(s => s.Id).ToHashSet();
        var idleIds = idle.Select(i => i.Subscription.Id).ToHashSet();
        var overlapCost = active.Where(s => extraIds.Contains(s.Id)).Sum(s => s.MonthlyCost);
        var idleCost = active.Where(s => idleIds.Contains(s.Id) && !extraIds.Contains(s.Id)).Sum(s => s.MonthlyCost);
        var monthly = active.Sum(s => s.MonthlyCost);

        var flagged = all.Select(s => s with
        {
            Flags = new[] { extraIds.Contains(s.Id) ? "Overlap" : null, idleIds.Contains(s.Id) ? "Idle" : null }.OfType<string>().ToArray()
        }).ToList();

        var renewals = active
            .Where(s => s.NextRenewal.DayNumber - today.DayNumber <= RenewalWindow)
            .OrderBy(s => s.NextRenewal)
            .Select(s => new Renewal(s.Id, s.Name, s.Cost, s.NextRenewal, s.NextRenewal.DayNumber - today.DayNumber))
            .ToList();

        var byCategory = active.GroupBy(s => s.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySpend(g.Key, g.Sum(s => s.MonthlyCost), g.Count()))
            .OrderByDescending(c => c.Monthly).ToList();

        return new Snapshot(flagged, new Analysis(monthly, monthly * 12, overlapCost, idleCost, overlapCost + idleCost,
            byCategory, overlaps, idle, renewals));
    }
}

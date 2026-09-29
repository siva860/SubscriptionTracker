using Microsoft.EntityFrameworkCore;

namespace SubscriptionTracker;

// ---------- Entities ----------

public enum BillingCycle { Weekly, Monthly, Quarterly, Yearly }

public class Subscription
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Cost { get; set; }
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    public DateOnly NextRenewal { get; set; }
    public bool Active { get; set; } = true;
    public DateOnly? CancelledOn { get; set; }
    public string? Notes { get; set; }
    public List<UsageLog> Usage { get; set; } = [];
}

public class UsageLog
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public DateOnly Date { get; set; }
}

// ---------- DTOs ----------

public record SubscriptionInput(string Name, string Category, decimal Cost, BillingCycle Cycle, DateOnly NextRenewal, string? Notes);
public record UsageInput(DateOnly? Date);

public record SubscriptionDto(
    int Id, string Name, string Category, decimal Cost, BillingCycle Cycle, decimal MonthlyCost,
    DateOnly NextRenewal, bool Active, DateOnly? CancelledOn, string? Notes,
    int UsesLast30, DateOnly? LastUsed, string[] Flags);

// ---------- Database ----------

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<UsageLog> UsageLogs => Set<UsageLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Subscription>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.Cost).HasPrecision(10, 2);
            e.HasMany(x => x.Usage).WithOne().HasForeignKey(u => u.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public static class Seed
{
    public static void Run(AppDb db)
    {
        if (db.Subscriptions.Any()) return;
        var t = DateOnly.FromDateTime(DateTime.Today);

        Subscription S(string name, string cat, decimal cost, BillingCycle cy, int renewIn, params int[] usedDaysAgo) => new()
        {
            Name = name, Category = cat, Cost = cost, Cycle = cy, NextRenewal = t.AddDays(renewIn),
            Usage = usedDaysAgo.Select(d => new UsageLog { Date = t.AddDays(-d) }).ToList()
        };

        db.Subscriptions.AddRange(
            S("Netflix", "Streaming", 15.49m, BillingCycle.Monthly, 6, 1, 3, 5, 8, 12, 15, 19, 24),
            S("Disney+", "Streaming", 13.99m, BillingCycle.Monthly, 11, 55),
            S("Spotify", "Music", 11.99m, BillingCycle.Monthly, 3, 0, 1, 2, 3, 4, 6, 7, 9, 11, 14, 20),
            S("Apple Music", "Music", 10.99m, BillingCycle.Monthly, 19, 20),
            S("Adobe Creative Cloud", "Software", 659.88m, BillingCycle.Yearly, 40, 70),
            S("Gym membership", "Fitness", 30m, BillingCycle.Monthly, 9, 2, 6, 10, 14),
            S("iCloud+", "Cloud storage", 2.99m, BillingCycle.Monthly, 22, 0, 1, 2, 4, 9),
            S("Google One", "Cloud storage", 1.99m, BillingCycle.Monthly, 27, 90));
        db.SaveChanges();
    }
}

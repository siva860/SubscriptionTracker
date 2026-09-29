using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SubscriptionTracker;

public static class SubscriptionEndpoints
{
    static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    static async Task<Snapshot> LoadAsync(AppDb db) =>
        Analyzer.Build(await db.Subscriptions.Include(s => s.Usage).AsNoTracking().ToListAsync(), Today);

    static Dictionary<string, string[]>? Validate(SubscriptionInput i)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(i.Name)) e["name"] = ["Enter a name."];
        else if (i.Name.Length > 100) e["name"] = ["Name must be 100 characters or fewer."];
        if (string.IsNullOrWhiteSpace(i.Category)) e["category"] = ["Enter a category."];
        if (i.Cost < 0) e["cost"] = ["Cost can't be negative."];
        if (!Enum.IsDefined(i.Cycle)) e["cycle"] = ["Choose a billing cycle."];
        return e.Count > 0 ? e : null;
    }

    static void Apply(Subscription s, SubscriptionInput i)
    {
        s.Name = i.Name.Trim(); s.Category = i.Category.Trim(); s.Cost = i.Cost;
        s.Cycle = i.Cycle; s.NextRenewal = i.NextRenewal; s.Notes = i.Notes?.Trim();
    }

    public static void MapSubscriptionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // status: active (default) | cancelled | all
        api.MapGet("/subscriptions", async (AppDb db, string? q, string? category, string? status) =>
        {
            var list = (await LoadAsync(db)).Subscriptions.AsEnumerable();
            list = (status ?? "active") switch
            {
                "cancelled" => list.Where(s => !s.Active),
                "all" => list,
                _ => list.Where(s => s.Active)
            };
            if (!string.IsNullOrWhiteSpace(q))
                list = list.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || s.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(category))
                list = list.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            return list.OrderBy(s => s.Name).ToList();
        });

        api.MapGet("/subscriptions/{id:int}", async (int id, AppDb db) =>
            (await LoadAsync(db)).Subscriptions.FirstOrDefault(s => s.Id == id) is { } s ? Results.Ok(s) : Results.NotFound());

        api.MapPost("/subscriptions", async (SubscriptionInput input, AppDb db) =>
        {
            if (Validate(input) is { } errors) return Results.ValidationProblem(errors);
            var s = new Subscription();
            Apply(s, input);
            db.Subscriptions.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/subscriptions/{s.Id}", new { s.Id });
        });

        api.MapPut("/subscriptions/{id:int}", async (int id, SubscriptionInput input, AppDb db) =>
        {
            if (Validate(input) is { } errors) return Results.ValidationProblem(errors);
            if (await db.Subscriptions.FindAsync(id) is not { } s) return Results.NotFound();
            Apply(s, input);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/subscriptions/{id:int}", async (int id, AppDb db) =>
            await db.Subscriptions.Where(s => s.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound());

        // Log a day you actually used it (defaults to today).
        api.MapPost("/subscriptions/{id:int}/usage", async (int id, UsageInput? input, AppDb db) =>
        {
            if (!await db.Subscriptions.AnyAsync(s => s.Id == id)) return Results.NotFound();
            var date = input?.Date ?? Today;
            if (date > Today) return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Usage can't be in the future."] });
            db.UsageLogs.Add(new UsageLog { SubscriptionId = id, Date = date });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/subscriptions/{id:int}/cancel", async (int id, AppDb db) =>
        {
            if (await db.Subscriptions.FindAsync(id) is not { } s) return Results.NotFound();
            s.Active = false; s.CancelledOn = Today;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/subscriptions/{id:int}/reactivate", async (int id, AppDb db) =>
        {
            if (await db.Subscriptions.FindAsync(id) is not { } s) return Results.NotFound();
            s.Active = true; s.CancelledOn = null;
            if (s.NextRenewal < Today) s.NextRenewal = Today;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/analysis", async (AppDb db) => (await LoadAsync(db)).Analysis);

        api.MapGet("/export.csv", async (AppDb db) =>
        {
            var sb = new StringBuilder("Name,Category,Cost,Cycle,MonthlyCost,NextRenewal,Active,UsesLast30,LastUsed,Flags\n");
            static string Q(string? v) => $"\"{(v ?? "").Replace("\"", "\"\"")}\"";
            foreach (var s in (await LoadAsync(db)).Subscriptions)
                sb.AppendLine(string.Join(',', Q(s.Name), Q(s.Category), s.Cost, s.Cycle, s.MonthlyCost,
                    s.NextRenewal, s.Active, s.UsesLast30, s.LastUsed?.ToString() ?? "", Q(string.Join(' ', s.Flags))));
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "subscriptions.csv");
        });
    }
}

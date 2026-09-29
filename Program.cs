using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SubscriptionTracker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=subscriptions.db"));
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();   // swap for migrations when the schema starts changing
    Seed.Run(db);
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();   // /openapi/v1.json

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapSubscriptionApi();

app.Run();

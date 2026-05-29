using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sounds.Data;
using Sounds.Hubs;

var builder = WebApplication.CreateBuilder(args);

var googleJson = builder.Configuration["GOOGLE_SERVICE_ACCOUNT_JSON"];

if (!string.IsNullOrWhiteSpace(googleJson))
{
    var googlePath = builder.Configuration["GoogleCalendar:ServiceAccountJson"]
                     ?? "/tmp/google-service-account.json";

    var directory = Path.GetDirectoryName(googlePath);

    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    File.WriteAllText(googlePath, googleJson);
}


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
});

builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapGet("/", (HttpContext ctx) =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true)
        return Results.Redirect("/Index");

    return Results.Redirect("/Identity/Account/Login");
});

app.MapControllers();
app.MapHub<CallHub>("/callhub");
app.MapRazorPages().WithStaticAssets();

app.Run();
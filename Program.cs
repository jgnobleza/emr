using medrec.Data;
using medrec.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(7);
});
var dataProtectionKeys = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "data-protection-keys"));
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MedRec")
    .PersistKeysToFileSystem(dataProtectionKeys);
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}
builder.Services.AddSingleton<MySqlConnectionFactory>();
builder.Services.AddScoped<EmrRepository>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<OfflineSyncService>();
builder.Services.AddHostedService<DailySyncService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/offline.html"))
    {
        context.Response.Redirect("/");
        return;
    }

    if (context.Request.Query.TryGetValue("offline", out var offlineMode)
        && offlineMode.Contains("1"))
    {
        var remainingQuery = context.Request.Query
            .Where(item => !item.Key.Equals("offline", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.Key, item => (string?)item.Value.ToString());
        var redirectUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            context.Request.Path.ToString(),
            remainingQuery);
        context.Response.Redirect(redirectUrl);
        return;
    }

    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.Use(async (context, next) =>
{
    if (IsPublicRequest(context)
        || context.Session.GetInt32("UserId").HasValue)
    {
        await next();
        return;
    }

    var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
    context.Response.Redirect($"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
});

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static bool IsPublicRequest(HttpContext context)
{
    var path = context.Request.Path;
    var value = path.Value ?? string.Empty;

    return path.StartsWithSegments("/Account/Login")
        || path.StartsWithSegments("/Account/Logout")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/lib")
        || path.StartsWithSegments("/uploads")
        || value.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase)
        || value.Equals("/service-worker.js", StringComparison.OrdinalIgnoreCase)
        || value.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
        || System.IO.Path.HasExtension(value);
}

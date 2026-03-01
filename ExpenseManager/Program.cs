using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ExpenseManager.Data;
using ExpenseManager.Services;
using ExpenseManager.Configuration;
using System.IO;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddProvider(new ExpenseManager.Services.MemoryLoggerProvider());

// Add services to the container.
var dbPath = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "database", "app.db"));
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
var connectionString = $"Data Source={dbPath}";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddScoped<IGeminiOptionsProvider, GeminiOptionsProvider>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IFinancialInsightsService, FinancialInsightsService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<IFinanceToolExecutor, FinanceToolExecutor>();
builder.Services.AddScoped<IChatAssistantService, ChatAssistantService>();
builder.Services.AddScoped<IExportImportService, ExportImportService>();
builder.Services.AddScoped<IAdminSettingsService, AdminSettingsService>();
builder.Services.AddScoped<IAiTokenUsageService, AiTokenUsageService>();
builder.Services.AddSingleton<ILogReaderService, LogReaderService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await dbContext.Database.MigrateAsync();
    await SeedData.SeedCategoriesAsync(dbContext);
    await SeedData.SeedDemoUserAsync(userManager);
    await SeedData.SeedAdminRoleAsync(userManager, roleManager, config);
    await SeedData.SeedDemoFinancialDataAsync(dbContext, userManager);
}

app.Run();

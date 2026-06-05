using System.Text.Encodings.Web;
using System.Text.Unicode;
using dreams.Data;
using dreams.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.Tokens.AuthenticatorIssuer = "EduPlatform LMS";
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/error/403";
});

builder.Services.AddScoped<dreams.Services.CertificateService>();
builder.Services.AddSingleton<dreams.Services.IEmailSender, dreams.Services.FileEmailSender>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<dreams.Services.MongoLogService>();
builder.Services.AddScoped<dreams.Services.UserActionLogger>();

// Чтобы Razor не экранировал кириллицу в &#x... сущности
builder.Services.Configure<WebEncoderOptions>(o =>
    o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// Локализация: RU/EN, ресурсы в Resources/
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(o =>
{
    var supported = new[]
    {
        new System.Globalization.CultureInfo("ru"),
        new System.Globalization.CultureInfo("en")
    };
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("ru");
    o.SupportedCultures = supported;
    o.SupportedUICultures = supported;
    // Порядок: ?culture=en → cookie → Accept-Language
});

builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

var app = builder.Build();

// Сид ролей + демо-данных при запуске
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { "Student", "Instructor", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseStaticFiles();

var locOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOptions);

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<dreams.Services.HttpLoggingMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

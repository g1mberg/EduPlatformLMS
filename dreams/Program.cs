using System.Text.Encodings.Web;
using System.Text.Unicode;
using EduPlatform.Application.Abstractions;
using EduPlatform.Application.Services;
using EduPlatform.Domain.Entities;
using EduPlatform.Infrastructure.Audit;
using EduPlatform.Infrastructure.Chat;
using EduPlatform.Infrastructure.Email;
using EduPlatform.Infrastructure.Logging;
using EduPlatform.Infrastructure.Mongo;
using EduPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// User-secrets явно (иначе только в Development), чтобы SMTP-настройки работали и под Production
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

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

// Application
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<INotificationPusher, dreams.Infrastructure.SignalRNotificationPusher>();
builder.Services.AddScoped<CertificateService>();
builder.Services.AddScoped<NotificationService>();

// Infrastructure: email
builder.Services.AddSingleton<FileEmailSender>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Email:Smtp:Host"]))
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FileEmailSender>());
}

// Infrastructure: misc
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<MongoLogService>();
builder.Services.AddSingleton<ChatMessageStore>();
builder.Services.AddScoped<UserActionLogger>();
builder.Services.AddSignalR();

// Чтобы Razor не экранировал кириллицу
builder.Services.Configure<WebEncoderOptions>(o =>
    o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// Локализация
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
app.UseMiddleware<HttpLoggingMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<dreams.Hubs.CourseChatHub>("/hubs/course-chat");
app.MapHub<dreams.Hubs.NotificationsHub>("/hubs/notifications");

app.Run();

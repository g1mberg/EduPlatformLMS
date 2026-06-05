# EduPlatform LMS — контекст проекта

Учебная LMS-платформа по ТЗ ([EduPlatform_TZ_Draft.pdf](EduPlatform_TZ_Draft.pdf)). Спринт 1.

## Стек

- **Backend:** ASP.NET MVC, .NET 10 (preview), Razor Views, Bootstrap 5
- **ORM:** Entity Framework Core 10 + SQL Server Express (`.\SQLEXPRESS`, БД `EduPlatform`)
- **Auth:** ASP.NET Identity (`IdentityUser<Guid>`, `IdentityRole<Guid>`), email confirmation, 2FA TOTP
- **Email (dev):** `FileEmailSender` пишет `.eml` в `App_Data/mail/`
- **NoSQL (планируется):** MongoDB — HTTP-логи, действия юзеров, чат
- **Real-time (планируется):** SignalR — групповой чат курса
- **Локализация (планируется):** `IStringLocalizer`, RU/EN

## Архитектура

Один проект `dreams/` без разделения на слои (Clean Architecture — TODO).

```
dreams/
  Controllers/
    AccountController       — login, register, email-confirm, resend, 2FA setup/login/disable
    HomeController
    CoursesController       — каталог из БД, фильтры/поиск/сортировка/пагинация,
                              Details, POST Enroll
    InstructorController    — CRUD курсов с проверкой лимита по подписке
    StudentController       — мои курсы, обзор, страница урока, тесты, сертификаты
    AdminController         — dashboard, users, courses, categories, plans, subscriptions
  Data/
    ApplicationDbContext.cs — IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    DbSeeder.cs             — runtime-сидер: роли, админ, демо-инструктор, 8 категорий,
                              12 курсов, 9 уроков × 5 курсов, 2 финальных теста
  Migrations/Init
  Models/
    Entities/               — ApplicationUser, Course/Section/Lesson/LessonAttachment,
                              Test/Question/AnswerOption/TestAttempt/StudentAnswer,
                              Enrollment/LessonProgress, SubscriptionPlan/
                              InstructorSubscription, Transaction, Certificate,
                              Review, Notification, Category
    Account/                — RegisterVM, LoginVM, TwoFactorLoginVM, TwoFactorSetupVM
    Courses/                — CourseCatalogVM, CourseFormVM, CourseDetailsVM,
                              StudentCourseOverviewVM, StudentLessonVM,
                              TakeTestVM, TestResultVM
  Services/
    IEmailSender + FileEmailSender
    CertificateService
    SlugHelper
  Views/
    Shared/                 — _Layout (home), _InnerLayout (catalog),
                              _DashboardLayout (cabinets), _AuthLayout, _AdminLayout
    Home/Courses/Subscription/Instructor/Student/Account/Admin
  wwwroot/
    assets/                 — Dreams LMS template + admin.css (свой Duralux-стиль)
    template-src/           — исходные HTML
  App_Data/mail/            — dev-почта (.eml)
  Program.cs                — DI, Identity, сидинг, WebEncoders=All для кириллицы
  appsettings.json          — ConnectionStrings:Default, Email:Sender, App:BaseUrl/Name
```

## Сделано (по приоритету ТЗ)

- ✅ **1.** Каталог курсов из БД (фильтры/поиск/сорт/пагинация)
- ✅ **2.** CRUD курсов для инструктора + проверка лимита по подписке
- ✅ **3.** Запись на курс, обзор курса с прогрессом, страница урока, отметка пройденного
- ✅ **4.** Финальный тест, выдача сертификата при 100% + сданных тестах, страница сертификата
- ✅ **5.** Email confirmation + 2FA TOTP (file-based email sender)
- ✅ **8.** Админка: dashboard, users (кредиты+блокировка), courses, categories CRUD,
            plans (редактирование), subscriptions

## Что дальше

1. **MongoDB-логи** — HTTP-запросы в `http_logs`, действия юзера (login/enroll/
   course-publish) в `user_actions`. Просмотр в `/admin/logs`.
2. **SignalR-чат** — групповой чат по курсу, история в MongoDB.
3. **Локализация** — `IStringLocalizer`, `.resx`, рабочий свитчер RU/EN.
4. **CRUD теста инструктором** — сейчас тесты только через сидер.
5. **Покупка подписки за кредиты** — `/instructor/subscription/buy/{planId}`,
   списание Credits, активация InstructorSubscription.
6. **Wizard добавления курса в 5 шагов**.
7. **Clean Architecture** — `Domain/Application/Infrastructure/Web`.
8. **SMTP** — заменить FileEmailSender, убрать `EmailConfirmed=true` у seed-юзеров.

## Auth

- `RequireConfirmedEmail = true`. Регистрация → письмо → confirm-link → можно логиниться.
- `/account/security` → `/account/2fa-setup` (QR через api.qrserver.com) → enable.
- На логине `RequiresTwoFactor` → `/account/login-2fa`.
- Logout — POST с antiforgery.

## Admin

`_AdminLayout` (light, Duralux-inspired, кастомный CSS — шаблон не скачали из-за
Cloudflare-защиты CodeCanyon). Все за `[Authorize(Roles="Admin")]`.

- `/admin` — 4 метрики + 2 таблицы.
- `/admin/users` — фильтры, начислить кредиты (`Transaction.TopUp`), блокировка
  (`LockoutEnd = MaxValue`).
- `/admin/courses` — toggle публикации, удалить (защита от удаления с enrollments).
- `/admin/categories` — inline CRUD, удаление блокируется при наличии курсов.
- `/admin/plans` — Free/Basic/Pro формы (MaxCourses, Commission, Price, AllowsTests).
- `/admin/subscriptions` — список + отмена.

## Полезное / грабли

### Запуск

```powershell
Get-Process dreams -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build C:\kfu\oris\Dream\dreams\dreams\dreams.csproj
Start-Process dotnet -ArgumentList @('run','--project','C:/kfu/oris/Dream/dreams/dreams/dreams.csproj','--no-launch-profile','--no-build') -WindowStyle Hidden
```

Слушает `http://localhost:5000` (default Kestrel).

### Миграции

```powershell
dotnet ef migrations add <Name> --project C:\kfu\oris\Dream\dreams\dreams\dreams.csproj
dotnet ef database update --project C:\kfu\oris\Dream\dreams\dreams\dreams.csproj
```

### Грабли

- **Razor hot-reload капризный** — `_Layout.cshtml` часто требует полного перезапуска.
- **`MapStaticAssets()` в .NET 10** — build-time манифест. Заменено на `UseStaticFiles()`.
- **PowerShell 5.1 + кодировки** — без `-Encoding utf8` ломает UTF-8.
- **Razor escape `@`** — `@@` для литерала, `@@media` в CSS.
- **`@section` зарезервировано Razor** — нельзя переменная `section` в `@foreach`,
  `@section.Title` парсится как директива. Используем `sec`.
- **SQL Server множественные каскадные пути** — часть FK → `Restrict/NoAction`.
- **bool-checkbox без TagHelper** — `<input type=checkbox value=true>` ПЕРЕД
  `<input type=hidden value=false>`, иначе ModelBinder возьмёт false.
- **.NET 10 Razor энкодит non-ASCII в `&#x...;`** — лечится
  `services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All))`.
- **`dotnet build` падает на залоченном .exe** после `dotnet run`.
  `Get-Process dreams | Stop-Process -Force` перед rebuild.
- **`AnswerOption` без `OrderIndex`** — EF не гарантирует порядок отдачи.
  В seed `IsCorrect=oi==1` не соответствует «первый отрендерен».
- **ThemeForest/CodeCanyon превью защищены Cloudflare** — `curl`/`WebFetch` → 403.
  Duralux пришлось воспроизводить вручную.

### Тестовые аккаунты

```
admin@eduplatform.local            / Admin!Pass1         — Admin
demo.instructor@eduplatform.local  / DemoInstructor!1    — Instructor (12 seed-курсов)
```

### Тестовая БД

```sql
SELECT Email, FullName, Credits, IsBlocked FROM AspNetUsers;
SELECT u.Email, r.Name FROM AspNetUserRoles ur
  JOIN AspNetUsers u ON ur.UserId = u.Id
  JOIN AspNetRoles r ON ur.RoleId = r.Id;
SELECT Code, MaxCourses, CommissionPercent, AllowsTests, PriceCredits FROM SubscriptionPlans;
```

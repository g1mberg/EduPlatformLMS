# EduPlatform LMS — контекст проекта

Учебная LMS-платформа по ТЗ ([EduPlatform_TZ_Draft.pdf](EduPlatform_TZ_Draft.pdf)). Все основные пункты ТЗ закрыты, плюс ряд сверху-приоритетных доработок.

## Стек

- **Backend:** ASP.NET MVC, .NET 10 (preview), Razor Views, Bootstrap 5
- **ORM:** EF Core 10 + SQL Server Express (`.\SQLEXPRESS`, БД `EduPlatform`)
- **Auth:** ASP.NET Identity (`IdentityUser<Guid>`), email confirm, **password reset**, 2FA TOTP
- **Email:** **MailKit (SMTP)** + fallback на `FileEmailSender` (.eml в `App_Data/mail/`). Конфиг через user-secrets.
- **NoSQL:** MongoDB — HTTP-логи (`http_logs`), действия юзеров (`user_actions`), история чата (`course_chat`)
- **Real-time:** SignalR — групповой чат курса + push-уведомления (NotificationsHub)
- **Локализация:** `IStringLocalizer<SharedResource>`, RU/EN, ~150 ключей

## Архитектура — Clean Architecture, 4 проекта

```
EduPlatform.slnx
├── EduPlatform.Domain/              — POCO-сущности (зависит только от Identity)
│   └── Entities/                    — Course/Section/Lesson/LessonAttachment,
│                                      Test/Question/AnswerOption/TestAttempt/StudentAnswer,
│                                      Enrollment/LessonProgress, SubscriptionPlan/InstructorSubscription,
│                                      Transaction, Certificate, Review, Notification, Category,
│                                      ApplicationUser
│
├── EduPlatform.Application/         — Бизнес-логика, не знает про EF/SignalR конкретно
│   ├── Abstractions/
│   │   ├── IApplicationDbContext.cs — все DbSet<T> + SaveChangesAsync
│   │   ├── INotificationPusher.cs   — push real-time без знания о SignalR
│   │   └── IEmailSender.cs
│   └── Services/
│       ├── CertificateService.cs
│       ├── NotificationService.cs
│       └── SlugHelper.cs            — RU→latin транслит
│
├── EduPlatform.Infrastructure/      — Реализации
│   ├── Persistence/
│   │   ├── ApplicationDbContext.cs  — IdentityDbContext<...> + IApplicationDbContext
│   │   └── DbSeeder.cs              — runtime-сид: роли, админ, демо-инструктор,
│   │                                  8 категорий, 12 курсов, 9 уроков × 5 курсов,
│   │                                  2 финальных теста. Демо-пароли сбрасываются каждый запуск.
│   ├── Migrations/Init              — все таблицы Identity + домен
│   ├── Email/                       — FileEmailSender, SmtpEmailSender (MailKit)
│   ├── Mongo/MongoLogService.cs     — graceful degradation
│   ├── Audit/UserActionLogger.cs    — scoped wrapper для аудита
│   ├── Logging/HttpLoggingMiddleware.cs
│   └── Chat/ChatMessageStore.cs     — Mongo + in-memory fallback
│
└── dreams/ (Web)                    — Controllers, Views, Hubs, Program.cs, wwwroot
    ├── Controllers/                 — Home, Error, Account, Courses, Reviews,
    │                                  Instructor, Instructors (public), Student,
    │                                  Chat, Notifications, Admin
    ├── Hubs/                        — CourseChatHub, NotificationsHub
    ├── Infrastructure/              — SignalRNotificationPusher (impl INotificationPusher)
    ├── Models/
    │   ├── Account/                 — Register/Login/2FA/Reset VMs
    │   ├── Courses/                 — ~10 VMs для каталога/курса/тестов/wizard
    │   └── Home/HomePageViewModel
    ├── Resources/SharedResource.{ru,en}.resx   — ~150 ключей
    ├── Views/                       — Home, Account, Courses, Subscription, Student,
    │                                  Instructor, Instructors, Chat, Notifications,
    │                                  Admin, Error, Shared
    └── App_Data/mail/               — dev-почта в .gitignore
```

Зависимости идут **только внутрь**: `Web → Application + Infrastructure + Domain`; `Infrastructure → Application + Domain`; `Application → Domain`.

## Сделано

### Студент
- Каталог из БД, фильтры/поиск/сорт/пагинация
- Запись на курс (бесплатно или **за кредиты** с реальным списанием + комиссия)
- Прохождение уроков (видео + текст + **прикреплённые файлы**)
- Тесты 3 типов: single / multi / **text answer**
- Авто-сертификат
- **Отзывы и рейтинги** с гистограммой
- **Чат курса** (SignalR + Mongo)
- **Уведомления** (страница + bell-badge + SignalR toast в реальном времени)
- Реальный профиль + смена пароля + **история действий из user_actions**

### Инструктор
- CRUD курсов + **5-шаговый wizard**
- **CRUD тестов** с гейтом по подписке (Basic/Pro)
- **Загрузка файлов** к уроку (50 МБ)
- **Per-course analytics** + общий **дашборд**
- Подписка с **покупкой за кредиты**
- Публичная страница `/instructors/{id}`

### Админ
- Dashboard, users, courses, categories, plans, subscriptions, logs
- **Reviews** (модерация)
- **Certificates** (реестр + отзыв)
- **Finance** (комиссия / обороты / топ-инструкторы / транзакции)

### Auth
- Register + email confirm
- Login + 2FA TOTP
- **Forgot password + reset через email**

### Прочее
- Real SMTP via **MailKit** + fallback
- Локализация RU/EN всех публичных страниц
- 404 / 403 / about
- Clean Architecture (4 проекта)
- Главная переписана с моков на динамические данные (топ курсов, инструкторы, статистика)

## TODO

- AJAX-пагинация каталога (сейчас server-side)
- Кастомный ExceptionHandler middleware
- Локализация админки (сейчас только публичка)
- Юнит-тесты для Application-сервисов
- CI на GitHub Actions

## Auth-флоу

- `RequireConfirmedEmail = true`. Регистрация → письмо → confirm-link → можно логиниться.
- `/account/forgot` → email со ссылкой → `/account/reset` → новый пароль.
- `/account/security` → `/account/2fa-setup` (QR через api.qrserver.com) → enable.
- На логине `RequiresTwoFactor` → `/account/login-2fa`.
- Logout — POST с antiforgery.

## Полезное / грабли

### Запуск

```powershell
Get-Process dreams -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build C:\kfu\oris\Dream\dreams\EduPlatform.slnx
$env:ASPNETCORE_ENVIRONMENT='Development'  # для user-secrets
Start-Process dotnet -ArgumentList @('run','--project','C:/kfu/oris/Dream/dreams/dreams/dreams.csproj','--no-launch-profile','--no-build') -WindowStyle Hidden
```

Слушает `http://localhost:5000` (default Kestrel).

### Миграции

```powershell
dotnet ef migrations add <Name> --project EduPlatform.Infrastructure --startup-project dreams
dotnet ef database update --project EduPlatform.Infrastructure --startup-project dreams
```

`MigrationsAssembly` явно задан на `EduPlatform.Infrastructure` в `AddDbContext`, поэтому EF ищет миграции в правильной сборке.

### Грабли

- **Razor hot-reload капризный** — `_Layout.cshtml` часто требует полного перезапуска.
- **`MapStaticAssets()` в .NET 10** — build-time манифест. Заменено на `UseStaticFiles()`.
- **PowerShell 5.1 + кодировки** — без `-Encoding utf8` ломает UTF-8.
- **Razor escape `@`** — `@@` для литерала, `@@media` в CSS, `@@microsoft/signalr` в `<script src>`.
- **`@section` зарезервировано Razor** — нельзя переменная `section` в `@foreach`, парсится как директива. Используем `sec`.
- **SQL Server множественные каскадные пути** — часть FK → `Restrict/NoAction`.
- **bool-checkbox без TagHelper** — `<input type=checkbox value=true>` ПЕРЕД `<input type=hidden value=false>`, иначе ModelBinder возьмёт false.
- **.NET 10 Razor энкодит non-ASCII в `&#x...;`** — лечится `services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All))`.
- **`dotnet build` падает на залоченном .exe** после `dotnet run`. `Get-Process dreams | Stop-Process -Force` перед rebuild.
- **`AnswerOption` без `OrderIndex`** — EF не гарантирует порядок отдачи.
- **ThemeForest/CodeCanyon превью защищены Cloudflare** — `curl`/`WebFetch` → 403. Duralux пришлось воспроизводить вручную.
- **`MigrationsAssembly` после раскола проектов** — без явного указания EF ищет миграции в Web-сборке и падает на `InvalidOperationException`.
- **User-secrets подхватываются только в Development** — добавил `builder.Configuration.AddUserSecrets<Program>(optional: true)` чтобы работало и без `ASPNETCORE_ENVIRONMENT=Development`.
- **Brevo SMTP на порту 587 рвёт соединение для новых аккаунтов** — переключайся на **2525**, у Brevo это альтернативный порт с мягче политикой.
- **Class lib без Web SDK** — для использования `HttpContext`, `IConfiguration`, `ILogger` в Infrastructure нужно `<FrameworkReference Include="Microsoft.AspNetCore.App" />` + явные `using` в каждом файле (ImplicitUsings ASP.NET-типы не включает).

### Тестовые аккаунты

```
admin@eduplatform.local            / Admin!Pass1         — Admin
demo.instructor@eduplatform.local  / DemoInstructor!1    — Instructor (12 seed-курсов)
```

Пароли сбрасываются сидером на каждом старте, всегда совпадают.

### Тестовая БД

```sql
SELECT Email, FullName, Credits, IsBlocked FROM AspNetUsers;
SELECT u.Email, r.Name FROM AspNetUserRoles ur
  JOIN AspNetUsers u ON ur.UserId = u.Id
  JOIN AspNetRoles r ON ur.RoleId = r.Id;
SELECT Code, MaxCourses, CommissionPercent, AllowsTests, PriceCredits FROM SubscriptionPlans;
```

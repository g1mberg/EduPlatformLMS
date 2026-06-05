# EduPlatform LMS

> Учебная платформа онлайн-курсов: каталог, кабинеты студента/инструктора/админа, тесты с автоматической выдачей сертификатов, подписочные планы, 2FA. Pet-проект, .NET 10 / ASP.NET MVC / EF Core 10 / SQL Server.

[![.NET](https://img.shields.io/badge/.NET-10%20preview-512BD4)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4)](https://learn.microsoft.com/ef/core/)
[![Bootstrap](https://img.shields.io/badge/Bootstrap-5-7952B3?logo=bootstrap)](https://getbootstrap.com/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-Express-CC2927?logo=microsoftsqlserver)](https://www.microsoft.com/en-us/sql-server)
[![MongoDB](https://img.shields.io/badge/MongoDB-Logs-47A248?logo=mongodb)](https://www.mongodb.com/)

---

## О проекте

**EduPlatform** — LMS-платформа с тремя ролями (Student / Instructor / Admin) и полным циклом «купи подписку → создай курс → продавай → пройди тест → получи сертификат». Делалась как pet-проект, чтобы покрыть весь стандартный набор задач продуктовой разработки: аутентификация со 2FA, RBAC, доменная модель из ~20 сущностей, EF-миграции, файловое подтверждение email, локализация RU/EN, отдельная админка, NoSQL-логирование, custom CSS-фреймворк.

## Что внутри

- 🎓 **Каталог курсов** из БД — фильтры по категории и цене, поиск, 5 видов сортировки, пагинация.
- 👨‍🏫 **Кабинет инструктора** — CRUD курсов c автогенерацией slug (транслит RU→latin), проверка лимита курсов по активной подписке (Free=3 / Basic=10 / Pro=∞).
- 👩‍🎓 **Кабинет студента** — запись на курс, просмотр уроков (видео + текст), пошаговая отметка прогресса, пересчёт `ProgressPercent` после каждого действия.
- ✅ **Тесты и сертификаты** — финальный тест с проходным баллом 70%, автоматическая выдача `Certificate` с уникальным номером при выполнении условий, страница сертификата с печатью.
- 🔐 **Auth полный** — ASP.NET Identity (Guid keys), email confirmation через `.eml`-файлы (без SMTP в dev), TOTP-двухфакторка с QR-кодом.
- 🛡 **Админка** в стиле Duralux — отдельный layout с собственным CSS (~250 LoC), dashboard с метриками, CRUD пользователей с начислением кредитов и блокировкой, CRUD категорий, редактирование планов подписок, просмотр MongoDB-логов.
- 📊 **MongoDB-логи** — middleware пишет каждый HTTP-запрос в `http_logs`, ключевые действия (`login`, `enroll`, `admin.credits.grant`, …) — в `user_actions`. Graceful degradation: если Mongo выключен, приложение работает без логов и показывает warning в `/admin/logs`.
- 🌐 **Локализация RU/EN** — `IStringLocalizer<SharedResource>`, `.resx`-файлы, переключение языка через `?culture=` или cookie, провайдеры в priority-order.

## Стек

| Слой        | Технологии |
|-------------|-----------|
| Backend     | ASP.NET MVC, .NET 10 (preview), Razor Views |
| ORM         | Entity Framework Core 10 + Microsoft SQL Server Express |
| Auth        | ASP.NET Identity `IdentityUser<Guid>` + `IdentityRole<Guid>`, email confirmation, 2FA TOTP (`otpauth://`) |
| NoSQL       | MongoDB.Driver 3 (HTTP-логи, действия пользователя) |
| Frontend    | Bootstrap 5, custom CSS (admin.css ≈ 250 LoC), Dreams LMS template для публички |
| Локализация | `IStringLocalizer` + `.resx` (ru / en) + cookie/query provider |
| Email (dev) | `FileEmailSender` пишет `.eml` в `App_Data/mail/` |

## Скриншоты

> Положу позже в `docs/screenshots/` — пока в репозитории только код.

## Запуск локально

### Требования

- **.NET 10 SDK (preview)** — https://dotnet.microsoft.com/download/dotnet/10.0
- **SQL Server Express** на `.\SQLEXPRESS` с Windows Auth
- (опционально) **MongoDB** на `localhost:27017` — для просмотра логов в `/admin/logs`

### Поехали

```powershell
git clone https://github.com/g1mberg/EduPlatformLMS.git
cd EduPlatformLMS

# Применить миграции (создаст БД EduPlatform на .\SQLEXPRESS)
dotnet ef database update --project dreams/dreams.csproj

# Запуск
dotnet run --project dreams/dreams.csproj --no-launch-profile
```

Открыть http://localhost:5000.

При первом запуске сидер автоматически создаст:
- 3 роли (Student / Instructor / Admin)
- 3 плана подписки (Free / Basic / Pro)
- 8 категорий, демо-аккаунты, 12 курсов с уроками и 2 финальными тестами

### Тестовые аккаунты

| Email | Пароль | Роль |
|---|---|---|
| `admin@eduplatform.local` | `Admin!Pass1` | Admin |
| `demo.instructor@eduplatform.local` | `DemoInstructor!1` | Instructor (12 seed-курсов) |

Можно зарегистрировать любого студента — confirmation-письмо положится в `dreams/App_Data/mail/*.eml`, оттуда копируешь confirm-ссылку.

### MongoDB (опционально)

```powershell
# Docker:
docker run -d --name mongo-edu -p 27017:27017 mongo:7
```

Без Mongo приложение работает — middleware просто молча no-op, `/admin/logs` показывает warning.

## Структура

```
dreams/
├── Controllers/                — Account, Home, Courses, Instructor, Student, Admin, Language
├── Data/
│   ├── ApplicationDbContext.cs — IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
│   └── DbSeeder.cs             — runtime-сидер (категории, demo-юзеры, курсы, уроки, тесты)
├── Models/
│   ├── Entities/               — ~20 POCO: ApplicationUser, Course/Section/Lesson,
│   │                             Test/Question/AnswerOption/TestAttempt/StudentAnswer,
│   │                             Enrollment/LessonProgress, SubscriptionPlan/
│   │                             InstructorSubscription, Transaction, Certificate, и т.д.
│   ├── Account/                — RegisterVM, LoginVM, TwoFactorVMs
│   └── Courses/                — CatalogVM, CourseFormVM, StudentLessonVM, и т.д.
├── Services/
│   ├── IEmailSender + FileEmailSender
│   ├── CertificateService      — выдача сертификата при 100% прогрессе и сданных тестах
│   ├── SlugHelper              — RU→latin транслит
│   ├── MongoLogService         — singleton с graceful degradation
│   ├── HttpLoggingMiddleware   — пишет HTTP-запросы в http_logs
│   └── UserActionLogger        — scoped wrapper для аудита
├── Migrations/Init             — все таблицы Identity + домен
├── Resources/                  — SharedResource.{ru,en}.resx
├── Views/
│   ├── Shared/_Layout, _InnerLayout, _DashboardLayout, _AuthLayout, _AdminLayout
│   ├── Account/ Courses/ Student/ Instructor/ Admin/ Home/ Subscription/
│   └── _ViewImports.cshtml     — @inject IHtmlLocalizer<SharedResource> L
├── wwwroot/
│   ├── assets/                 — шаблон Dreams LMS + custom admin.css
│   └── ...
├── App_Data/mail/              — dev-почта в .gitignore
├── Program.cs                  — DI, Identity, миграции, миддлвары, локализация, WebEncoders
└── appsettings.json            — ConnStr, Email, MongoDB, App
```

## Реализованные пункты ТЗ

- ✅ Каталог курсов из БД (фильтры/поиск/сорт/пагинация)
- ✅ CRUD курсов для инструктора с лимитом по подписке
- ✅ Запись на курс, прогресс, страницы уроков, отметка пройденного
- ✅ Финальный тест с автовыдачей сертификата
- ✅ Email confirmation + 2FA TOTP
- ✅ Админка: dashboard, users (кредиты + блокировка), courses, categories CRUD, plans, subscriptions
- ✅ MongoDB-логи (HTTP + user actions) с graceful degradation
- ✅ Локализация RU/EN

## TODO / Roadmap

- [ ] SignalR-чат курса (история сообщений в MongoDB)
- [ ] CRUD теста для инструктора (сейчас только сидер)
- [ ] Покупка подписки за кредиты с реальным списанием
- [ ] Wizard добавления курса в 5 шагов
- [ ] Реальный SMTP вместо `FileEmailSender`
- [ ] Clean Architecture: вынести Domain / Application / Infrastructure / Web
- [ ] Полная локализация всех вьюх (сейчас покрыты nav и часть auth)

## Лицензия

[MIT](LICENSE)

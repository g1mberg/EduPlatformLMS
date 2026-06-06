# EduPlatform LMS

> Учебная платформа онлайн-курсов: каталог, кабинеты студента/инструктора/админа, тесты с автовыдачей сертификатов, подписки за виртуальные кредиты, real-time чат курса и push-уведомления, 2FA, MailKit-SMTP. Pet-проект, .NET 10 / ASP.NET MVC / EF Core 10 / SQL Server / MongoDB / SignalR.

[![.NET](https://img.shields.io/badge/.NET-10%20preview-512BD4)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4)](https://learn.microsoft.com/ef/core/)
[![Bootstrap](https://img.shields.io/badge/Bootstrap-5-7952B3?logo=bootstrap)](https://getbootstrap.com/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-Express-CC2927?logo=microsoftsqlserver)](https://www.microsoft.com/en-us/sql-server)
[![MongoDB](https://img.shields.io/badge/MongoDB-Logs%20%2B%20Chat-47A248?logo=mongodb)](https://www.mongodb.com/)
[![SignalR](https://img.shields.io/badge/SignalR-Chat%20%2B%20Notifications-FF6D00)](https://learn.microsoft.com/aspnet/core/signalr/)

---

## О проекте

**EduPlatform** — LMS-платформа с тремя ролями (Student / Instructor / Admin) и полным циклом «купи подписку → создай курс через 5-шаговый wizard → продавай за кредиты → пройди тест → получи сертификат → оставь отзыв». Pet-проект, покрывает весь стандартный набор задач продуктовой разработки: аутентификация со 2FA, RBAC, доменная модель из ~20 сущностей, EF-миграции, реальный SMTP через MailKit (с fallback на .eml файлы), real-time чат и уведомления через SignalR, локализация RU/EN, отдельная админка, MongoDB-логирование, кастомный CSS-фреймворк.

## Что внутри

### Для студента
- 🎓 **Каталог из БД** — фильтры по категории/цене, поиск, 5 сортировок, пагинация
- 📚 **Прохождение курса** — запись на курс, видео+текст уроки, прикреплённые файлы для скачивания, пошаговая отметка прогресса
- ✅ **Тесты** трёх типов: single-choice, multiple-choice, **текстовый ответ** (сравнение с эталоном)
- 🏆 **Авто-сертификат** при 100% прогрессе + сданных тестах
- ⭐ **Отзывы и рейтинги** курса (1-5 звёзд + текст), гистограмма, своя оценка с возможностью изменить/удалить
- 💬 **Чат курса** — групповой real-time через SignalR, история в MongoDB
- 🔔 **Уведомления** — push через SignalR в реальном времени с toast в углу + страница `/student/notifications` + bell-badge в шапке

### Для инструктора
- 🪄 **5-шаговый wizard** создания курса (инфо → разделы → уроки → финальный тест → публикация) — всё в одной транзакции
- 📝 **CRUD тестов** к урокам — single/multi/text questions, автопроверка, гейт по подписке
- 📎 **Загрузка файлов** к уроку (PDF, изображения, до 50 МБ)
- 📊 **Per-course analytics** (`/instructor/courses/{id}/analytics`) — 8 KPI, бар-чарт записей за 6 мес, топ-студенты, отзывы
- 📈 **Дашборд** инструктора со сводкой по всем курсам и выручке
- 💳 **Подписки за кредиты**: Free (3 курса, 30% комиссии) / Basic (10 курсов, 20% / тесты+сертификаты) / Pro (∞ курсов, 10%)
- 💰 **Реальная экономика**: студент купил курс за N кр. → инструктору поступило `N - комиссия`, платформе — комиссия. Транзакции пишутся обе стороны.
- 🌐 **Публичная страница** `/instructors/{id}` с био, аватаром, статистикой и списком курсов

### Для админа
- 🛡 **Полная админка** в стиле Duralux с собственным CSS (~250 LoC, без покупки шаблона)
- 👥 **Управление пользователями** — фильтры, начисление кредитов, блокировка, 2FA-статус
- 📚 **Управление курсами** — публикация/снятие, удаление с защитой от удаления с записями
- 💵 **Финансы** — общая комиссия платформы (за всё время и окно), оборот, выручка с подписок, топ-10 инструкторов, последние 500 транзакций
- ⭐ **Модерация отзывов** — фильтры «все/на модерации/одобренные», одобрить/скрыть/удалить с авто-пересчётом рейтинга
- 🏆 **Реестр сертификатов** — поиск по №/email/курсу, отзыв сертификата
- 🏷 **CRUD категорий и планов подписок**
- 📋 **MongoDB-логи** — HTTP-запросы (`http_logs`) + действия юзера (`user_actions`) с фильтрами

### Прочее
- 🔐 **Полный auth** — Identity (Guid keys), email confirmation, **password reset** через email, 2FA TOTP с QR-кодом
- 📧 **Реальный SMTP через MailKit** с graceful fallback в `.eml` файлы при ошибке/без конфига. Секреты через `user-secrets`
- 🌐 **Локализация RU/EN** — `IStringLocalizer<SharedResource>`, `.resx`, ~150 ключей, переключение через `?culture=` или cookie
- 📜 **История действий** в профиле — последние 30 user_actions с человекочитаемыми названиями
## Стек

| Слой | Технология |
|---|---|
| Backend | ASP.NET MVC, .NET 10 (preview), Razor Views |
| ORM | EF Core 10 + SQL Server Express |
| Auth | ASP.NET Identity `IdentityUser<Guid>`, email confirmation, password reset, 2FA TOTP |
| NoSQL | MongoDB.Driver 3 (HTTP-логи, user actions, история чата) |
| Real-time | SignalR (чат курса + persistent уведомления) |
| Email | MailKit (SMTP) с fallback на `FileEmailSender` (.eml в `App_Data/mail/`) |
| Frontend | Bootstrap 5, custom CSS (admin.css ≈ 250 LoC, кастомный home hero), Dreams LMS template для прочего |
| Локализация | `IStringLocalizer` + `.resx` (ru / en) с cookie/query provider |

## Запуск локально

### Требования

- **.NET 10 SDK (preview)** — https://dotnet.microsoft.com/download/dotnet/10.0
- **SQL Server Express** на `.\SQLEXPRESS` с Windows Auth
- (опционально) **MongoDB** на `localhost:27017` — для просмотра логов в `/admin/logs` и истории чата

### Поехали

```powershell
git clone https://github.com/g1mberg/EduPlatformLMS.git
cd EduPlatformLMS

# Миграции (применяются автоматически из сборки EduPlatform.Infrastructure)
dotnet ef database update --project EduPlatform.Infrastructure --startup-project dreams

# Запуск
dotnet run --project dreams --no-launch-profile
```

Открыть http://localhost:5000.

При первом запуске сидер автоматически создаст:
- 3 роли (Student / Instructor / Admin)
- 3 плана подписки (Free / Basic / Pro)
- 8 категорий, демо-аккаунты, 12 курсов с уроками и 2 финальными тестами

Демо-пароли каждый раз сбрасываются сидером — всегда соответствуют README.

### Тестовые аккаунты

| Email | Пароль | Роль |
|---|---|---|
| `admin@eduplatform.local` | `Admin!Pass1` | Admin |
| `demo.instructor@eduplatform.local` | `DemoInstructor!1` | Instructor (12 seed-курсов) |

Регистрируешь студента — confirmation-письмо ляжет в `dreams/App_Data/mail/*.eml`, оттуда копируешь confirm-ссылку. Или настрой реальный SMTP (см. ниже).

### Реальный SMTP (Brevo, Yandex, Gmail, …)

Через **user-secrets** (в репозиторий не попадают):

```powershell
dotnet user-secrets init --project dreams
dotnet user-secrets set "Email:Smtp:Host"     "smtp-relay.brevo.com" --project dreams
dotnet user-secrets set "Email:Smtp:Port"     "2525"                 --project dreams
dotnet user-secrets set "Email:Smtp:User"     "<your-smtp-login>"    --project dreams
dotnet user-secrets set "Email:Smtp:Password" "<your-smtp-key>"      --project dreams
dotnet user-secrets set "Email:Smtp:From"     "<verified-sender>"    --project dreams
dotnet user-secrets set "Email:Smtp:FromName" "EduPlatform LMS"      --project dreams
```

При наличии `Email:Smtp:Host` — используется MailKit. Без него — старый dev-режим с `.eml` файлами. При ошибке отправки — graceful fallback в файл, приложение не падает.

### MongoDB (опционально)

```powershell
docker run -d --name mongo-edu -p 27017:27017 mongo:7
```

Без Mongo приложение работает — middleware просто no-op, `/admin/logs` показывает warning, чат хранится в in-memory ring buffer (200 сообщений / курс).

## Карта основных URL

### Публично
- `/` — главная с динамической статистикой, топ-курсами, категориями, топ-инструкторами
- `/about` — о платформе
- `/courses`, `/courses/{slug}` — каталог и страница курса (отзывы, чат, покупка)
- `/instructors/{id}` — публичная страница инструктора
- `/account/login`, `/account/register`, `/account/forgot`, `/account/reset` — auth
- `/error/404`, `/error/403` — собственные страницы ошибок

### Студент
- `/student/courses`, `/student/courses/{slug}`, `/student/courses/{slug}/lessons/{id}`
- `/student/certificates`, `/student/notifications`
- `/account/profile` — реальный профиль с историей действий из MongoDB + смена пароля

### Инструктор
- `/instructor/dashboard` — общий дашборд (KPI + чарт + топ курсов + последние записи)
- `/instructor/courses`, `/instructor/courses/wizard`, `/instructor/courses/{id}/edit`
- `/instructor/courses/{id}/tests`, `/instructor/courses/{id}/analytics`
- `/instructor/lessons/{id}/attachments` — файлы урока
- `/instructor/lessons/{id}/test/edit` — редактор теста
- `/instructor/subscription` — план + покупка за кредиты

### Админ
- `/admin` — dashboard с метриками
- `/admin/users`, `/admin/courses`, `/admin/categories`, `/admin/plans`, `/admin/subscriptions`
- `/admin/reviews` — модерация отзывов
- `/admin/certificates` — реестр сертификатов
- `/admin/finance` — финансы платформы (комиссия, обороты, топ-инструкторы, транзакции)
- `/admin/logs` — MongoDB-логи

## Лицензия

[MIT](LICENSE)

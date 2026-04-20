# FinPair API-контракт v1

## 1. Назначение документа

Этот документ описывает согласованный API-контракт между `finpair-frontend` и `finpair-backend`.

Цель документа:
- зафиксировать HTTP API, которое требуется фронтенду;
- определить единые правила авторизации, форматы запросов и ответов;
- исключить расхождения между экранами SPA и backend-сервисами;
- зафиксировать спорные места, которые должны быть решены явно, а не “по ходу”.

Документ основан на текущих README frontend и backend репозиториев. Он покрывает:
- авторизацию и регистрацию;
- подключение партнёра по invite code;
- дашборд;
- транзакции;
- аналитику;
- цели;
- настройки;
- поддержку.

---

## 2. Согласование с frontend README

Контракт покрывает все основные пользовательские сценарии, заявленные во frontend README:

- регистрация по email;
- вход в систему;
- подключение партнёра по коду;
- просмотр общего финансового состояния пары;
- просмотр и добавление транзакций;
- просмотр аналитики;
- управление целями;
- изменение настроек;
- доступ к FAQ и поддержке.

### Важное уточнение

Во frontend README есть формулировка **«чат поддержки»**.  
В рамках текущего v1-контракта это трактуется как **отправка обращения в поддержку**, а не полноценный realtime-чат.

Если потребуется именно чат, контракт должен быть расширен:
- списком диалогов;
- списком сообщений;
- long polling / WebSocket / SSE;
- статусами доставки и прочтения.

---

## 3. Согласование с backend README

Контракт разложен по backend-доменам:

- **AuthService** — авторизация, регистрация, токены, email;
- **CoupleService** — связь партнёров, invite code, настройки пары;
- **FinanceService** — профиль финансовых настроек, транзакции, dashboard;
- **AnalyticsService** — аналитика и графики;
- **GoalService** — цели;
- **SupportService** — FAQ, контакты, обращения в поддержку.

### Важное уточнение

В backend README нет отдельного `SettingsService`, хотя frontend требует экран настроек.  
Поэтому в данном контракте настройки разложены так:

- email → `AuthService`
- income / currency / notifications → `FinanceService`
- split type / invite code → `CoupleService`

Это нужно считать **зафиксированным решением v1**.

---

## 4. Базовые правила API

### Base URL

```text
/api/v1
```

### Формат данных

```http
Content-Type: application/json
Accept: application/json
```

### Авторизация

Для защищённых endpoint’ов используется:
- access token;
- refresh token;
- cookie-based refresh flow.

### Обязательное правило для фронтенда

Frontend должен отправлять запросы с cookie:

```js
credentials: "include"
```

или эквивалентной настройкой HTTP-клиента.

---

## 5. Модель авторизации: access token + refresh token + cookies

Это обязательная часть контракта, потому что без неё требования “рефреши” и “куки” считаются непокрытыми.

### Принятая схема

- **access token** возвращается в JSON body;
- **refresh token** хранится **только** в `HttpOnly` cookie;
- frontend **не читает refresh token из JavaScript**;
- endpoint refresh читает refresh token из cookie;
- logout очищает refresh cookie.

### Требования к refresh cookie

Сервер должен выставлять refresh token cookie с параметрами:

- `HttpOnly`
- `Secure`
- `SameSite=Lax` или `SameSite=None` — в зависимости от окружения и схемы размещения frontend/backend
- `Path=/api/v1/auth`
- разумный `Max-Age` / `Expires`

### Поведение frontend

Frontend:
- хранит access token в памяти приложения;
- при `401 Unauthorized` может вызвать refresh endpoint;
- после успешного refresh повторяет исходный запрос;
- не хранит refresh token в localStorage / sessionStorage.

### Открытый вопрос по окружению

Для production нужно явно зафиксировать:
- frontend и backend находятся на одном домене или нет;
- нужен ли `SameSite=None`;
- нужен ли отдельный CSRF-механизм.

До этого момента v1 предполагает cookie refresh flow без отдельного CSRF-эндпоинта.

---

## 6. Общий формат ответа

### Успешный ответ

```json
{
  "data": {},
  "error": null,
  "meta": {}
}
```

### Ошибка

```json
{
  "data": null,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Некорректный запрос",
    "details": {
      "email": ["Email обязателен"]
    }
  },
  "meta": {}
}
```

### Общие правила

- `data` — полезная нагрузка;
- `error` — объект ошибки или `null`;
- `meta` — дополнительные данные: пагинация, служебная информация.

---

## 7. Коды ошибок

Общие коды ошибок:

```text
VALIDATION_ERROR
UNAUTHORIZED
FORBIDDEN
NOT_FOUND
CONFLICT
RATE_LIMITED
INTERNAL_ERROR
PARTNER_ALREADY_LINKED
INVALID_INVITE_CODE
GOAL_NOT_ACCESSIBLE
TRANSACTION_NOT_ACCESSIBLE
EMAIL_ALREADY_EXISTS
INVALID_CREDENTIALS
REFRESH_TOKEN_EXPIRED
REFRESH_TOKEN_INVALID
```

---

## 8. AuthService

Покрывает:
- регистрация;
- логин;
- авторизация;
- refresh;
- logout;
- получение текущего пользователя;
- изменение email.

### 8.1 POST `/auth/register`

Регистрация пользователя по email и паролю.

**request**
```json
{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

**response 201**
```json
{
  "data": {
    "user": {
      "id": "usr_001",
      "email": "user@example.com",
      "emailVerified": false,
      "hasPartner": false
    },
    "accessToken": "jwt_access_token",
    "expiresIn": 900
  },
  "error": null,
  "meta": {}
}
```

**cookies**
- сервер устанавливает refresh token в `HttpOnly` cookie.

---

### 8.2 POST `/auth/login`

Логин по email и паролю.

**request**
```json
{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

**response 200**
```json
{
  "data": {
    "user": {
      "id": "usr_001",
      "email": "user@example.com",
      "emailVerified": true,
      "hasPartner": true
    },
    "accessToken": "jwt_access_token",
    "expiresIn": 900
  },
  "error": null,
  "meta": {}
}
```

**cookies**
- сервер устанавливает refresh token в `HttpOnly` cookie.

---

### 8.3 POST `/auth/refresh`

Обновление access token по refresh cookie.

**request body**
```json
{}
```

Тело запроса не обязательно. Refresh token должен читаться из cookie.

**response 200**
```json
{
  "data": {
    "accessToken": "new_access_token",
    "expiresIn": 900
  },
  "error": null,
  "meta": {}
}
```

**cookies**
- сервер может перевыпустить refresh token cookie.

---

### 8.4 POST `/auth/logout`

Выход из системы.

**request**
```json
{}
```

**response 204**

**cookies**
- сервер очищает refresh token cookie.

---

### 8.5 GET `/auth/me`

Получение текущего пользователя.

**response 200**
```json
{
  "data": {
    "id": "usr_001",
    "email": "user@example.com",
    "emailVerified": true,
    "hasPartner": true
  },
  "error": null,
  "meta": {}
}
```

---

### 8.6 PATCH `/auth/email`

Изменение email пользователя.

**request**
```json
{
  "email": "new@example.com",
  "password": "StrongPass123!"
}
```

**response 200**
```json
{
  "data": {
    "email": "new@example.com",
    "emailVerified": false
  },
  "error": null,
  "meta": {}
}
```

### Правило
Для изменения email требуется подтверждение паролем.

---

## 9. CoupleService

Покрывает:
- состояние пары;
- подключение по invite code;
- получение / перевыпуск invite code;
- настройки способа деления расходов.

### 9.1 GET `/couple`

**response 200**
```json
{
  "data": {
    "id": "cpl_001",
    "inviteCode": "ABCD1234",
    "splitType": "income_ratio",
    "members": [
      {
        "userId": "usr_001",
        "role": "A",
        "email": "a@example.com"
      },
      {
        "userId": "usr_002",
        "role": "B",
        "email": "b@example.com"
      }
    ]
  },
  "error": null,
  "meta": {}
}
```

---

### 9.2 POST `/couple/join`

Подключение партнёра по invite code.

**request**
```json
{
  "inviteCode": "ABCD1234"
}
```

**response 200**
```json
{
  "data": {
    "coupleId": "cpl_001",
    "status": "linked"
  },
  "error": null,
  "meta": {}
}
```

---

### 9.3 POST `/couple/invite-code/regenerate`

Перевыпуск invite code.

**response 200**
```json
{
  "data": {
    "inviteCode": "ZXCV5678"
  },
  "error": null,
  "meta": {}
}
```

---

### 9.4 PATCH `/couple/settings`

Изменение настроек пары.

**request**
```json
{
  "splitType": "equal"
}
```

**allowed values**
```text
equal
income_ratio
custom
```

**response 200**
```json
{
  "data": {
    "id": "cpl_001",
    "splitType": "equal"
  },
  "error": null,
  "meta": {}
}
```

---

## 10. FinanceService

Покрывает:
- dashboard;
- профиль финансовых настроек;
- транзакции;
- категории.

### 10.1 GET `/finance/dashboard`

Агрегированный endpoint для главного экрана.

**query params**
```text
period=month
date=2026-04
```

**response 200**
```json
{
  "data": {
    "currency": "RUB",
    "totalIncome": 320000,
    "totalExpense": 180000,
    "balance": 140000,
    "financialLoadPercent": 56.25,
    "splitType": "income_ratio",
    "partnerSummary": [
      {
        "userId": "usr_001",
        "income": 200000,
        "expense": 110000,
        "sharePercent": 62.5
      },
      {
        "userId": "usr_002",
        "income": 120000,
        "expense": 70000,
        "sharePercent": 37.5
      }
    ]
  },
  "error": null,
  "meta": {}
}
```

---

### 10.2 GET `/finance/profile`

Финансовые настройки пользователя.

**response 200**
```json
{
  "data": {
    "income": 120000,
    "currency": "RUB",
    "notifications": {
      "email": true,
      "push": false
    }
  },
  "error": null,
  "meta": {}
}
```

---

### 10.3 PATCH `/finance/profile`

Изменение финансового профиля и пользовательских настроек.

**request**
```json
{
  "income": 130000,
  "currency": "RUB",
  "notifications": {
    "email": true,
    "push": true
  }
}
```

**response 200**
```json
{
  "data": {
    "income": 130000,
    "currency": "RUB",
    "notifications": {
      "email": true,
      "push": true
    }
  },
  "error": null,
  "meta": {}
}
```

---

### 10.4 GET `/finance/transactions`

Список транзакций.

**query params**
```text
type=expense|income
category=food
userId=usr_001
dateFrom=2026-04-01
dateTo=2026-04-30
page=1
pageSize=20
sortBy=date
sortOrder=desc
```

**response 200**
```json
{
  "data": {
    "items": [
      {
        "id": "txn_001",
        "type": "expense",
        "amount": 2500,
        "currency": "RUB",
        "category": "food",
        "title": "Пятёрочка",
        "userId": "usr_001",
        "date": "2026-04-18T10:15:00Z"
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalItems": 125,
      "totalPages": 7
    }
  },
  "error": null,
  "meta": {}
}
```

---

### 10.5 POST `/finance/transactions`

Создание транзакции.

**request**
```json
{
  "type": "expense",
  "amount": 2500,
  "currency": "RUB",
  "category": "food",
  "title": "Пятёрочка",
  "date": "2026-04-18T10:15:00Z"
}
```

**response 201**
```json
{
  "data": {
    "id": "txn_001",
    "type": "expense",
    "amount": 2500,
    "currency": "RUB",
    "category": "food",
    "title": "Пятёрочка",
    "userId": "usr_001",
    "date": "2026-04-18T10:15:00Z"
  },
  "error": null,
  "meta": {}
}
```

---

### 10.6 GET `/finance/transactions/{transactionId}`

**response 200**
```json
{
  "data": {
    "id": "txn_001",
    "type": "expense",
    "amount": 2500,
    "currency": "RUB",
    "category": "food",
    "title": "Пятёрочка",
    "userId": "usr_001",
    "date": "2026-04-18T10:15:00Z"
  },
  "error": null,
  "meta": {}
}
```

---

### 10.7 PATCH `/finance/transactions/{transactionId}`

**request**
```json
{
  "amount": 2700,
  "category": "groceries",
  "title": "Лента"
}
```

**response 200**
```json
{
  "data": {
    "id": "txn_001",
    "amount": 2700,
    "category": "groceries",
    "title": "Лента"
  },
  "error": null,
  "meta": {}
}
```

---

### 10.8 DELETE `/finance/transactions/{transactionId}`

**response 204**

---

### 10.9 GET `/finance/categories`

Список доступных категорий.

**response 200**
```json
{
  "data": {
    "items": [
      { "id": "food", "name": "Еда" },
      { "id": "transport", "name": "Транспорт" },
      { "id": "salary", "name": "Зарплата" }
    ]
  },
  "error": null,
  "meta": {}
}
```

---

## 11. AnalyticsService

Покрывает экран аналитики.

### 11.1 GET `/analytics/overview`

**query params**
```text
period=month
date=2026-04
```

**response 200**
```json
{
  "data": {
    "averageExpenses": 6000,
    "topCategory": {
      "id": "food",
      "name": "Еда",
      "amount": 42000
    },
    "savingsRatePercent": 18.4,
    "financialLoadPercent": 56.25,
    "partnerComparison": [
      {
        "userId": "usr_001",
        "amount": 110000
      },
      {
        "userId": "usr_002",
        "amount": 70000
      }
    ],
    "expenseTrend": [
      { "date": "2026-04-01", "amount": 2400 },
      { "date": "2026-04-02", "amount": 3100 }
    ],
    "categoryDistribution": [
      { "category": "food", "amount": 42000 },
      { "category": "transport", "amount": 15000 }
    ]
  },
  "error": null,
  "meta": {}
}
```

---

## 12. GoalService

Покрывает:
- список целей;
- создание цели;
- изменение цели;
- удаление цели;
- пополнение цели.

### 12.1 GET `/goals`

**response 200**
```json
{
  "data": {
    "items": [
      {
        "id": "goal_001",
        "title": "Отпуск",
        "targetAmount": 200000,
        "currentAmount": 70000,
        "monthlyContribution": 15000,
        "deadline": "2026-12-01",
        "isShared": true,
        "progressPercent": 35,
        "remainingAmount": 130000,
        "forecastDate": "2026-11-15"
      }
    ]
  },
  "error": null,
  "meta": {}
}
```

---

### 12.2 POST `/goals`

**request**
```json
{
  "title": "Отпуск",
  "targetAmount": 200000,
  "currentAmount": 50000,
  "monthlyContribution": 15000,
  "deadline": "2026-12-01",
  "isShared": true
}
```

**response 201**
```json
{
  "data": {
    "id": "goal_001",
    "title": "Отпуск",
    "targetAmount": 200000,
    "currentAmount": 50000,
    "monthlyContribution": 15000,
    "deadline": "2026-12-01",
    "isShared": true,
    "progressPercent": 25,
    "remainingAmount": 150000,
    "forecastDate": "2027-02-01"
  },
  "error": null,
  "meta": {}
}
```

---

### 12.3 GET `/goals/{goalId}`

**response 200**
```json
{
  "data": {
    "id": "goal_001",
    "title": "Отпуск",
    "targetAmount": 200000,
    "currentAmount": 70000,
    "monthlyContribution": 15000,
    "deadline": "2026-12-01",
    "isShared": true,
    "progressPercent": 35,
    "remainingAmount": 130000,
    "forecastDate": "2026-11-15"
  },
  "error": null,
  "meta": {}
}
```

---

### 12.4 PATCH `/goals/{goalId}`

**request**
```json
{
  "currentAmount": 80000,
  "monthlyContribution": 18000
}
```

**response 200**
```json
{
  "data": {
    "id": "goal_001",
    "currentAmount": 80000,
    "monthlyContribution": 18000,
    "progressPercent": 40,
    "remainingAmount": 120000,
    "forecastDate": "2026-10-20"
  },
  "error": null,
  "meta": {}
}
```

---

### 12.5 DELETE `/goals/{goalId}`

**response 204**

---

### 12.6 POST `/goals/{goalId}/contributions`

Добавление пополнения цели.

**request**
```json
{
  "amount": 10000,
  "date": "2026-04-20"
}
```

**response 201**
```json
{
  "data": {
    "goalId": "goal_001",
    "currentAmount": 90000,
    "progressPercent": 45
  },
  "error": null,
  "meta": {}
}
```

---

## 13. SupportService

### 13.1 GET `/support/faq`

**response 200**
```json
{
  "data": {
    "items": [
      {
        "id": "faq_001",
        "question": "Как подключить партнёра?",
        "answer": "Перейдите в настройки и используйте invite code."
      }
    ]
  },
  "error": null,
  "meta": {}
}
```

---

### 13.2 GET `/support/contacts`

**response 200**
```json
{
  "data": {
    "email": "support@finpair.app",
    "telegram": "@finpair_support"
  },
  "error": null,
  "meta": {}
}
```

---

### 13.3 POST `/support/messages`

Создание обращения в поддержку.

**request**
```json
{
  "subject": "Проблема с целями",
  "message": "Не обновляется прогресс цели"
}
```

**response 201**
```json
{
  "data": {
    "ticketId": "sup_001",
    "status": "created"
  },
  "error": null,
  "meta": {}
}
```

---

## 14. Общие модели

### UserSummary
```json
{
  "id": "usr_001",
  "email": "user@example.com",
  "emailVerified": true,
  "hasPartner": true
}
```

### CoupleMember
```json
{
  "userId": "usr_001",
  "role": "A",
  "email": "a@example.com"
}
```

### Transaction
```json
{
  "id": "txn_001",
  "type": "expense",
  "amount": 2500,
  "currency": "RUB",
  "category": "food",
  "title": "Пятёрочка",
  "userId": "usr_001",
  "date": "2026-04-18T10:15:00Z"
}
```

### Goal
```json
{
  "id": "goal_001",
  "title": "Отпуск",
  "targetAmount": 200000,
  "currentAmount": 70000,
  "monthlyContribution": 15000,
  "deadline": "2026-12-01",
  "isShared": true,
  "progressPercent": 35,
  "remainingAmount": 130000,
  "forecastDate": "2026-11-15"
}
```

---

## 15. Правила валидации

### Email
- обязателен;
- должен быть валидным email;
- должен быть уникальным.

### Password
- обязателен;
- минимальная длина: 8 символов;
- рекомендуется хотя бы 1 буква и 1 цифра.

### Invite code
- обязателен для join endpoint;
- должен быть действительным и неистёкшим.

### Transaction
- `amount > 0`
- `type` обязателен
- `category` обязательна
- `date` обязательна

### Goal
- `title` обязателен
- `targetAmount > 0`
- `currentAmount >= 0`
- `monthlyContribution >= 0`

---

## 16. Пагинация

Для списков используется схема:

```json
{
  "page": 1,
  "pageSize": 20,
  "totalItems": 125,
  "totalPages": 7
}
```

---





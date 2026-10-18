# Frontend (web SPA va Telegram Mini App)

Bitta React 19 + TypeScript (strict) SPA: `web/`. Vite 8, react-router 8 (`createBrowserRouter`).
Yangi bog'liqlik qo'shilmagan: UI-freymvork, CSS-freymvork, state kutubxonasi yoki react-query yo'q.
Server holati uchun kichik o'z hook'larimiz bor (`useQuery`, `useAction`). Uslublar — oddiy CSS va
dizayn tokenlari (yorug' va qorong'i rejim `prefers-color-scheme` orqali), 360 / 768 / 1280 px uchun
moslashuvchan.

Kontrakt: `docs/API.md` (shu jumladan §1.1 BE1a o'zgarishlari). Kontraktda shakli yozilmagan DTO'lar
`web/src/api/types.ts` da **ASSUMED** deb belgilangan va quyidagi "Kontraktdagi bo'shliqlar"
bo'limida sanab o'tilgan.

## 1. Ishga tushirish va tekshirish

```bash
export PATH=$HOME/.local/opt/node/bin:$PATH
cd web
npm ci
npm run dev            # http://localhost:5173, /api va /health → OQTISH_API_DEV_URL (standart :5080)
npm run lint           # oxlint --deny-warnings
npm run typecheck      # tsc -b
npm test               # vitest run (jsdom, API fetch darajasida mock qilinadi)
npm run build          # tsc -b && vite build → web/dist
npm audit --omit=dev
```

Runtime konfiguratsiya: `/config.json` (`{ "apiBaseUrl": "/api" }`, web konteyneri ishga tushganda
`deploy/nginx/40-write-config-json.sh` yozadi). Fayl bo'lmasa — `VITE_API_BASE_URL` yoki `/api`.
Agar `apiBaseUrl` boshqa origin bo'lsa, CSP `connect-src` ni ham kengaytirish kerak (hozir `'self'`).

## 2. Papkalar tuzilishi

```
web/src/
  main.tsx                 kirish nuqtasi: config → api client → router
  config.ts                /config.json o'qish
  api/                     HTTP qatlami
    client.ts              fetch o'rami: ApiError(kind, code, traceId), X-OqTish-Client, Idempotency-Key
    types.ts               DTO turlari (API.md bilan bir xil; ASSUMED belgilari bilan)
    errors.ts              API.md §9 dagi har bir code → i18n kaliti (err.<code>)
    idempotency.ts         IdempotencyKeeper (bir yuborish = bir kalit)
    auth.ts me.ts public.ts patient.ts clinic.ts finance.ts admin.ts telegram.ts   — har bir endpoint
    index.ts context.ts    createApi(), ApiContext / useApi()
  auth/                    SessionProvider (GET /me), guard'lar, LoginPage, Telegram Login Widget
  app/                     routes.tsx (marshrutlar xaritasi), layout'lar, lazy chunk'lar
  features/
    public/                bosh sahifa (qidiruv), klinika profili, shikoyat
    booking/               yozilish ustasi (web va Mini App uchun umumiy)
    patient/               qabullarim, qabul tafsiloti (bekor qilish, sharh)
    account/               profil, telefon/Telegram bog'lash, xavfsizlik (TOTP, parol, seanslar), takliflar
    appointments/          umumiy: holatlar tarixi, hisoblagich, sabab kodlari, formatlash
    clinic/                klinika paneli sahifalari, permissions.ts (menyu va huquqlar)
    finance/               to'lovlar, qoldiq, hisobotlar, hisob-kitoblar, audit
    admin/                 platforma admini sahifalari
  telegram/                Mini App: SDK yuklash, tema, MainButton/BackButton, ekranlar
  i18n/                    messages.ts (agregator) + sections/*.ts (uz → ru, en)
  lib/                     format.ts (pul/vaqt), money.ts (BigInt tiyin), time.ts (IANA zona), phone.ts, script.ts
  hooks/                   useQuery, useAction, useNow, useTitle
  ui/                      holatlar, formalar, dialoglar, vidjetlar, sahifalash
  test/                    harness (fetch mock + router), fiktiv fixture'lar, flows/*.test.tsx
```

`src/demo/data.ts` va `src/domain/types.ts` o'chirildi: ishlab chiqarish ekranlari namunaviy
ma'lumotdan foydalanmaydi. Fiktiv ma'lumot faqat `src/test/fixtures.ts` da.

## 3. Marshrutlar xaritasi

Huquq ustuni: **anon** — kirish shart emas; **user** — kirgan foydalanuvchi; klinika marshrutlarida
a'zolikning `permissions` massividagi huquq (birortasi yetarli); admin marshrutlarida
`platformPermissions`. UI faqat menyu va tugmalarni yashiradi — **haqiqiy tekshiruv serverda**.

### 3.1 Ommaviy sayt va bemor

| Marshrut | Vazifasi | API chaqiruvlari | Huquq |
|---|---|---|---|
| `/` | Klinikalarni qidirish: q, yo'nalish, xizmat, tuman, saralash (reyting/narx/masofa/nom), "7 kun ichida bo'sh vaqt", sahifalash | `GET /public/facets`, `GET /public/clinics` | anon |
| `/clinics/:clinicId` | Klinika profili: filiallar, ish vaqti, xizmatlar va narxlar (Fixed / From), sharhlar, shikoyat | `GET /public/clinics/{id}`, `GET /public/clinics/{id}/reviews`, `POST /reports` | anon (shikoyat — user) |
| `/book/:clinicId` | Yozilish ustasi: filial → xizmat → sana (30 kun) + vaqt → aloqa → tekshirish → yuborish | `GET /public/clinics/{id}`, `GET /public/branches/{id}/availability`, `POST /appointments` (+Idempotency-Key) | ko'rish anon; yuborish user |
| `/login` | Telefon OTP (qayta yuborish hisoblagichi), xodim email+parol (+TOTP), parolni tiklash, Telegram Login Widget | `POST /auth/otp/request`, `/auth/otp/verify`, `/auth/password/login`, `/auth/password/reset/*`, `GET /telegram/config`, `POST /auth/telegram/widget` | anon |
| `/login/telegram` | Login Widget redirect rejimining qaytish manzili | `POST /auth/telegram/widget` | anon |
| `/me` | Profil (ism), telefonni bog'lash (OTP), Telegram bog'lash (deep link) / uzish, hisobni o'chirish | `PUT /me/profile`, `POST /me/phone/*`, `POST /me/telegram/link`, `DELETE /me/telegram`, `POST /me/delete` | user |
| `/me/security` | TOTP sozlash/o'chirish (secret + otpauth URI matn sifatida), parol, seanslar, hamma joydan chiqish | `POST /me/totp/*`, `POST /me/password`, `GET/DELETE /me/sessions`, `POST /auth/logout-all` | user |
| `/me/invitations` | Xodimlik takliflarini qabul qilish | `GET /me/invitations`, `POST /me/invitations/{id}/accept` | user |
| `/me/appointments` | Qabullarim (holat filtri, sahifalash, Pending hisoblagichi) | `GET /me/appointments` | user |
| `/me/appointments/:id` | Tafsilot, holatlar tarixi, bekor qilish (`canCancel`), sharh, klinika ustidan shikoyat | `GET /me/appointments/{id}`, `POST …/cancel`, `POST …/review`, `POST /reports` | user (o'ziniki) |
| `/clinic` | Yagona a'zolik bo'lsa — o'sha klinikaga; aks holda tanlash + ro'yxatdan o'tkazish havolasi | — | user |
| `/clinic/register` | Yangi klinika arizasi | `POST /clinics` | user |

### 3.2 Klinika paneli (`/clinic/:clinicId/…`)

Filial tanlovi URL'da: `?branch=<id>` (menyu havolalari uni saqlaydi). Barcha sahifalar
`GET /clinics/{id}/branches` ni layout'da yuklaydi.

| Marshrut | Vazifasi | API chaqiruvlari | Huquq |
|---|---|---|---|
| `` (dashboard) | Bugungi hisoblagichlar, tushum (faqat server qaytarsa), tezkor havolalar | `GET …/dashboard` | har qanday a'zolik |
| `requests` | Pending so'rovlar: hisoblagich, mos shifokorlar ro'yxati, tasdiqlash / rad etish (sabab kodi); `booking.expired` / `not_pending` / `doctor_busy` da ro'yxat yangilanadi; har 30 s avtomatik yangilanish | `GET …/appointments?status=Pending`, `GET …/eligible-doctors`, `POST …/confirm`, `POST …/reject` | `appointment.triage` |
| `appointments` | Ro'yxat: sana oralig'i, holat, shifokor filtrlari | `GET …/appointments`, `GET …/doctors` | `appointment.view` |
| `appointments/:appointmentId` | Tafsilot va tarix; Pending: tasdiqlash/rad; Confirmed: yakunlash (summa taklif qilingan narx bilan to'ldiriladi), kelmadi, ko'chirish, bekor qilish; qoldiq, to'lovlar, to'lov kiritish, qaytarish, "o'tmagan" | `GET …/appointments/{id}`, `POST …/complete`, `/no-show`, `/reschedule`, `/cancel`, `GET …/balance`, `GET …/payments`, `POST …/payments`, `POST …/payments/{id}/refund`, `/fail` | `appointment.view` (+ har bir amal o'z huquqi bilan) |
| `walk-in` | Qabulxona/telefon orqali yozuv (darhol Confirmed, shifokor tanlanadi) | `GET …/services`, `GET …/doctors`, `GET /public/branches/{id}/availability` (taklif uchun), `POST …/appointments` (+Idempotency-Key) | `appointment.create_walkin` |
| `calendar` | Kun jadvali: ustunlar — shifokorlar, bloklar vaqt bo'yicha joylashtirilgan (filial vaqti) | `GET …/calendar?branchId&date` | `appointment.view` |
| `doctors`, `doctors/:doctorId` | Shifokorlar CRUD, filial/xizmat bog'lanishi, xodim hisobini bog'lash, haftalik jadval muharriri, dam olish (time off) | `GET/POST/PUT …/doctors`, `GET/PUT …/working-hours`, `GET/POST/DELETE …/time-off`, `GET …/staff` (faqat `staff.manage` bo'lsa) | `schedule.manage` / `schedule.own_timeoff` (o'z yozuvi) / `catalog.manage` (ko'rish) |
| `services` | Xizmatlar va har bir filial narxi/davomiyligi | `GET/POST/PUT …/services`, `PUT …/branches/{b}/services/{s}`, `GET /public/facets` | `catalog.manage` |
| `branches` | Filiallar va ish vaqti | `POST/PUT …/branches`, `PUT …/branches/{id}/hours` | `branch.manage` |
| `staff` | A'zolar (rol, doira, filiallar), takliflar; egasi bo'lmagan administrator Owner/Admin rolini bera olmaydi; o'zini tahrirlash yo'q | `GET/PUT/DELETE …/staff`, `GET/POST/DELETE …/invitations` | `staff.manage` |
| `payments` | To'lovlar ro'yxati, tezkor to'lov kiritish (so'nggi 3 kun qabullari), qaytarish, "o'tmagan" | `GET …/payments`, `GET …/appointments`, `GET …/balance`, `POST …/payments` | `payment.record` yoki `finance.report.view` |
| `reports` | Kunlik (oraliq, jami) va oylik hisobot, solishtirish (reconciliation) belgisi | `GET …/reports/daily`, `GET …/reports/monthly` | `finance.report.view` |
| `settlements` | Platforma bilan hisob-kitoblar (faqat ko'rish) | `GET …/settlements` | `finance.settlement.view` |
| `audit` | Klinika audit jurnali | `GET …/audit` | `audit.view` |
| `profile` | Klinika profili va tekshiruv holati | `GET/PUT /clinics/{id}` | ko'rish — a'zolik; tahrir — `clinic.profile.manage` |

### 3.3 Platforma admini (`/admin/…`)

| Marshrut | Vazifasi | API chaqiruvlari | Huquq |
|---|---|---|---|
| `` | Holat bo'yicha klinikalar, bugungi qabullar, ochiq shikoyatlar | `GET /admin/dashboard` | har qanday platforma huquqi |
| `clinics` | Tekshirish: tasdiqlash / rad (sabab) / to'xtatish (sabab) / tiklash | `GET /admin/clinics`, `POST …/approve`, `/reject`, `/suspend`, `/reinstate` | ro'yxat `platform.clinic.verify`; to'xtatish/tiklash `platform.account.suspend` |
| `users` | Hisoblarni to'xtatish / tiklash | `GET /admin/users`, `POST …/suspend`, `/reinstate` | `platform.account.suspend` |
| `reports` | Shikoyatlarni hal qilish | `GET /admin/reports`, `POST …/resolve` | `platform.reports.handle` |
| `reviews` | Sharhlarni e'lon qilish / yashirish | `GET /admin/reviews`, `POST …/publish`, `/hide` | `platform.reports.handle` |
| `commission?clinic=<id>` | Komissiya qoidalari: yaratish, tugatish (tahrirlash yo'q), `contractReference` ko'rsatiladi | `GET/POST /admin/clinics/{id}/commission-rules`, `POST …/end` | `platform.finance.settle` |
| `settlements` | Klinika+oy uchun yaratish, yakunlash, to'langan deb belgilash, bekor qilish | `GET/POST /admin/settlements`, `POST …/finalize`, `/mark-paid`, `/void` | `platform.finance.settle` |
| `tenant-data` | Break-glass: klinika qabullarini ko'rish, sabab (≥15 belgi) `X-Access-Reason` sarlavhasida | `GET /admin/clinics/{id}/appointments` | `platform.tenant-data.read` |
| `audit` | Platforma audit jurnali (clinicId, action filtrlari) | `GET /admin/audit` | `platform.audit.view` |
| `notifications` | Outbox holati, qayta yuborish | `GET /admin/notifications`, `POST …/retry` | `platform.audit.view` |

### 3.4 Telegram Mini App (`/tg/…`)

| Marshrut | Vazifasi | API chaqiruvlari |
|---|---|---|
| `/tg` | Kirish (initData), klinikalarni qidirish | `POST /auth/telegram/webapp`, `GET /public/clinics` |
| `/tg/clinics/:clinicId` | Yozilish (umumiy usta, yuborish — Telegram MainButton) | booking endpoint'lari |
| `/tg/appointments`, `/tg/appointments/:id` | Qabullarim: holat, hisoblagich, bekor qilish | `GET /me/appointments*`, `POST …/cancel` |
| `/tg/staff` | Xodimlar: har bir klinikaning Pending so'rovlari (shifokor tanlab tasdiqlash / rad), bugungi qabullar | `GET /clinics/{id}/appointments`, `GET …/eligible-doctors`, `POST …/confirm`, `/reject` |

## 4. Holat va autentifikatsiya

- **Seans**: `SessionProvider` ilova ochilganda `GET /me` ni chaqiradi (401 → mehmon). Har qanday
  so'rov 401 `auth.unauthenticated` qaytarsa, seans tozalanadi. Token'lar brauzerda saqlanmaydi —
  faqat server cookie'si (`__Host-oqtish`, HttpOnly).
- **Guard'lar** (`auth/guards.tsx`): `RequireAuth` → `/login?next=<joriy yo'l>` (faqat `/` bilan
  boshlanadigan nisbiy `next` qabul qilinadi); klinika a'zoligi bo'lmasa — 403 holati; admin uchun
  `platformPermissions` bo'sh bo'lsa — 403 yoki (agar `Me.totpSetupRequired`) TOTP sozlash holati;
  `RequirePermission` / `RequirePlatformPermission` — bo'lim darajasida. API `auth.totp_setup_required`
  qaytarsa — `/me/security` ga havola bilan TOTP holati ko'rsatiladi.
- **Menyular** `features/clinic/permissions.ts` dagi `clinicMenu` / `adminMenu` va a'zolikning
  `permissions` massivi bo'yicha filtrlanadi (rol nomi bo'yicha emas). `membership.totpSetupRequired`
  bo'lsa, panel TOTP holatini ko'rsatadi (server bu vaqtda `permissions: []` qaytaradi).
- **CSRF**: barcha non-GET so'rovlarda `X-OqTish-Client: web` (Mini App ichida `tg-miniapp`).
- **Idempotency**: booking, walk-in, to'lov va qaytarishda `crypto.randomUUID()`. Bir xil body
  qayta yuborilsa (tarmoq xatosi, 5xx) — o'sha kalit; body o'zgarsa yoki aniq 4xx/muvaffaqiyat
  bo'lsa — yangi kalit (`api/idempotency.ts`).
- **Xatolar**: `ApiError.code` → `errorMessage()` → `err.<code>` kaliti (uz/ru/en). Server matni
  ko'rsatilmaydi; maydon xatolari (`errors`) va 5xx uchun `traceId` ko'rsatiladi.
- **Pul**: `{ amount: string }` hech qachon float'ga aylantirilmaydi; kiritish `lib/money.ts` da satr
  sifatida tekshiriladi, solishtirish/yig'ish BigInt tiyin orqali.
- **Vaqt**: UTC instant'lar filialning IANA zonasida ko'rsatiladi (`lib/time.ts`, faqat `Intl`).
  Hisoblagichlar faqat ma'lumot uchun — muddatni server belgilaydi.

## 5. i18n

- `src/i18n/sections/*.ts`: har bir bo'limda `uz` kalitlarni belgilaydi, `ru` va `en`
  `Record<keyof typeof uz, string>` turida — kalit yetishmasa build yiqiladi. `messages.ts` ularni
  birlashtiradi.
- Testlar: har bir tilda barcha kalitlar va bir xil `{placeholder}`lar; bo'limlar orasida kalit
  takrorlanmasligi; `docs/API.md` §9 dagi **har bir** xato kodi uchun xabar borligi (test hujjatni
  o'qiydi, yangi kod qo'shilsa test yiqiladi).
- Til tanlagich har bir layout'da. Standart — o'zbekcha. Tanlov `localStorage` da (`oqtish.locale`).
  Mini App'da foydalanuvchi o'zi tanlamagan bo'lsa, Telegram `language_code` (uz/ru/en) olinadi.

## 6. Telegram Mini App

- SDK (`https://telegram.org/js/telegram-web-app.js`) faqat `/tg` marshrutlarida dinamik yuklanadi.
  `WebApp.ready()`, `expand()`, `themeParams` → CSS o'zgaruvchilari (`--bg`, `--text`, `--accent` …),
  `themeChanged` hodisasi kuzatiladi. `BackButton` ichki sahifalarda, `MainButton` yozilishni yuborishda.
- Kirish: `POST /api/auth/telegram/webapp { initData }`; TOTP yoqilgan hisoblar uchun `totpCode`
  so'raladi. `initData` hech qayerda saqlanmaydi va log qilinmaydi (test buni tekshiradi).
- Telegram'dan tashqarida (`initData` bo'sh) — tushunarli xabar va oddiy `/login` havolasi.
- **BotFather sozlamalari**:
  1. `/newapp` (yoki bot → *Bot Settings → Configure Mini App*): Mini App URL = `https://<host>/tg`.
  2. *Menu Button*: `/setmenubutton` → URL `https://<host>/tg`, matn masalan "OqTish".
  3. Login Widget uchun: `/setdomain` → `<host>` (sayt domeni).
  4. Xodimlar uchun to'g'ridan-to'g'ri havola: `https://<host>/tg/staff` (bot tugmasi sifatida ham qo'yish mumkin).
- **Login Widget (sayt)**: `data-onauth` rejimi ishlatilmaydi (u `eval` talab qiladi, CSP taqiqlaydi).
  Agar `GET /telegram/config` `botId` qaytarsa — o'z tugmamiz `Telegram.Login.auth()` (popup);
  aks holda vidjet redirect rejimida `/login/telegram` ga qaytadi (nginx bu manzilni access log'ga
  yozmaydi, SPA so'rov parametrlarini darhol manzil satridan o'chiradi).

## 7. Xavfsizlik sarlavhalari (deploy/nginx)

- `security-headers.conf` (barcha sahifalar): `X-Frame-Options: DENY`, `frame-ancestors 'none'`,
  `script-src 'self' https://telegram.org`, `frame-src https://oauth.telegram.org`, `style-src 'self'`,
  `object-src 'none'`, nosniff, Referrer-Policy, Permissions-Policy (geolocation=self).
  SPA ichida sahifalar mijoz tomonida almashadi, shuning uchun birinchi yuklangan hujjatning CSP'si
  hamma marshrutga tegishli — Login Widget `/login` da ishlashi uchun ruxsat umumiy siyosatda.
- `security-headers-tg.conf` (`/tg`, `/tg/*`): yuqoridagi bilan bir xil, faqat
  `frame-ancestors https://web.telegram.org https://*.telegram.org` va `X-Frame-Options` yo'q.
  `/tg` `rewrite … break` bilan beriladi (ichki redirect yo'q), shuning uchun aynan shu sarlavhalar
  qo'llanadi. 2026-10-18 da `nginx -t` va haqiqiy konteynerda `curl -I` bilan tekshirildi.
- Inline style ishlatilmaydi (React `style` CSSOM orqali o'rnatiladi — CSP bilan mos).

## 8. Qabul qilingan qarorlar (frontend)

| # | Qaror |
|---|---|
| F1 | react-query qo'shilmadi: `useQuery` (kalit bo'yicha, eskirgan javoblarni e'tiborsiz qoldiradi, polling) va `useAction` yetarli; bog'liqliklar soni o'zgarmadi |
| F2 | Yozilish ustasi tanlovlari URL query'da (`?step&branch&service&date&start`) — kirishdan keyin bemor aynan shu joyga qaytadi. Ism/telefon URL'ga qo'yilmaydi (faqat history state) |
| F3 | Idempotency kaliti: tarmoq/5xx da saqlanadi, aniq 4xx yoki muvaffaqiyatdan keyin tashlanadi |
| F4 | Ro'yxat filtrlari: qabullar va to'lovlar uchun `from`/`to` — filial zonasidagi kun boshlari bo'yicha UTC instant'lar, `[from, to)`; kunlik hisobot uchun `YYYY-MM-DD` mahalliy sanalar (inclusive) |
| F5 | Rad etish/bekor qilish sabab kodlari: `NoDoctorAvailable`, `TimeUnavailable`, `ServiceUnavailable`, `PatientUnreachable`, `Other` / `DoctorUnavailable`, `PatientRequest`, `ClinicEmergency`, `Other` |
| F6 | `X-Access-Reason` qiymati `encodeURIComponent` bilan (UTF-8 percent-encoding) — header'lar ISO-8859-1 bo'lishi shart, sabablar o'zbek/rus tilida |
| F7 | Komissiya: foiz UI'da kiritiladi va 4 kasrli ulush satriga aylantiriladi (`7,5` → `"0.0750"`); `effectiveFrom/To` — Asia/Tashkent 00:00 dagi UTC instant; `mark-paid.paidAt` — o'sha kun 12:00 |
| F8 | To'lov turi har safar ongli tanlanadi (standart qiymat yo'q); summa qoldiqdan katta bo'lsa — faqat ogohlantirish, qarorni server qiladi |
| F9 | Yagona mos shifokor bo'lsa, u oldindan tanlanadi (ko'rinib turadi) |
| F10 | TOTP QR kod yo'q (yangi bog'liqliksiz); secret va otpauth URI matn + nusxalash tugmasi |
| F11 | Klinika paneli, admin va Mini App alohida lazy chunk'larda (boshlang'ich yuklama ~166 kB gzip) |
| F12 | Testlar `docs/API.md` ni `?raw` bilan o'qiydi; buning uchun `vite.config.ts` da faqat `VITEST` rejimida `server.fs.allow` ga `../docs` qo'shilgan |
| F13 | Mini App'da `style-src 'self'` saqlandi: Telegram web klienti yuboradigan maxsus `<style>` (kosmetik) bloklanishi mumkin |

## 9. Kontraktdagi bo'shliqlar (backend tasdiqlashi kerak)

1. Javob shakli yozilmagan, UI taxmin qilgan (`api/types.ts`, ASSUMED): `GET /clinics/{id}`,
   dashboard, branches, services (filial narxlari `offers[]` ichida deb taxmin), staff, invitations
   (klinika va `/me/invitations`), doctors (`userId` bilan), time-off, eligible-doctors (`{id,name}[]`),
   calendar, payments, daily (massiv) va monthly (`reconciled`, `commissionRuleConfigured`) hisobotlar,
   settlements, audit, admin ro'yxatlari (dashboard, clinics, users, reports, reviews, commission
   rules, notifications).
2. Bitta qabul to'lovlarini olish usuli yo'q: UI `GET …/payments` ga `appointmentId` qo'shib yuboradi
   va natijani mijozda ham filtrlaydi. Taklif: `appointmentId` filtrini kontraktga qo'shish.
3. `from`/`to` semantikasi (instant yoki sana, inclusive/exclusive) aniqlanmagan — F4 ga qarang.
4. `reasonCode` qiymatlari ro'yxati yo'q — F5 ga qarang.
5. `X-Access-Reason` kodlanishi aniqlanmagan — F6 ga qarang (server percent-decode qilishi kerak).
6. Komissiya `rate` formati va sanalar turi aniqlanmagan — F7.
7. `GET /telegram/config` da `botId` yo'q: eval'siz popup kirish uchun kerak; hozircha redirect rejimi.
8. `Me.totpSetupRequired` umumiy: platforma huquqlari aynan TOTP kutayotganini bilish uchun alohida
   belgi (masalan `platformTotpSetupRequired`) yo'q.
9. `/admin/settlements` va `/clinics/{id}/settlements` sahifalanmagan massiv deb taxmin qilindi.
10. Sharh qoldirilgan-qoldirilmaganini bildiruvchi maydon yo'q — UI formani ko'rsatadi va
    `review.already_exists` ni qayta ishlaydi.
11. Walk-in uchun "shu vaqtda bo'sh shifokorlar" endpoint'i yo'q: UI ommaviy availability
    (faqat tasdiqlangan klinikalar uchun ishlaydi) va shifokorlar ro'yxatidan foydalanadi; band
    bo'lsa server 409 qaytaradi.
12. §5 da approve/reject va suspend/reinstate uchun huquqlar bitta katakda: UI approve/reject uchun
    `platform.clinic.verify`, suspend/reinstate uchun `platform.account.suspend` ni oldi.
13. `/me/invitations*` hali implement qilinmagan (BE1b) — UI tayyor.

## 10. Testlar

Vitest + Testing Library, API `fetch` chegarasida mock qilinadi (`src/test/harness.tsx`), sanalar
uchun fake timer'lar. Asosiy oqimlar: xato kodlari ↔ API.md §9; client sarlavhalari va idempotency;
OTP kirish (qayta yuborish hisoblagichi, TOTP bosqichi), xodim + TOTP; guard'lar (login'ga redirect,
403, TOTP holati); yozilish ustasi (muvaffaqiyat, 409, 422, mehmon); qabulni bekor qilish va
hisoblagich; so'rovni shifokor tanlab tasdiqlash va `booking.expired`; to'lov formasi validatsiyasi
va bir xil Idempotency-Key bilan qayta urinish; menyuni huquqlar bo'yicha filtrlash; admin klinikani
tasdiqlashi va break-glass sarlavhasi; Mini App kirishi (mock `window.Telegram`), tema, til,
MainButton va Telegram'dan tashqari holat.

## 11. Ma'lum cheklovlar

- Ishlayotgan backend'ga qarshi tekshirilmagan (backend parallel yozilmoqda) — faqat kontrakt va mock'lar.
- Haqiqiy Telegram klientlarida va haqiqiy bot bilan Login Widget sinab ko'rilmagan.
- 360/768/1280 px moslashuvchanlik CSS'da yozilgan, lekin haqiqiy brauzerda vizual tekshirilmagan.
- Uchala til katalogi boshlang'ich chunk'da (alohida til yuklash yo'q).
- `/api/dev/*` (Fake SMS va h.k.) uchun UI yo'q — lokal sinovda to'g'ridan-to'g'ri chaqiriladi.
- Kalendar bir kun ichidagi bloklarni ko'rsatadi; yarim tundan o'tadigan bloklar kesiladi.

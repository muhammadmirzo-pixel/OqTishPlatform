# Frontend (web SPA va Telegram Mini App)

Bitta React 19 + TypeScript (strict) SPA: `web/`. Vite 8, react-router 8 (`createBrowserRouter`).
UI-freymvork, CSS-freymvork, state kutubxonasi yoki react-query yo'q. Qayta dizaynda (2026-10-19,
`docs/FRONTEND_PHASE_A.md` §0 "Moviy klinika") faqat `lucide-react` (ikonlar), `@fontsource-variable/inter`
(shrift o'z serverimizda — CSP) va `motion` (cheklangan) qo'shildi; dev: `@playwright/test`,
`@axe-core/playwright`. Server holati uchun kichik o'z hook'larimiz bor (`useQuery`, `useAction`).
Uslublar — oddiy CSS qatlamlari va dizayn tokenlari (yorug' / qorong'i / tizim, qo'lda almashtirish),
360 / 768 / 1280 px da Playwright + axe bilan tekshirilgan (§10).

Kontrakt: `docs/API.md`. Auth/seans (§1, §1.1), ommaviy katalog (§2.1), klinika paneli sozlash
ekranlari (§4.1) va admin dashboard/klinikalar/foydalanuvchilar/audit (§5.1) DTO'lari backend
implementatsiyasi bilan moslashtirilgan (2026-10-18, BE1a/BE1b). Hali yozilmagan shakllar
(booking, moliya, sharhlar, Telegram va h.k.) `web/src/api/types.ts` da **ASSUMED** deb
belgilangan va quyidagi "Kontraktdagi bo'shliqlar" bo'limida sanab o'tilgan.

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
npm run e2e            # Playwright UI to'plami: axe (WCAG 2.2 AA), gorizontal skrol, skrinshotlar — 5 loyiha
npm audit --omit=dev
```

Taxminiy mobil Web Vitals: `npm run build && npx vite preview --port 4173`, keyin
`E2E_PERF=1 E2E_BASE_URL=http://localhost:4173 npx playwright test perf --project=mobile-360`.

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
  styles/                  tokens.css (rang/tip/bo'shliq/motion, yorug'/qorong'i), base, components, layouts, landing
  ui/                      biznes mantiqsiz komponentlar: Button, Dialog (tone="danger"), ReasonDialog/ConfirmDialog
                           (danger, acknowledge), Drawer, Toast(useToast), Skeleton, Tooltip(InfoTip), ContextChip,
                           LineChart (SVG), CountdownRing, Monogram, holatlar (ikonkali), formalar, vidjetlar
  dev/UiGallery.tsx        /__ui komponent galereyasi — faqat development (production build'ga kirmaydi)
  test/                    harness (fetch mock + router), fiktiv fixture'lar, flows/*.test.tsx
web/e2e/                   Playwright: ui.spec.ts (mock API), perf.spec.ts (E2E_PERF), support/mockApi.ts
```

Lazy chunk'lar (`app/chunks/`): `patient` (booking, akkaunt, qabullarim), `clinic`, `admin`, `telegram`.
Boshlang'ich JS ≈ 181 kB gzip (react-dom + react-router + i18n katalogi).

`src/demo/data.ts` va `src/domain/types.ts` o'chirildi: ishlab chiqarish ekranlari namunaviy
ma'lumotdan foydalanmaydi. Fiktiv ma'lumot faqat `src/test/fixtures.ts` da.

## 3. Marshrutlar xaritasi

Huquq ustuni: **anon** — kirish shart emas; **user** — kirgan foydalanuvchi; klinika marshrutlarida
a'zolikning `permissions` massividagi huquq (birortasi yetarli); admin marshrutlarida
`platformPermissions`. UI faqat menyu va tugmalarni yashiradi — **haqiqiy tekshiruv serverda**.

### 3.1 Ommaviy sayt va bemor

| Marshrut | Vazifasi | API chaqiruvlari | Huquq |
|---|---|---|---|
| `/` | Landing: qidiruv, qanday ishlaydi, yo'nalishlar, tavsiya klinikalar (reyting bo'yicha), tasdiqlash tushuntirishi, FAQ | `GET /public/facets`, `GET /public/clinics?sort=rating&pageSize=3` | anon |
| `/clinics` | Katalog: q, yo'nalish (11 ta qat'iy kalit, nomlari UI katalogidan `category.<key>`), xizmat, tuman, saralash (reyting/narx/masofa/nom), "7 kun ichida bo'sh vaqt", sahifalash | `GET /public/facets`, `GET /public/clinics` | anon |
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
| `` (dashboard) | Ikonkali metrikalar + ta'riflar (InfoTip): kutilayotgan so'rovlar (istalgan sana), bugungi tasdiqlangan/yakunlangan/kelmadi, bugun yig'ilgan to'lovlar (faqat server qaytarsa); bugungi qabullar (`appointment.view`); tezkor amallar; 14 kunlik "bajarilgan xizmatlar qiymati / yig'ilgan to'lovlar" grafigi + jadval ko'rinishi (`finance.report.view`) | `GET …/dashboard`, `GET …/appointments?from&to`, `GET …/reports/daily` | har qanday a'zolik |
| `requests` | Pending so'rovlar: hisoblagich, mos shifokorlar ro'yxati, tasdiqlash / rad etish (sabab kodi); `booking.expired` / `not_pending` / `doctor_busy` da ro'yxat yangilanadi; har 30 s avtomatik yangilanish; 10 daqiqadan kam qolganiga "Shoshilinch" belgisi; natija Toast'da; J/K/Enter (faqat ro'yxat fokusda) | `GET …/appointments?status=Pending`, `GET …/eligible-doctors`, `POST …/confirm`, `POST …/reject` | `appointment.triage` |
| `appointments` | Ro'yxat: sana oralig'i, holat, shifokor filtrlari | `GET …/appointments`, `GET …/doctors` | `appointment.view` |
| `appointments/:appointmentId` | Tafsilot va tarix; Pending: tasdiqlash/rad; Confirmed: yakunlash (summa taklif qilingan narx bilan to'ldiriladi), kelmadi, ko'chirish, bekor qilish; qoldiq, to'lovlar, to'lov kiritish, qaytarish, "o'tmagan" | `GET …/appointments/{id}`, `POST …/complete`, `/no-show`, `/reschedule`, `/cancel`, `GET …/balance`, `GET …/payments`, `POST …/payments`, `POST …/payments/{id}/refund`, `/fail` | `appointment.view` (+ har bir amal o'z huquqi bilan) |
| `walk-in` | Qabulxona/telefon orqali yozuv (darhol Confirmed, shifokor tanlanadi) | `GET …/services`, `GET …/doctors`, `GET /public/branches/{id}/availability` (taklif uchun), `POST …/appointments` (+Idempotency-Key) | `appointment.create_walkin` |
| `calendar` | Kun (ustunlar — shifokorlar) / Hafta (bitta shifokor, 7 kun) / Ro'yxat; shifokor ish vaqti foni, filial vaqti bo'yicha "hozir" chizig'i, ustma-ust bloklar yonma-yon, boshqa filialdagi bandlik, legenda (`?view=day\|week\|list&date&doctor`) | `GET …/calendar?branchId&date` (hafta — 7 ta so'rov, D6) | `appointment.view` |
| `doctors`, `doctors/:doctorId` | Shifokorlar CRUD, filial/xizmat bog'lanishi, xodim hisobini bog'lash (faqat `staff.manage` bilan; aks holda joriy `userId` o'zgarmasdan qaytariladi — API bo'sh `userId` ni "uzish" deb tushunadi), haftalik jadval muharriri (filiallar bo'yicha ham ustma-ust tushish tekshiriladi), dam olish (time off) | `GET/POST/PUT …/doctors`, `GET/PUT …/working-hours`, `GET/POST/DELETE …/time-off`, `GET …/staff` (faqat `staff.manage` bo'lsa) | `schedule.manage` / `schedule.own_timeoff` (o'z yozuvi) / `catalog.manage` (ko'rish) |
| `services` | Xizmatlar (qat'iy yo'nalish kalitlari) va har bir filial narxi/davomiyligi | `GET/POST/PUT …/services`, `PUT …/branches/{b}/services/{s}` (204) | xizmat yaratish/tahrirlash — `catalog.manage` **butun klinika** bo'yicha; filial narxi — o'sha filialda `catalog.manage` |
| `branches` | Filiallar (vaqt mintaqasi faqat `Asia/Tashkent` / `Asia/Samarkand`, telefon ixtiyoriy, koordinatalar ikkalasi birga, ≤ 6 kasr) va ish vaqti | `POST/PUT …/branches`, `PUT …/branches/{id}/hours` (204) | yaratish — `branch.manage` butun klinika bo'yicha; tahrir — o'sha filialda |
| `staff` | A'zolar (rol, doira, filiallar), takliflar (muddati o'tganlari belgilanadi; SMS yuborilmaydi); egasi bo'lmagan administrator Owner/Admin rolini bera olmaydi; filial doirasidagi menejer faqat "Tanlangan filiallar" doirasini beradi; o'zini tahrirlash yo'q | `GET/PUT/DELETE …/staff`, `GET/POST/DELETE …/invitations` | `staff.manage` |
| `payments` | To'lovlar ro'yxati, tezkor to'lov kiritish (so'nggi 3 kun qabullari), qaytarish, "o'tmagan" | `GET …/payments`, `GET …/appointments`, `GET …/balance`, `POST …/payments` | `payment.record` yoki `finance.report.view` |
| `reports` | Kunlik (oraliq, jami) va oylik hisobot, solishtirish (reconciliation) belgisi | `GET …/reports/daily`, `GET …/reports/monthly` | `finance.report.view` |
| `settlements` | Platforma bilan hisob-kitoblar (faqat ko'rish) | `GET …/settlements` | `finance.settlement.view` |
| `audit` | Klinika audit jurnali | `GET …/audit` | `audit.view` |
| `profile` | Klinika profili, tekshiruv holati va rad/to'xtatish sababi (`statusReason`) | `GET/PUT /clinics/{id}` | ko'rish — a'zolik; tahrir — `clinic.profile.manage` (butun klinika bo'yicha) |

### 3.3 Platforma admini (`/admin/…`)

| Marshrut | Vazifasi | API chaqiruvlari | Huquq |
|---|---|---|---|
| `` | Holat bo'yicha klinikalar, bugungi qabullar, ochiq shikoyatlar, faol / bloklangan foydalanuvchilar | `GET /admin/dashboard` | har qanday platforma huquqi |
| `clinics` | Tekshirish: tasdiqlash (Pending yoki Rejected) / rad (faqat Pending, sabab) / to'xtatish (sabab) / tiklash; `statusReason` ko'rsatiladi | `GET /admin/clinics`, `POST …/approve`, `/reject`, `/suspend`, `/reinstate` | ro'yxat `platform.clinic.verify`; to'xtatish/tiklash `platform.account.suspend` |
| `users` | Hisoblarni to'xtatish (sabab; o'z hisobini emas) / tiklash, `statusReason` | `GET /admin/users`, `POST …/suspend`, `/reinstate` | `platform.account.suspend` |
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
- To'rt til: `uz` (lotin), `uz-Cyrl` (kirill — `uz` dan avtomatik transliteratsiya, `i18n/translit.ts`;
  istisnolar `i18n/uzCyrlOverrides.ts`), `ru`, `en`. O'zbekcha apostrof standarti: `o‘`/`g‘` (U+2018),
  tutuq belgisi `’` (U+2019); yangi kalit: `PYTHONUTF8=1 python web/scripts/add-i18n.py <section.ts> <keys.json>`.
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

### 9.1 Hal qilingan (BE1a/BE1b, 2026-10-18)

- **R1.** `GET /clinics/{id}` (`statusReason` bilan), dashboard (`branchId`, `collected: Money | null`),
  branches, services (`offers[]` ichida — taxmin to'g'ri chiqdi), staff, invitations (klinika va
  `/me/invitations`), doctors (`userId`), working hours, audit (`actorKind`, `actorUserId`), admin
  dashboard (`activeUsers`, `suspendedUsers`), admin clinics (`isSuspended`, `statusReason`), admin users
  (`statusReason`) — API.md §4.1/§5.1 dagi yakuniy shakllar `api/types.ts` ga ko'chirildi, ASSUMED
  belgilari olib tashlandi.
- **R2.** 204 javoblar: `PUT …/hours`, `PUT …/branches/{b}/services/{s}`, `PUT …/working-hours`,
  `POST /me/invitations/{id}/accept`, `POST /me/password`, `POST /me/totp/enable|disable`,
  `POST /admin/users/{id}/suspend|reinstate` — `void`; `PUT` profil/filial/xizmat/xodim/shifokor va
  admin klinika amallari 200 + DTO qaytaradi.
- **R3.** Yo'nalishlar — 11 ta qat'iy kalit (`diagnostics` … `other`); UI ularni `category.<key>`
  orqali uz/ru/en da ko'rsatadi, xizmat formasi faqat shu kalitlarni yuboradi. Test API.md §2.1
  ro'yxati bilan solishtiradi.
- **R4.** Vaqt mintaqasi — faqat `Asia/Tashkent` (standart) va `Asia/Samarkand` (tanlov ro'yxati).
- **R5.** Huquqlar: xizmat yaratish/tahrirlash va filial yaratish — butun klinika doirasida;
  profil tahriri — butun klinika (`canClinicWide`); shifokorning `userId` sini o'zgartirish —
  qo'shimcha `staff.manage`.
- **R6.** §5 approve/reject — `platform.clinic.verify`, suspend/reinstate — `platform.account.suspend`
  (BE1b-11 tasdiqladi; avvalgi 12-band).
- **R7.** `/me/invitations*` implement qilindi (avvalgi 13-band); boshqa telefonga yuborilgan
  taklif → 404, muddati o'tgan → 422 `staff.invitation_expired` (UI ro'yxatni yangilaydi).
- **R8.** `X-Access-Reason` UTF-8 percent-encoded bo'lib keladi va server decode qiladi — §5.1 da
  BE2 uchun yozib qo'yildi (endpoint hali yo'q, avvalgi 5-band).
- **R9.** Yangi xato kodlari (`staff.already_member`, `staff.invitation_expired`,
  `clinic.registration_limit`, `schedule.hours_overlap`, `schedule.branch_not_linked`,
  `schedule.doctor_user_invalid`, `schedule.doctor_user_taken`, `admin.invalid_status_transition`,
  `admin.cannot_suspend_self`) uz/ru/en da lokalizatsiya qilindi.

### 9.2 Hal qilingan (BE2/BE3/BE4, 2026-10-18)

- **R10.** Taxminiy (ASSUMED) shakllar yakuniylashtirildi va `api/types.ts` dan belgilar olib tashlandi:
  time-off, eligible-doctors (`withinWorkingHours`, `provisional`), calendar (`branchId`, `workingHours`,
  `otherBranch`, `blockId`), `AppointmentDto` (`priceKind`, `hasReview`, `canReview`), payments
  (`refunded`, `refundable`, `reason`, `failureReason`, `failedAt`; status faqat `Recorded|Failed`),
  daily (`priorPeriodRefunded`), monthly (`branchId`, `priorPeriodRefunded`, `locked`), settlements
  (`periodStart/End`, `itemCount`, `commissionRuleConfigured`, `paymentTerms`), admin reviews/reports,
  komissiya qoidalari (`rate` — 4 xonali kasr satri, `endedAt` va h.k.), admin notifications
  (`audience`, `notBefore`, `sentAt`, `lastErrorCode`; holatlar Pending/Processing/Sent/Failed/Skipped/Cancelled),
  `TelegramConfig.botId`.
- **R11.** So'rovlar: to'lov va qaytarishda `Idempotency-Key` majburiy (allaqachon yuborilardi);
  `GET …/payments?appointmentId` serverda filtrlanadi (mijoz filtri olib tashlandi); `fail`, `finalize`,
  `mark-paid`, `void`, `commission-rules/{id}/end` endi 200 + DTO qaytaradi (API turlari yangilandi);
  `RecordPaymentBody.status?: 'Failed'` qo'shildi.
- **R12.** `reasonCode`: rad etish — `NoDoctorAvailable|TimeUnavailable|ServiceUnavailable|PatientUnreachable|Other`,
  klinika bekor qilishi — `DoctorUnavailable|ClinicClosed|PatientRequest|ClinicEmergency|Other` (UI ro'yxati
  `ClinicClosed` bilan to'ldirildi, uz/ru/en). `from`/`to` — UTC instant, `[from, to)` (F4); komissiya `rate` — kasr satri (F7).
- **R13.** UI: sharh formasi `canReview` bo'yicha; tasdiqlash tanlagichida `withinWorkingHours=false` va
  `provisional` belgilanadi, ish vaqtidan tashqari tanlovda ogohlantirish (tasdiqlash baribir mumkin);
  qaytarish dialogi `refundable` bo'yicha cheklanadi (qolmasa tugma yashirin; qaytarishi bor to'lovni
  "muvaffaqiyatsiz" qilib bo'lmaydi); bildirishnomalar jadvalida auditoriya, `lastErrorCode`, `sentAt`.
  Telegram Login Widget `botId` bilan popup rejimida (CSP: `telegram.org` skripti, `oauth.telegram.org` frame
  — ruxsat etilgan), `botId` yo'q bo'lsa redirect rejimi.
- **R14.** Yangi xato kodlari lokalizatsiya qilindi: `schedule.link_in_use`, `finance.appointment_not_payable`,
  `finance.invalid_payment_state`, `finance.invalid_settlement_state`, `telegram.webhook_unauthorized`
  (`errors.test.ts` API.md §9 bilan solishtiradi).

### 9.3 Ochiq

1. Walk-in shifokor tanlagichi `withinWorkingHours` ni ko'rsata olmaydi: bu maydon faqat
   `…/appointments/{id}/eligible-doctors` da bor, walk-in uchun "bo'sh shifokorlar" endpoint'i yo'q
   (UI shifokorlar ro'yxatidan foydalanadi, band bo'lsa server 409 qaytaradi).
2. `Me.totpSetupRequired` umumiy: platforma huquqlari aynan TOTP kutayotganini bilish uchun alohida
   belgi (masalan `platformTotpSetupRequired`) yo'q.
3. `/admin/settlements` va `/clinics/{id}/settlements` sahifalanmagan massiv (≤ 500) — UI shunday ishlaydi.
4. `hasReview=true` bo'lganda UI formani yashiradi, lekin "sharh qoldirilgan" yozuvini ko'rsatmaydi.
5. `availableWithinDays` filtri va `nextFreeSlot` — server holatiga qarab tekshirilmagan (BE2 dan keyin qayta ko'rish kerak).
6. Admin audit `clinicId` ustunida faqat id bor (klinika nomi DTO'da yo'q).
7. Xodim o'z a'zoligidan chiqishi mumkin (`DELETE …/staff/{own}`), lekin UI'da "Klinikadan chiqish"
   tugmasi hali yo'q.
8. Kalendar `workingHours` (ish vaqti foni) va `otherBranch` ni ko'rsatadi (2026-10-19). `blockId` orqali
   time-off o'chirish tugmasi kalendarda hali yo'q (doktor sahifasidan bajariladi).
9. Hafta kalendari uchun `from&to` endpoint'i yo'q (D6) — hafta 7 ta kunlik so'rovdan yig'iladi.

## 10. Testlar

Vitest + Testing Library, API `fetch` chegarasida mock qilinadi (`src/test/harness.tsx`), sanalar
uchun fake timer'lar. Asosiy oqimlar: xato kodlari ↔ API.md §9; client sarlavhalari va idempotency;
OTP kirish (qayta yuborish hisoblagichi, TOTP bosqichi), xodim + TOTP; guard'lar (login'ga redirect,
403, TOTP holati); yozilish ustasi (muvaffaqiyat, 409, 422, mehmon); qabulni bekor qilish va
hisoblagich; so'rovni shifokor tanlab tasdiqlash va `booking.expired`; to'lov formasi validatsiyasi
va bir xil Idempotency-Key bilan qayta urinish; menyuni huquqlar bo'yicha filtrlash; admin klinikani
tasdiqlashi (Rejected → Approved, `statusReason`, `admin.invalid_status_transition`), o'zini bloklash tugmasi yo'qligi va break-glass sarlavhasi; klinika sozlash ekranlari (`clinicSetup.test.tsx`: qat'iy yo'nalishlar, filial doirasidagi `catalog.manage`/`branch.manage`, vaqt mintaqalari va koordinata juftligi, shifokor `userId` va `staff.manage`, `schedule.hours_overlap`, muddati o'tgan taklif, profil sababi); yo'nalish kalitlari ↔ API.md §2.1 (`lib/categories.test.ts`); Mini App kirishi (mock `window.Telegram`), tema, til,
MainButton va Telegram'dan tashqari holat; filial almashganda eski raqam ko'rinmasligi (J4,
`clinic.test.tsx`); kalendar yo'laklari (`calendarLayout.test.ts`).

**Playwright UI to'plami** (`npm run e2e`, `web/e2e/ui.spec.ts`): landing, katalog, klinika sahifasi,
login, klinika dashboard'i, so'rovlar, kalendar, admin dashboard va klinikalar — 360 / 768 / 1280 px,
qorong'i rejim va reduced-motion loyihalarida axe WCAG 2.2 AA (serious/critical = 0) va gorizontal
skrol yo'qligi; har biriga to'liq sahifa skrinshoti (`test-results/`). API mock'lari fiktiv
(`e2e/support/mockApi.ts`, faqat QA). Real backend bilan foydalanuvchi yo'llari (`e2e/journeys`,
`E2E_BACKEND=1`) hali yozilmagan — backend kerak.

## 11. Ma'lum cheklovlar

- Qayta dizayn ishlayotgan backend'ga qarshi E2E bilan tekshirilmagan — faqat kontrakt, vitest va
  Playwright mock'lari (backend uchun `.env`/user-secrets yoki Docker kerak).
- Haqiqiy Telegram klientlarida va haqiqiy bot bilan Login Widget sinab ko'rilmagan.
- Barcha til kataloglari boshlang'ich chunk'da (alohida til yuklash yo'q) — boshlang'ich JS ~181 kB gzip
  (maqsad ≤ 180); ru/en ni lazy yuklash ~8 kB tejaydi.
- Klinika dashboard'i sekin mobil'da LCP ≈ 2.8 s (so'rovlar zanjiri: /me → filiallar → dashboard).
- `/api/dev/*` (Fake SMS va h.k.) uchun UI yo'q — lokal sinovda to'g'ridan-to'g'ri chaqiriladi.
- Kalendar bir kun ichidagi bloklarni ko'rsatadi; yarim tundan o'tadigan bloklar kesiladi.

## 12. Dizayn tizimi ("Moviy klinika")

Yo'nalish, tokenlar va referens bilan to'qnashuvlar jadvali — `docs/FRONTEND_PHASE_A.md` §0. Asosiy
qoidalar: holat har doim rang + ikonka + matn; pul — satr/BigInt, grafiklarda ham ko'rsatiladigan
summa API satridan formatlanadi; moliya atamalari `FinanceTerm` ((i) ta'rif) bilan; saqlash natijasi
Toast'da, xato va muhim yo'riqnomalar sahifada qoladi; xavfli amallar `danger` dialog + sabab (yoki
qaytarib bo'lmaydiganlarda "Tushundim" belgisi); jadvallar 640 px dan kichikda yorliq/qiymat
kartochkaga aylanadi (`table--stack` + `data-label`); grafik ranglari (`--c-data-1/2`) dataviz
validatoridan o'tgan, qorong'i rejim uchun alohida qiymatlar bilan.

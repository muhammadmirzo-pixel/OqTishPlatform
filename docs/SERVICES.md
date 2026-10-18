# Servislar, handlerlar va portlar

Bu hujjat backend modullari ichidagi har bir handler, servis va port nimani qilishini tavsiflaydi:
maqsad, kirish, chiqish, ruxsat (permission), yon ta'sirlar (audit / outbox) va konfiguratsiya kalitlari.
Har bir modul o'z bo'limini qo'shadi. Texnik nomlar (klass, endpoint, jadval, kalit, xato kodi) inglizcha qoladi.

Umumiy qoidalar: `docs/CONVENTIONS.md`. API kontrakti: `docs/API.md`. Ruxsatlar xaritasi: `docs/SECURITY.md` §8.

---

## 1. Identity & Common (BE1a, 2026-10-18)

### 1.1 Common — natija va xatolar

| Tur | Joyi | Vazifasi |
|---|---|---|
| `Result`, `Result<T>` | `Application/Common/Result.cs` | Handler natijasi: qiymat yoki `Error`. Biznes natijalari uchun exception ishlatilmaydi. `Map` yordamchisi bor |
| `Error(Kind, Code, Message, FieldErrors?)` + `RetryAfter` | `Application/Common/Error.cs` | `ErrorKind` → HTTP: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, RuleViolation 422, RateLimited 429. `Code` barqaror (`docs/API.md` §9). `Message` — inglizcha dasturchi izohi, hech qachon shaxsiy ma'lumot emas |
| `CommonErrors` | o'sha fayl | `common.not_found`, `common.forbidden`, `common.idempotency_mismatch` (422), `common.concurrency` (409), `common.validation` |
| `ProblemResults` | `Api/Common/ProblemResults.cs` | `Error` → RFC 7807 ProblemDetails (`code`, `traceId`, ixtiyoriy `errors`). `result.ToHttp(http, onSuccess)` endpointlarda ishlatiladi. RateLimited uchun `Retry-After` qo'yadi. Framework xatolariga (404 marshrut, 405, 500) standart `code` beriladi |
| `ApiExceptionHandler` | `Api/Common/ApiExceptionHandler.cs` | So'rov tanasini o'qish xatolari (`BadHttpRequestException`, `RouteHandlerOptions.ThrowOnBadRequest = true`) → 400 `common.validation`; `MoneyFormatException` → 400 `finance.amount_invalid`. Boshqa exceptionlar → 500, ichki tafsilotsiz |
| `Page<T>`, `PageRequest`, `ToPageAsync` | `Application/Common/Page.cs`, `PagingExtensions.cs` | `{ items, page, pageSize, totalCount }`. `PageRequest.Create(page, pageSize)`: page ≥ 1, pageSize 1..100, aks holda 400 (kesib qo'yilmaydi). `ToPageAsync` sanash va sahifalashni SQL'da bajaradi (so'rov oldindan tartiblangan bo'lishi shart) |

### 1.2 Common — Money va JSON

| Tur | Vazifasi |
|---|---|
| `Domain/Common/Money` | `decimal` + `Currency` (v1: faqat `UZS`). `Round()` = 2 xona, `MidpointRounding.AwayFromZero` (yagona yaxlitlash qoidasi). Har xil valyutalar bilan arifmetika/taqqoslash → `InvalidOperationException`. numeric(19,2) chegarasi tekshiriladi. `TryCreateExact` foydalanuvchi kiritmasi uchun: 2 xonadan ortiq bo'lsa rad etadi |
| `MoneyJsonConverter` | Yozish: `{ "amount": "1500000.00", "currency": "UZS" }`. O'qish qat'iy: `amount` satr yoki son, ≤ 2 o'nlik xona, eksponentsiz; `currency` qo'llab-quvvatlanadigan kod; boshqa xususiyat yoki yetishmagan maydon → `MoneyFormatException` → 400 `finance.amount_invalid` |
| `JsonDefaults` | API, outbox payload, audit details va idempotency snapshotlari uchun yagona sozlamalar: camelCase, enumlar satr sifatida, Money converter |

### 1.3 Common — ma'lumotlar bazasi portlari

| Port (Application) | Amalga oshirish (Infrastructure) | Vazifasi va qoidalari |
|---|---|---|
| `IOqTishDb` | `OqTishDbContext` | LINQ uchun `DbSet`lar va `SaveChangesAsync`. Raw SQL, lock, tranzaksiya — boshqa portlar orqali. Keyingi modullar o'z `DbSet`larini shu interfeysga qo'shadi |
| `ITransactionRunner.RunAsync(work, ct, isolation = ReadCommitted, maxAttempts = 3)` | `TransactionRunner` | Bitta aniq tranzaksiya. **Muvaffaqiyatli `Result` → commit, xato `Result` → rollback** (qisman yozuv qolmaydi). Faqat `40001` va `40P01` qayta uriniladi (jami ≤ 3, jitter bilan); tugasa → 409 `common.concurrency`. Urinishlar orasida change tracker tozalanadi — `work` kerakli hamma narsani qayta yuklashi kerak. Ichma-ich chaqiruv mavjud tranzaksiyaga qo'shiladi |
| `IDbErrorClassifier.Classify(ex)` | `DbErrorClassifier` | `PostgresException`ni (DbUpdateException ichida ham) `DbError(Kind, SqlState, ConstraintName)`ga aylantiradi: 23P01 Exclusion, 23505 Unique, 23503 ForeignKey, 23514 Check, 40001, 40P01 (`IsTransient`) |
| `IRowLocks` | `RowLocks` | Faqat tranzaksiya ichida (aks holda exception). `LockBranchAsync(branchId)` — `tenancy.branch_booking_locks` qatorini `FOR UPDATE` bilan bloklaydi (false = filial yo'q). `LockAsync<T>(key)`, `LoadForUpdateAsync<T>(key)` — EF metadata'dan jadval/ustun nomini olib, bitta ustunli PK bo'yicha. `AdvisoryLockAsync(key)` — `pg_advisory_xact_lock`. Lock tartibi: **branch → appointment → boshqalar** |
| `IAuditLog.Record(AuditEvent)` | `AuditLog` | `audit.audit_log`ga qator qo'shadi; keyingi `SaveChanges` bilan, ya'ni biznes o'zgarishi bilan **bitta tranzaksiyada** yoziladi. Aktyor, correlation id (`TraceIdentifier`), IP hash `IRequestContext`dan. `Details` — faqat id va kodlar (telefon, ism, kod, token emas). Jadval append-only (trigger + REVOKE) |
| `IOutbox.EnqueueAsync(type, dedupKey, payload, notBefore, ct)` | `Outbox` | `notify.outbox_messages`ga `INSERT … ON CONFLICT (dedup_key) DO NOTHING`, joriy tranzaksiyada (tranzaksiyasiz chaqirilsa exception). `true` = qo'shildi, `false` = shu dedup kaliti bor. Yetkazish (dispatcher, retry, backoff) — BE4 ishi |
| `IIdempotencyStore.RunAsync<T>(userId, key, request, work, ct)` | `IdempotencyStore` | `Idempotency-Key` (≤ 64 ASCII) uchun, tranzaksiya ichida. Kalit qatori birinchi bo'lib qo'yiladi; parallel dublikat PK'da kutadi. Boshqa so'rov tanasi bilan → 422 `common.idempotency_mismatch`; tugallangan → saqlangan javob qaytariladi (work ishlamaydi); muvaffaqiyatda javob o'sha tranzaksiyada saqlanadi; xatoda rollback va kalit bo'shaydi. Request hash = so'rov obyektining camelCase JSON'i SHA-256 |
| `IRequestContext`, `ICurrentUser` | `Api/Common/HttpRequestContext` (API), `SystemRequestContext` (Worker/CLI) | Joriy foydalanuvchi id, sessiya id, IP, User-Agent, correlation id |

### 1.4 Common — xavfsizlik va boshqa infratuzilma

| Komponent | Vazifasi | Konfiguratsiya |
|---|---|---|
| `ClientHeaderMiddleware` | CSRF himoyasi (OD-23): `/api` ostidagi har bir GET/HEAD/OPTIONS bo'lmagan so'rov `X-OqTish-Client: web` yoki `tg-miniapp` talab qiladi, aks holda 403 `security.client_header_missing`. Autentifikatsiyadan oldin ishlaydi. `/api/telegram/webhook/*` istisno (secret bilan himoyalangan) | — |
| `RateLimitPolicies` | ASP.NET Core rate limiter, IP bo'yicha 1 daqiqalik oynalar: global, `auth-otp` (kod yuborish), `auth-credentials` (kod/parol tekshirish). Rad etish → 429 `auth.rate_limited` + `Retry-After` | `RateLimiting:GlobalPermitPerMinute` (300), `RateLimiting:OtpSendPermitPerMinute` (10), `RateLimiting:CredentialsPermitPerMinute` (20) |
| `IKeyedHasher` → `HmacKeyedHasher` | HMAC-SHA256, kalit `Security:OtpPepper`, maqsad prefiksi bilan (`phone`, `email`, `ip`, `otp`). OTP kodlari, rate-limit identifikatorlari, taklifnoma telefon hashi | `Security:OtpPepper` (Development'dan tashqarida majburiy, ≥ 32 belgi) |
| `SecureTokens` | 256-bit tasodifiy tokenlar (base64url), ularning SHA-256 shakli, constant-time taqqoslash, raqamli kod | — |
| `ISecretProtector` → `DataProtectionSecretProtector` | TOTP sirlarini shifrlash (ASP.NET Data Protection; kalitlar `identity.data_protection_keys` jadvalida) | — |
| `IPasswordHashService` → `PasswordHashService` | ASP.NET Core Identity `PasswordHasher` (PBKDF2, V3), Identity user store'siz (ADR-016). `VerifyDummy` noma'lum akkauntlar uchun vaqtni tenglashtiradi | — |
| `StartupGuards.Validate` | Ishga tushishda: pepper yo'q / qisqa / dev qiymati (Development'dan tashqarida) → xato; `Sms:Provider=Fake` Production'da → xato; bo'sh provider faqat Development'da Fake; Eskiz login/parolsiz → xato | `Security:OtpPepper`, `Sms:*` |
| `StartupLog.TotpRuleDisabled` | `Security:RequireTotpForPrivilegedRoles=false` Development'dan tashqarida bo'lsa warning | `Security:RequireTotpForPrivilegedRoles` (default true, faqat appsettings.Development.json'da false) |

### 1.5 Seed mexanizmi

| Komponent | Vazifasi |
|---|---|
| `ISeedContributor { Order, Name, SeedAsync }` | Fiktiv demo ma'lumot qo'shuvchi; idempotent bo'lishi shart. Tartib diapazonlari: 100 identity, 200 tenancy/staff, 300 catalog/scheduling, 400 booking, 500 finance, 600 notifications. `services.AddScoped<ISeedContributor, …>()` bilan ro'yxatdan o'tadi |
| `SeedRunner` | Barcha contributor'larni tartib bo'yicha ishga tushiradi |
| `SeedCommand` (Api) | `dotnet OqTish.Api.dll seed` — faqat Development va Staging'da; Production'da har doim rad etadi (exit 2), override yo'q |
| `POST /api/dev/seed` | Faqat Development (boshqa muhitlarda 404) |
| `IdentitySeedContributor` (Order 100) | Platforma admini `admin@oqtish.local` (parol `Seed:AdminPassword`; dev-only default `Admin123!demo` faqat appsettings.Development.json'da), barcha platforma ruxsatlari; demo bemor `+998900000001` ("Demo Bemor"). Mavjud akkaunt parolini qayta o'rnatmaydi, yetishmagan grantlarni qo'shadi. Konfiguratsiya: `Seed:AdminEmail`, `Seed:AdminPassword`, `Seed:DemoPatientPhone` |

### 1.6 Identity — umumiy servislar

| Servis | Vazifasi | Yon ta'sirlar | Konfiguratsiya |
|---|---|---|---|
| `PhoneNumber` | Faqat O'zbekiston: `+998` + 9 raqam. Qabul qilinadigan shakllar: `+998 90 123-45-67`, `998901234567`, `00998…`, `(90) 123 45 67`, `901234567`. `Mask` → `+998*****4567` (loglarda faqat shu shakl) | — | — |
| `SessionService` | `Create(user, method)` — sessiya (token SHA-256, user security stamp nusxasi), `ValidateAsync(token)` — **har bir so'rovda**: bekor qilingan, muddati o'tgan, 14 kun ishlatilmagan, stamp mos kelmagan yoki user Active bo'lmagan sessiya rad etiladi; `last_seen_at`/sliding muddat ko'pi bilan har 5 daqiqada yoziladi. `RevokeAsync`, `RevokeAllAsync(except)`, `RefreshStampAsync` | `identity.sessions` | `Auth:Session:SlidingLifetimeDays` (30), `IdleTimeoutDays` (14), `TouchIntervalSeconds` (300), `RequireSecureCookie` |
| `OtpService` | `IssueAsync(purpose, identifierHash, phone?, userId?)` — 6 xonali kod, HMAC sifatida saqlanadi; advisory lock ostida limitlar: identifikator bo'yicha 60 s cooldown va 15 daqiqada 3 ta, IP bo'yicha 15 daqiqada 10 ta → 429. Commit'dan keyin SMS to'g'ridan-to'g'ri yuboriladi (outbox emas, ADR-018); xato telefonsiz loglanadi, javob o'zgarmaydi. `phone = null` → decoy challenge. `VerifyAsync` — har urinish atomik sanaladi (5 ta), muddat DB vaqti bilan; egalik kod to'g'ri bo'lgandan keyin tekshiriladi. `ConsumeAsync` — bir martalik iste'mol (shartli UPDATE) | `identity.otp_challenges`, SMS | `Auth:Otp:*` (`CodeLength`, `TtlSeconds` 300, `MaxAttempts` 5, `ResendCooldownSeconds` 60, `MaxPerPhone` 3, `MaxPerIp` 10, `WindowMinutes` 15, `MessageTemplate`) |
| `Totp` (statik), `Base32` | RFC 6238: HMAC-SHA1, 30 s, 6 raqam, ±1 qadam; `Verify` mos qadamni qaytaradi va `lastUsedStep`dan katta bo'lmagan qadamni rad etadi; otpauth URI | — | — |
| `TotpService` | `RequireSecondFactorAsync(userId, code)` — TOTP yoqilgan bo'lsa kod talab qiladi (401 `auth.totp_required` / `auth.totp_invalid`). `VerifyAndConsumeAsync` — `last_used_step`ni shartli UPDATE bilan yangilaydi (replay himoyasi, parallel ham). Sirni Data Protection bilan shifrlaydi | `identity.totp_credentials` | — |
| `SignInService` | Barcha kirish usullarining yakuni (tranzaksiya ichida): user Active emas → 403 `auth.account_suspended`; sessiya yaratadi; `auth.signed_in` audit; `MeDto` qaytaradi. `CreatePatientUser` — yangi bemor (user + patient profile) | sessiya, audit | — |
| `MeQuery` | `GET /api/me`. Identifikatorlar (telefon maskalangan), bemor profili, TOTP holati, har bir a'zolik uchun **samarali ruxsatlar** (`IAccessService`dan), platforma ruxsatlari, `totpSetupRequired` | — | — |
| `ISmsSender` (port) | `FakeSmsSender` (xotiradagi `FakeSmsMailbox`, `GET /api/dev/sms` faqat Development) va `EskizSmsSender` (Eskiz.uz: `POST /api/auth/login` email/parol → bearer token keshlanadi, 401'da bir marta yangilanadi; `POST /api/message/sms/send` `mobile_phone` (+ belgisiz), `message`, `from`). **Eskiz jonli servisga nisbatan tekshirilmagan** (credential yo'q). Telefon/matn/token hech qachon loglanmaydi | SMS | `Sms:Provider` (`Fake`/`Eskiz`), `Sms:Eskiz:BaseUrl`, `Email`, `Password` (sir), `From` (4546), `TimeoutSeconds` |
| `ITelegramLoginVerifier` → `TelegramLoginVerifier` | Mini App `initData`: kalit = HMAC_SHA256("WebAppData", botToken), hash = hex(HMAC_SHA256(kalit, data_check_string)). Login Widget: kalit = SHA256(botToken). Constant-time taqqoslash, `auth_date` ≤ 24 soat (kelajakka ≤ 5 daqiqa) | — | `Telegram:BotToken` (sir), `Telegram:AuthMaxAgeSeconds` (86400) |

Cookie va autentifikatsiya (Api): `SessionCookie` — HTTPS'da yoki `RequireSecureCookie=true`da `__Host-oqtish` (HttpOnly, Secure, SameSite=Lax, Path=/), oddiy HTTP Development'da `oqtish-dev`. `SessionAuthenticationHandler` (scheme `Session`) — cookie'ni `SessionService.ValidateAsync` orqali tekshiradi; challenge → 401 `auth.unauthenticated`, forbid → 403 `common.forbidden`; sliding muddat uzaysa cookie qayta beriladi.

### 1.7 Identity — handlerlar (endpointlar)

Ruxsat ustuni: **anon** — kirish shart emas, **user** — har qanday kirgan foydalanuvchi.
Barcha GET bo'lmagan so'rovlar `X-OqTish-Client` sarlavhasini talab qiladi.

| Handler | Endpoint | Ruxsat | Kirish → chiqish | Yon ta'sirlar |
|---|---|---|---|---|
| `RequestOtpHandler` | `POST /api/auth/otp/request` | anon, `auth-otp` | `{ phone }` → 202 `{ challengeId, expiresInSeconds, resendAfterSeconds }` (ma'lum/noma'lum raqamga bir xil) | challenge, SMS |
| `VerifyOtpHandler` | `POST /api/auth/otp/verify` | anon, `auth-credentials` | `{ challengeId, code, totpCode? }` → 200 `Me` + cookie. Yangi raqam → bemor akkaunti | challenge iste'moli, user/identity/profil (yangi), sessiya, audit `auth.signed_in` |
| `PasswordLoginHandler` | `POST /api/auth/password/login` | anon, `auth-credentials` | `{ email, password, totpCode? }` → 200 `Me`. Xato/noma'lum/bloklangan → 401 `auth.invalid_credentials`; 10 xato → 15 daqiqa blok | sessiya, audit `auth.signed_in` / `auth.password.login_failed` |
| `RequestPasswordResetHandler` | `POST /api/auth/password/reset/request` | anon, `auth-otp` | `{ email }` → har doim 202 `{ challengeId }` (noma'lum e-mail uchun decoy) | challenge, tasdiqlangan telefonga SMS |
| `ConfirmPasswordResetHandler` | `POST /api/auth/password/reset/confirm` | anon, `auth-credentials` | `{ challengeId, code, newPassword }` → 204 | parol, security stamp, **barcha sessiyalar bekor**, audit `auth.password.reset` |
| `TelegramSignInHandler` | `POST /api/auth/telegram/webapp`, `/widget` | anon, `auth-credentials` | `{ initData, totpCode? }` yoki widget maydonlari → 200 `Me`. Noma'lum Telegram id → yangi bemor (hech qachon birlashtirilmaydi). O'chiq → 422 `telegram.disabled` | user/identity (yangi), sessiya, audit |
| `TelegramConfigQuery` | `GET /api/telegram/config` | anon | → `{ enabled, botUsername, miniAppUrl }` | — |
| `LogoutHandler` | `POST /api/auth/logout`, `/logout-all` | user | → 204, cookie o'chiriladi | sessiya(lar) bekor, audit `auth.logout` / `auth.logout_all` |
| `MeQuery` | `GET /api/me` | user | → `Me` | — |
| `UpdateProfileHandler` | `PUT /api/me/profile` | user | `{ fullName }` → 200 `Me` | bemor profili |
| `SetPasswordHandler` | `POST /api/me/password` | user | `{ email?, currentPassword?, newPassword }` → 204. Birinchi parol: tasdiqlangan telefon va e-mail kerak | parol, e-mail identity, stamp, **boshqa sessiyalar bekor**, audit `auth.password.changed` |
| `RequestPhoneLinkHandler` | `POST /api/me/phone/request` | user, `auth-otp` | `{ phone }` → 202 (boshqa akkauntga tegishli ekanligi bu yerda oshkor qilinmaydi) | challenge, SMS |
| `VerifyPhoneLinkHandler` | `POST /api/me/phone/verify` | user | `{ challengeId, code }` → 200 `Me`; boshqa akkauntniki → 409 `auth.identity_in_use` | identity, audit `identity.phone_linked` / `identity.link_conflict` (konfliktda ham commit qilinadi) |
| `TotpHandler.SetupAsync/EnableAsync/DisableAsync` | `POST /api/me/totp/setup`, `/enable`, `/disable` | user | setup → `{ secret, otpauthUri }`; enable/disable `{ code }` → 204 | TOTP yozuvi; enable **boshqa sessiyalarni bekor qiladi**; audit `auth.totp.enabled` / `auth.totp.disabled` |
| `TelegramLinkService.CreateLinkAsync` | `POST /api/me/telegram/link` | user | → `{ deepLink, expiresAt }` (10 daqiqa, token SHA-256 sifatida saqlanadi) | link token |
| `TelegramLinkService.ConsumeTelegramLinkTokenAsync(token, telegramUserId, chatId)` | (bot, BE4 chaqiradi) | — | → `TelegramLinkResult(UserId)`; bir martalik; boshqa akkauntdagi Telegram id → `auth.identity_in_use`; yaroqsiz → `telegram.link_invalid` | Telegram identity, `notify.telegram_links` (chat id), audit `identity.telegram_linked` / `identity.link_conflict` |
| `TelegramLinkService.UnlinkAsync` | `DELETE /api/me/telegram` | user | → 204; yagona kirish usuli bo'lsa 422 `auth.last_identity` | identity va chat link o'chiriladi, audit |
| `SessionsHandler` | `GET /api/me/sessions`, `DELETE /api/me/sessions/{id}` | user | faol sessiyalar ro'yxati; boshqaning id'si → 404 | audit `auth.session.revoked` |
| `DeleteAccountHandler` | `POST /api/me/delete` | user | → 204; a'zolik yoki platforma granti bo'lsa 422 `account.delete_blocked` | anonimlashtirish (OD-18): status Deleted, profil anonim, identitylar/parol/TOTP/Telegram o'chiriladi, sessiyalar bekor, `IAccountDeletionContributor`lar chaqiriladi, audit `account.deleted` |
| `DevEndpoints` | `GET /api/dev/sms?phone`, `POST /api/dev/seed` | anon, **faqat Development** | Fake SMS qutisi; seed | — |

Kengaytirish nuqtasi: `IAccountDeletionContributor.AnonymiseAsync(userId, ct)` — shaxsiy ma'lumot saqlaydigan modullar (masalan, booking'dagi aloqa maydonlari) o'chirish tranzaksiyasi ichida de-identifikatsiya qiladi. Scoped sifatida ro'yxatdan o'tkaziladi.

### 1.8 Staff — kirish huquqi xizmati (access service)

| Tur | Vazifasi |
|---|---|
| `Permissions` | Klinika ruxsatlari konstantalari va **rol → ruxsat xaritasi** (SECURITY.md §8, yagona manba; `tests/OqTish.UnitTests/Snapshots/role-permissions.txt` snapshot testi). `PermissionReach.OwnOnly` — Doctor uchun (o'z appointmentlari, o'z time-off). `ClinicWideOnly` = `clinic.profile.manage`, `audit.view`, `finance.settlement.view` (Branches-scoped a'zolikda hech qachon). `TotpRequiredRoles` = ClinicOwner, FinanceManager. `RolesManageableBy(role)` — owner barcha rollarni, admin Owner/Admin'dan tashqarilarni boshqaradi |
| `PlatformPermissions` | `platform.clinic.verify`, `platform.account.suspend`, `platform.reports.handle`, `platform.finance.settle`, `platform.tenant-data.read`, `platform.audit.view` — faqat aniq grantlar |
| `IAccessService.GetScopeAsync(ct)` | Har so'rovda bir marta (DI scope'da keshlanadi) DB'dan hisoblanadi: suspend qilinmagan klinikalardagi Active a'zoliklar, filial grantlari, Clinic-scope uchun klinikaning barcha filiallari, platforma grantlari, TOTP holati, doctor bog'lanishi. Suspend/Deleted user → bo'sh scope. `GetScopeForUserAsync(userId)` — aniq user uchun (keshsiz) |
| `AccessScope.Check(permission, clinicId, branchId?)` | A'zolik yo'q / klinika suspend → 404 `common.not_found`; TOTP qoidasi → 403 `auth.totp_setup_required`; rolda ruxsat yo'q yoki filial Branches-scope'dan tashqarida → 403 `common.forbidden`. `branchId = null` bo'lsa Branches-scope a'zolik o'z filiallaridagi ruxsatlar uchun o'tadi (ClinicWideOnly'dan tashqari) |
| `CheckClinicWide`, `CheckMember`, `CheckPlatform` | Klinika miqyosidagi amallar (Clinic-scope talab qilinadi), "har qanday a'zolik" o'qish endpointlari, platforma ruxsatlari |
| `BranchIdsWith(permission, clinicId)` | Ruxsat bor filial id'lari — ro'yxat so'rovlarida SQL filtr uchun (`ids.Contains(x.BranchId)`), yuklagandan keyin filtrlash taqiqlangan |
| `IsOwnOnly`, `DoctorIdFor(clinicId)` | Doctor-own semantikasi. `IDoctorIdentityResolver` standart holda hech kimni bilmaydi; Scheduling moduli (BE1b) uni `services.Replace(...)` bilan almashtiradi |

Konfiguratsiya: `Security:RequireTotpForPrivilegedRoles` (default `true`; faqat appsettings.Development.json'da `false`).

### 1.9 Tenancy / Staff sxemasi

BE1a faqat sxema, entity va EF konfiguratsiyalarini yaratdi (endpointlar — BE1b): `tenancy.clinics`, `branches` (UNIQUE(clinic_id, id)), `branch_opening_hours`, `branch_booking_locks` (filial qo'shilganda trigger orqali avtomatik yaratiladi — qo'lda qo'shmang), `staff.clinic_memberships`, `branch_access` (kompozit FK'lar), `staff_invitations` (telefon HMAC + maskalangan shakl), `platform_admin_grants`. Domain fabrikalari: `Clinic.Register/Approve/Reject/Suspend/Reinstate`, `Branch.Create/Update`, `BranchOpeningHours.Create`, `ClinicMembership.Create/Change/SetStatus`, `BranchAccess.Create`, `StaffInvitation.Create/MarkAccepted/Revoke`, `PlatformAdminGrant.Create/Revoke`. Jadvallar tafsiloti: `docs/DATABASE.md` §2.1.

### 1.10 Konfiguratsiya kalitlari (jamlanma)

| Kalit | Default | Izoh |
|---|---|---|
| `Security:OtpPepper` | — (Development: ichki dev qiymati) | Sir. Production/Staging'da majburiy, ≥ 32 belgi |
| `Security:RequireTotpForPrivilegedRoles` | `true` | Development'da `false` |
| `Auth:Otp:*`, `Auth:Session:*`, `Auth:Password:*` (`MinLength` 10, `MaxLength` 128, `MaxFailedAttempts` 10, `LockoutMinutes` 15), `Auth:TelegramLinkTokenMinutes` (10) | appsettings.json | |
| `Sms:Provider`, `Sms:Eskiz:*` | Development: `Fake` | Production'da `Fake` taqiqlangan |
| `Telegram:Enabled`, `BotToken` (sir), `BotUsername`, `MiniAppUrl`, `AuthMaxAgeSeconds` | `false` | |
| `Seed:AdminEmail`, `Seed:AdminPassword`, `Seed:DemoPatientPhone` | dev parol faqat appsettings.Development.json'da | |
| `RateLimiting:*` | 300 / 10 / 20 per minute | |

Docker Compose bu kalitlarni `.env`dan uzatadi: `OQTISH_OTP_PEPPER`, `OQTISH_SMS_PROVIDER`, `OQTISH_ESKIZ_*`, `OQTISH_TELEGRAM_*` (`.env.example`).

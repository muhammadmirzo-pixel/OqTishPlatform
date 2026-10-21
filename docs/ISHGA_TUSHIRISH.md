# OqTish — dasturni lokal ishga tushirish va sinab ko'rish qo'llanmasi

Bu qo'llanmada dasturni o'z kompyuteringizda **Docker bilan** yoki **Docker'siz** to'liq ishga tushirish,
demo ma'lumotlarni yuklash, har bir rol bilan kirish va asosiy jarayonlarni sinab ko'rish bosqichma-bosqich
tushuntiriladi.

> Hujjat holati: 2026-10-21. Barcha buyruqlar **Windows 11** da (Docker Desktop 4.88, PowerShell + Git Bash,
> kompyuterdagi PostgreSQL 17.5, korporativ tarmoq) tekshirildi. Uchala usulda ham `deploy/smoke-test.sh`
> natijasi: **24/24 ✅**. Linux uchun farq qiladigan buyruqlar alohida berilgan.

---

## 0. Qaysi usulni tanlash kerak

| Usul | Qachon | Baza | Sayt manzili |
|---|---|---|---|
| **A. Docker + ichki baza** (birinchi marta uchun tavsiya) | Eng oson yo'l: kompyuterga faqat Docker kerak | Docker ichidagi PostgreSQL 18 | http://localhost:8080 |
| **B. Docker + kompyuterdagi PostgreSQL** | O'zingizning `OqTish_db` bazangizda ishlash | `localhost:5432 / OqTish_db` | http://localhost:8080 |
| **C. Docker'siz** | Kodni o'zgartirib, natijani tez ko'rish (dasturchilar uchun) | `localhost:5432 / OqTish_db` | http://localhost:5173 |

Bir vaqtda **faqat bitta usulni** ishlating. A va B bir xil 8080-portni ishlatadi, C esa 5080 va 5173-portlarni.
Boshqa usulga o'tishdan oldin avvalgisini to'xtating (`docker compose … down` yoki terminallarda `Ctrl+C`).

Har bir usuldan keyin [6-bo'lim](#6-ishlayotganini-tekshirish-smoke-test) dagi avtomatik tekshiruvni ishga tushiring.

---

## 1. Kerakli dasturlar

| Dastur | Versiya | Qaysi usul uchun | Tekshirish |
|---|---|---|---|
| Git (Windows'da **Git for Windows**, ichida Git Bash bor) | istalgan | hammasi | `git --version` |
| Docker Desktop (Windows/macOS) yoki Docker Engine + Compose (Linux) | Compose v2.24+ | A, B | `docker compose version` |
| PostgreSQL | 17 yoki 18 | B, C | `pg_isready -h localhost -p 5432` |
| .NET SDK | 10.0.100 yoki undan yangi (SDK 11 ham ishlaydi) | C (va testlar) | `dotnet --list-sdks` |
| .NET **runtime** 10 (`Microsoft.AspNetCore.App 10.0.x`) | 10.0.x | C | `dotnet --list-runtimes` |
| Node.js | 24 LTS | C | `node -v` |
| Python 3 + bash (Windows'da Git Bash) | 3.x | smoke-test | `python --version` |

Izohlar:
- Loyiha `net10.0` uchun yozilgan. SDK 11 bilan ham yig'iladi, lekin ishga tushirish uchun **.NET 10 runtime** kerak.
  `global.json` 10.0.100 dan yangi istalgan SDK'ni qabul qiladi, shuning uchun Docker ichida SDK 10, kompyuterda esa SDK 11
  ishlatilishi mumkin.
- Windows'da Docker Desktop **Linux konteynerlar** (WSL 2) rejimida ishlashi kerak. Bu standart rejim.
- Buyruqlar loyiha papkasida bajariladi: `cd C:\Projects\OqTishPlatform` (Linux'da `cd ~/Projects/OqTishPlatform`).
  PowerShell va Git Bash buyruqlari farq qilsa, ikkalasi ham berilgan.

---

## 2. Bir martalik tayyorgarlik

### 2.1. Shell skriptlarning qator oxiri (faqat Windows)

Konteyner ichida ishlaydigan skriptlarda (`deploy/postgres/init/*.sh`, `deploy/nginx/*.sh`) qator oxiri **LF** bo'lishi
shart. Aks holda Postgres konteyneri yoki web konteyneri `$'\r': command not found` xatosi bilan to'xtaydi.
Repozitoriyadagi `.gitattributes` buni avtomatik ta'minlaydi. Lekin loyiha shu fayl qo'shilishidan oldin klon
qilingan bo'lsa, skriptlarni bir marta qayta oling (`.sh` fayllarda saqlanmagan o'zgarish bo'lmasa):

```powershell
git ls-files '*.sh' | ForEach-Object { Remove-Item $_; git checkout -- $_ }
git ls-files --eol '*.sh'        # har bir qatorda "w/lf" bo'lishi kerak
```

### 2.2. `.env` fayli

Barcha Docker sozlamalari va parollar loyiha ildizidagi `.env` faylida turadi. Bu fayl git'ga tushmaydi.
`.env.example` **server (Production) uchun namuna**. Uni lokal ishlatish uchun to'g'ridan-to'g'ri nusxalab
bo'lmaydi: masalan, `OQTISH_SMS_PROVIDER=Eskiz` qatori qolsa, SMS kodlar soxta qutiga emas, Eskiz'ga yuboriladi.

Lokal ishlash uchun `.env` ni quyidagicha yozing:

```dotenv
# Lokal ishlash uchun (git'ga tushmaydi). Qo'llanma: docs/ISHGA_TUSHIRISH.md
OQTISH_ENVIRONMENT=Development

# Docker ichidagi baza parollari. compose.yaml ularni har bir usulda talab qiladi (B usulida ishlatilmaydi,
# lekin bo'sh qoldirib bo'lmaydi).
POSTGRES_SUPERUSER_PASSWORD=<tasodifiy parol>
OQTISH_DB_OWNER_PASSWORD=<tasodifiy parol>
OQTISH_DB_API_PASSWORD=<tasodifiy parol>
OQTISH_DB_WORKER_PASSWORD=<tasodifiy parol>

# Kompyuterdagi PostgreSQL (B va C usullari).
OQTISH_HOSTDB_CONNECTION="Host=localhost;Port=5432;Database=OqTish_db;Username=postgres;Password=<postgres paroli>"
# Shu baza konteyner ichidan qaraganda (B usuli, Docker Desktop: Windows/macOS).
OQTISH_HOSTDB_DOCKER_CONNECTION="Host=host.docker.internal;Port=5432;Database=OqTish_db;Username=postgres;Password=<postgres paroli>"

OQTISH_TELEGRAM_ENABLED=false
```

Tasodifiy parol yaratish (har bir qator uchun alohida):

```powershell
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })   # PowerShell
```
```bash
openssl rand -hex 16                                                                        # Git Bash / Linux
```

Bunda SMS, Telegram va OTP pepper qiymatlari bo'sh qoladi. Development rejimida bu xavfsiz standartlarni yoqadi:
SMS va Telegram xabarlari soxta qutiga tushadi ([8.1](#81-sms-kodni-qanday-olish-bemor-sifatida-kirish)),
pepper o'rnida ichki dev qiymati ishlatiladi. Production'da dastur bunday sozlamalar bilan **ishga tushmaydi**.

> Ichki bazaning parollari (`OQTISH_DB_*`) baza **birinchi marta** yaratilganda o'rnatiladi. Keyin ularni
> o'zgartirsangiz, migratsiya ulana olmaydi. Bunday holda yo eski qiymatni qaytaring, yo ichki bazani
> `down -v` bilan o'chirib, qaytadan yarating (3.3).

### 2.3. Korporativ tarmoq: HTTPS'ni tekshiruvchi proksi (faqat kerak bo'lsa)

Ba'zi ish tarmoqlarida DLP yoki proksi tizimi (masalan, **Falcongaze SecureTower**) barcha HTTPS ulanishlarni o'z
sertifikati bilan qayta imzolaydi. Windows bu sertifikatga ishonadi, Docker konteynerlari esa ishonmaydi.
Natijada image yig'ish paytida quyidagi xatolardan biri chiqadi:

- `error NU1301: … The remote certificate is invalid because of errors in the certificate chain: PartialChain`
- `npm error code UNABLE_TO_VERIFY_LEAF_SIGNATURE`

Bunday sertifikat bormi, tekshirish uchun (Git Bash):

```bash
echo | openssl s_client -connect api.nuget.org:443 -servername api.nuget.org 2>/dev/null | openssl x509 -noout -issuer
# issuer=… CN=Falcongaze SecureTower   ← korporativ sertifikat (oddiy tarmoqda: Microsoft yoki DigiCert)
```

Yechim: ildiz sertifikatni `deploy/docker/extra-ca/` papkasiga `.crt` (PEM) ko'rinishida saqlang. Bu papkadagi
`.crt` fayllar git'ga tushmaydi, image yig'ishda esa avtomatik ishonchli deb qabul qilinadi. Papka bo'sh bo'lsa,
yig'ish o'zgarmaydi (CI va server uchun ham shunday).

```powershell
# "Falcongaze" o'rniga yuqoridagi issuer'dagi CN nomini yozing
$c = Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root | Where-Object Subject -like '*Falcongaze*' | Select-Object -First 1
$pem = "-----BEGIN CERTIFICATE-----`n" + [Convert]::ToBase64String($c.RawData, 'InsertLineBreaks').Replace("`r`n", "`n") + "`n-----END CERTIFICATE-----`n"
[IO.File]::WriteAllText("$PWD\deploy\docker\extra-ca\corporate-root.crt", $pem)
```

Kompyuterdagi `npm` ham shu xatoni bersa (C usuli), Node'ga Windows sertifikatlarini ishlatishni ayting:
`$env:NODE_USE_SYSTEM_CA = "1"` (PowerShell) yoki `export NODE_USE_SYSTEM_CA=1` (bash).

### 2.4. Kompyuterdagi bazani tayyorlash (B va C usullari)

PostgreSQL ishlayotganini tekshiring va bazani yarating (bir marta; baza mavjud bo'lsa, bu qadamni o'tkazib yuboring):

```powershell
pg_isready -h localhost -p 5432                  # "accepting connections" chiqishi kerak
createdb -h localhost -U postgres OqTish_db      # postgres foydalanuvchisi parolini so'raydi
```

Jadvallarni migratsiya yaratadi (B usulida `migrate` konteyneri, C usulida `dotnet-ef`). Birinchi migratsiya
`btree_gist` va `citext` kengaytmalarini o'rnatadi, shuning uchun lokal ishlashda `postgres` (superuser)
foydalanuvchisi bilan ulaning.

---

## 3. Usul A — Docker + ichki baza

Kompyuterdagi PostgreSQL kerak emas. Baza Docker ichida yaratiladi va `pgdata` volume'ida saqlanadi.

### 3.1. Ishga tushirish

Docker Desktop ishga tushgan bo'lishi kerak: pastki paneldagi kit belgisi yashil bo'lsin, `docker info` xatosiz
ishlasin.

```powershell
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait
```

Birinchi marta image'lar yig'iladi (bir necha daqiqa). Ishga tushish tartibi:

1. **postgres**: bo'sh bazada `deploy/postgres/init/01-create-roles.sh` bir marta ishlaydi. U `oqtish` bazasini va
   cheklangan rollarni (`oqtish_owner`, `oqtish_api`, `oqtish_worker`) yaratadi.
2. **migrate**: barcha jadvallarni yaratadi va to'xtaydi. `Exited (0)` normal holat.
3. **api** va **worker** (fon jarayonlari: muddati o'tgan so'rovlarni bekor qilish, bildirishnomalar va h.k.).
4. **web** (http://localhost:8080): sayt va API uchun yagona kirish nuqtasi (nginx).

Buyruq barcha konteynerlar `healthy` bo'lgunicha kutadi. Tekshirish:

```powershell
docker compose -f compose.yaml -f compose.dev.yaml ps -a
curl.exe http://localhost:8080/health/ready          # Healthy
```

### 3.2. Demo ma'lumotlarni yuklash (bir marta)

```powershell
docker compose -f compose.yaml -f compose.dev.yaml run --rm api seed
```

Bu buyruq to'qima demo klinikalar, filiallar, xizmatlar, shifokorlar, xodim loginlari, qabullar va sharhlarni
yaratadi. Oxirida `Seeding finished (5 contributors)` chiqadi. Uni qayta ishga tushirish xavfsiz (idempotent):
ma'lumotlar ikki marta yaratilmaydi. Seed faqat Development/Staging rejimida ishlaydi, Production'da rad etiladi.

### 3.3. To'xtatish va tozalash

```powershell
docker compose -f compose.yaml -f compose.dev.yaml down       # to'xtatish, ma'lumotlar saqlanadi
docker compose -f compose.yaml -f compose.dev.yaml down -v    # ⚠️ ichki bazani BUTUNLAY o'chiradi
```

- Ichki bazaga tashqaridan ulanish (masalan, DBeaver orqali; faqat shu usulda): `localhost:55432`,
  foydalanuvchi `postgres`, parol `.env` dagi `POSTGRES_SUPERUSER_PASSWORD`, baza `oqtish`.
- `down -v` faqat hammasini boshidan boshlash uchun kerak (masalan, `OQTISH_DB_*` parollari o'zgarganda).

---

## 4. Usul B — Docker + kompyuterdagi PostgreSQL (`OqTish_db`)

Avval 2.2 (`.env`) va 2.4 (baza) bo'limlarini bajaring. Ichki `postgres` konteyneri bu usulda ishga tushmaydi.

### 4.1. Windows va macOS (Docker Desktop)

Konteynerlar kompyuterdagi bazaga `host.docker.internal` orqali ulanadi. Shuning uchun `.env` da
`OQTISH_HOSTDB_DOCKER_CONNECTION` bo'lishi kerak. Docker Desktop bu ulanishni `127.0.0.1` dan kelgandek uzatadi,
shuning uchun `pg_hba.conf` ni o'zgartirish shart emas.

```powershell
docker compose -f compose.yaml -f compose.hostdb.desktop.yaml up -d --build --wait
docker compose -f compose.yaml -f compose.hostdb.desktop.yaml run --rm api seed      # bir marta
```

To'xtatish (ma'lumotlar `OqTish_db` da qoladi):

```powershell
docker compose -f compose.yaml -f compose.hostdb.desktop.yaml down
```

### 4.2. Linux (Docker Engine)

Linux'da kompyuterdagi PostgreSQL odatda faqat `127.0.0.1` da tinglaydi. Shuning uchun xizmatlar host tarmog'iga
ulanadi va `.env` dagi `OQTISH_HOSTDB_CONNECTION` (`Host=localhost`) ishlatiladi:

```bash
docker compose -f compose.yaml -f compose.hostdb.yaml up -d --build --wait
docker compose -f compose.yaml -f compose.hostdb.yaml run --rm api seed
docker compose -f compose.yaml -f compose.hostdb.yaml down
```

Bu rejimda API `127.0.0.1:5080` da, sayt `127.0.0.1:8080` da ishlaydi.

> `compose.hostdb.yaml` ni Windows'da ishlatmang. U `network_mode: host` ga tayanadi, Docker Desktop'da esa bu
> sozlama standart holatda o'chiq. Natijada API bazaga ulanmaydi va sayt ochilmaydi.

### 4.3. Har safar uzun buyruq yozmaslik uchun (ixtiyoriy)

`.env` ga qaysi compose fayllar ishlatilishini yozib qo'ysangiz, oddiy `docker compose …` ning o'zi yetadi:

```dotenv
COMPOSE_FILE=compose.yaml;compose.hostdb.desktop.yaml      # Windows (ajratuvchi ";")
# COMPOSE_FILE=compose.yaml:compose.hostdb.yaml            # Linux (ajratuvchi ":")
# COMPOSE_FILE=compose.yaml;compose.dev.yaml               # A usuli (Windows)
```

Shundan keyin: `docker compose up -d --build --wait`, `docker compose run --rm api seed`, `docker compose down`.

---

## 5. Usul C — Docker'siz (3 ta terminal)

Avval 2.2 (`.env`, faqat ulanish satri uchun) va 2.4 (baza) bo'limlarini bajaring. Docker Desktop kerak emas.
Uni o'chirib qo'ysangiz, xotira bo'shaydi (10-bo'limdagi `OutOfMemoryException` ga qarang).

### 5.1. Bir martalik sozlash

Ulanish satrini .NET **user-secrets** ga yozing. Bu sozlama kompyuteringizdagi foydalanuvchi profilida saqlanadi
(git'ga tushmaydi) va faqat Development rejimida o'qiladi:

```powershell
$conn = "Host=localhost;Port=5432;Database=OqTish_db;Username=postgres;Password=<postgres paroli>"
dotnet tool restore
dotnet user-secrets set "ConnectionStrings:Main" $conn --project src/OqTish.Api
dotnet user-secrets set "ConnectionStrings:Main" $conn --project src/OqTish.Worker
```

### 5.2. Migratsiya va demo ma'lumotlar

Birinchi marta, keyin esa har safar yangi migratsiya qo'shilganda bajariladi:

```powershell
$env:OQTISH_MIGRATIONS_CONNECTION = $conn
dotnet tool run dotnet-ef database update --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure
dotnet run --project src/OqTish.Api -- seed          # demo ma'lumotlar (bir marta, qayta ishga tushirish xavfsiz)
```

Bash (Linux yoki Git Bash) uchun `.env` dan o'qish mumkin:

```bash
set -a; . ./.env; set +a
OQTISH_MIGRATIONS_CONNECTION="$OQTISH_HOSTDB_CONNECTION" dotnet tool run dotnet-ef database update \
  --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure
dotnet run --project src/OqTish.Api -- seed
```

### 5.3. Ishga tushirish

```powershell
# 1-terminal: API (http://localhost:5080). Development rejimi launchSettings.json dan olinadi.
dotnet run --project src/OqTish.Api

# 2-terminal: Worker (muddati o'tgan so'rovlar, bildirishnomalar, eslatmalar)
$env:DOTNET_ENVIRONMENT = "Development"          # bash: export DOTNET_ENVIRONMENT=Development
dotnet run --project src/OqTish.Worker

# 3-terminal: sayt (http://localhost:5173)
cd web
npm ci                                           # birinchi marta va package-lock.json o'zgarganda
npm run dev
```

- Vite dev server `/api` va `/health` so'rovlarini avtomatik `localhost:5080` ga yo'naltiradi.
  Sayt va API bitta manzilda ochiladi, shuning uchun cookie va CSRF xuddi nginx orqali ishlagandek ishlaydi.
- Worker uchun `DOTNET_ENVIRONMENT=Development` shart. Aks holda u Production rejimida ishga tushadi,
  user-secrets'ni o'qimaydi va `Connection string 'ConnectionStrings:Main' is not configured` xatosini beradi.
- Kod o'zgarganda API va Worker'ni qayta ishga tushiring (`Ctrl+C`, keyin yana `dotnet run`).
  Frontend o'zgarishlari darhol ko'rinadi.
- Tekshirish: `curl.exe http://localhost:5173/health/ready` → `Healthy`.

---

## 6. Ishlayotganini tekshirish (smoke test)

`deploy/smoke-test.sh` 24 ta qadamni avtomatik tekshiradi: kirish (barcha rollar va SMS kod), bo'sh vaqt, bron
qilish, tasdiqlash, ikkinchi tasdiqlashning rad etilishi, boshqa klinika va rol ruxsatlari, to'lov, takroriy
to'lov, qaytarish qoidalari, balans, hisobot va admin. Skript seed bajarilgan Development rejimini talab qiladi.

Windows'da **Git Bash** da (VS Code: terminal → `Git Bash`), Linux'da oddiy terminalda ishga tushiring:

```bash
bash deploy/smoke-test.sh                                   # A va B usullari (http://localhost:8080)
BASE=http://localhost:5173 bash deploy/smoke-test.sh        # C usuli
```

Oxirida `Hammasi muvaffaqiyatli (all steps passed).` chiqishi kerak. Skriptga Python 3 kerak. U avval `python3` ni,
keyin `python` ni sinaydi (Windows'da `python3` ko'pincha Microsoft Store "yorlig'i" bo'ladi va ishlamaydi).
Kerak bo'lsa, Python'ni aniq ko'rsating: `PY=/c/Python312/python bash deploy/smoke-test.sh`.

---

## 7. Manzillar

| Manzil (A/B usuli; C usulida `8080` o'rniga `5173`) | Nima |
|---|---|
| http://localhost:8080 | Sayt: bemorlar qismi |
| http://localhost:8080/login | Kirish sahifasi |
| http://localhost:8080/clinic | Klinika paneli |
| http://localhost:8080/admin | Platforma admini paneli |
| http://localhost:8080/tg | Telegram Mini App (bemor); oddiy brauzerda ogohlantirish bilan ochiladi |
| http://localhost:8080/tg/staff | Telegram Mini App (xodim) |
| http://localhost:8080/swagger/ | API hujjatlari (Swagger, faqat Development; C usulida http://localhost:5080/swagger/) |
| http://localhost:8080/health/ready | Server va baza holati |

---

## 8. Demo loginlar

Bular **faqat lokal sinov uchun**, to'qima akkauntlar (seed yaratadi).

| Rol | E-mail | Parol | Telefon |
|---|---|---|---|
| Klinika egasi (ClinicOwner) | `owner@demo.oqtish.local` | `Demo123!owner` | +998900000011 |
| Klinika administratori | `clinicadmin@demo.oqtish.local` | `Demo123!admin` | +998900000012 |
| Qabulxona (faqat "Demo Chilonzor filiali") | `reception@demo.oqtish.local` | `Demo123!reception` | +998900000013 |
| Shifokor | `doctor@demo.oqtish.local` | `Demo123!doctor` | +998900000014 |
| Moliya menejeri | `finance@demo.oqtish.local` | `Demo123!finance` | +998900000015 |
| Tekshiruvdagi klinika egasi | `pending@demo.oqtish.local` | `Demo123!pending` | +998900000016 |
| Platforma admini | `admin@oqtish.local` | `Admin123!demo` | — |
| Demo bemor | — (SMS kod bilan) | — | +998900000001 |

Xodim va admin loginlari bilan kirish: `/login` sahifasi → **Xodimlar** yorlig'i → email va parol.

Lokal rejimda (Development) ikki bosqichli tasdiqlash (TOTP) majburiy emas. Production'da egasi, moliyachi va
admin uchun majburiy.

### 8.1. SMS kodni qanday olish (bemor sifatida kirish)

Lokal rejimda haqiqiy SMS yuborilmaydi. Kodlar "soxta SMS qutisi"ga tushadi:

1. `/login` → **Bemorlar** → telefon raqam (masalan `+998 90 000 00 01`) → **Kod yuborish**.
2. Kodni ko'rish uchun brauzerda (yoki terminalda) oching:
   ```powershell
   curl.exe "http://localhost:8080/api/dev/sms?phone=%2B998900000001"
   ```
   Javobdagi 6 xonali kodni kiriting.
3. Har qanday yangi `+998` raqam ham ishlaydi, bunda yangi bemor akkaunti yaratiladi.

`/api/dev/*` manzillari faqat Development rejimida ishlaydi, Production'da 404 qaytaradi.

---

## 9. Sinab ko'rish ssenariylari

### 9.1. Bemor: klinika topish va qabulga yozilish
1. http://localhost:8080 → klinikalar ro'yxati. Xizmat, tuman va saralash bo'yicha filtrlab ko'ring.
2. Klinikani oching → filiallar, ish vaqti, xizmatlar va narxlar, sharhlar.
3. **Qabulga yozilish** → filial → xizmat → sana → bo'sh vaqt → ism va telefon → **So'rov yuborish**.
   Kirmagan bo'lsangiz, avval kirish so'raladi (8.1).
4. **Qabullarim**: so'rov "Kutilmoqda" holatida, yonida klinika javobi uchun 30 daqiqalik hisoblagich.
5. Shu vaqtni boshqa bemor (boshqa raqam) bilan band qilishga urinib ko'ring. Yagona mos shifokor band bo'lsa,
   "Bu vaqt band bo'lib qoldi" degan xabar chiqadi.

### 9.2. Klinika: so'rovni tasdiqlash
1. Boshqa brauzerda yoki inkognito oynada `reception@demo.oqtish.local` bilan kiring → **Klinika paneli**.
2. **So'rovlar**: yangi so'rov, hisoblagich bilan. Shifokorni tanlang → **Tasdiqlash**. Ro'yxatda faqat shu
   xizmatni bajaradigan, shu filialda ishlaydigan va o'sha vaqtda bo'sh shifokorlar ko'rinadi.
3. Bemor oynasida holat **Tasdiqlangan** ga o'zgaradi va shifokor nomi ko'rinadi.
4. **Kalendar**: kunlik jadvalda qabul shifokor ustunida ko'rinadi.
5. 30 daqiqa javob bermasangiz, so'rov avtomatik **Muddati o'tgan** bo'ladi (buni worker bajaradi, shuning uchun
   C usulida 2-terminal ishlab turishi kerak).

### 9.3. Klinika: boshqa amallar
- Qabulni ko'chirish (reschedule), bekor qilish, **Yakunlandi** (narxni kiritib) yoki **Kelmadi**.
- **Walk-in**: telefon qilgan yoki joyida kelgan bemorni darhol tasdiqlangan qabulga yozish.
- **Shifokorlar**: haftalik jadval va dam olish kunlari. Band vaqtga dam olish qo'yib bo'lmaydi.
- **Xodimlar**: yangi xodimni telefon raqami bo'yicha taklif qilish.
- Rollarni solishtiring: `doctor@…` bilan kirsangiz, faqat o'z qabullaringiz ko'rinadi. Qabulxona boshqa
  filialni ko'rmaydi.

### 9.4. Platforma admini
1. `admin@oqtish.local` bilan kiring → http://localhost:8080/admin.
2. **Klinikalarni tekshirish**: "Demo Yangi Klinika (tekshiruvda)" → **Tasdiqlash**. Shundan keyin u ochiq
   qidiruvda ko'rinadi.
3. **Sharhlar**: moderatsiyani kutayotgan sharhni nashr qiling. Klinika reytingi yangilanadi.
4. **Audit jurnali**: barcha muhim amallar shu yerda yoziladi.

### 9.5. Moliya (to'lov va hisobot)
1. `reception@…` bilan tasdiqlangan qabulni oching → **To'lov kiritish** (summa, naqd/karta/o'tkazma).
2. `finance@…` bilan kiring → to'lovni **Qaytarish** (qabulxona qaytara olmaydi, bu to'g'ri).
3. **Hisobotlar**: kunlik tushum, qaytarish va komissiya. Demo klinikada 10% komissiya qoidasi bor
   ("DEMO-0001"). Masalan, 50 000 to'lov va 10 000 qaytarishdan keyin komissiya 4 000 bo'ladi.
4. Admin panel → **Hisob-kitoblar**: o'tgan oy uchun hisob-kitob yaratish, yakunlash, "to'landi" deb belgilash.

### 9.6. Bildirishnomalar va Telegram (lokal rejimda)
Lokal rejimda haqiqiy Telegram'ga hech narsa yuborilmaydi. Yuboriladigan xabarlar soxta qutiga tushadi:
```powershell
curl.exe http://localhost:8080/api/dev/telegram
```
Admin panel → **Bildirishnomalar**: har bir xabarning holati (yuborildi / kanal yo'q / xato).

http://localhost:8080/tg (Mini App) oddiy brauzerda "Telegram ichida oching" ogohlantirishini ko'rsatadi.
Bu to'g'ri xatti-harakat. Haqiqiy Telegram'da sinash uchun bot tokeni va HTTPS manzil kerak:
`docs/SERVICES.md` §5.9 "Telegram botni ulash" (lokal sinov uchun HTTPS tunnel bo'yicha eslatma ham bor).

---

## 10. Kundalik buyruqlar

`<fayllar>` o'rniga o'z usulingizni yozing: A — `-f compose.yaml -f compose.dev.yaml`,
B (Windows) — `-f compose.yaml -f compose.hostdb.desktop.yaml`, B (Linux) — `-f compose.yaml -f compose.hostdb.yaml`.
`COMPOSE_FILE` sozlangan bo'lsa (4.3), `<fayllar>` ni umuman yozmang.

| Vazifa | Buyruq |
|---|---|
| Holat | `docker compose <fayllar> ps -a` |
| API loglari | `docker compose <fayllar> logs -f api` |
| Worker loglari | `docker compose <fayllar> logs -f worker` |
| Migratsiya logi (xato bo'lsa) | `docker compose <fayllar> logs migrate` |
| Kod yangilangandan keyin | `docker compose <fayllar> up -d --build --wait` |
| To'xtatish | `docker compose <fayllar> down` |
| Zaxira nusxa | `BACKUP_SOURCE=host deploy/backup/backup.sh` (Git Bash; `docs/BACKUP_AND_RESTORE.md`) |
| Backend testlari | `dotnet test` (integratsiya testlari uchun Docker Desktop ishlab turishi kerak) |
| Frontend testlari | `cd web; npm run lint; npm run typecheck; npm test` |

---

## 11. Muammolar va yechimlar

| Belgi | Sabab | Yechim |
|---|---|---|
| `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine` | Docker Desktop ishga tushmagan | Docker Desktop'ni oching va `docker info` ishlaguncha kuting |
| `required variable OQTISH_DB_API_PASSWORD is missing a value` | `.env` da ichki baza parollari yo'q | 2.2: to'rtta `OQTISH_DB_*`/`POSTGRES_*` parolini yozing (B usulida ham kerak) |
| `postgres` yoki `web` konteyneri qayta-qayta to'xtaydi, logda `$'\r': command not found` | `.sh` fayllar CRLF bilan olingan | 2.1 |
| `NU1301 … PartialChain` yoki `UNABLE_TO_VERIFY_LEAF_SIGNATURE` (image yig'ishda) | Korporativ proksi HTTPS'ni qayta imzolaydi | 2.3 |
| `NU1507: There are 2 package sources defined` | Kompyuterda qo'shimcha NuGet manbasi (masalan, kompaniya feed'i) bor | Repozitoriyadagi `nuget.config` buni hal qiladi. Uni o'chirmang |
| `A compatible .NET SDK was not found` | SDK 10+ o'rnatilmagan | .NET SDK 10 (yoki 11) o'rnating |
| `You must install or update .NET to run this application` (`Microsoft.AspNetCore.App 10.0`) | .NET 10 runtime yo'q | .NET 10 runtime (ASP.NET Core) o'rnating |
| `migrate` `Exited (1)` | Bazaga ulanib bo'lmadi yoki parol noto'g'ri | `docker compose <fayllar> logs migrate`; B usulida `pg_isready -h localhost` va `.env` dagi parol; A usulida 2.2 dagi eslatma |
| `/health/ready` → `Unhealthy`, logda `1 pending migration(s)` | Yangi migratsiya qo'llanmagan | A/B: `up -d --build --wait` (migrate qayta ishlaydi); C: 5.2 |
| B usuli Windows'da: API `healthy` bo'lmaydi, sayt ochilmaydi | `compose.hostdb.yaml` (Linux varianti) ishlatilgan | 4.1: `compose.hostdb.desktop.yaml` |
| `OutOfMemoryException`, `GC heap initialization failed … 0x8007000E` | Kompyuterda xotira tugagan (ko'pincha Docker Desktop VM + build jarayonlari) | C usulida Docker Desktop'ni yoping; `dotnet build-server shutdown`; WSL xotirasini cheklang: `%UserProfile%\.wslconfig` → `[wsl2]` / `memory=4GB` |
| `'vite' is not recognized …` | `web/node_modules` o'rnatilmagan yoki to'liq emas | `cd web; npm ci` |
| `Connection string 'ConnectionStrings:Main' is not configured` (C usuli) | user-secrets yozilmagan yoki Worker Production rejimida | 5.1; Worker uchun `DOTNET_ENVIRONMENT=Development` |
| `port is already allocated` (8080 / 5080 / 55432) | Boshqa usul yoki dastur ishlab turibdi | Avvalgi usulni `down` qiling yoki `.env` da `OQTISH_HTTP_PORT=8090` / `OQTISH_DB_PORT=55433` |
| Kirgandan keyin darhol chiqarib yuboradi | Production rejimi HTTP orqali ochilgan (cookie faqat HTTPS uchun) | `.env` da `OQTISH_ENVIRONMENT=Development` va 3–4-bo'limlardagi overlay fayllar bilan ishga tushiring |
| SMS kod kelmayapti | Lokal rejimda SMS yuborilmaydi | 8.1: `/api/dev/sms?phone=…`. `.env` da `OQTISH_SMS_PROVIDER` bo'sh bo'lishi kerak |
| "Juda ko'p urinish" (429) | Kod so'rash limiti (15 daqiqada 3 marta) | 15 daqiqa kuting yoki boshqa raqam ishlating |
| Smoke test: `python 3 not found` | Python o'rnatilmagan yoki faqat Store yorlig'i bor | Python 3 o'rnating yoki `PY=…` bilan yo'lini ko'rsating (6-bo'lim) |

---

## 12. Xavfsizlik eslatmalari

- `.env`, user-secrets va `deploy/docker/extra-ca/*.crt` faqat sizning kompyuteringiz uchun. Ular git'ga tushmaydi,
  ularni hech kimga bermang.
- Demo loginlar faqat lokal sinov uchun. Production'da seed ishlamaydi.
- Production'da birinchi admin quyidagi buyruq bilan yaratiladi (parol buyruq qatorida berilmaydi):
  `OQTISH_ADMIN_PASSWORD=… dotnet OqTish.Api.dll create-platform-admin --email … --phone …`
- `postgres` superuser va oddiy parollar faqat lokal ishlash uchun. Serverda cheklangan rollar
  (`oqtish_owner` / `oqtish_api` / `oqtish_worker`) va kuchli parollar ishlatiladi (`docs/DEPLOYMENT.md`).

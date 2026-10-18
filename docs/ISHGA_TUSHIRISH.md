# OqTish — dasturni lokal ishga tushirish va sinab ko'rish qo'llanmasi

Bu qo'llanma dasturni o'z kompyuteringizda **to'liq ishga tushirish**, demo ma'lumotlarni yuklash,
har bir rol bilan kirish va asosiy ish jarayonlarini qo'lda sinab ko'rishni bosqichma-bosqich tushuntiradi.

> Hujjat holati: 2026-10-18 kechqurun. Moliya (to'lovlar, hisobotlar, komissiya) va Telegram bot hozir
> qurilmoqda. Ular tayyor bo'lgach, ushbu qo'llanma yangilanadi va har bir buyruq qayta tekshiriladi.

---

## 0. Hozir nimalar tayyor

| Qism | Holat |
|---|---|
| Kirish: telefon + SMS kod, email + parol (+ TOTP), sessiyalar | ✅ |
| Klinika ro'yxatdan o'tkazish, filiallar, ish soatlari, xizmatlar va narxlar | ✅ |
| Xodimlar, takliflar (telefon orqali), rollar va ruxsatlar | ✅ |
| Shifokorlar, haftalik jadval, dam olish kunlari | ✅ |
| Ochiq qidiruv: klinikalar, filtrlar, masofa, reyting | ✅ |
| Qabulga yozilish, 30 daqiqalik muddat, tasdiqlash/rad etish/bekor qilish, ko'chirish, yakunlash, kelmaslik, walk-in | ✅ |
| Kalendar, sharhlar va moderatsiya, shikoyatlar | ✅ |
| Platforma admini: klinikalarni tekshirish, foydalanuvchilarni bloklash, audit | ✅ |
| UI: bemorlar sayti, klinika paneli, admin paneli, Telegram Mini App sahifalari | ✅ |
| To'lovlar, qaytarishlar, komissiya, hisob-kitoblar, hisobotlar | 🔄 qurilmoqda |
| Telegram bot, bildirishnomalar (Telegram/SMS), eslatmalar | 🔄 navbatda |

UI'da hali tayyor bo'lmagan bo'limlar (masalan, to'lovlar) ochilsa, server xato qaytaradi. Bu shu
bosqichlar tugaguncha kutilgan holat.

---

## 1. Kerakli dasturlar

| Dastur | Versiya | Tekshirish |
|---|---|---|
| Docker Engine + Docker Compose | Docker 29, Compose v5 | `docker compose version` |
| .NET SDK | 10.0.401 | `dotnet --version` (Docker'siz ishlatish va seed uchun) |
| Node.js | 24 LTS | `node -v` (faqat Docker'siz frontend uchun) |
| PostgreSQL | 18 | kompyuterdagi baza ishlatilsa |

### Bu kompyuter uchun majburiy sozlamalar

Har bir **yangi terminalda** avval:

```bash
cd ~/Projects/OqTishProgram
export PATH=$HOME/.dotnet:$HOME/.local/opt/node/bin:$PATH
export DOCKER_CONTEXT=default
```

- `.NET` va `Node` bu kompyuterda `PATH` ga qo'shilmagan, shuning uchun birinchi `export` kerak.
- Docker Desktop odatda ishlamaydi, tizimdagi Docker Engine ishlatiladi. Buni `DOCKER_CONTEXT=default` ta'minlaydi.

---

## 2. Qaysi usulni tanlash kerak

| Usul | Qachon | Baza |
|---|---|---|
| **A. Docker + kompyuterdagi PostgreSQL** (tavsiya) | Siz sozlagan `OqTish_db` bazasida ishlash | `localhost:5432 / OqTish_db` |
| B. Docker + ichki baza | Kompyuterdagi bazaga tegmasdan, alohida sinash | Docker ichidagi PostgreSQL |
| C. Docker'siz | Kodni o'zgartirib, tez ko'rish (dasturchilar uchun) | `OqTish_db` |

Bir vaqtda **faqat bitta usulni** ishlating, chunki hammasi bir xil portlardan (8080 / 5080) foydalanadi.

---

## 3. Usul A — Docker + kompyuterdagi PostgreSQL (`OqTish_db`)

### 3.1. Bir martalik tekshiruv

`.env` faylida ulanish satri bo'lishi kerak. U allaqachon yozilgan:

```bash
grep '^OQTISH_HOSTDB_CONNECTION=' .env | sed 's/Password=[^;"]*/Password=***/'
# OQTISH_HOSTDB_CONNECTION="Host=localhost;Port=5432;Database=OqTish_db;Username=postgres;Password=***"
```

PostgreSQL ishlayotganini tekshiring:

```bash
pg_isready -h localhost -p 5432        # "accepting connections" chiqishi kerak
```

### 3.2. Ishga tushirish

```bash
docker compose -f compose.yaml -f compose.hostdb.yaml up -d --build --wait
```

Birinchi marta konteynerlar yig'iladi, bu bir necha daqiqa oladi. Ishga tushish tartibi:

1. **migrate**: `OqTish_db` bazasiga barcha yangi jadvallarni qo'shadi va to'xtaydi (`Exited (0)` normal).
2. **api** (`127.0.0.1:5080`) va **worker** (fon jarayonlari: muddati o'tgan so'rovlarni bekor qilish va h.k.).
3. **web** (`http://localhost:8080`): sayt va API uchun yagona kirish nuqtasi.

Tekshirish:

```bash
docker compose -f compose.yaml -f compose.hostdb.yaml ps -a
curl http://localhost:8080/health/ready          # Healthy
```

### 3.3. Demo ma'lumotlarni yuklash (bir marta)

```bash
docker compose -f compose.yaml -f compose.hostdb.yaml run --rm api seed
```

Bu buyruq to'qima demo klinikalar, filiallar, xizmatlar, shifokorlar, xodim loginlari, qabullar va
sharhlarni yaratadi. Uni qayta ishga tushirish xavfsiz (idempotent): ma'lumotlar ikki marta yaratilmaydi.
Seed faqat Development/Staging rejimida ishlaydi va Production'da rad etiladi.

### 3.4. To'xtatish

```bash
docker compose -f compose.yaml -f compose.hostdb.yaml down
```

Ma'lumotlar `OqTish_db` da saqlanib qoladi.

---

## 4. Usul B — Docker + ichki baza

```bash
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait
docker compose -f compose.yaml -f compose.dev.yaml run --rm api seed
```

- Sayt: http://localhost:8080
- Ichki bazaga ulanish (faqat shu rejimda): `localhost:55432`
- To'xtatish: `docker compose -f compose.yaml -f compose.dev.yaml down`

> ⚠️ `down -v` ichki bazani **butunlay o'chiradi**. Faqat tozalab, boshidan boshlash uchun ishlating.

---

## 5. Usul C — Docker'siz (3 ta terminal)

Ulanish satri .NET **user-secrets** da saqlangan, shuning uchun qo'shimcha sozlash kerak emas.

```bash
# 1-terminal: migratsiya va seed (bir marta, keyin har yangilanishda)
set -a; . ./.env; set +a
OQTISH_MIGRATIONS_CONNECTION="$OQTISH_HOSTDB_CONNECTION" dotnet tool run dotnet-ef database update \
  --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure
dotnet run --project src/OqTish.Api -- seed

# 1-terminal: API  (http://localhost:5080)
dotnet run --project src/OqTish.Api

# 2-terminal: Worker (muddati o'tgan so'rovlarni bekor qiladi)
DOTNET_ENVIRONMENT=Development dotnet run --project src/OqTish.Worker

# 3-terminal: sayt (http://localhost:5173)
cd web && npm ci && npm run dev
```

Vite dev server `/api` va `/health` so'rovlarini avtomatik `localhost:5080` ga yo'naltiradi.

---

## 6. Manzillar

| Manzil | Nima |
|---|---|
| http://localhost:8080 | Sayt: bemorlar qismi (usul A/B) |
| http://localhost:8080/login | Kirish sahifasi |
| http://localhost:8080/clinic | Klinika paneli |
| http://localhost:8080/admin | Platforma admini paneli |
| http://localhost:8080/tg | Telegram Mini App (bemor), brauzerda ogohlantirish bilan ochiladi |
| http://localhost:8080/tg/staff | Telegram Mini App (xodim) |
| http://localhost:8080/swagger/ | API hujjatlari (Swagger, faqat Development) |
| http://localhost:8080/health/ready | Server va baza holati |
| http://localhost:5173 | Usul C'da sayt |

---

## 7. Demo loginlar

Bular **faqat lokal sinov uchun**, to'qima akkauntlar.

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

Lokal rejimda (Development) ikki bosqichli tasdiqlash (TOTP) majburiy emas. Production'da egasi,
moliyachi va admin uchun majburiy bo'ladi.

### 7.1. SMS kodni qanday olish (bemor sifatida kirish)

Lokal rejimda haqiqiy SMS yuborilmaydi. Kodlar "soxta SMS qutisi"ga tushadi:

1. `/login` → **Bemorlar** → telefon raqam (masalan `+998 90 000 00 01`) → **Kod yuborish**.
2. Kodni ko'rish uchun brauzerda (yoki terminalda) oching:
   ```bash
   curl 'http://localhost:8080/api/dev/sms?phone=%2B998900000001'
   ```
   Javobdagi 6 xonali kodni kiriting.
3. Har qanday yangi `+998` raqam ham ishlaydi, bunda yangi bemor akkaunti yaratiladi.

`/api/dev/*` manzillari faqat Development rejimida ishlaydi va Production'da 404 qaytaradi.

---

## 8. Sinab ko'rish ssenariylari

### 8.1. Bemor: klinika topish va qabulga yozilish
1. http://localhost:8080 → klinikalar ro'yxati. Xizmat, tuman va saralash bo'yicha filtrlab ko'ring.
2. Klinikani oching → filiallar, ish vaqti, xizmatlar va narxlar, sharhlar.
3. **Qabulga yozilish** → filial → xizmat → sana → bo'sh vaqt → ism va telefon → **So'rov yuborish**.
   Kirmagan bo'lsangiz, avval kirish so'raladi (7.1).
4. **Qabullarim**: so'rov "Kutilmoqda" holatida, yonida klinika javobi uchun 30 daqiqalik hisoblagich.
5. Shu vaqtni boshqa bemor (boshqa raqam) bilan band qilishga urinib ko'ring. Agar yagona mos shifokor
   band bo'lsa, "Bu vaqt band bo'lib qoldi" degan xabar chiqadi.

### 8.2. Klinika: so'rovni tasdiqlash
1. Boshqa brauzer yoki inkognito oynada `reception@demo.oqtish.local` bilan kiring → **Klinika paneli**.
2. **So'rovlar**: yangi so'rov, hisoblagich bilan. Shifokorni tanlang → **Tasdiqlash**.
   Ro'yxatda faqat shu xizmatni bajaradigan, shu filialda ishlaydigan va bo'sh shifokorlar ko'rinadi.
3. Bemor oynasida holat **Tasdiqlangan** ga o'zgaradi va shifokor nomi ko'rinadi.
4. **Kalendar**: kunlik jadvalda qabul shifokor ustunida ko'rinadi.
5. 30 daqiqa javob bermasangiz, so'rov avtomatik **Muddati o'tgan** bo'ladi (worker bajaradi).

### 8.3. Klinika: boshqa amallar
- Qabulni ko'chirish (reschedule), bekor qilish, **Yakunlandi** (narxni kiritib) yoki **Kelmadi**.
- **Walk-in**: telefon yoki joyida kelgan bemorni darhol tasdiqlangan qabulga yozish.
- **Shifokorlar**: haftalik jadval va dam olish kunlari. Band vaqtga dam olish qo'yib bo'lmaydi.
- **Xodimlar**: yangi xodimni telefon raqami bo'yicha taklif qilish.
- Rollarni solishtiring: `doctor@…` bilan kirsangiz, faqat o'z qabullaringiz ko'rinadi. Qabulxona
  boshqa filialni ko'rmaydi.

### 8.4. Platforma admini
1. `admin@oqtish.local` bilan kiring → http://localhost:8080/admin.
2. **Klinikalarni tekshirish**: "Demo Yangi Klinika (tekshiruvda)" → **Tasdiqlash**. U shundan keyin ochiq
   qidiruvda ko'rinadi.
3. **Sharhlar**: moderatsiyani kutayotgan sharhni nashr qiling. Klinika reytingi yangilanadi.
4. **Audit jurnali**: barcha muhim amallar shu yerda yoziladi.

### 8.5. Telegram Mini App (brauzerda)
http://localhost:8080/tg sahifasi oddiy brauzerda "Telegram ichida oching" ogohlantirishini ko'rsatadi.
Bu to'g'ri xatti-harakat. Telegram ichida to'liq ishlashi uchun bot tokeni va HTTPS manzil kerak. Bot
tayyor bo'lgach, sozlash bo'yicha alohida bo'lim qo'shiladi.

---

## 9. Kundalik buyruqlar (usul A misolida)

| Vazifa | Buyruq |
|---|---|
| Holat | `docker compose -f compose.yaml -f compose.hostdb.yaml ps -a` |
| API loglari | `docker compose -f compose.yaml -f compose.hostdb.yaml logs -f api` |
| Worker loglari | `docker compose -f compose.yaml -f compose.hostdb.yaml logs -f worker` |
| Kod yangilangandan keyin | `docker compose -f compose.yaml -f compose.hostdb.yaml up -d --build --wait` |
| Zaxira nusxa | `BACKUP_SOURCE=host deploy/backup/backup.sh` (`docs/BACKUP_AND_RESTORE.md`) |
| To'xtatish | `docker compose -f compose.yaml -f compose.hostdb.yaml down` |

---

## 10. Muammolar va yechimlar

| Belgi | Sabab | Yechim |
|---|---|---|
| `failed to connect to the docker API … desktop/docker.sock` | Docker Desktop ishlamayapti | `export DOCKER_CONTEXT=default` |
| `dotnet: command not found` / `npm: command not found` | PATH sozlanmagan | 1-bo'limdagi `export PATH=…` |
| `migrate` `Exited (1)` | Bazaga ulanib bo'lmadi | `pg_isready -h localhost`, `.env` dagi parol, `docker compose … logs migrate` |
| `api` `healthy` bo'lmayapti | Migratsiya qo'llanmagan yoki baza ishlamayapti | `curl localhost:8080/health/ready`, `logs api` |
| `port is already allocated` (8080/5080) | Boshqa usul yoki dastur ishlab turibdi | Avvalgi usulni `down` qiling yoki `.env` da `OQTISH_HTTP_PORT=8090` |
| Kirgandan keyin darhol chiqarib yuboradi | Production rejimida HTTP orqali ochilgan (cookie faqat HTTPS uchun) | Lokal sinovda `compose.dev.yaml` yoki `compose.hostdb.yaml` overlay'ini ishlating (Development) |
| SMS kod kelmayapti | Lokal rejimda SMS yuborilmaydi | 7.1-bo'lim: `/api/dev/sms?phone=…` |
| "Juda ko'p urinish" (429) | Kod so'rash limiti (15 daqiqada 3 marta) | 15 daqiqa kuting yoki boshqa raqam |
| `NU1102: Unable to find package` | NuGet keshi eskirgan | `dotnet restore --no-http-cache --force-evaluate` |

---

## 11. Xavfsizlik eslatmalari

- `.env` va user-secrets'dagi parollarni hech kimga bermang. `.env` git'ga tushmaydi.
- Demo loginlar faqat lokal sinov uchun. Production'da seed ishlamaydi.
- Production'da birinchi admin quyidagi buyruq bilan yaratiladi (parol buyruq qatorida berilmaydi):
  `OQTISH_ADMIN_PASSWORD=… dotnet OqTish.Api.dll create-platform-admin --email … --phone …`
- `postgres` superuser va oddiy parol faqat lokal ishlash uchun. Serverda cheklangan rollar
  (`oqtish_owner` / `oqtish_api` / `oqtish_worker`) va kuchli parollar ishlatiladi.

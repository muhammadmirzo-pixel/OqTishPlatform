# OqTish — dasturni ishga tushirish bo'yicha qo'llanma

Bu qo'llanma dasturni o'z kompyuteringizda (lokal) ishga tushirish, tekshirish va to'xtatishni
tushuntiradi. Hozirgi holat: **2-bosqich (asos)**. Tizim ishga tushadi, ma'lumotlar bazasi tayyorlanadi
va server holati ko'rinadi. Klinikalar, qabulga yozilish va to'lovlar kabi biznes funksiyalari hali
yo'q, ular 3-bosqichdan boshlab qo'shiladi.

---

## 1. Nimalar kerak

| Dastur | Versiya | Tekshirish buyrug'i |
|---|---|---|
| Docker Engine + Docker Compose | Docker 29, Compose v5 | `docker compose version` |
| .NET SDK (faqat testlar va Docker'siz ishga tushirish uchun) | 10.0.401 | `dotnet --version` |
| Node.js (faqat frontendni alohida ishlatish uchun) | 24 LTS | `node -v` |

Faqat Docker orqali ishga tushirmoqchi bo'lsangiz, **Docker'ning o'zi yetarli**. .NET va Node
konteyner ichida o'rnatiladi.

### Shu kompyuterga xos sozlamalar

Bu kompyuterda .NET va Node `PATH` ga qo'shilmagan, Docker Desktop esa odatda ishlamaydi. Har bir
yangi terminalda avval quyidagilarni bajaring:

```bash
export PATH=$HOME/.dotnet:$HOME/.local/opt/node/bin:$PATH
export DOCKER_CONTEXT=default                     # docker / docker compose uchun
```

Testlarni ishga tushirishdan oldin bunisi ham kerak:

```bash
export DOCKER_HOST=unix:///var/run/docker.sock    # Testcontainers (dotnet test) uchun
```

---

## 2. Birinchi marta ishga tushirish (Docker orqali, tavsiya etiladi)

Barcha buyruqlar loyiha papkasida bajariladi:

```bash
cd ~/Projects/OqTishProgram
```

### 2.1. Maxfiy sozlamalar faylini (`.env`) yaratish

`.env` faylida ma'lumotlar bazasi parollari saqlanadi. U git'ga **hech qachon** qo'shilmaydi.

```bash
cp .env.example .env
for v in POSTGRES_SUPERUSER_PASSWORD OQTISH_DB_OWNER_PASSWORD OQTISH_DB_API_PASSWORD OQTISH_DB_WORKER_PASSWORD; do
  sed -i "s|^$v=.*|$v=$(openssl rand -hex 16)|" .env
done
sed -i 's|^OQTISH_ENVIRONMENT=.*|OQTISH_ENVIRONMENT=Development|' .env
chmod 600 .env
```

Bu buyruqlar 4 ta parolni tasodifiy, kuchli qiymatlar bilan to'ldiradi.

> **Eslatma:** Bu kompyuterda `.env` allaqachon yaratilgan. Uni qayta yaratmang, aks holda yangi
> parollar mavjud ma'lumotlar bazasiga mos kelmay qoladi (6-bo'limga qarang).

### 2.2. Tizimni ishga tushirish

```bash
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait
```

Birinchi marta konteyner tasvirlari yuklab olinadi va yig'iladi, bu bir necha daqiqa davom etadi.

Ishga tushish tartibi:

1. **postgres** — ma'lumotlar bazasi ishga tushadi va kerakli rollarni yaratadi.
2. **migrate** — baza tuzilmasini (migratsiyalarni) qo'llaydi va o'zi to'xtaydi. Bu normal holat.
3. **api** va **worker** — server va fon xizmati ishga tushadi.
4. **web** — sayt va API uchun kirish nuqtasi.

### 2.3. Ishlayotganini tekshirish

```bash
docker compose ps -a
```

Kutilgan natija:

| Xizmat | Holat |
|---|---|
| postgres | `Up … (healthy)` |
| migrate | `Exited (0)` (muvaffaqiyatli tugagan) |
| api | `Up … (healthy)` |
| worker | `Up …` |
| web | `Up … (healthy)` |

Brauzerda oching:

| Manzil | Nima ko'rinadi |
|---|---|
| http://localhost:8080 | Bosh sahifa: **"Server holati: Ishlayapti"** |
| http://localhost:8080/swagger/ | API hujjatlari (Swagger, faqat Development rejimida) |
| http://localhost:8080/health/ready | `Healthy` — server va baza tayyor |

Terminal orqali tekshirish:

```bash
curl http://localhost:8080/health/ready      # Healthy
```

---

## 2A. Kompyuterdagi PostgreSQL bilan ishlash (`OqTish_db`)

Dastur Docker ichidagi bazaning o'rniga **kompyuterning o'zidagi PostgreSQL'dan** ham foydalana oladi.
Bu kompyuterda shu rejim sozlangan:

| Parametr | Qiymat |
|---|---|
| Host | `localhost` |
| Port | `5432` |
| Baza | `OqTish_db` |
| Foydalanuvchi | `postgres` |
| Parol | `.env` faylida (git'ga tushmaydi) |

Ulanish satri **faqat** `.env` faylida, qo'shtirnoq ichida saqlanadi:

```bash
OQTISH_HOSTDB_CONNECTION="Host=localhost;Port=5432;Database=OqTish_db;Username=postgres;Password=..."
```

Qo'shtirnoq majburiy. Busiz shell `;` belgisini buyruqlar ajratuvchisi deb tushunadi.

### Docker orqali ishga tushirish

```bash
docker compose -f compose.yaml -f compose.hostdb.yaml up -d --build --wait
```

- Docker ichidagi `postgres` xizmati bu rejimda **ishga tushmaydi**.
- `migrate` jadvallarni `OqTish_db` ga yozadi. Baza bo'lmasa, uni o'zi yaratadi.
- API `127.0.0.1:5080` da, sayt http://localhost:8080 da ishlaydi.
- Xizmatlar `host` tarmog'ida ishlaydi, chunki PostgreSQL faqat `127.0.0.1` ni tinglaydi.

To'xtatish: `docker compose -f compose.yaml -f compose.hostdb.yaml down`.

> Bitta vaqtda faqat bitta rejimni ishlating: `compose.dev.yaml` **yoki** `compose.hostdb.yaml`.
> Ikkalasi ham 8080 portini ishlatadi.

### Docker'siz ishga tushirish (`dotnet run`)

Ulanish satri .NET **user-secrets** ga ham yozilgan. U loyiha papkasidan tashqarida, `~/.microsoft/usersecrets`
da saqlanadi. Shuning uchun quyidagi buyruqlar qo'shimcha sozlashsiz ishlaydi:

```bash
dotnet run --project src/OqTish.Api                               # http://localhost:5080
DOTNET_ENVIRONMENT=Development dotnet run --project src/OqTish.Worker
cd web && npm run dev                                             # http://localhost:5173
```

Parol yoki baza o'zgarsa, ikkala joyni yangilang:

```bash
set -a; . ./.env; set +a
for p in Api Worker; do dotnet user-secrets set "ConnectionStrings:Main" "$OQTISH_HOSTDB_CONNECTION" --project src/OqTish.$p; done
```

Migratsiyalarni qo'lda qo'llash:

```bash
set -a; . ./.env; set +a
OQTISH_MIGRATIONS_CONNECTION="$OQTISH_HOSTDB_CONNECTION" dotnet tool run dotnet-ef database update \
  --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure
```

> ⚠️ **Xavfsizlik:** `postgres` superuser va oddiy parol faqat lokal ishlash uchun maqbul. Production'da
> dastur alohida, cheklangan huquqli rollar (`oqtish_owner`, `oqtish_api`, `oqtish_worker`) bilan
> ishlashi kerak (`docs/DATABASE.md` §3) va parol kuchli bo'lishi shart.

---

## 3. Kundalik ishlatish

| Vazifa | Buyruq |
|---|---|
| Ishga tushirish | `docker compose -f compose.yaml -f compose.dev.yaml up -d --wait` |
| Kod o'zgargandan keyin qayta yig'ish | `docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait` |
| Holatni ko'rish | `docker compose ps -a` |
| Loglarni ko'rish | `docker compose logs -f api` (yoki `worker`, `web`, `postgres`, `migrate`) |
| To'xtatish (ma'lumotlar saqlanadi) | `docker compose down` |

> ⚠️ **`docker compose down -v`** ma'lumotlar bazasini **butunlay o'chiradi**. Uni faqat lokal,
> keraksiz bazani tozalash uchun ishlating.

Ma'lumotlar bazasiga to'g'ridan-to'g'ri ulanish (faqat dev rejimida, port 55432):

```bash
set -a; . ./.env; set +a
PGPASSWORD="$OQTISH_DB_API_PASSWORD" psql -h 127.0.0.1 -p 55432 -U oqtish_api -d oqtish
```

---

## 4. Docker'siz ishga tushirish (dasturchilar uchun)

Bu usul kodni tez o'zgartirib ko'rish uchun qulay. Baza baribir Docker'da ishlaydi.

```bash
# 1) Faqat bazani ishga tushirish va migratsiyalarni qo'llash
docker compose -f compose.yaml -f compose.dev.yaml up -d --wait postgres
docker compose -f compose.yaml -f compose.dev.yaml up migrate

# 2) API (http://localhost:5080)
set -a; . ./.env; set +a
export ConnectionStrings__Main="Host=localhost;Port=55432;Database=oqtish;Username=oqtish_api;Password=$OQTISH_DB_API_PASSWORD"
dotnet run --project src/OqTish.Api

# 3) Frontend, boshqa terminalda (http://localhost:5173)
cd web
npm ci
npm run dev
```

Frontend `/api` va `/health` so'rovlarini avtomatik ravishda `localhost:5080` dagi API'ga yo'naltiradi.

---

## 5. Testlar va tekshiruvlar

```bash
export DOCKER_HOST=unix:///var/run/docker.sock

dotnet build                 # ogohlantirishlar ham xato hisoblanadi
dotnet test                  # 13 ta test, haqiqiy PostgreSQL 18 konteynerida

cd web
npm run lint                 # kod sifati
npm run typecheck            # TypeScript tekshiruvi
npm test                     # 18 ta frontend testi
npm run build                # production yig'ish
npm audit --omit=dev         # kutubxonalardagi zaifliklar
```

Oxirgi tekshiruvda (2026-10-18) hammasi muvaffaqiyatli o'tgan: 13/13 va 18/18.

---

## 6. Muammolar va yechimlar

| Belgi | Sabab | Yechim |
|---|---|---|
| `failed to connect to the docker API at unix:///home/.../docker.sock` | Docker Desktop ishlamayapti | `export DOCKER_CONTEXT=default` yoki Docker Desktop'ni ishga tushiring |
| `dotnet test` da Docker'ga ulanib bo'lmaydi | Testcontainers Docker'ni topa olmayapti | `export DOCKER_HOST=unix:///var/run/docker.sock` |
| `dotnet: command not found` | .NET `PATH` da yo'q | `export PATH=$HOME/.dotnet:$PATH` |
| `migrate` `Exited (1)` yoki boshqa nol bo'lmagan kod bilan tugadi | Bazaga ulanib bo'lmadi yoki migratsiya xatosi | `docker compose logs migrate` |
| `api` `healthy` bo'lmayapti | Migratsiyalar qo'llanmagan yoki baza ishlamayapti | `docker compose logs api`, `curl localhost:8080/health/ready` |
| `password authentication failed` | `.env` bazadan keyin qayta yaratilgan, parollar mos emas | Lokal bazani tozalang: `docker compose down -v`, keyin 2.2-qadamni takrorlang (**ma'lumotlar o'chadi**) |
| `port is already allocated` (8080) | Port band | `.env` da `OQTISH_HTTP_PORT=8090` qilib, qayta ishga tushiring |
| 5432 port band | Kompyuterda boshqa PostgreSQL ishlayapti | Hech narsa qilish shart emas: loyiha bazasi 55432 portda |
| `NU1102: Unable to find package` | NuGet keshi eskirgan | `dotnet restore --no-http-cache --force-evaluate` |

---

## 7. Muhim xavfsizlik qoidalari

- `.env` faylini hech kimga yubormang va git'ga qo'shmang. `.gitignore` buni oldini oladi.
- `compose.dev.yaml` faqat lokal ishlash uchun. Serverda (production) faqat `compose.yaml` ishlatiladi.
  Production'ga chiqarish yo'riqnomasi (HTTPS, zaxira nusxa, tiklash) 8-bosqichda yoziladi.
- Noma'lum yoki production bazasida `down -v`, migratsiya yoki boshqa o'chiruvchi buyruqlarni zaxira
  nusxasiz ishlatmang.

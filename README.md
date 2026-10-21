# OqTish — stomatologiya klinikalari uchun onlayn qabul platformasi

OqTish mustaqil stomatologiya klinikalari, ularning filiallari, xodimlari va bemorlarni bog'laydi.
- **Bemorlar:** klinika topadi, narxlarni solishtiradi va qabulga yoziladi.
- **Klinika:** so'rovni 30 daqiqa ichida tasdiqlaydi va shifokor tayinlaydi.
- **Moliya:** to'lovlar va komissiya o'zgarmas buxgalteriya daftarida hisobga olinadi.
- **Telegram:** bot va Mini App orqali yozilish va bildirishnomalar.

## Tarkibi

| Qism | Nima qiladi |
|---|---|
| **Bemorlar sayti** | Klinika qidirish (xizmat, tuman, masofa, narx, reyting), klinika sahifasi, qabulga yozilish, "Qabullarim", sharhlar |
| **Klinika paneli** | So'rovlar (30 daqiqalik hisoblagich), kalendar, shifokorlar va jadval, xizmatlar va narxlar, filiallar, xodimlar, walk-in, to'lovlar, hisobotlar, audit |
| **Platforma admini** | Klinikalarni tekshirish, foydalanuvchilarni bloklash, shikoyatlar, sharhlar moderatsiyasi, komissiya shartnomalari, oylik hisob-kitoblar, audit, bildirishnomalar |
| **Telegram bot + Mini App** | Hisobni ulash, qabul holati xabarlari va eslatmalar, Telegram ichida yozilish (bemor) va so'rovlarni tasdiqlash (xodim) |
| **Worker (fon xizmati)** | Muddati o'tgan so'rovlarni bekor qilish, bildirishnomalarni yuborish, eslatmalar, moliyaviy moslik, eski ma'lumotlarni tozalash |

Interfeys tillari: o'zbek (lotin), rus, ingliz.

## Tez boshlash (lokal)

Docker + ichki baza (Windows, macOS, Linux). Avval `.env` ni yozing:
[docs/ISHGA_TUSHIRISH.md](docs/ISHGA_TUSHIRISH.md) §2.2.

```bash
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait     # ichki PostgreSQL bilan
docker compose -f compose.yaml -f compose.dev.yaml run --rm api seed        # demo ma'lumotlar (bir marta)
bash deploy/smoke-test.sh                                                   # 24 ta avtomatik tekshiruv (Windows'da Git Bash)
```

Sayt: http://localhost:8080. Kompyuterdagi PostgreSQL bilan ishlash, Docker'siz ishga tushirish, demo loginlar,
SMS kodni olish va qo'lda sinov ssenariylari [docs/ISHGA_TUSHIRISH.md](docs/ISHGA_TUSHIRISH.md) da.

## Texnologiyalar

.NET 10 (ASP.NET Core API + Worker Service, EF Core + Npgsql), PostgreSQL 18, React 19 + TypeScript +
Vite, nginx, Docker Compose, GitLab CI, xUnit v3 + Testcontainers (haqiqiy PostgreSQL), Vitest.
Aniq versiyalar: [docs/DECISIONS.md](docs/DECISIONS.md) §1.1.

## Arxitektura qisqacha

- **Modulli monolit:** bitta API, bitta Worker, bitta PostgreSQL. Qabul, bildirishnoma navbati (outbox)
  va moliya daftari bitta tranzaksiyada yoziladi, shuning uchun ma'lumot hech qachon "yarim" saqlanmaydi.
- **Qatlamlar:**
  - `Domain`: qoidalar va holatlar mashinasi;
  - `Application`: amallar (handler'lar) va ruxsat tekshiruvi;
  - `Infrastructure`: baza, SMS, Telegram;
  - `Api` va `Worker`: kirish nuqtalari.

  Qatlamlar orasidagi bog'liqlik testlar bilan nazorat qilinadi.
- **Ikki marta band qilib bo'lmaydi:**
  - Bemor so'rov yuborganda server mos shifokorning vaqtini yashirin "band" qiladi.
  - PostgreSQL'ning GiST exclusion cheklovi bitta shifokorga ikkita ustma-ust vaqt yozilishini **ma'lumotlar bazasi darajasida** taqiqlaydi.
  - Bir vaqtdagi so'rovlar 50 martalik testlar bilan tekshirilgan.
- **30 daqiqalik muddat** server vaqti bilan tekshiriladi. Worker muddati o'tgan so'rovlarni bekor qiladi va vaqtni bo'shatadi.
- **Moliya:** ikki tomonlama (double-entry), faqat qo'shiladigan (append-only) daftar.
  - Xatolar o'chirilmaydi, teskari yozuv bilan tuzatiladi.
  - Takroriy so'rov ikki marta yozilmaydi (idempotentlik).
  - Komissiya faqat admin kiritgan shartnoma qoidasidan olinadi; qoida bo'lmasa, komissiya 0.

Batafsil: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/SERVICES.md](docs/SERVICES.md)
(har bir servis va handler nima qiladi, o'zbekcha).

## Xavfsizlik qisqacha

- **Klinikalar ajratilgan:**
  - Har bir so'rovda resurs bazadan o'qiladi va **uning o'z** klinikasi/filiali bo'yicha ruxsat tekshiriladi.
  - Boshqa klinika ma'lumotiga urinish → 404, ruxsat yo'q → 403.
  - Bazada klinikaga bog'langan kompozit tashqi kalitlar bor.
- **Kirish:**
  - Bemorlar SMS kod yoki Telegram orqali kiradi.
  - Xodimlar email + parol bilan kiradi; egasi, moliyachi va admin uchun **TOTP majburiy**.
  - Sessiyalar serverda saqlanadi va bekor qilinishi mumkin (mutlaq muddat 60 kun).
  - Xato kodlar soni cheklangan.
- **Himoya:** CSRF uchun majburiy sarlavha, IP va foydalanuvchi bo'yicha limitlar, xavfsizlik sarlavhalari (CSP va boshqalar).
- **Ma'lumotlar bazasi rollari:** dastur jadval yarata olmaydi va daftar yoki auditni o'zgartira olmaydi.
- **Maxfiylik:**
  - Loglarda telefon, ism, kod va tokenlar yo'q.
  - Telegram xabarlarida tibbiy ma'lumot yo'q; xodimlarga bemor telefoni yuborilmaydi.
- **Production rejimi** xavfli sozlamalar bilan ishga tushmaydi.

Batafsil: [docs/SECURITY.md](docs/SECURITY.md), tekshiruv natijalari: [docs/DECISIONS.md](docs/DECISIONS.md) §6.

## Testlar

```bash
export DOCKER_HOST=unix:///var/run/docker.sock
dotnet build && dotnet test            # ~430 ta test (arxitektura, unit, haqiqiy PostgreSQL integratsiya, bir vaqtdagi so'rovlar)
cd web && npm run lint && npm run typecheck && npm test && npm run build    # 93 ta frontend testi
```

## Hujjatlar

| Hujjat | Mazmuni | Til |
|---|---|---|
| [docs/ISHGA_TUSHIRISH.md](docs/ISHGA_TUSHIRISH.md) | Lokal ishga tushirish, demo loginlar, sinov ssenariylari | 🇺🇿 |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Serverga chiqarish, yangilash, orqaga qaytarish | 🇺🇿 |
| [docs/SERVICES.md](docs/SERVICES.md) | Har bir servis, handler, fon jarayoni va Telegram botni ulash | 🇺🇿 |
| [docs/FRONTEND.md](docs/FRONTEND.md) | Sahifalar xaritasi, ruxsatlar, Mini App | 🇺🇿 |
| [docs/YAKUNIY_HISOBOT.md](docs/YAKUNIY_HISOBOT.md) | Nima qilindi, qanday tekshirildi, ma'lum cheklovlar | 🇺🇿 |
| [docs/BACKUP_AND_RESTORE.md](docs/BACKUP_AND_RESTORE.md) | Zaxira nusxa va tiklash (sinovdan o'tgan) | 🇬🇧 |
| [docs/API.md](docs/API.md) | Barcha API endpointlari, ruxsatlar, xato kodlari | 🇬🇧 (texnik ma'lumotnoma) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/DATABASE.md](docs/DATABASE.md) | Arxitektura, ER-diagramma, cheklovlar | 🇬🇧 |
| [docs/SECURITY.md](docs/SECURITY.md), [docs/FINANCIAL_RULES.md](docs/FINANCIAL_RULES.md) | Xavfsizlik, ruxsatlar matritsasi, moliya qoidalari | 🇬🇧 |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Barcha qarorlar (§6 tekshiruv natijalari o'zbekcha) | 🇬🇧/🇺🇿 |
| [docs/SRS.md](docs/SRS.md), [docs/TESTING.md](docs/TESTING.md), [docs/CONVENTIONS.md](docs/CONVENTIONS.md), [docs/WORKLOG.md](docs/WORKLOG.md) | Talablar, testlar katalogi, kod qoidalari, ish jurnali | 🇬🇧 |

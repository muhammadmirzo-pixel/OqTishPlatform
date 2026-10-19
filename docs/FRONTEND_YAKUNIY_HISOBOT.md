# Frontend qayta dizayni — yakuniy hisobot

Sana: 2026-10-19. Yo‘nalish: "Moviy klinika" (egasi bergan referens, `FRONTEND_PHASE_A.md` §0).
Holat: **A–E fazalari tugadi; F fazasining backend talab qilmaydigan qismi tugadi.** Real backend bilan
foydalanuvchi yo‘llari (E2E) hali qilinmagan — buning uchun ishlaydigan backend kerak (pastda, §5).

## 1. Fazalar bo‘yicha natija

| Faza | Mazmun | Holat |
|---|---|---|
| A | Tahlil, 3 yo‘nalish, ekranlar ro‘yxati, tokenlar, API bo‘shliqlari (D1–D14) | ✅ |
| B | Dizayn tizimi: ranglar, Inter, Lucide, komponentlar, qorong‘i rejim, o‘zbek kirill | ✅ |
| C | Bemor qismi: landing, katalog, klinika sahifasi, 3 bosqichli booking, holat sahnasi, Qabullarim | ✅ |
| D | Klinika paneli: dashboard (metrikalar + ta’riflar, bugungi qabullar, 14 kunlik moliya grafigi), so‘rovlar qutisi (shoshilinch belgisi, J/K), kalendar (Kun / Hafta / Ro‘yxat, ish vaqti foni, "hozir" chizig‘i), filial chip’i, qolgan 11 sahifa | ✅ |
| E | Platforma admini: xavfli amallar uchun qizil dialog + sabab (qaytarib bo‘lmaydiganlarga "Tushundim" belgisi), barcha admin sahifalari, "Platforma" belgisi | ✅ |
| F | To‘liq vitest, a11y + responsive (Playwright + axe), performance o‘lchovi, apostrof normallashtirish, hujjatlar | ✅ (backend’siz qismi) |
| F | Real backend bilan J1–J8 yo‘llari, Telegram ichida Mini App tekshiruvi, Lighthouse | ⏳ backend kerak |

Commit’lar (`main`, push qilinmagan): `acf54d5` … `1bae850` (D: 5, E: 3, F: 4, hujjatlar).

## 2. Tekshiruvlar (ushbu kompyuterda, 2026-10-19)

| Tekshiruv | Natija |
|---|---|
| `npm run typecheck` (app + e2e) | toza |
| `npm run lint` (oxlint, warnings = xato) | toza |
| `npm test` — to‘liq vitest to‘plami | **18 fayl, 112 test — hammasi o‘tdi** |
| `npm run e2e` — 9 ekran × 5 loyiha (360, 768, 1280, qorong‘i, reduced-motion) | **90/90**: axe WCAG 2.2 AA bo‘yicha jiddiy/kritik xato yo‘q, gorizontal skrol yo‘q |
| `npm run build` | muvaffaqiyatli; boshlang‘ich JS ≈ 181 kB gzip (maqsad ≤ 180) |

Playwright axe birinchi yurishda 28 ta testni yiqitdi (6 xil haqiqiy muammo) va ular tuzatildi: mobil konsolda profil
havolasining nomi yo‘q edi; pastki tab bar va qorong‘i rejimdagi bir nechta element kontrasti
(faol menyu, "Tanlash" tugmasi, info belgilar, kalendar "hozir" yorlig‘i) 4.5:1 dan past edi.

**Taxminiy mobil o‘lchov** (production build, Lighthouse’ning mobil sharoitiga o‘xshash: CPU 4× sekin,
1.6 Mbit/s, 150 ms; API mock — faqat frontend o‘lchanadi; Lighthouse emas):

| Sahifa | LCP | CLS | Yuklangan JS |
|---|---|---|---|
| Landing | 2.13 s | 0 | 177 kB |
| Katalog | 1.99 s | 0.001 | 177 kB |
| Klinika sahifasi | 2.24 s | 0 | 177 kB |
| Klinika dashboard’i | 2.81 s | 0 (avval 0.063) | 209 kB |

Maqsadlar: LCP ≤ 2.5 s, CLS ≤ 0.05. Bemor sahifalari maqsad ichida; klinika dashboard’i (kirish
talab qiladigan ish paneli) LCP bo‘yicha biroz sekin — sabab so‘rovlar zanjiri (`/me` → filiallar →
dashboard).

## 3. Halollik qoidalari (bajarildi)

- Soxta ma’lumot yo‘q: reyting, narx, bo‘sh vaqt, tushum, "tasdiqlandi" — faqat serverdan. Booking
  yuborilgach holat **Pending**; onlayn to‘lov yo‘q; bemor shifokor tanlamaydi.
- Dashboard’da API’da yo‘q ko‘rsatkichlar ("o‘rtacha kutish", "+12% kechagiga nisbatan") yo‘q.
  Har bir metrikada aniq ta’rif bor (masalan, "kutilayotgan so‘rovlar — sanadan qat’i nazar",
  "yig‘ilgan to‘lovlar — qaytarilganlar ayirilgan"). Noaniq "Tushum" so‘zi "Yig‘ilgan to‘lovlar" ga
  almashtirildi; moliya atamalari (hisoblangan, yig‘ilgan, qaytarilgan, komissiya, to‘lanadigan summa,
  qoldiq) yonida (i) ta’rif; oylik hisobotda "Taxminiy / Yakuniy" belgisi serverdagi `locked` dan.
- Grafikdagi summalar ko‘rsatilganda API satridan formatlanadi (float emas); ranglar dataviz
  validatoridan o‘tgan, jadval ko‘rinishi bor.
- Huquqlar: menyu va tugmalar `permissions` bo‘yicha; haqiqiy tekshiruv serverda (o‘zgarmagan).
- Playwright’dagi mock ma’lumotlar faqat test uchun, fiktiv ("Test Bemor", "Namuna Klinika") va
  ilovaga kirmaydi.

## 4. Tuzatilgan eski xatolar (yo‘l-yo‘lakay topilgan)

- Hisob-kitob davri o‘zbekchada Intl orqali "2026 M10" ko‘rinardi → o‘zbekcha oy nomlari.
- uz-Cyrl’da IANA vaqt mintaqasi identifikatori ham kirillga o‘girilardi ("Асиа/Ташкент") —
  Phase B’dan beri bitta test qizil edi → identifikator lotincha qoladi.
- Klinika menyusida "Sozlamalar" sarlavhasi ikki marta chiqardi → "Klinika profili" shu guruhga ko‘chdi.
- Qisqa konsol sahifalarida mobil sidebar cho‘zilib ketardi.
- `/__ui` dev galereyasining chunk’i production build’ga kirardi.
- 270 ta o‘zbekcha matnda ASCII apostrof (`o'`) → `o‘`, tutuq belgisi → `’` (C12).

## 5. Ochiq masalalar va keyingi qadamlar

1. **Real backend bilan E2E (J1–J8).** Bu kompyuterda backend ishga tushmadi: Docker Desktop o‘chiq,
   `.env` va `oqtish-api-dev` user-secrets yo‘q, lokal PostgreSQL paroli noma’lum. Ikki yo‘l:
   (a) Docker Desktop’ni yoqish → `docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait`
   (ichki baza, parol kerak emas); (b) `.env` da `OQTISH_HOSTDB_CONNECTION` berish (ISHGA_TUSHIRISH §5).
   Shundan so‘ng `web/e2e/journeys/*` yoziladi va `E2E_BACKEND=1 npm run e2e` bilan ishlatiladi.
2. Telegram Mini App’ni haqiqiy Telegram ichida tekshirish (BookingWizard yangi ko‘rinishda).
3. Boshlang‘ich JS’ni 180 kB dan pastga tushirish: ru/en kataloglarini lazy yuklash (~8 kB).
4. Klinika dashboard LCP: dashboard so‘rovini filiallar yuklanishini kutmasdan boshlash.
5. Backend/egasi qarori kerak (FRONTEND_PHASE_A §9.2): klinika rasmlari (D1), landing fotosi (D2),
   xarita (D3), narx/reyting filtrlari (D4/D5), hafta kalendari endpoint’i (D6 — hozir 7 so‘rov),
   bemor bildirishnomalari (D7), FAQ/maxfiylik matnlari (D14).

## 6. Muhit bo‘yicha eslatmalar (Windows)

`NODE_OPTIONS=--use-system-ca npm ci` (korporativ TLS), `npx playwright install chromium`,
Git Bash’da `export MSYS_NO_PATHCONV=1`, i18n skripti uchun `PYTHONUTF8=1`. Batafsil:
`docs/FRONTEND_RESUME.md` §3.

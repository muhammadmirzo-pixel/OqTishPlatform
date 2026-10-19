# OqTish frontend — Phase A (Discovery)

Sana: 2026-10-19. Holat: **yo‘nalish tasdiqlandi (§0)**, Phase B tugadi, Phase C davom etmoqda. Til: uz (lotin), uz-Cyrl (kirill, avtomatik), ru, en.

## 0. Tasdiqlangan yo‘nalish — "Moviy klinika" (egasi bergan referens, 2026-10-19)

Egasi referens maket berdi (landing, mobil katalog, klinika sahifasi, 3 qadamli booking, muvaffaqiyat
ekrani, klinika dashboard’i, xabarlar, profil, dizayn tizimi). **U §2 dagi A/B/C variantlari o‘rnini
bosadi**; §5 dagi ranglar va shriftlar quyidagilar bilan almashtiriladi (bo‘shliq, motion, komponent
arxitekturasi va boshqa bo‘limlar o‘z kuchida).

| Element | Qiymat |
|---|---|
| Asosiy rang | `#2563EB` (ko‘k) — tugmalar, havolalar, faol holat |
| Ikkinchi rang | `#10B981` (yashil-teal) — muvaffaqiyat, ikonka fonlari, grafik 2-seriya |
| Uchinchi rang | `#8B5CF6` (binafsha) — faqat dataviz va ikonka aksentlari |
| Neytral | `#6B7280` (kulrang matn), fon `#F5F8FD` (juda och ko‘k), kartochka `#FFFFFF` |
| Klinika sidebar’i | to‘q ko‘k-dengiz `#0F1E3D`, oq matn |
| Shrift | **Inter** (o‘z serverimizda, `@fontsource-variable/inter`; o‘zbek ‘ va kirill bor) |
| Shakl | Radius 12–16px kartochkalar, 10px tugma/input, yumshoq ko‘k soyalar, ikonka "plitka"lari (och fon + rangli ikonka) |
| Ikonlar | Lucide (chiziqli, 1.75 qalinlik) |
| Logo | Tish konturli belgi + "OqTish" so‘zi, shior "Sog‘lom tabassum — baxtli hayot" |
| Mobil | Pastki tab bar (Bosh sahifa, Navbatlar, Men), filtr chiplari, kartochka ro‘yxatlari |
| Qorong‘i rejim | Qo‘lda almashtirish (yorug‘ / qorong‘i / tizim) |

### 0.1 Referensdagi va backend/SRS o‘rtasidagi to‘qnashuvlar (halollik qoidasi bo‘yicha hal qilindi)

| Referensda | Muammo | Qaror |
|---|---|---|
| "Navbat muvaffaqiyatli band qilindi! Navbatingiz tasdiqlandi" | So‘rov **Pending** bo‘lib yaratiladi, klinika 30 daqiqada tasdiqlaydi (FR-B3/B4) | Xuddi shu vizual sahna, matn: "So‘rov yuborildi — klinika tasdiqlashini kuting", halqa hisoblagichi; yashil "tasdiqlandi" sahnasi faqat server `Confirmed` qaytarganda |
| To‘lov usullari (Uzcard, Payme, Click), "Onlayn to‘lov", "Xavfsiz to‘lov" | Platforma onlayn to‘lov qabul qilmaydi (SRS §1 tashqarida) | Ko‘rsatilmaydi. O‘rniga "To‘lov klinikada: naqd, karta yoki o‘tkazma" |
| Dashboard: "O‘rtacha kutish vaqti", "+12% o‘tgan kunga nisbatan" | Bunday ma’lumot va taqqoslash API’da yo‘q (D9, D10) | Ko‘rsatilmaydi. Kartochkalar: Kutilayotgan, Tasdiqlangan, Bajarilgan, Kelmadi, Yig‘ilgan to‘lovlar (huquq bo‘lsa) |
| "Jami daromad" grafigi | Atama noaniq (prompt talabi) | "Yig‘ilgan to‘lovlar" va "Bajarilgan xizmatlar" — `reports/daily` dan, faqat `finance.report.view` bilan |
| "Tajribali shifokorlar — reyting va sharhlar" | Sharh va reyting faqat klinika darajasida; bemor shifokor tanlamaydi | "Tekshirilgan klinikalar — haqiqiy sharhlar" |
| "Doimiy qo‘llab-quvvatlash 24/7" | Tasdiqlanmagan va’da | Ko‘rsatilmaydi |
| Klinika fotosuratlari kartochkalarda | API’da rasm yo‘q (D1) | Rasm joyi saqlanadi, hozircha monogramma/illyustratsiya; D1 qo‘shilganda real foto |
| Hero’dagi bemor fotosi | Litsenziyali foto yo‘q (D2) | Joy tayyor, hozircha o‘zimizning SVG illyustratsiya; egasi foto bersa almashtiriladi |
| Mobil bosh sahifadagi xarita | Xarita provayderi yo‘q (D3) | Hozircha "Xaritada ochish" havolasi; provayder tanlangach xarita |
| "Xabarlar" ekrani (bildirishnomalar markazi) | Bemor bildirishnomalari API’da yo‘q (D7) | Phase C’da yashirin; D7 qo‘shilganda yoqiladi |
| Profil menyusida "To‘lovlar" | Bemor uchun to‘lovlar endpoint’i yo‘q | Ko‘rsatilmaydi |
| Booking 3 qadam (Xizmat → Sana va vaqt → Tasdiqlash) | Bizda filial va aloqa qadamlari ham bor | 3 vizual qadam: **Xizmat** (filial bitta bo‘lsa avtomatik, aks holda shu qadamda), **Sana va vaqt**, **Tasdiqlash** (aloqa + tekshirish). API oqimi o‘zgarmaydi |

Qolgan qarorlar (egasi javob bermagan, standart bo‘yicha): `/` = landing, katalog `/clinics` da;
xarita — tashqi havola; foto — joy tayyor, illyustratsiya bilan.

Manbalar: `docs/SRS.md`, `ARCHITECTURE.md`, `SECURITY.md`, `DATABASE.md`, `DECISIONS.md`, `API.md`,
`FRONTEND.md`, `TESTING.md`, `web/` kodi (React 19, TS strict, Vite 8, react-router 8).
Vizual yo‘nalishlar namunasi alohida sahifada (Artifact, 3 ta yo‘nalish yonma-yon).

---

## 1. Hozirgi frontend tanqidi

### 1.1 Kuchli tomonlari (saqlanadi)

| Soha | Holat |
|---|---|
| Funksional qamrov | Bemor, klinika paneli (16 bo‘lim), admin (10 bo‘lim), Telegram Mini App — barchasi haqiqiy API’ga ulangan, mock yo‘q |
| Arxitektura | Feature papkalar, typed API client (`ApiError`, `traceId`), idempotency kaliti, lazy chunk’lar (boshlang‘ich ~166 kB gzip) |
| Xavfsizlik | Cookie-only seans, CSRF sarlavhasi, permission bo‘yicha menyu, `next` faqat nisbiy, CSP (`style-src 'self'`) bilan mos |
| Biznes qoidalar | Pul — satr/BigInt, vaqt — filial IANA zonasi, hisoblagich faqat ma’lumot uchun, server holatini hech qachon taxmin qilmaydi |
| i18n | uz/ru/en, kalitlar tip darajasida majburiy, API xato kodlari test bilan solishtiriladi |
| Testlar | Vitest + Testing Library, asosiy oqimlar (booking, 409, triage, to‘lov, guard’lar, Mini App) |

**Xulosa: qayta yozish kerak emas.** Mantiq, API qatlami, guard’lar va testlar saqlanadi; vizual qatlam,
komponent tizimi va UX tafsilotlari qayta quriladi.

### 1.2 Zaif tomonlari (Phase B–F da tuzatiladi)

| # | Muammo | Ta’siri |
|---|---|---|
| C1 | Vizual identitet yo‘q: `system-ui` shrifti, bitta 333 qatorli `index.css`, umumiy teal + kulrang | Mahsulot "admin shablon" kabi ko‘rinadi, ishonch hissi past |
| C2 | Bosh sahifa = sarlavha + qidiruv qatori + filtrlar. Hero, "qanday ishlaydi", ommabop xizmatlar, ishonch, FAQ, footer yo‘q | Birinchi taassurot zaif, yangi foydalanuvchi platforma nima ekanini tushunmaydi |
| C3 | Ikonka tizimi yo‘q; yulduzchalar `★` belgisi bilan | Holatlar faqat rang + matn; skanerlash sekin |
| C4 | Motion yo‘q (faqat spinner). Holat o‘zgarishi, dialog ochilishi, qadam almashishi "sakraydi" | Tizim "javob bermayotgandek" tuyuladi |
| C5 | Skeleton yo‘q — hamma joyda "Yuklanmoqda…" spinner | Layout shift, sekin tuyuladi |
| C6 | Booking ustasida doimiy xulosa (summary) paneli yo‘q; Pending holati oddiy badge + matn | Eng muhim oqim ishonch uyg‘otmaydi |
| C7 | Jadvallar mobil’da faqat gorizontal scroll | 360 px da qabulxona/moliya ekranlari noqulay |
| C8 | Filtrlar mobil’da sahifa ichida ochiq turadi (drawer/sheet yo‘q), faol filtr chiplari va "tozalash" yo‘q | Qidiruv natijalari pastga tushib ketadi |
| C9 | Kalendar faqat kun ko‘rinishi, ish vaqti foni va "hozir" chizig‘i yo‘q, ustma-ust bloklar alohida ko‘rsatilmaydi | Qabulxona uchun asosiy ish quroli kuchsiz |
| C10 | Toast/saqlandi feedback’i yo‘q; muvaffaqiyat faqat sahifa ichidagi alert | Amal bajarilganini sezish qiyin |
| C11 | Faol klinika/filial konteksti faqat topbar’dagi `select`; filial almashganda vizual signal yo‘q | Noto‘g‘ri filialda ishlash xavfi |
| C12 | O‘zbek apostrofi aralash: 392 ta `o'`, 9 ta `o‘` | Matn sifati notekis |
| C13 | Brauzerda vizual tekshirilmagan, Playwright/axe yo‘q | Responsive va a11y faqat nazariy |
| C14 | Dashboard faqat 5 ta raqam; ta’riflar (tooltip) yo‘q | "Tushum" kabi atamalar noaniq qolishi mumkin |

---

## 2. Uchta vizual yo‘nalish

Har biri bitta tamoyil atrofida qurilgan. Barchasi bir xil token tuzilmasiga tushadi, faqat qiymatlar farq qiladi.

### A. "Emal" — chinni oqlik va tahririy tipografiya

- **G‘oya.** "Oq tish" — chinni/emal sirt. Iliq-sovuq oraliqdagi oq fon (yashilroq kulrang tus), chuqur
  dengiz-siyoh matn, teal asosiy aksent, ko‘k yordamchi aksent faqat ma’lumot uchun.
- **Tipografiya.** Sarlavhalar — serif (Literata, katta o‘lchamda, tahririy), matn — Onest (gumanistik
  grotesk, kirill ham bor). Raqamlar tabular.
- **Imzo elementi — "tabassum yoyi".** Logodagi yoy shakli: booking qadam indikatori, Pending 30 daqiqa
  halqasi (faqat ma’lumot), bo‘lim ajratuvchilari. Bitta joyda, takrorlanmaydi.
- **Sirtlar.** Ingichka 1px chiziqlar, kichik soyalar, radius 6/10/16. Glassmorphism yo‘q.
- **Kuchli tomoni:** ishonchli, sokin, esda qoladi; bemor va klinika uchun mos.
- **Zaif tomoni:** serif sarlavha zich jadval ekranlarida og‘ir → workspace’da faqat sahifa sarlavhasida.

### B. "Asbob" — aniq tibbiy asbob

- **G‘oya.** Grafit va sovuq oq, elektr-teal aksent, ko‘rinadigan setka, mono raqamlar. Dark-first.
- **Tipografiya.** IBM Plex Sans + Plex Mono.
- **Kuchli tomoni:** workspace uchun juda samarali, zich, texnologik.
- **Zaif tomoni:** bemor uchun sovuq va "texnik"; keksa foydalanuvchiga begona. Ko‘plab SaaS panellariga o‘xshab qoladi.

### C. "Tabassum" — iliq va do‘stona

- **G‘oya.** Yalpiz-yashil + marjon (coral), katta radiuslar (20–24px), yumaloq shrift (Rubik), illyustratsiyalar.
- **Kuchli tomoni:** qo‘rquvni kamaytiradi, bolalar stomatologiyasiga mos.
- **Zaif tomoni:** professional ishonch pasayadi, moliya/audit ekranlarida "o‘yinchoq"dek; marjon rangi xato/ogohlantirish bilan aralashadi.

### Tavsiya: **A "Emal"** + workspace uchun B’dan zichlik qoidalari

Bitta tizim, ikki zichlik: **Marketplace = "comfortable"** (keng bo‘shliq, serif sarlavhalar, illyustratsiya),
**Workspace = "compact"** (36px qatorlar, sans sarlavhalar, tabular raqamlar, klaviatura yorliqlari).
Sabablar: (1) "OqTish" nomi bilan bevosita bog‘langan — logosiz ham taniladi; (2) ishonch va sokinlik
tibbiyot uchun eng muhim; (3) C’ning iliqligi illyustratsiya va matn ohangi orqali beriladi, rang orqali emas;
(4) yangi bog‘liqliklar minimal.

---

## 3. Ekranlar inventari (rol bo‘yicha)

Belgilar: **M** — mavjud, qayta dizayn; **Y** — yangi; **B** — backend kerak (§9).

### 3.1 Mehmon / bemor (web)

| Ekran | Marshrut | Holat |
|---|---|---|
| Landing: hero + qidiruv, qanday ishlaydi, yo‘nalishlar, tavsiya klinikalar, tasdiqlash tushuntirishi, FAQ, footer | `/` | Y (hozir `/` = katalog) |
| Klinikalar katalogi: qidiruv, filtr drawer, faol chiplar, saralash, sahifalash, bo‘sh holat | `/clinics` | M (`/` dan ko‘chadi, eski URL query’lari redirect) |
| Klinika profili: identitet, "Tekshirilgan" belgisi, filial tablari, ish vaqti (hozir ochiq/yopiq), xizmatlar va narxlar, sharhlar, xaritada ochish havolasi | `/clinics/:id` | M |
| Booking ustasi: filial → xizmat → sana/vaqt → aloqa → tekshirish; sticky summary | `/book/:id` | M |
| Booking natijasi: Pending (halqa + deadline), keyin real holat | `/me/appointments/:id` | M |
| Kirish: telefon OTP, Telegram, xodim email+parol+TOTP, parol tiklash | `/login` | M |
| Qabullarim: kelgusi / tarix tablari, holat filtri | `/me/appointments` | M |
| Qabul tafsiloti: holat sahnasi, tarix (timeline), bekor qilish, sharh | `/me/appointments/:id` | M |
| Profil, telefon/Telegram bog‘lash, hisobni o‘chirish | `/me` | M |
| Xavfsizlik: TOTP, parol, seanslar | `/me/security` | M |
| Takliflar | `/me/invitations` | M |
| Bildirishnoma sozlamalari | `/me/notifications` | B (endpoint yo‘q; hozircha faqat Telegram bog‘lanish holati) |
| Statik: Maxfiylik, Foydalanish shartlari, Klinikalar uchun | `/privacy`, `/terms`, `/for-clinics` | Y (matnni egasi tasdiqlaydi) |
| 404, xato chegarasi (error boundary), offline | — | M/Y |

### 3.2 Klinika xodimlari (`/clinic/:clinicId/…`, permission bo‘yicha)

| Ekran | Asosiy rol | Holat |
|---|---|---|
| Klinika tanlash / ro‘yxatdan o‘tkazish | har kim | M |
| Dashboard: bugun (pending, confirmed, completed, no-show), yig‘ilgan to‘lov (huquq bo‘lsa), ta’riflar | hammasi | M |
| Pending inbox: muddat bo‘yicha saralangan, j/k navigatsiya, shifokor tanlash, tasdiqlash/rad | Receptionist, Admin | M |
| Kalendar: kun (shifokor ustunlari), hafta (B yoki 7 so‘rov), ro‘yxat; ish vaqti foni, "hozir" chizig‘i | Receptionist, Doctor | M + Y |
| Qabullar ro‘yxati + tafsilot (yakunlash, kelmadi, ko‘chirish, bekor, to‘lovlar) | Receptionist | M |
| Walk-in | Receptionist | M |
| Shifokorlar, shifokor tafsiloti (jadval, dam olish) | Admin, Doctor (o‘zi) | M |
| Xizmatlar va filial narxlari | Admin | M |
| Filiallar va ish vaqti | Admin, Owner | M |
| Xodimlar va takliflar | Owner, Admin | M |
| To‘lovlar | Receptionist, Finance | M |
| Hisobotlar (kunlik/oylik) | Finance, Owner | M |
| Hisob-kitoblar (settlements) | Owner, Finance | M |
| Audit | Owner | M |
| Klinika profili | Owner | M |

### 3.3 Platforma admini (`/admin/…`)

Dashboard, Klinikalar (tekshirish/to‘xtatish), Foydalanuvchilar, Shikoyatlar, Sharhlar, Komissiya,
Hisob-kitoblar, Break-glass ma’lumot, Audit, Bildirishnomalar — barchasi **M**. Admin interfeysi
vizual jihatdan alohida: neytral grafit sidebar + "Platforma" belgisi, xavfli amallar uchun qizil chegarali
tasdiqlash dialogi va sabab maydoni.

### 3.4 Telegram Mini App (`/tg/…`)

Bosh/qidiruv, booking, qabullarim, xodim inbox — **M**. Telegram `themeParams` tokenlarni bosib o‘tadi;
"Emal" faqat tipografiya, radius va motion orqali seziladi.

---

## 4. Foydalanuvchi yo‘llari (journey map)

```
MEHMON                     BEMOR                          KLINIKA                     PLATFORMA
Landing ─► Katalog ─► Profil ─► Booking ─► [Kirish OTP] ─► Pending ─────► Inbox ─► Tasdiq/Rad
   │          │ filtr           filial→xizmat→vaqt          │ halqa (info)  │ shifokor   │
   │          └ bo‘sh holat     409 → boshqa vaqt            ▼               ▼            │
   │                                                    Confirmed ◄── Calendar ◄── Walk-in
   │                                                        │                            │
   │                                                    Completed ─► To‘lov ─► Hisobot ─► Settlement (admin)
   └ Klinikalar uchun ─► Ro‘yxatdan o‘tish ─► Pending verification ─────────────────────► Admin tasdiqlaydi
```

| # | Yo‘l | Muvaffaqiyat mezoni | Xato/tiklash |
|---|---|---|---|
| J1 | Mehmon xizmat bo‘yicha klinika topadi | ≤ 3 harakatda natija; filtrlar URL’da | Natija yo‘q → filtrlarni yumshatish taklifi |
| J2 | Bemor so‘rov yuboradi | Bitta bosish = bitta so‘rov (idempotency); kirishdan keyin aynan shu qadamga qaytadi | 409 → "Bu vaqt band bo‘ldi", tanlov saqlanadi, yangi vaqtlar yuklanadi |
| J3 | Bemor real holatni ko‘radi | Pending/Confirmed/Rejected/Expired — faqat server javobi bo‘yicha; polling | Expired → "Boshqa vaqt tanlash" bilan qayta booking |
| J4 | Qabulxona filial almashtiradi | Eski filial ma’lumoti bir kadr ham ko‘rinmaydi (so‘rov kaliti filialga bog‘liq, skeleton) | 403 → filial tanlagichga qaytish |
| J5 | Klinika tasdiqlaydi va shifokor biriktiradi | Inbox’dan 2 bosishda | `booking.expired`, `doctor_busy` → ro‘yxat yangilanadi, sabab ko‘rsatiladi |
| J6 | Xodim to‘lov kiritadi | Summa satr, tur ongli tanlanadi, idempotent | 4xx → forma qiymatlari saqlanadi |
| J7 | Huquqsiz foydalanuvchi | Menyu va tugma yo‘q; URL orqali → 403 holati | — |
| J8 | Tarmoq/5xx xatosi | Aniq matn + "Qayta urinish" + `traceId` | Offline banner |

---

## 5. Dizayn tokenlari (taklif, "Emal")

Joylashuv: `web/src/styles/tokens.css` (`@layer tokens`). Faqat CSS o‘zgaruvchilari — Telegram tema
ularni runtime’da bosib o‘tadi (hozirgi mexanizm saqlanadi).

### 5.1 Ranglar

| Token | Yorug‘ | Qorong‘i | Vazifa |
|---|---|---|---|
| `--c-enamel` (bg) | `#F6F7F4` | `#0E1417` | Sahifa foni |
| `--c-surface` | `#FFFFFF` | `#151C20` | Kartochka/panel |
| `--c-surface-2` | `#EEF1EE` | `#1C252A` | Ichki fon, jadval sarlavhasi |
| `--c-ink` | `#0F2533` | `#E6EDEF` | Asosiy matn |
| `--c-ink-2` | `#4A5D68` | `#9DB0B8` | Ikkilamchi matn (AA ≥ 4.5:1) |
| `--c-line` | `#DCE2DF` | `#2A363C` | Chiziqlar |
| `--c-primary` | `#0B6E6E` | `#4CC3BE` | Asosiy aksent (teal) |
| `--c-primary-ink` | `#FFFFFF` | `#062524` | Aksent ustidagi matn |
| `--c-primary-soft` | `#E1F1EF` | `#123634` | Tanlangan fon |
| `--c-blue` | `#2B5FD9` | `#8AAEFF` | Ma’lumot, havola |
| `--c-success` / `-soft` | `#1D7A46` / `#E3F3E9` | `#6BD196` / `#15301F` | |
| `--c-warning` / `-soft` | `#8A5A00` / `#FCF1D6` | `#F0C35A` / `#352A10` | Pending |
| `--c-danger` / `-soft` | `#B3261E` / `#FCE9E7` | `#FF8B7E` / `#3A1C19` | |
| `--c-disabled` | `#A9B4B0` | `#4A575C` | |
| `--c-focus` | `#2B5FD9` | `#8AAEFF` | Fokus halqasi |
| `--c-data-1…6` | teal, ko‘k, olxo‘ri, oxra, yashil, kulrang | | Grafiklar (dataviz validator bilan tekshiriladi) |

**Holatlar** (rang + ikonka + matn, hech qachon faqat rang):

| Holat | Ton | Ikonka (Lucide) |
|---|---|---|
| Pending | warning | `hourglass` |
| Confirmed | primary | `calendar-check` |
| Completed | success | `check-circle-2` |
| Rejected | neutral | `x-circle` |
| CancelledByPatient / ByClinic | neutral | `calendar-x` |
| Expired | neutral | `timer-off` |
| NoShow | danger | `user-x` |

### 5.2 Tipografiya

- `--font-display: 'Literata Variable', Georgia, serif` — faqat marketplace H1/H2 va katta raqamlar.
- `--font-sans: 'Onest Variable', system-ui, 'Segoe UI', Roboto, sans-serif` — hamma qolgan joy.
- Shriftlar `@fontsource-variable/*` npm paketlaridan **o‘z serverimizda** (CSP `default-src 'self'`; Google Fonts CDN yo‘q).
- Shkala (rem): 0.75 · 0.8125 · 0.875 · 1 · 1.125 · 1.25 · 1.5 · 2 · 2.75 · 3.5 (oxirgi ikkitasi faqat marketplace hero).
- Og‘irliklar: 400, 500, 600, 700. Qator balandligi: matn 1.55, sarlavha 1.15, jadval 1.35.
- **Apostrof standarti:** `o‘`, `g‘` uchun U+2018 (‘), tutuq belgisi uchun U+2019 (’): "Oʻ" (U+02BB) shrift qo‘llabquvvati Phase B’da tekshiriladi; agar Literata/Onest’da glif bo‘lsa — U+02BB’ga o‘tish mumkin. Barcha i18n satrlari bitta skript bilan normallashtiriladi.

### 5.3 Bo‘shliq, radius, soya, qatlam

- Spacing (4px asos): `--s-1`…`--s-12` = 4, 8, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96.
- Radius: `--r-sm 6px` (input, chip), `--r-md 10px` (kartochka, tugma), `--r-lg 16px` (dialog, hero panel), `--r-pill 999px` (badge).
- Soya: `--e-1` (1px chiziq + 1px soya), `--e-2` (popover), `--e-3` (dialog). Qorong‘ida soya o‘rniga yorqinroq chiziq.
- Z: `--z-sticky 10`, `--z-drawer 40`, `--z-dialog 50`, `--z-toast 60`.
- Kenglik: `--w-text 68ch`, `--w-page 1200px`, `--w-wide 1440px`.
- Breakpoint: 360 (asos), 600, 768, 1024, 1280, 1536.
- Zichlik: `[data-density=compact]` — boshqaruv balandligi 36px (aks holda 44px), jadval padding’i kichikroq.
- Fokus: `outline: 2px solid var(--c-focus); outline-offset: 2px` + qorong‘i fonda ham ko‘rinadigan halqa.

### 5.4 Motion tokenlari

| Token | Qiymat | Qo‘llanilishi |
|---|---|---|
| `--d-instant` | 80ms | Hover, bosish |
| `--d-fast` | 140ms | Tugma holati, chip, checkbox |
| `--d-base` | 220ms | Dialog, popover, toast, qadam almashishi |
| `--d-slow` | 360ms | Holat sahnasi, sahifa kirishi |
| `--ease-out` | `cubic-bezier(.2,.8,.2,1)` | Kirish |
| `--ease-in` | `cubic-bezier(.4,0,1,1)` | Chiqish |
| `--ease-inout` | `cubic-bezier(.65,0,.35,1)` | Joy almashish |
| `--stagger` | 40ms (max 6 element) | Kartochka guruhlari |

---

## 6. Komponent arxitekturasi

```
web/src/
  styles/        tokens.css, base.css, utilities.css   (@layer tokens, base, components, features)
  ui/            dizayn tizimi — biznes mantiqsiz, faqat props
    primitives/  Button, IconButton, Input, SearchInput, Select, Checkbox, RadioGroup, Textarea, FormField
    overlay/     Dialog, ConfirmDialog, Drawer (sheet), Popover, Tooltip, Toast(+useToast)
    feedback/    Alert, EmptyState, ErrorState, Skeleton, Spinner, StatusBadge, Badge
    nav/         Tabs, Breadcrumbs, Pagination, Steps (tabassum yoyi)
    data/        DataTable (desktop jadval ↔ mobil kartochka), MetricCard, ChartContainer, Details
    time/        DatePicker (sana tasmasi + oy), TimeSlotPicker, Calendar (kun/hafta grid)
    domain/      ClinicCard, BranchCard, ServiceCard, AppointmentCard, PriceDisplay, Avatar, FilterBar, CountdownRing
    icons.ts     Lucide’dan faqat ishlatiladiganlar qayta eksport
  features/      mavjud tuzilma saqlanadi; sahifalar faqat ui/* dan yig‘iladi
```

Qoidalar:
- Har bir komponent `loading`, `disabled`, `invalid`, fokus va mobil holatini qo‘llab-quvvatlaydi.
- `ui/` API, i18n yoki routing’ni import qilmaydi (matnlar props orqali) — test va qayta foydalanish oson.
- Biznes qoidalar (`lib/money.ts`, `lib/time.ts`, permissions) o‘zgarishsiz qoladi; komponentlar ularni chaqirmaydi, sahifalar chaqiradi.
- CSS: global klasslar o‘rniga komponentga yaqin `.css` fayllar (`Button.css`), BEM-nomlash saqlanadi. Tailwind qo‘shilmaydi (CSP va mavjud konvensiya).
- Dev-only `/__ui` galereyasi (production build’ga kirmaydi) — Storybook o‘rniga.

### 6.1 Vositalar bo‘yicha qaror

| Vosita | Qaror | Sabab |
|---|---|---|
| React + TypeScript | ✅ bor | — |
| Plain CSS + CSS layers | ✅ | Mavjud, CSP bilan mos, yangi build qadam yo‘q |
| Tailwind / CSS Modules | ❌ | Ikkinchi stil tizimi kerak emas |
| Lucide React | ✅ qo‘shiladi | Izchil ikonlar, tree-shaking |
| Motion for React | ✅ cheklangan | Faqat `LazyMotion` + `domAnimation` (~15 kB gz), faqat chiqish animatsiyasi kerak joylarda (dialog, toast, holat sahnasi, qadamlar). Qolgani CSS |
| `@fontsource-variable` (Onest, Literata) | ✅ | Shriftlarni o‘zimizda saqlash |
| TanStack Query | ⏸ hozircha yo‘q | `useQuery` (F1) kalit bo‘yicha ishlaydi; filial almashishida eskirgan ma’lumot xavfi `useQuery` kalitini kengaytirish bilan hal qilinadi. Murakkablik oshsa Phase F’da qayta ko‘riladi |
| React Hook Form + Zod | ⏸ yo‘q | ~20 ta forma ishlaydi va testlangan; ko‘chirish katta xavf, kam foyda |
| Playwright + `@axe-core/playwright` | ✅ dev | E2E va a11y real brauzerda |
| Storybook | ❌ | Mashina sekin; `/__ui` galereya yetarli |
| Xarita (Yandex/Leaflet) | ⏸ qaror kerak | CSP va API kalit; hozircha "Xaritada ochish" tashqi havolasi (§9) |

---

## 7. Animatsiya va mikrointeraksiya spetsifikatsiyasi

| Hodisa | Kirish | Chiqish | Reduced motion |
|---|---|---|---|
| Sahifa almashishi | `main` fade + 8px yuqoriga, `--d-base`, `--ease-out`; scroll tepaga, fokus H1’ga | yo‘q (darhol) | faqat fade 0ms |
| Kartochka guruhi | fade + 6px, `--stagger` 40ms, max 6 | — | yo‘q |
| Skeleton → kontent | cross-fade `--d-fast`; skeleton o‘lchami kontent bilan bir xil (CLS = 0) | — | darhol |
| Dialog | backdrop fade `--d-base`; panel scale .98→1 + fade | teskari, `--ease-in`, 160ms | fade |
| Drawer/sheet (mobil filtr) | pastdan 100%→0, `--d-base` | 180ms | fade |
| Toast | o‘ngdan/pastdan 12px + fade; 5s, hover’da to‘xtaydi | fade | fade |
| Booking qadami | panel x: 16px → 0 (oldinga), −16px (orqaga); yoy indikatori to‘ladi | — | yoy darhol |
| Slot tanlash | tanlangan slot fon `--d-fast`; summary narxi yangilanadi | — | — |
| Yuborish | tugma ichida spinner, matn "Yuborilmoqda…", qayta bosish bloklanadi | — | spinner aylanmaydi, nuqtalar |
| Holat sahnasi (Pending→Confirmed) | ikonka almashishi (scale .9→1), badge rang o‘tishi `--d-slow`; `aria-live="polite"` e’lon | — | darhol, faqat e’lon |
| Pending halqasi | halqa server `expiresAt` gacha har sekundda kamayadi (CSS conic, JS faqat sekund) | 0 da "Klinika javobini kutyapmiz" — holatni o‘zgartirmaydi | raqamli matn, halqasiz |
| Dashboard raqami | 0 dan emas, oldingi qiymatdan yangisiga 400ms tween (faqat yangilanishda) | — | darhol |
| Filial almashishi | kontekst chip qisqa "pulse" (1 marta), kontent skeleton | — | yo‘q |
| Validatsiya xatosi | maydon chegarasi rangi + xabar fade-in; silkinish **yo‘q** | — | — |
| Kalendar kun/hafta | gorizontal slayd 12px + fade | — | fade |

Qoidalar: animatsiya hech qachon tugmani bloklamaydi; doimiy dekorativ animatsiya yo‘q; navigatsiyada
scroll va fokus boshqariladi; `prefers-reduced-motion` global `--d-*` ni 0ms ga tushiradi va Motion’da
`MotionConfig reducedMotion="user"`.

---

## 8. Ijodiy detallar (backend qo‘llab-quvvatlaydiganlar)

1. **Tabassum yoyi** — booking qadamlari va Pending halqasi uchun yagona vizual metafora.
2. **Sticky booking xulosasi** — desktop’da o‘ng ustun, mobil’da pastki panel (xizmat, filial, narx `Fixed/From`, davomiylik, sana, vaqt, filial vaqt mintaqasi).
3. **"Hozir ochiq / soat 18:00 da yopiladi"** — filial `hours` + filial zonasi bo‘yicha.
4. **Narx halollik belgisi** — `From` narxlar uchun "dan boshlab: yakuniy narx ko‘rikdan keyin" tooltip.
5. **Klinika monogrammasi** — rasm yo‘qligi uchun: nomdan 2 harf, emal plitka (soxta foto emas).
6. **Qabulxona yorliqlari** — inbox’da `j/k` (keyingi/oldingi), `c` tasdiqlash, `r` rad (dialog ochadi), `?` yordam.
7. **Muddati yaqin so‘rovlar** — inbox’da 10 daqiqadan kam qolganlar yuqorida va "shoshilinch" belgisi (ikonka + matn).
8. **Moliyaviy atamalar lug‘ati** — har bir metrikada `(i)` tooltip: "Yig‘ilgan to‘lov", "Yakunlangan xizmat qiymati", "Komissiya", "Klinikaga to‘lanadigan"; "taxminiy/yakuniy" belgisi `locked`/settlement holatidan.
9. **Server vaqti farqi** — hisoblagich uchun API javobidagi `Date` sarlavhasidan soat farqi hisoblanadi (yangi endpoint kerak emas).
10. **Bo‘sh holatlar** — har birida keyingi amal: "Hali shifokor yo‘q → Shifokor qo‘shish".

---

## 9. API bog‘liqliklari xaritasi

### 9.1 Mavjud (ekran → endpoint)

`docs/FRONTEND.md` §3 dagi jadval to‘liq amal qiladi; har bir ekran uchun endpoint, huquq, idempotency
va xato kodlari u yerda. Qayta dizayn ularni o‘zgartirmaydi.

### 9.2 Yetishmayotgan yoki qaror kerak (UI ularsiz ham ishlaydi, imkoniyat cheklanadi)

| # | Ehtiyoj | Hozirgi holat | UI’dagi vaqtinchalik yechim | Kim hal qiladi |
|---|---|---|---|---|
| D1 | Klinika/filial rasmlari, logo | API va saqlash yo‘q | Monogramma plitka; rasm ko‘rsatilmaydi | Backend + egasi |
| D2 | Landing uchun foto | Litsenziyali foto yo‘q | O‘z SVG illyustratsiyalarimiz; foto keyin `public/` ga | Egasi (foto berishi kerak) |
| D3 | Xarita | Provayder, kalit, CSP yo‘q | Ro‘yxat + "Xaritada ochish" (Yandex/Google havola lat/lng bilan) | Egasi (provayder tanlash) |
| D4 | Narx oralig‘i filtri (`priceMax`) | API’da yo‘q | Faqat narx bo‘yicha saralash; filtr ko‘rsatilmaydi | Backend |
| D5 | Reyting filtri (`minRating`) | Yo‘q | Reyting bo‘yicha saralash | Backend |
| D6 | Hafta kalendari | Faqat `calendar?date` | 7 ta parallel so‘rov (kesh bilan) yoki `from/to` endpoint | Backend (tavsiya: `from&to`, ≤ 7 kun) |
| D7 | Bemor bildirishnomalar markazi va sozlamalari | Yo‘q (faqat Telegram xabarlari) | `/me` da Telegram bog‘lanish holati | Backend |
| D8 | Bemor tomonidan ko‘chirish | Yo‘q (faqat klinika) | "Bekor qilib, yangi vaqt tanlang" yo‘riqnomasi | Biznes qarori |
| D9 | Dashboard taqqoslash (kecha/o‘tgan hafta) | Yo‘q | Taqqoslash ko‘rsatilmaydi | Backend (ixtiyoriy) |
| D10 | "Kutmoqda/kechikdi" holati | Model’da yo‘q | Ko‘rsatilmaydi | Biznes qarori |
| D11 | Moliya eksporti (CSV/XLSX) | Yo‘q | Ko‘rsatilmaydi | Backend |
| D12 | Walk-in uchun bo‘sh shifokorlar | Yo‘q (FRONTEND §9.3-1) | Ro‘yxat + server 409 | Backend |
| D13 | Admin audit’da klinika nomi | Faqat id | id | Backend |
| D14 | FAQ, maxfiylik, shartlar matni | Yo‘q | Qoralama matn, "egasi tasdiqlashi kerak" belgisi bilan | Egasi + yurist |

---

## 10. Accessibility va responsive testlash rejasi

**Maqsad: WCAG 2.2 AA.**

| Tekshiruv | Vosita | Qachon |
|---|---|---|
| Avtomatik a11y (har bir asosiy sahifa, yorug‘/qorong‘i) | `@axe-core/playwright` | Phase F, CI |
| Kontrast tokenlari (matn ≥ 4.5:1, UI ≥ 3:1) | token jadvali + skript | Phase B |
| Klaviatura: barcha oqimlar sichqonchasiz (booking, triage, to‘lov) | Playwright `keyboard` | Phase C–D |
| Dialog fokus tuzog‘i va qaytishi | Vitest + Playwright | Phase B |
| `aria-live` holat e’lonlari (booking natijasi, toast, holat o‘zgarishi) | Testing Library | Phase C |
| Reduced motion | Playwright `reducedMotion: 'reduce'` + skrinshot | Phase F |
| 200% zoom va 320px reflow | Playwright | Phase F |
| Fokus ko‘rinishi (2.4.11) va maqsad o‘lchami ≥ 24px (2.5.8; bizda 36/44px) | qo‘lda + CSS | Phase B |

**Responsive matritsa:** 360×740, 412×915, 768×1024, 1280×800, 1536×960. Har birida: gorizontal scroll yo‘q,
asosiy amal ko‘rinadi, jadval → kartochka (≤ 768), filtr → sheet (≤ 1024). Playwright skrinshot to‘plami
(`e2e/visual/*`), yorug‘ va qorong‘i.

**Performance maqsadlari** (Lighthouse mobile, `vite preview`): LCP ≤ 2.5s, CLS ≤ 0.05, INP ≤ 200ms;
boshlang‘ich JS ≤ 180 kB gz (hozir ~166). Motion va ikonlar faqat kerakli chunk’larda. Har faza oxirida o‘lchanadi.

---

## 11. Bosqichma-bosqich reja va qabul mezonlari

Har faza oxirida: o‘zgargan fayllar, ekranlar, ishlatilgan buyruqlar, `npm run typecheck`/`lint`/`build`
natijasi, yetishmayotgan API’lar, hujjat yangilanishi. Testlar (memory qoidasi bo‘yicha) faza ichida
yoziladi, to‘liq to‘plam oxirida bir marta ishga tushiriladi.

| Faza | Mazmun | Qabul mezonlari |
|---|---|---|
| **B. Dizayn tizimi** | Tokenlar, shriftlar, `styles/` qatlamlari, §6 dagi barcha `ui/` komponentlar, Toast, Drawer, Skeleton, Steps, CountdownRing, DataTable, `/__ui` galereya, apostrof normallashtirish, Lucide + Motion (lazy) | Galereyada har komponent barcha holatlarda; kontrast skripti o‘tadi; mavjud sahifalar yangi tokenlarda buzilmasdan ochiladi; typecheck/lint/build yashil |
| **C. Bemor tajribasi** | Landing, katalog (`/clinics`), klinika profili, booking ustasi + sticky summary, holat sahnasi, qabullarim, profil | J1–J3 Playwright’da; 409/expired/offline holatlari; 360 px da gorizontal scroll yo‘q; axe 0 jiddiy xato |
| **D. Klinika paneli** | Shell (kontekst chip, filial almashish), dashboard + ta’riflar, inbox + yorliqlar, kalendar (kun + hafta), qabul tafsiloti, to‘lovlar, hisobotlar, sozlash ekranlari | J4–J7; filial almashishida eski ma’lumot ko‘rinmaydi (test); moliya ekranlari huquqsiz foydalanuvchida yo‘q |
| **E. Admin** | Alohida shell, klinikalar tekshiruvi, xavfli amal dialoglari, audit, break-glass | Har xavfli amal sabab + tasdiq; platform permission bo‘yicha menyu |
| **F. Integratsiya va sayqal** | Real backend bilan E2E (compose), Mini App sayqali, performance o‘lchovi, reduced motion, to‘liq test to‘plami, `FRONTEND.md` yangilash | §16 dagi 9 ta kritik yo‘l real backend’da yashil; Core Web Vitals o‘lchangan va hisobotda |

### Tasdiqlash uchun savollar (egasiga)

1. Vizual yo‘nalish: **A "Emal"** (tavsiya) / B / C?
2. Xarita provayderi: hozircha tashqi havola yetarlimi, yoki Yandex Maps API (kalit + CSP) kerakmi?
3. Landing uchun haqiqiy foto bormi (klinika ichi, shifokorlar)? Yo‘q bo‘lsa — illyustratsiya.
4. Bosh sahifa: `/` = landing, katalog `/clinics` ga ko‘chadimi (tavsiya), yoki `/` katalog bo‘lib qoladimi?

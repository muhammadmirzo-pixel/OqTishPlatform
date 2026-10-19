# Frontend qayta dizayni — davom ettirish yo‘riqnomasi (Claude uchun)

Oxirgi yangilanish: 2026-10-19. Phase D tugadi (`acf54d5` … `c27f61f`, 5 commit). Keyingi: **Phase E**.
`global.json` dagi lokal o‘zgarish (SDK 11 RC) egasiniki — commit qilinmaydi.
Bu fayl yangi sessiya (boshqa akkaunt ham bo‘lishi mumkin) ishni aynan shu joydan davom ettirishi uchun.

## 0. Avval o‘qing

1. `docs/FRONTEND_PHASE_A.md` — **§0 (tasdiqlangan "Moviy klinika" yo‘nalishi va referens bilan to‘qnashuvlar jadvali)**, §6 komponentlar, §7 motion, §9.2 yetishmayotgan API’lar (D1–D14), §11 fazalar.
2. `docs/WORKLOG.md` oxirgi qatorlari.
3. `docs/FRONTEND.md` — marshrutlar, API, huquqlar (eski, lekin mantiq o‘zgarmagan).
4. `git log --oneline -8`.

## 1. Vazifa (egasining asl topshirig‘i, qisqacha)

OqTish (stomatologiya klinikalariga onlayn yozilish platformasi) uchun premium, ishlaydigan, haqiqiy API’ga ulangan,
responsive, a11y’ga mos, testlanadigan frontend. Fazalar: A discovery ✅ → B dizayn tizimi ✅ → C bemor qismi ✅ →
**D klinika paneli ✅ →
**E platforma admini (hozir shu)** → F integratsiya, sayqal, testlar.
Egasi referens maket bergan: ko‘k `#2563EB`, yashil-teal `#10B981`, binafsha `#8B5CF6`, Inter shrifti, oq kartochkalar,
to‘q ko‘k (navy) sidebar, mobil’da pastki tab bar. Egasi o‘zbekcha **kirill** tilini ham qo‘shishni so‘radi (qilingan).

## 2. Qat’iy qoidalar (buzmang)

- **Soxta ma’lumot yo‘q**: reyting, narx, bo‘sh vaqt, daromad, "tasdiqlandi" — faqat serverdan. Booking yuborilgach holat
  **Pending** (klinika 30 daqiqada tasdiqlaydi). Onlayn to‘lov yo‘q (to‘lov klinikada). Bemor shifokor tanlamaydi.
  Dashboard’da "o‘rtacha kutish vaqti", "+12% kechagiga nisbatan" kabi API’da yo‘q ko‘rsatkichlar ko‘rsatilmaydi.
  To‘liq jadval: FRONTEND_PHASE_A.md §0.1.
- Pul: `{ amount: string }`, hech qachon float emas (`lib/money.ts` → `compareAmounts`, `sumMoney`). Vaqt: filial IANA zonasi (`lib/time.ts`).
- Huquqlar: menyu/tugmalar `membership.permissions` bo‘yicha (`features/clinic/permissions.ts`), haqiqiy tekshiruv serverda.
- CSP: `style-src 'self'`, tashqi CDN yo‘q → shriftlar `@fontsource-variable/inter` orqali, ikonlar `lucide-react`.
  React `style={{}}` mumkin (CSSOM).
- Matnlar faqat i18n orqali: `src/i18n/sections/*.ts` (uz → ru, en). **uz-Cyrl avtomatik** (`i18n/translit.ts`,
  istisnolar `i18n/uzCyrlOverrides.ts`). Yangi kalit qo‘shish: `python3 web/scripts/add-i18n.py <section.ts> <keys.json>`
  (json: `{"uz":{...},"ru":{...},"en":{...}}`). Hozir yangi kalitlar `sections/ui.ts` va `sections/landing.ts` da.
- Testlar mavjud matnlarni qidiradi (en locale): status nomlari, "Waiting for the clinic: 24:59", "Dr Test", narx bir marta
  ko‘rinishi va h.k. Matnni bir sahifada ikki marta alohida element sifatida chiqarmang (getByText yiqiladi).
- Memory qoidalari: ish davomida faqat `npm run typecheck` + `npm run lint` (va zarur bo‘lsa bitta maqsadli vitest fayl);
  to‘liq test to‘plami faqat oxirida (Phase F). Token tejash: subagentlar kam, qisqa javoblar. Javoblar o‘zbekcha.
- Har faza qismi tugagach — commit (main, push qilinmaydi), `docs/WORKLOG.md` ga bir qator.

## 3. Muhit

```bash
export PATH=$HOME/.local/opt/node/bin:$PATH     # node 24
cd web && npm run typecheck && npm run lint
npx vite --port 5199 --strictPort &              # dev server (backend :5080 ishlab turgan bo‘lsa /api proxy ishlaydi)
node scripts/screenshot.mjs http://localhost:5199/clinics /tmp/.../x.png 390 844 [light|dark]   # Playwright skrinshot
```
Dev-only komponent galereyasi: `http://localhost:5199/__ui`. Playwright chromium o‘rnatilgan (`@playwright/test` 1.63.0).

**Windows mashinasi (2-sessiya) bo‘yicha eslatmalar:**
- Korporativ TLS: `npm ci` faqat `NODE_OPTIONS=--use-system-ca npm ci` bilan ishlaydi (tekshiruvni o‘chirmang).
- Playwright 1.63 `chromium_headless_shell-1243` ni kutadi; o‘rnatilgani `%LOCALAPPDATA%/ms-playwright/chromium-1228/chrome-win64/chrome.exe`
  → `chromium.launch({ executablePath })` bilan ishlatiladi (yoki `npx playwright install chromium`).
- Git Bash `/clinic/c1` kabi argumentlarni Windows yo‘liga aylantiradi → `export MSYS_NO_PATHCONV=1`.
- `add-i18n.py` ni `PYTHONUTF8=1 python …` bilan ishga tushiring (aks holda cp1251).
- Fayllar diskda CRLF/LF aralash (`core.autocrlf=true`); qidirib-almashtiruvchi skriptlar ikkalasini ham hisobga olsin.
  Uzun bash heredoc’lar ba’zan kesiladi — katta patchlarni faylga yozib ishga tushiring.
- Login’siz konsol skrinshotlari: Playwright’da `page.route('http://localhost:5199/api/**')` bilan **soxta (faqat QA)** javoblar
  berib sahifani chizish mumkin (Phase D shunday tekshirildi; skript repoga kiritilmagan, ilovaga hech qachon kirmaydi).
Backend ishga tushirish: `docs/ISHGA_TUSHIRISH.md`. Klinika/admin sahifalarini skrinshot qilish uchun login kerak
(dev OTP — `/api/dev/*`, ISHGA_TUSHIRISH’da yozilgan).

## 4. Nima qilingan (fayllar)

- `src/styles/`: `tokens.css` (rang/tip/bo‘shliq/motion tokenlari, yorug‘/qorong‘i), `base.css`, `components.css`
  (btn, forma, badge, card, table `.table--stack` mobil kartochka, dialog/drawer/toast/tooltip, stat, steps, ring, monogram),
  `layouts.css` (sayt header/footer/tabbar, konsol navy sidebar, katalog, klinika sahifasi, wizard, scene, account), `landing.css`.
- `src/ui/`: Button(+buttonClass), Dialog(+useModal focus trap), Drawer, Toast(+toastContext `useToast`), Skeleton(+SkeletonCards),
  Tooltip(`InfoTip`), CountdownRing, Monogram, Logo/LogoMark, ThemeSwitcher(+theme.ts), widgets (StatusBadge ikonka bilan,
  StatusIcon, StatCard `icon/iconTone/hint/info`, Stars), states (ikonkali EmptyState/Error/Forbidden).
- `src/app/`: PublicLayout (header, footer, mobil tabbar), ConsoleShell (navy sidebar, bo‘limlar, ikonlar — `menuMeta.ts`,
  `variant="admin"`), routes (`/` landing, `/clinics` katalog, dev `/__ui`).
- `src/features/public/`: LandingPage, CatalogPage, ClinicCardView, ClinicPage (filial tanlagich `?branch=`), categoryIcons.
- `src/features/booking/BookingWizard.tsx`: 3 vizual bosqich + BookingSummary (mantiq/URL o‘zgarmagan).
- `src/features/patient/`: StatusScene (Pending halqasi, keyingi amal), AppointmentDetailPage (Pending’da 15 s polling),
  MyAppointmentsPage (chip filtr, DateBlock).
- `src/features/account/AccountLayout.tsx`: profil kartochkasi + menyu.
- `src/lib/format.ts`: o‘zbekcha oy/hafta nomlari (Intl "M10" xatosi tuzatilgan), `so‘m`, `formatCompactNumber`
  (grafik o‘qi: ming/mln), `formatMonthYear`.
- **Phase D:** `features/clinic/DashboardPage.tsx` (ikonkali metrikalar + InfoTip, bugungi qabullar, tezkor amallar,
  14 kunlik moliya grafigi), `ui/LineChart.tsx` (kutubxonasiz SVG, crosshair, ← →, jadval ko‘rinishi; ranglar
  `--c-data-1/2`, dataviz validatoridan o‘tgan, qorong‘ida `#5b8cff/#0d9f6e`), `RequestsPage.tsx` (shoshilinch < 10 daq,
  Toast, J/K/Enter faqat ro‘yxat fokusda), `CalendarPage.tsx` + `calendarLayout.ts(+test)` (Kun | Hafta (bitta shifokor,
  7 so‘rov) | Ro‘yxat, ish vaqti foni, "hozir" chizig‘i, yo‘laklar, legenda), `ui/ContextChip.tsx` (klinika/filial chip,
  pulse), `ConsoleShell` (mobil’da til/mavzu menyu ichida), `features/finance/FinanceTerm.tsx` (atama + ta’rif,
  `PeriodState` taxminiy/yakuniy), `PageHeader icon`, `.entity` kartochkalar, `.term`, `.nowrap`, `.hide-mobile`.
  Saqlash natijalari Toast’da; xato/eskirgan holat va muhim yo‘riqnomalar sahifada qoladi.
  J4 testi: `test/flows/clinic.test.tsx` → "branch switch (J4)".

## 5. KEYINGI QADAM — Phase E (platforma admini), shu tartibda

1. `ui/ReasonDialog.tsx`: `ReasonDialog`/`ConfirmDialog` ga `danger` → `Dialog tone="danger"`, ogohlantirish ikonkasi,
   qizil tugma. Xavfli amallar: klinikani rad etish/to‘xtatish, foydalanuvchini bloklash, sharhni yashirish,
   hisob-kitobni bekor qilish (void) va yakunlash (finalize — qaytarib bo‘lmaydi), komissiya qoidasini tugatish,
   break-glass (tenant-data) — har biri sabab + aniq tasdiq. Server qoidalari o‘zgarmaydi.
2. Admin sahifalari (`features/admin/*`): PageHeader ikonkasi (`app/menuMeta.ts` dagi ikonalar), jadvallar
   `table--stack` + `data-label`, bo‘sh holat ikonkalari, natija Toast’da, AdminDashboard metrikalari ikonka bilan.
3. Audit sahifalari: filtrlar bir qatorda, `AuditTable` (allaqachon `table--stack`), bildirishnomalar holati ikonka+matn.
4. Har qadamdan keyin typecheck + lint + tegishli vitest (`src/test/flows/admin.test.tsx`), commit "FE Phase E (n/m): …".

Keyin **Phase F** (Playwright E2E `web/e2e/` — FRONTEND_PHASE_A.md §4 J1–J8 va prompt §16 dagi 9 ta yo‘l,
`@axe-core/playwright` a11y, 360/768/1280 skrinshotlar, reduced-motion, Lighthouse o‘lchovi, to‘liq `npm test` bir marta,
FRONTEND.md yangilash, yakuniy hisobot). Phase F’da yana: eski bo‘limlardagi uz matnlarida `'` → `‘` normallashtirish (C12;
`clinicOps.ts`, `finance.ts` va boshqalarda hali ASCII apostrof bor).

## 6. Ma’lum ochiq masalalar

- Klinika rasmlari/logo (D1), landing fotosi (D2), xarita provayderi (D3), narx/reyting filtrlari (D4/D5), hafta kalendari
  endpoint’i (D6 — hozir 7 ta kunlik so‘rov, bitta shifokor), bemor bildirishnomalari (D7) — backend/egasi qarori kerak; UI ularsiz halol ishlaydi.
- Mini App (`/tg`) BookingWizard’ni ishlatadi — yangi layout u yerda ham; Phase F’da Telegram ichida tekshirish.
- `me/appointments` sahifasi AccountLayout ichida emas (alohida `.page`) — ataylab.

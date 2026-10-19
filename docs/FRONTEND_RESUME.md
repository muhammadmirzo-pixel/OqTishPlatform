# Frontend qayta dizayni — davom ettirish yo‘riqnomasi (Claude uchun)

Oxirgi yangilanish: 2026-10-19. Oxirgi commit: `f54d10b` (Phase C tugadi). Ishchi daraxt toza.
Bu fayl yangi sessiya (boshqa akkaunt ham bo‘lishi mumkin) ishni aynan shu joydan davom ettirishi uchun.

## 0. Avval o‘qing

1. `docs/FRONTEND_PHASE_A.md` — **§0 (tasdiqlangan "Moviy klinika" yo‘nalishi va referens bilan to‘qnashuvlar jadvali)**, §6 komponentlar, §7 motion, §9.2 yetishmayotgan API’lar (D1–D14), §11 fazalar.
2. `docs/WORKLOG.md` oxirgi qatorlari.
3. `docs/FRONTEND.md` — marshrutlar, API, huquqlar (eski, lekin mantiq o‘zgarmagan).
4. `git log --oneline -8`.

## 1. Vazifa (egasining asl topshirig‘i, qisqacha)

OqTish (stomatologiya klinikalariga onlayn yozilish platformasi) uchun premium, ishlaydigan, haqiqiy API’ga ulangan,
responsive, a11y’ga mos, testlanadigan frontend. Fazalar: A discovery ✅ → B dizayn tizimi ✅ → C bemor qismi ✅ →
**D klinika paneli (hozir shu)** → E platforma admini → F integratsiya, sayqal, testlar.
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
- `src/lib/format.ts`: o‘zbekcha oy/hafta nomlari (Intl "M10" xatosi tuzatilgan), `so‘m`.

## 5. KEYINGI QADAM — Phase D (klinika paneli), shu tartibda

1. **DashboardPage** (`features/clinic/DashboardPage.tsx`): sarlavha "Xush kelibsiz, {ism}" + bugungi sana (filial zonasi);
   StatCard’lar ikonka bilan (Hourglass/CalendarCheck/CheckCircle2/UserX/Wallet) va har biriga `InfoTip` ta’rif
   (pending = hozir javob kutayotgan so‘rovlar, istalgan sana; confirmed/completed/noShow = bugun boshlanadiganlar;
   collected = bugun qayd etilgan to‘lovlar, faqat `finance.report.view` bo‘lsa — DTO’da `null` bo‘lsa ko‘rsatilmaydi).
   "Bugungi qabullar" jadvali: `api.clinic.appointments(clinicId, { branchId, from, to })` — `localRangeToUtc(today, today, tz)`,
   faqat `Perm.appointmentView` bo‘lsa; ustunlar vaqt/bemor/xizmat/shifokor/holat, `table--stack` + `data-label`.
   Ixtiyoriy grafik: oxirgi 14 kun `api.finance.daily` (faqat `Perm.reportView`) — "Bajarilgan xizmatlar qiymati (charged)"
   va "Yig‘ilgan to‘lovlar (collected)" ikki seriya; grafik yozishdan oldin `dataviz` skill’ini yuklang; kutubxonasiz inline SVG.
   Tezkor amallar — ikonkali tugmalar.
2. **RequestsPage**: kartochkalar muddat bo‘yicha (mavjud), 10 daqiqadan kam qolganiga "shoshilinch" belgisi (ikonka+matn),
   Toast bilan natija (`useToast`), skeleton; ixtiyoriy klaviatura: j/k fokus, Enter ochish.
3. **CalendarPage**: "hozir" chizig‘i (filial vaqti), `workingHours` fonini chizish (DTO’da bor, FRONTEND.md §9.3-8),
   kun almashtirish tugmalari (oldingi/bugun/keyingi), segmented (Kun | Ro‘yxat). Hafta ko‘rinishi — 7 ta so‘rov (D6).
4. ClinicLayout topbar: klinika/filial select’larini kontekst chip ko‘rinishiga keltirish (Store ikonka), filial almashganda
   eski ma’lumot ko‘rinmasligi (useQuery kaliti filialni o‘z ichiga oladi — tekshiring).
5. Qolgan klinika sahifalari (appointments, walk-in, doctors, services, branches, staff, payments, reports, settlements, audit,
   profile) — CSS orqali allaqachon yangi uslubda; har biriga: PageHeader ikonkasi, jadvallarga `table--stack`+`data-label`,
   bo‘sh holatlarga ikonka, saqlashda `useToast`. Moliya atamalariga `InfoTip` (charged, collected, refunded, commission, payable;
   "taxminiy/yakuniy" — `locked`/settlement holatidan).
6. Har bir qadamdan keyin typecheck+lint, kerak bo‘lsa skrinshot, commit "FE Phase D (n/m): …".

Keyin **Phase E** (admin: xavfli amallar uchun `Dialog tone="danger"` + sabab, audit jadvallari) va **Phase F**
(Playwright E2E `web/e2e/` — FRONTEND_PHASE_A.md §4 J1–J8 va prompt §16 dagi 9 ta yo‘l, `@axe-core/playwright` a11y,
360/768/1280 skrinshotlar, reduced-motion, Lighthouse o‘lchovi, to‘liq `npm test` bir marta, FRONTEND.md yangilash, yakuniy hisobot).

## 6. Ma’lum ochiq masalalar

- Klinika rasmlari/logo (D1), landing fotosi (D2), xarita provayderi (D3), narx/reyting filtrlari (D4/D5), hafta kalendari
  endpoint’i (D6), bemor bildirishnomalari (D7) — backend/egasi qarori kerak; UI ularsiz halol ishlaydi.
- Mini App (`/tg`) BookingWizard’ni ishlatadi — yangi layout u yerda ham; Phase F’da Telegram ichida tekshirish.
- `me/appointments` sahifasi AccountLayout ichida emas (alohida `.page`) — ataylab.

# Yakuniy hisobot — OqTish platformasi

Sana: 2026-10-19. Bu hujjat nima qurilgani, qanday tekshirilgani va qaysi cheklovlar qolganini
tushuntiradi. Barcha raqamlar haqiqatda ishga tushirilgan buyruqlardan olingan.

## 1. Nima qurildi

| Qism | Tarkibi |
|---|---|
| Kirish va hisoblar | SMS kod (Fake + Eskiz adapteri), email + parol, TOTP (2FA), Telegram Mini App va Login Widget orqali kirish, serverdagi sessiyalar, telefon/Telegram ulash (hech qachon birlashtirmasdan), parolni tiklash, hisobni o'chirish (anonimlashtirish) |
| Klinikalar | Ro'yxatdan o'tish (admin tasdiqlashi bilan), filiallar va ish soatlari, xizmatlar va filial narxlari, xodimlar va telefon orqali taklif, 5 ta rol va filial darajasidagi ruxsatlar |
| Shifokorlar | Filial/xizmat bog'lanishlari, haftalik jadval (ustma-ust tushmaydi), dam olish kunlari |
| Qabul tizimi | Bo'sh vaqtlarni hisoblash, yashirin shifokor bandi, 30 daqiqalik muddat, tasdiqlash/rad etish/bekor qilish/ko'chirish/yakunlash/kelmaslik, walk-in, kalendar, sharhlar va reyting, shikoyatlar |
| Moliya | To'lovlar (naqd/karta/o'tkazma, qisman), qaytarishlar, muvaffaqiyatsiz to'lovlar, ikki tomonlama daftar, 4 turdagi komissiya (shartnoma bo'yicha), oylik hisob-kitoblar, kunlik/oylik hisobotlar, tungi moslik tekshiruvi |
| Bildirishnomalar | Outbox navbati, Telegram (asosiy) va SMS (zaxira), 24 va 2 soatlik eslatmalar, qayta urinishlar, admin uchun holat jadvali |
| Telegram bot | `/start` (hisobni ulash), `/appointments`, `/stop`, `/help`; Polling va Webhook rejimlari; Mini App (bemor + xodim) |
| UI | Bemorlar sayti, klinika paneli, admin paneli, Telegram Mini App; 3 til; telefon, planshet va kompyuter uchun |
| Ekspluatatsiya | Docker Compose (ichki baza va kompyuterdagi baza rejimlari), migratsiya konteyneri, health check, zaxira nusxa/tiklash skriptlari, smoke-test skripti, GitLab CI |

## 2. Qanday tekshirildi

| Tekshiruv | Natija |
|---|---|
| Backend testlari (`dotnet test`) | 428 ta: birinchi to'liq ishga tushirishda 426 tasi o'tdi, 2 tasi tuzatildi (bildirishnoma shabloni va test poygasi). Tekshiruvdan keyingi tuzatishlardan so'ng tegishli testlar qayta ishga tushirildi: 268 + 16 + 8, hammasi o'tdi |
| Bir vaqtdagi so'rovlar | 50 martalik testlar: bitta vaqtga 20 ta parallel so'rov → faqat 1 tasi muvaffaqiyatli; tasdiqlash va muddat tugashi poygasida faqat bitta natija; takroriy to'lov ikki marta yozilmaydi. Himoya vaqtincha olib tashlanganda testlar xatoni ushlashi tekshirilgan |
| Frontend testlari | 93 ta, hammasi o'tdi; lint va typecheck toza |
| Haqiqiy ishga tushirish | `OqTish_db` bazasida: barcha migratsiyalar, seed, `deploy/smoke-test.sh` → **24/24** |
| Zaxira nusxa / tiklash | Ikkala rejimda sinovdan o'tdi; tiklangan bazada huquqlar va "faqat qo'shiladigan" cheklov saqlanadi |
| Mustaqil tekshiruv | 10 ta topilma: 6 tasi tuzatildi, 1 tasi xato signal bo'lib chiqdi, 1 tasi ataylab qoldirildi, 2 tasi ma'lum cheklov sifatida hujjatlashtirildi (`docs/DECISIONS.md` §6) |

## 3. Siz uchun qabul qilingan qarorlar

Siz menga topshirgan savollar `docs/DECISIONS.md` §4 da. Eng muhimlari:
- **Komissiya:** platforma admini har bir klinika uchun shartnoma qoidasini kiritadi (to'lovdan %,
  xizmatdan %, tasdiqlangan qabul uchun qat'iy summa, yangi bemor uchun qat'iy summa). Qoida bo'lmasa,
  komissiya **0** va hisobot buni ochiq aytadi. Men shartnoma shartlarini o'ylab topmadim.
- **Bemor bekor qilishi:** kutilayotgan so'rovni istalgan vaqtda, tasdiqlanganni boshlanishidan 2 soat oldingacha.
- **Bemorda bir vaqtda** eng ko'pi bilan 3 ta kutilayotgan so'rov bo'lishi mumkin. Qabulga eng kamida 60 daqiqa oldin, ko'pi bilan 30 kun oldin yozilish mumkin.
- **Qaytarishda** komissiya mutanosib ravishda qaytariladi (foizli turlarda).
- **Qaytarishning hisobi:** avval bemorning ortiqcha to'lovi qaytariladi, qolgani xizmat narxining kamayishi deb hisoblanadi. **Buxgalter ko'rib chiqishi tavsiya etiladi.**

## 4. Ma'lum cheklovlar (halol ro'yxat)

| Cheklov | Ta'siri | Nima qilish kerak |
|---|---|---|
| Eskiz SMS va haqiqiy Telegram API **jonli xizmatlarda sinalmagan** (hisob ma'lumotlari yo'q) | Lokal rejimda soxta qutilar ishlaydi; serverda birinchi ulanishda tekshirish kerak | Eskiz akkaunti va bot tokeni bilan sinab ko'rish |
| Telegram Mini App haqiqiy Telegram ichida ochib ko'rilmagan | Brauzerda ogohlantirish ko'rsatadi (to'g'ri) | HTTPS manzil + bot bilan sinash (`docs/SERVICES.md` §5.9) |
| UI brauzerda ko'z bilan tekshirilmagan (360/768/1280 px) | Testlar va API tekshirilgan, vizual ko'rinish emas | Siz brauzerda ko'rib chiqing |
| R5: yangi telefon/Telegram ulashda qo'shimcha tasdiq yo'q | Sessiyasi o'g'irlangan hisobga yangi kirish usuli qo'shilishi mumkin | Keyingi versiya: TOTP/qayta kirish talabi + xabar |
| R9: shifrlash kalitlari bazada qo'shimcha himoyasiz | Bazaga kira olgan odam TOTP sirlarini o'qiy oladi | Serverda sertifikat/KMS bilan himoyalash (`docs/DEPLOYMENT.md` §3.8) |
| Shifokor tanlash "ochko'z" usulda | Kamdan-kam holatda bitta shifokorgina bajara oladigan so'rov bo'sh vaqt bo'lsa ham rad etilishi mumkin | Klinika tasdiqlashda shifokorni almashtira oladi; kerak bo'lsa algoritmni yaxshilash |
| Qidiruvdagi "yaqin 7 kunda bo'sh vaqt" xotirada hisoblanadi | Juda ko'p klinikada sekinlashishi mumkin | Ko'payganda kesh yoki oldindan hisoblash |
| QQS / soliq hisob-fakturalari yo'q | Hisob-kitob summalari brutto | Buxgalter bilan kelishib qo'shish |
| Qonuniy talablar (shaxsiy ma'lumotlar, reestr, tibbiy qoidalar) | Serverga chiqarishdan oldin majburiy | Yurist xulosasi |

## 5. Keyingi qadamlar

1. Lokal sinab ko'ring: `docs/ISHGA_TUSHIRISH.md` (demo loginlar va ssenariylar).
2. Buxgalter bilan moliya qarorlarini ko'rib chiqing (`docs/FINANCIAL_RULES.md`, `docs/DECISIONS.md` §4, §5.3).
3. Eskiz akkaunti va Telegram bot yarating, keyin serverda sinab ko'ring.
4. Yurist bilan server joylashuvi va shaxsiy ma'lumotlar talablarini kelishing.
5. Serverga chiqarish: `docs/DEPLOYMENT.md`.

## 6. Ish jarayoni (qisqacha)

Ish bosqichma-bosqich, har bosqich oxirida commit bilan bajarildi (`git log`):
1. Reja va dizayn.
2. Asos.
3. Kirish tizimi.
4. Klinikalar va katalog.
5. UI.
6. Qabul tizimi.
7. Moliya.
8. Bildirishnomalar va Telegram.
9. UI'ni moslashtirish.
10. Testlar.
11. Haqiqiy ishga tushirish.
12. Mustaqil tekshiruv va tuzatishlar.
13. Hujjatlar.

Batafsil xronologiya: `docs/WORKLOG.md`.

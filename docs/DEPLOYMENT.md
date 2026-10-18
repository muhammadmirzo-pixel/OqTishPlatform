# Serverga chiqarish (Deployment) va orqaga qaytarish (Rollback)

Bu hujjat OqTish'ni haqiqiy serverda (Production) ishga tushirish, yangilash va muammo bo'lsa oldingi
versiyaga qaytarishni tushuntiradi. Lokal sinov uchun `docs/ISHGA_TUSHIRISH.md` ga qarang.

> ⚖️ **Qonun talabi:** O'zbekiston fuqarolarining shaxsiy ma'lumotlari O'zbekiston hududidagi serverlarda
> saqlanishi va operator davlat reestridan o'tishi kerak (ZRU-547). Server tanlashdan oldin yurist
> xulosasini oling (`docs/SECURITY.md` §5).

---

## 1. Talablar

| Narsa | Talab |
|---|---|
| Server | Linux (Ubuntu 24.04 tavsiya), kamida 2 vCPU / 4 GB RAM / 40 GB SSD |
| Dasturlar | Docker Engine 29+, Docker Compose v5+, git |
| Domen | masalan `oqtish.uz`, DNS A-yozuvi server IP manziliga |
| TLS | Tashqi proksi (nginx yoki Caddy) + Let's Encrypt sertifikati |
| SMS | Eskiz.uz akkaunti (email, parol, tasdiqlangan "from" nomi va xabar shablonlari) |
| Telegram | @BotFather orqali yaratilgan bot tokeni (ixtiyoriy, lekin tavsiya etiladi) |

---

## 2. Arxitektura (serverda)

```
Internet ──HTTPS──► tashqi proksi (TLS, 443)  ──HTTP──►  web (nginx, 127.0.0.1:8080)
                                                          ├── /        → React sayt
                                                          └── /api/*   → api (ASP.NET, ichki tarmoq)
                                       api, worker ──► postgres (ichki tarmoq, tashqariga ochilmaydi)
                                       migrate (bir martalik) ──► postgres
```

- **web**, **api**, **worker**, **postgres**, **migrate**: `compose.yaml` (Production asosi).
- **Real mijoz IP manzili:** tashqi proksi `X-Forwarded-For` qo'yadi. nginx faqat ishonchli
  manzillardan (127.0.0.1 va backend tarmog'i, `OQTISH_BACKEND_SUBNET`) kelgan qiymatni qabul qiladi.
  Shu tufayli IP bo'yicha limitlar har bir mijozga alohida ishlaydi (tekshiruv topilmasi R1).

---

## 3. Birinchi o'rnatish

### 3.1. Kodni olish

```bash
sudo mkdir -p /opt/oqtish && sudo chown $USER /opt/oqtish
git clone <repo-manzili> /opt/oqtish && cd /opt/oqtish
```

### 3.2. `.env` faylini to'ldirish

```bash
cp .env.example .env && chmod 600 .env
```

Har bir `__REPLACE_ME__` ni kuchli tasodifiy qiymat bilan almashtiring
(`openssl rand -base64 48 | tr -d '/+=' | cut -c1-40`).

| O'zgaruvchi | Production qiymati |
|---|---|
| `POSTGRES_SUPERUSER_PASSWORD`, `OQTISH_DB_OWNER_PASSWORD`, `OQTISH_DB_API_PASSWORD`, `OQTISH_DB_WORKER_PASSWORD` | 4 xil kuchli parol |
| `OQTISH_ENVIRONMENT` | `Production` |
| `OQTISH_HTTP_BIND` / `OQTISH_HTTP_PORT` | `127.0.0.1` / `8080` (faqat tashqi proksi ulanadi) |
| `OQTISH_OTP_PEPPER` | kamida 32 belgili tasodifiy qator (**o'zgartirmang**: eski kodlar yaroqsiz bo'ladi) |
| `OQTISH_SMS_PROVIDER` | `Eskiz` |
| `OQTISH_ESKIZ_EMAIL`, `OQTISH_ESKIZ_PASSWORD`, `OQTISH_ESKIZ_FROM` | Eskiz kabinetidan |
| `OQTISH_PUBLIC_BASE_URL` | `https://oqtish.uz` |
| `OQTISH_TELEGRAM_ENABLED` | `true` (bot ishlatilsa) |
| `OQTISH_TELEGRAM_BOT_TOKEN`, `OQTISH_TELEGRAM_BOT_USERNAME` | @BotFather'dan |
| `OQTISH_TELEGRAM_MINIAPP_URL` | `https://oqtish.uz/tg` |
| `OQTISH_TELEGRAM_CLIENT` | bo'sh qoldiring yoki `Real` (`Fake` Production'da rad etiladi) |
| `OQTISH_TELEGRAM_MODE` | `Webhook` (serverda tavsiya) yoki `Polling` |
| `OQTISH_TELEGRAM_WEBHOOK_SECRET` | 32–256 belgili tasodifiy qator |
| `OQTISH_TELEGRAM_WEBHOOK_BASE_URL` | `https://oqtish.uz` |

Production rejimida dastur xavfli sozlamalar bilan **ishga tushmaydi**: Fake SMS yoki Fake Telegram,
yo'q yoki zaif OTP pepper, noto'g'ri webhook sozlamasi, o'chirilgan TOTP talabi. Bu ataylab qilingan.

### 3.3. Tashqi TLS proksi (nginx misoli)

```nginx
server {
    listen 443 ssl http2;
    server_name oqtish.uz;
    ssl_certificate     /etc/letsencrypt/live/oqtish.uz/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/oqtish.uz/privkey.pem;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}
server { listen 80; server_name oqtish.uz; return 301 https://$host$request_uri; }
```

Sertifikat: `sudo certbot --nginx -d oqtish.uz`.

### 3.4. Ishga tushirish

```bash
docker compose up -d --build --wait
docker compose ps -a        # migrate: Exited (0); api, web: healthy; worker: Up
curl -s https://oqtish.uz/health/ready    # Healthy
```

### 3.5. Birinchi platforma admini

Seed Production'da ishlamaydi. Admin bir marta quyidagicha yaratiladi (parol buyruq qatorida
berilmaydi, faqat muhit o'zgaruvchisi orqali):

```bash
read -rs OQTISH_ADMIN_PASSWORD && export OQTISH_ADMIN_PASSWORD
docker compose run --rm -e OQTISH_ADMIN_PASSWORD api create-platform-admin --email admin@oqtish.uz --phone +998XXXXXXXXX
unset OQTISH_ADMIN_PASSWORD
```

Admin birinchi kirishda **TOTP (Google Authenticator va h.k.)** yoqishi shart, aks holda platforma
huquqlari ishlamaydi.

### 3.6. Telegram bot

Bosqichma-bosqich yo'riqnoma: `docs/SERVICES.md` §5.9 "Telegram botni ulash". Qisqacha:
1. @BotFather → `/newbot` → token `.env` ga.
2. `/setdomain` → `oqtish.uz` (Login Widget uchun).
3. `/setmenubutton` → URL `https://oqtish.uz/tg` (Mini App).
4. Webhook rejimida API ishga tushganda `setWebhook` ni o'zi chaqiradi.

### 3.7. Zaxira nusxa

`docs/BACKUP_AND_RESTORE.md`: kunlik `deploy/backup/backup.sh` (systemd timer), oylik tiklash mashqi,
shifrlangan nusxa O'zbekiston ichidagi boshqa joyda.

### 3.8. Kalitlarni himoyalash (tekshiruv topilmasi R9)

Data Protection kalitlari (cookie va TOTP sirlarini shifrlaydi) bazada saqlanadi. Production'da ularni
qo'shimcha shifrlash tavsiya etiladi: sertifikat bilan (`ProtectKeysWithCertificate`) yoki KMS. Bu
kod o'zgarishini talab qiladi va v1'da qilinmagan. Unga qadar baza va zaxira nusxalariga kirishni
qat'iy cheklang va zaxira nusxalarni shifrlang.

---

## 4. Yangilash (yangi versiyani chiqarish)

```bash
cd /opt/oqtish
deploy/backup/backup.sh                       # 1) har doim avval zaxira nusxa
git fetch && git checkout <yangi-tag-yoki-commit>
docker compose up -d --build --wait           # 2) migrate yangi migratsiyalarni qo'llaydi
curl -s https://oqtish.uz/health/ready        # 3) Healthy bo'lishi kerak
docker compose logs --since 10m api worker | grep -iE 'error|fail' | head
```

Migratsiyalar faqat oldinga yuradi va "expand/contract" tamoyili bo'yicha yoziladi: eski kod yangi
sxema bilan ishlay oladi. Ma'lumotni o'chiruvchi migratsiyalar alohida tasdiq va zaxira nusxasiz
qo'llanmaydi (`docs/DATABASE.md` §5).

---

## 5. Orqaga qaytarish (Rollback)

| Holat | Harakat |
|---|---|
| Yangi versiya ishlamayapti, migratsiya **yo'q** edi | `git checkout <oldingi-commit> && docker compose up -d --build --wait` |
| Migratsiya qo'llangan, eski kod yangi sxema bilan ishlaydi (odatiy holat) | Xuddi yuqoridagidek, faqat kodni qaytaring. Sxema o'z holicha qoladi |
| Ma'lumot buzilgan yoki sxemani ham qaytarish shart | `docs/BACKUP_AND_RESTORE.md` §5: yangilashdan oldingi zaxira nusxadan tiklash |

Har bir yangilashdan oldin zaxira nusxa olingani (4-bo'lim, 1-qadam) tufayli istalgan nuqtaga qaytish
mumkin.

---

## 6. Monitoring va kundalik ishlar

| Nima | Qanday |
|---|---|
| Holat | `curl https://oqtish.uz/health/ready` (tashqi monitoring xizmatiga qo'shing) |
| Loglar | `docker compose logs -f api worker` (JSON format, shaxsiy ma'lumotsiz) |
| Bildirishnomalar | Admin panel → "Bildirishnomalar": muvaffaqiyatsiz xabarlarni ko'rish va qayta yuborish |
| Moliyaviy moslik | Worker har kecha 02:00 da kunlik hisobotlarni daftardan qayta hisoblaydi; nomuvofiqlik loglarda `warning` va auditda chiqadi |
| Eski ma'lumotlarni tozalash | Worker har kecha 03:30: eski OTP, sessiya va bildirishnoma yozuvlari (audit va daftarga tegmaydi) |
| Zaif kutubxonalar | `dotnet list package --vulnerable --include-transitive`, `cd web && npm audit --omit=dev` (oyiga bir marta va har yangilashda) |

// Telegram Mini App.

const uz = {
  'tg.notInTelegram': "Bu sahifa Telegram Mini App sifatida ishlaydi va Telegram ichida ochilishi kerak. Brauzerda davom etish uchun saytga oddiy usulda kiring.",
  'tg.openWebLogin': 'Saytga kirish',
  'tg.tabClinics': 'Klinikalar',
  'tg.tabMine': 'Qabullarim',
  'tg.tabStaff': 'Klinika',
  'tg.staffTitle': 'Klinika so‘rovlari',
  'tg.staffHint': 'Tasdiqlash uchun shifokorni tanlang. To‘liq boshqaruv — klinika panelida.',
  'tg.openPanel': 'Klinika panelini ochish',
  'tg.noClinics': "Siz klinika xodimi emassiz.",
  'tg.today': 'Bugungi qabullar',
  'tg.noToday': 'Bugun qabul yo‘q.',
} as const

type Keys = keyof typeof uz

const ru: Record<Keys, string> = {
  'tg.notInTelegram': 'Эта страница работает как Telegram Mini App и должна открываться внутри Telegram. Чтобы продолжить в браузере, войдите на сайт обычным способом.',
  'tg.openWebLogin': 'Войти на сайт',
  'tg.tabClinics': 'Клиники',
  'tg.tabMine': 'Мои записи',
  'tg.tabStaff': 'Клиника',
  'tg.staffTitle': 'Заявки клиники',
  'tg.staffHint': 'Чтобы подтвердить, выберите врача. Полное управление — в панели клиники.',
  'tg.openPanel': 'Открыть панель клиники',
  'tg.noClinics': 'Вы не сотрудник клиники.',
  'tg.today': 'Записи на сегодня',
  'tg.noToday': 'Сегодня записей нет.',
}

const en: Record<Keys, string> = {
  'tg.notInTelegram': 'This page is a Telegram Mini App and must be opened inside Telegram. To continue in a browser, sign in to the website the usual way.',
  'tg.openWebLogin': 'Sign in on the website',
  'tg.tabClinics': 'Clinics',
  'tg.tabMine': 'My appointments',
  'tg.tabStaff': 'Clinic',
  'tg.staffTitle': 'Clinic requests',
  'tg.staffHint': 'Choose a doctor to confirm. Full management is in the clinic console.',
  'tg.openPanel': 'Open clinic console',
  'tg.noClinics': 'You are not clinic staff.',
  'tg.today': "Today's appointments",
  'tg.noToday': 'No appointments today.',
}

export const telegram = { uz, ru, en }

// Development-only component gallery (route /__ui, never in production builds). All data here is
// sample data for checking component states; nothing is fetched from the API.
import { Building2, CalendarCheck, CheckCircle2, Hourglass, MapPin, Search, Stethoscope, UserX, Wallet } from 'lucide-react'
import { useState } from 'react'
import type { AppointmentStatus } from '../api/types'
import { Field } from '../ui/forms'
import { Button } from '../ui/Button'
import { CountdownRing } from '../ui/CountdownRing'
import { Dialog } from '../ui/Dialog'
import { Drawer } from '../ui/Drawer'
import { Monogram } from '../ui/Monogram'
import { Skeleton, SkeletonCards } from '../ui/Skeleton'
import { EmptyState } from '../ui/states'
import { useToast } from '../ui/toastContext'
import { InfoTip } from '../ui/Tooltip'
import { Badge, StatCard, Stars, StatusBadge } from '../ui/widgets'

const statuses: AppointmentStatus[] = ['Pending', 'Confirmed', 'Completed', 'Rejected', 'CancelledByPatient', 'CancelledByClinic', 'Expired', 'NoShow']

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="card stack">
      <h2>{title}</h2>
      {children}
    </section>
  )
}

export function UiGallery() {
  const toast = useToast()
  const [dialog, setDialog] = useState(false)
  const [drawer, setDrawer] = useState(false)
  const [loading, setLoading] = useState(false)
  const [deadline] = useState(() => new Date(Date.now() + 18 * 60_000).toISOString())
  const [step, setStep] = useState(1)

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <span className="eyebrow">Dev</span>
          <h1>UI galereya</h1>
          <p className="muted">Dizayn tizimi komponentlari va holatlari. Namunaviy ma’lumot.</p>
        </div>
      </header>

      <Section title="Tugmalar">
        <div className="row">
          <Button>Asosiy</Button>
          <Button variant="secondary">Ikkilamchi</Button>
          <Button variant="subtle">Yumshoq</Button>
          <Button variant="danger">Xavfli</Button>
          <Button disabled>O‘chirilgan</Button>
          <Button loading={loading} onClick={() => { setLoading(true); setTimeout(() => setLoading(false), 1500) }}>
            Yuborish
          </Button>
          <Button icon={Search} aria-label="Qidirish" variant="secondary" />
          <Button size="small" icon={CalendarCheck}>Kichik</Button>
          <Button size="large">Navbat band qilish</Button>
        </div>
      </Section>

      <Section title="Forma">
        <div className="grid-2">
          <Field label="Ism">{(p) => <input {...p} defaultValue="Aziza" />}</Field>
          <Field label="Telefon" hint="+998 bilan boshlanadi" error="Telefon raqami noto‘g‘ri">
            {(p) => <input {...p} defaultValue="+99890" />}
          </Field>
          <Field label="Xizmat turi">
            {(p) => (
              <select {...p}>
                <option>Terapevtik davolash</option>
                <option>Implantatsiya</option>
              </select>
            )}
          </Field>
          <Field label="Izoh" optional>{(p) => <textarea {...p} />}</Field>
        </div>
        <div className="row">
          <button type="button" className="chip" aria-pressed="true">Reyting</button>
          <button type="button" className="chip" aria-pressed="false">Narx</button>
          <button type="button" className="chip">
            <MapPin className="icon" aria-hidden="true" /> Masofa
          </button>
          <div className="segmented" role="group" aria-label="Ko‘rinish">
            <button type="button" aria-pressed="true">Kun</button>
            <button type="button" aria-pressed="false">Hafta</button>
            <button type="button" aria-pressed="false">Ro‘yxat</button>
          </div>
        </div>
      </Section>

      <Section title="Holatlar (rang + ikonka + matn)">
        <div className="row">
          {statuses.map((s) => (
            <StatusBadge key={s} status={s} />
          ))}
        </div>
        <div className="row">
          <Badge tone="violet">Yangi</Badge>
          <Stars rating={4.4} label="Reyting 4,4 / 5" />
          <span className="rating">
            <Stars rating={5} label="5" /> 4.8 <span className="rating__count">(124)</span>
          </span>
        </div>
      </Section>

      <Section title="Metrikalar">
        <div className="stats">
          <StatCard label="Kutilayotgan so‘rovlar" value="3" icon={Hourglass} iconTone="warning" tone="warning" />
          <StatCard label="Tasdiqlangan" value="12" icon={CalendarCheck} />
          <StatCard label="Bajarilgan" value="8" icon={CheckCircle2} iconTone="teal" />
          <StatCard label="Kelmadi" value="1" icon={UserX} iconTone="danger" />
          <StatCard
            label="Yig‘ilgan to‘lovlar"
            value="2 450 000 so‘m"
            icon={Wallet}
            iconTone="violet"
            info={<InfoTip>Bugun kassaga tushgan to‘lovlar yig‘indisi, qaytarishlar ayirilgan.</InfoTip>}
            hint="Bugun, filial vaqti bo‘yicha"
          />
        </div>
      </Section>

      <Section title="Booking qadamlari va Pending halqasi">
        <ol className="steps">
          {['Xizmat', 'Sana va vaqt', 'Tasdiqlash'].map((label, i) => (
            <li key={label} className={`steps__item${i < step ? ' steps__item--done' : i === step ? ' steps__item--current' : ''}`} aria-current={i === step ? 'step' : undefined}>
              <span className="steps__num">{i < step ? <CheckCircle2 className="icon" aria-hidden="true" /> : i + 1}</span>
              <span className="steps__label">{label}</span>
            </li>
          ))}
        </ol>
        <div className="row">
          <Button variant="secondary" size="small" onClick={() => setStep((s) => Math.max(0, s - 1))}>Orqaga</Button>
          <Button size="small" onClick={() => setStep((s) => Math.min(2, s + 1))}>Keyingi</Button>
        </div>
        <div className="row">
          <CountdownRing deadline={deadline} totalMs={30 * 60_000} expiredText="0:00" />
          <div>
            <strong>So‘rov klinikaga yuborildi</strong>
            <p className="muted small">Klinika tasdiqlashini kuting. Shifokorni klinika biriktiradi.</p>
          </div>
        </div>
      </Section>

      <Section title="Kartochka, monogramma, skeleton">
        <div className="row">
          <Monogram name="Smile Dental Clinic" />
          <Monogram name="DentLife" />
          <Monogram name="Oq Tabassum" />
          <span className="icon-tile"><Stethoscope className="icon" aria-hidden="true" /></span>
          <span className="icon-tile icon-tile--teal"><Building2 className="icon" aria-hidden="true" /></span>
        </div>
        <Skeleton variant="title" />
        <Skeleton width="80%" />
        <SkeletonCards count={3} label="Yuklanmoqda" />
      </Section>

      <Section title="Overlay va feedback">
        <div className="row">
          <Button onClick={() => setDialog(true)}>Dialog</Button>
          <Button variant="secondary" onClick={() => setDrawer(true)}>Drawer (filtr)</Button>
          <Button variant="subtle" onClick={() => toast({ title: 'Saqlandi', body: 'O‘zgarishlar serverda saqlandi.' })}>Toast</Button>
          <Button variant="subtle" onClick={() => toast({ title: 'Saqlanmadi', body: 'Tarmoq xatosi. Qayta urinib ko‘ring.', tone: 'error' })}>Xato toast</Button>
        </div>
        <div className="alert alert--info">Klinika so‘rovingizni 30 daqiqa ichida ko‘rib chiqadi.</div>
        <div className="alert alert--warning">Bu vaqt shifokorning ish vaqtidan tashqarida.</div>
        <div className="alert alert--success">To‘lov yozib olindi.</div>
        <EmptyState title="Hali qabul yo‘q">
          <p>Klinikani toping va birinchi qabulga yoziling.</p>
        </EmptyState>
      </Section>

      <Section title="Jadval (mobil’da kartochka)">
        <div className="table-wrap">
          <table className="table table--stack">
            <thead>
              <tr><th>Bemor</th><th>Xizmat</th><th>Vaqt</th><th>Holat</th></tr>
            </thead>
            <tbody>
              {[['Aliyev A.', 'Terapevtik davolash', '09:00', 'Completed'], ['Saidov M.', 'Tozalash', '09:30', 'Confirmed'], ['Karimova D.', 'Ortodontiya', '10:00', 'Pending']].map(([n, s, tm, st]) => (
                <tr key={n}>
                  <td data-label="Bemor">{n}</td>
                  <td data-label="Xizmat">{s}</td>
                  <td data-label="Vaqt" className="tabular">{tm}</td>
                  <td data-label="Holat"><StatusBadge status={st as AppointmentStatus} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Section>

      {dialog && (
        <Dialog title="So‘rovni rad etish" tone="danger" onClose={() => setDialog(false)}>
          <div className="stack">
            <p>Bemorga rad etish sababi yuboriladi. Bu amalni qaytarib bo‘lmaydi.</p>
            <div className="row">
              <Button variant="danger" onClick={() => setDialog(false)}>Rad etish</Button>
              <Button variant="secondary" onClick={() => setDialog(false)}>Bekor qilish</Button>
            </div>
          </div>
        </Dialog>
      )}
      {drawer && (
        <Drawer title="Filtrlar" onClose={() => setDrawer(false)} footer={<><Button variant="secondary" onClick={() => setDrawer(false)}>Tozalash</Button><Button onClick={() => setDrawer(false)}>Natijalarni ko‘rsatish</Button></>}>
          <Field label="Tuman">{(p) => <select {...p}><option>Chilonzor</option><option>Yunusobod</option></select>}</Field>
        </Drawer>
      )}
    </div>
  )
}

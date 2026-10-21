import { CalendarClock, Check, MapPin, Stethoscope, UserRoundCheck, Wallet } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { isApiError } from '../../api/client'
import { useApi } from '../../api/context'
import type { AppointmentDto, PublicBranch, PublicServiceOffer } from '../../api/types'
import { useIdempotencyKeeper } from '../../api/idempotency'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useI18n } from '../../i18n/context'
import type { MessageKey } from '../../i18n/messages'
import { formatDate, formatLocalDay, formatMoney, formatTime } from '../../lib/format'
import { formatPhone, isValidUzPhone, normalizePhone } from '../../lib/phone'
import { addDays, localDate, weekdayOf } from '../../lib/time'
import { Field, FormError, Notice, PhoneInput } from '../../ui/forms'
import { ApiErrorState, EmptyState, LoadingState } from '../../ui/states'
import { Details } from '../../ui/widgets'
import { Monogram } from '../../ui/Monogram'
import { HORIZON_DAYS, effectiveStep, type BookingState, type BookingStep, type SubmitControlProps } from './bookingState'
export type { BookingState, BookingStep, SubmitControlProps } from './bookingState'
import { useNow } from '../../hooks/useNow'

const stepLabels: Record<BookingStep, MessageKey> = {
  branch: 'book.stepBranch',
  service: 'book.stepService',
  time: 'book.stepTime',
  contact: 'book.stepContact',
  summary: 'book.stepSummary',
}

/**
 * The booking flow (branch → service → date & slot → contact → summary → submit), shared by the
 * website and the Telegram Mini App. State is owned by the caller (URL query on the web, memory in
 * the Mini App). The server re-checks every rule on submit; 409 slot_unavailable refreshes slots.
 */
export function BookingWizard({
  clinicId,
  state,
  onChange,
  onBooked,
  loginHref,
  renderSubmit,
}: {
  clinicId: string
  state: BookingState
  onChange: (next: BookingState) => void
  onBooked: (appointment: AppointmentDto) => void
  /** Where to send anonymous users at the contact step (null = already signed in by the host). */
  loginHref: string | null
  /** Optional custom submit control (the Mini App binds Telegram's MainButton). */
  renderSubmit?: (props: SubmitControlProps) => ReactNode
}) {
  const { t, locale } = useI18n()
  const api = useApi()
  const { me } = useSession()
  const clinic = useQuery(`clinic:${clinicId}`, () => api.public.clinic(clinicId))
  const [conflict, setConflict] = useState(false)
  const [slotRefresh, setSlotRefresh] = useState(0)
  const keeper = useIdempotencyKeeper()
  const book = useAction((body: Parameters<typeof api.patient.book>[0]) => api.patient.book(body, keeper.keyFor(body)))
  const headingRef = useRef<HTMLHeadingElement>(null)

  const step = effectiveStep(state, clinic.data)
  const branch = clinic.data?.branches.find((b) => b.id === state.branchId)
  const service = branch?.services.find((s) => s.serviceId === state.serviceId)

  // Single-option steps are pre-selected so the patient doesn't click through a list of one.
  useEffect(() => {
    if (!clinic.data) return
    if (!state.branchId && clinic.data.branches.length === 1) {
      onChange({ ...state, branchId: clinic.data.branches[0]!.id, step: state.step === 'branch' ? 'service' : state.step })
    }
  }, [clinic.data, state, onChange])

  useEffect(() => {
    headingRef.current?.focus()
  }, [step])

  if (clinic.error !== undefined) return <ApiErrorState error={clinic.error} onRetry={clinic.reload} />
  if (!clinic.data) return <LoadingState />

  const go = (patch: Partial<BookingState>) => onChange({ ...state, ...patch })

  async function submit() {
    if (!branch || !service || !state.startsAt || !state.contactName || !state.contactPhone) return
    const body = {
      branchId: branch.id,
      serviceId: service.serviceId,
      startsAt: state.startsAt,
      contactName: state.contactName.trim(),
      contactPhone: normalizePhone(state.contactPhone),
    }
    const r = await book.run(body)
    if (r.ok) {
      keeper.reset()
      onBooked(r.value)
      return
    }
    keeper.afterError(r.error)
    if (isApiError(r.error, 'booking.slot_unavailable')) {
      setConflict(true)
      setSlotRefresh((n) => n + 1)
      book.clearError()
      go({ step: 'time', startsAt: undefined })
    }
  }

  // Five logical steps are shown as three visual stages (service · date & time · confirm).
  const stage = stageOf[step]

  return (
    <div className="wizard">
      <header className="wizard__head">
        <Monogram name={clinic.data.name} seed={clinic.data.id} imageId={clinic.data.logoId} size={44} />
        <h1 className="wizard__clinic">{clinic.data.name}</h1>
      </header>
      <ol className="steps" aria-label={t('book.progress')}>
        {stages.map((s, i) => (
          <li key={s} className={i < stage ? 'steps__item steps__item--done' : i === stage ? 'steps__item steps__item--current' : 'steps__item'} aria-current={i === stage ? 'step' : undefined}>
            <span className="steps__num" aria-hidden="true">
              {i < stage ? <Check className="icon" /> : i + 1}
            </span>
            <span className="steps__label">{t(s)}</span>
          </li>
        ))}
      </ol>

      <div className={step === 'summary' ? 'wizard__layout wizard__layout--single' : 'wizard__layout'}>
      {step !== 'summary' && (
        <BookingSummary clinicName={clinic.data.name} branch={branch} service={service} startsAt={state.startsAt} showBranch={clinic.data.branches.length > 1} />
      )}
      <section className="card stack wizard__panel" aria-labelledby="wizard-step-title">
        <h2 id="wizard-step-title" tabIndex={-1} ref={headingRef}>
          {t(stepLabels[step])}
        </h2>

        {step === 'branch' && (
          <ChoiceList
            items={clinic.data.branches.map((b) => ({ id: b.id, title: b.name, detail: `${b.district}, ${b.address}` }))}
            selected={state.branchId}
            empty={t('clinic.noBranches')}
            onPick={(id) => go({ branchId: id, serviceId: undefined, date: undefined, startsAt: undefined, step: 'service' })}
          />
        )}

        {step === 'service' && branch && (
          <>
            <ChoiceList
              items={branch.services.map((s) => ({
                id: s.serviceId,
                title: s.name,
                detail: `${priceText(s, locale, t)} · ${t('common.minutes', { minutes: s.durationMinutes })}`,
              }))}
              selected={state.serviceId}
              empty={t('clinic.noServices')}
              onPick={(id) => go({ serviceId: id, startsAt: undefined, step: 'time' })}
            />
            <BackButton onClick={() => go({ step: 'branch' })} hidden={clinic.data.branches.length === 1} />
          </>
        )}

        {step === 'time' && branch && service && (
          <>
            {conflict && (
              <div className="alert alert--warning" role="alert">
                {t('book.conflict')}
              </div>
            )}
            <TimeStep
              branch={branch}
              service={service}
              date={state.date}
              startsAt={state.startsAt}
              onDate={(date) => go({ date, startsAt: undefined })}
              onSlot={(startsAt) => {
                setConflict(false)
                go({ startsAt, step: 'contact' })
              }}
              refreshToken={slotRefresh}
            />
            <BackButton onClick={() => go({ step: 'service' })} />
          </>
        )}

        {step === 'contact' &&
          (me || loginHref === null ? (
            <ContactStep
              initialName={state.contactName ?? me?.patient?.fullName ?? (me?.displayName || '')}
              initialPhone={state.contactPhone ?? me?.patient?.contactPhone ?? ''}
              onBack={() => go({ step: 'time' })}
              onNext={(contactName, contactPhone) => go({ contactName, contactPhone, step: 'summary' })}
            />
          ) : (
            <div className="stack">
              <p>{t('book.signInNeeded')}</p>
              <div className="row">
                <Link className="btn" to={loginHref}>
                  {t('nav.login')}
                </Link>
                <BackButton onClick={() => go({ step: 'time' })} />
              </div>
            </div>
          ))}

        {step === 'summary' && branch && service && state.startsAt && (
          <div className="stack">
            <Details
              items={[
                [t('common.clinic'), clinic.data.name],
                [t('common.branch'), `${branch.name} — ${branch.district}, ${branch.address}`],
                [t('common.service'), `${service.name} (${t('common.minutes', { minutes: service.durationMinutes })})`],
                [t('book.when'), `${formatDate(localDate(state.startsAt, branch.timeZone), locale)}, ${formatTime(state.startsAt, branch.timeZone, locale)}`],
                [t('clinic.price'), priceText(service, locale, t)],
                [t('book.contact'), `${state.contactName} · ${formatPhone(normalizePhone(state.contactPhone ?? ''))}`],
              ]}
            />
            <ul className="notes">
              <li>{t('book.doctorNote')}</li>
              <li>{t('book.expiryNote')}</li>
              <li>{t('book.pendingLimitNote')}</li>
            </ul>
            <FormError error={book.error} />
            {isApiError(book.error) && ['booking.outside_hours', 'booking.lead_time'].includes(book.error.code ?? '') && (
              <button type="button" className="btn btn--ghost" onClick={() => go({ step: 'time', startsAt: undefined })}>
                {t('book.pickAnotherTime')}
              </button>
            )}
            <div className="row">
              {renderSubmit ? (
                renderSubmit({ label: t('book.submit'), pending: book.pending, onSubmit: () => void submit() })
              ) : (
                <button type="button" className="btn" disabled={book.pending} aria-busy={book.pending || undefined} onClick={() => void submit()}>
                  {book.pending ? t('book.sending') : t('book.submit')}
                </button>
              )}
              <BackButton onClick={() => go({ step: 'contact' })} />
            </div>
          </div>
        )}
      </section>
      </div>
    </div>
  )
}

const stages: MessageKey[] = ['bookFlow.service', 'bookFlow.time', 'bookFlow.confirm']
const stageOf: Record<BookingStep, number> = { branch: 0, service: 0, time: 1, contact: 2, summary: 2 }

/** Persistent summary of what is chosen so far (hidden on the final review, which shows it all). */
function BookingSummary({ clinicName, branch, service, startsAt, showBranch }: { clinicName: string; branch?: PublicBranch; service?: PublicServiceOffer; startsAt?: string; showBranch: boolean }) {
  const { t, locale } = useI18n()
  return (
    <aside className={service ? 'card wizard__summary' : 'card wizard__summary wizard__summary--empty'} aria-label={t('bookFlow.summary')}>
      <p className="wizard__summary-title">{t('bookFlow.summary')}</p>
      <dl className="wizard__facts">
        <div>
          <dt>
            <Stethoscope className="icon" aria-hidden="true" />
            {t('common.service')}
          </dt>
          <dd>{service ? `${service.name} · ${t('common.minutes', { minutes: service.durationMinutes })}` : <span className="muted">{t('bookFlow.notChosen')}</span>}</dd>
        </div>
        {(showBranch || branch) && (
          <div>
            <dt>
              <MapPin className="icon" aria-hidden="true" />
              {t('common.branch')}
            </dt>
            <dd>{branch ? `${branch.name} — ${branch.address}` : <span className="muted">{t('bookFlow.notChosen')}</span>}</dd>
          </div>
        )}
        <div>
          <dt>
            <CalendarClock className="icon" aria-hidden="true" />
            {t('book.when')}
          </dt>
          <dd>
            {startsAt && branch ? `${formatDate(localDate(startsAt, branch.timeZone), locale)}, ${formatTime(startsAt, branch.timeZone, locale)}` : <span className="muted">{t('bookFlow.notChosen')}</span>}
          </dd>
        </div>
      </dl>
      {service && <p className="wizard__price">{priceText(service, locale, t)}</p>}
      <ul className="wizard__notes">
        <li>
          <UserRoundCheck className="icon" aria-hidden="true" />
          {t('bookFlow.doctor')}
        </li>
        <li>
          <Wallet className="icon" aria-hidden="true" />
          {t('bookFlow.payAtClinic')}
        </li>
      </ul>
      <span className="visually-hidden">{clinicName}</span>
    </aside>
  )
}

function priceText(s: PublicServiceOffer, locale: Parameters<typeof formatMoney>[1], t: (k: MessageKey, p?: Record<string, string | number>) => string): string {
  const price = formatMoney(s.price, locale)
  return s.priceKind === 'From' ? t('clinic.priceFrom', { price }) : price
}

function BackButton({ onClick, hidden }: { onClick: () => void; hidden?: boolean }) {
  const { t } = useI18n()
  if (hidden) return null
  return (
    <button type="button" className="btn btn--ghost" onClick={onClick}>
      {t('common.back')}
    </button>
  )
}

function ChoiceList({ items, selected, empty, onPick }: { items: { id: string; title: string; detail: string }[]; selected?: string; empty: string; onPick: (id: string) => void }) {
  if (items.length === 0) return <EmptyState title={empty} />
  return (
    <ul className="choices">
      {items.map((item) => (
        <li key={item.id}>
          <button type="button" className={item.id === selected ? 'choice choice--selected' : 'choice'} aria-pressed={item.id === selected} onClick={() => onPick(item.id)}>
            <span className="choice__title">{item.title}</span>
            <span className="choice__detail">{item.detail}</span>
          </button>
        </li>
      ))}
    </ul>
  )
}

function TimeStep({
  branch,
  service,
  date,
  startsAt,
  onDate,
  onSlot,
  refreshToken,
}: {
  branch: PublicBranch
  service: PublicServiceOffer
  date?: string
  startsAt?: string
  onDate: (date: string) => void
  onSlot: (startsAt: string) => void
  refreshToken: number
}) {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const today = localDate(now, branch.timeZone)
  const days = useMemo(() => Array.from({ length: HORIZON_DAYS }, (_, i) => addDays(today, i)), [today])
  const openDays = new Set(branch.hours.map((h) => h.weekday))
  const selectedDate = date && days.includes(date) ? date : (days.find((d) => openDays.has(weekdayOf(d))) ?? today)
  const availability = useQuery(`availability:${branch.id}:${service.serviceId}:${selectedDate}:${refreshToken}`, () =>
    api.public.availability(branch.id, service.serviceId, selectedDate),
  )

  return (
    <div className="stack">
      <div className="date-strip" role="group" aria-label={t('book.chooseDate')}>
        {days.map((d) => {
          const closed = !openDays.has(weekdayOf(d))
          const label = formatLocalDay(d, locale)
          return (
            <button
              key={d}
              type="button"
              className={d === selectedDate ? 'date-chip date-chip--selected' : 'date-chip'}
              aria-pressed={d === selectedDate}
              disabled={closed}
              aria-label={`${formatDate(d, locale)}${closed ? ` — ${t('clinic.closed')}` : ''}`}
              onClick={() => onDate(d)}
            >
              <span className="date-chip__weekday">{d === today ? t('clinicPage.today') : label.weekday}</span>
              <span className="date-chip__day">{label.day}</span>
            </button>
          )
        })}
      </div>

      <div aria-live="polite" aria-busy={availability.loading}>
        {availability.error !== undefined ? (
          <ApiErrorState error={availability.error} onRetry={availability.reload} />
        ) : !availability.data ? (
          <LoadingState inline />
        ) : availability.data.slots.length === 0 ? (
          <p className="muted">{t('book.noSlots')}</p>
        ) : (
          <div className="slots" role="group" aria-label={t('book.chooseTime', { date: formatDate(selectedDate, locale) })}>
            {availability.data.slots.map((slot) => (
              <button key={slot.startsAt} type="button" className={slot.startsAt === startsAt ? 'slot slot--selected' : 'slot'} aria-pressed={slot.startsAt === startsAt} onClick={() => onSlot(slot.startsAt)}>
                {slot.localTime}
              </button>
            ))}
          </div>
        )}
      </div>
      <p className="muted small">{t('book.slotsNote', { tz: branch.timeZone })}</p>
    </div>
  )
}

function ContactStep({ initialName, initialPhone, onBack, onNext }: { initialName: string; initialPhone: string; onBack: () => void; onNext: (name: string, phone: string) => void }) {
  const { t } = useI18n()
  const [name, setName] = useState(initialName)
  const [phone, setPhone] = useState(initialPhone)
  const [touched, setTouched] = useState(false)
  const nameError = !name.trim() ? t('valid.required') : name.trim().length > 100 ? t('valid.maxLength', { max: 100 }) : null
  const phoneError = !phone.trim() ? t('valid.required') : !isValidUzPhone(phone) ? t('valid.phone') : null

  function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (nameError || phoneError) return
    onNext(name.trim(), normalizePhone(phone))
  }

  return (
    <form className="stack" onSubmit={submit} noValidate>
      <Notice tone="info">{t('book.contactNote')}</Notice>
      <Field label={t('book.name')} error={touched ? nameError : null}>
        {(p) => <input {...p} autoComplete="name" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />}
      </Field>
      <Field label={t('book.phone')} error={touched ? phoneError : null}>
        {(p) => <PhoneInput {...p} autoComplete="tel" value={phone} onChange={setPhone} />}
      </Field>
      <div className="row">
        <button type="submit" className="btn">
          {t('common.next')}
        </button>
        <button type="button" className="btn btn--ghost" onClick={onBack}>
          {t('common.back')}
        </button>
      </div>
    </form>
  )
}

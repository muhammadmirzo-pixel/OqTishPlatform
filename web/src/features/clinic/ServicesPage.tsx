import { ListChecks, Pencil, Plus } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import { serviceCategories, type BranchDto, type BranchOfferBody, type BranchOfferDto, type PriceKind, type ServiceBody, type ServiceCategory, type ServiceDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { categoryLabel, isServiceCategory } from '../../lib/categories'
import { formatMoney } from '../../lib/format'
import { amountForInput, parseAmount, uzs } from '../../lib/money'
import { Dialog } from '../../ui/Dialog'
import { Select } from '../../ui/Select'
import { Checkbox, Field, FormError, SubmitButton } from '../../ui/forms'
import { EmptyState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { categoryIcon, categoryTone } from '../public/categoryIcons'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'

export function ServicesPage() {
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.services'))
  const api = useApi()
  const { clinicId, branches, can, canClinicWide } = useClinic()
  const services = useQuery(`services:${clinicId}`, () => api.clinic.services(clinicId))
  const [editing, setEditing] = useState<ServiceDto | 'new' | null>(null)
  const [offer, setOffer] = useState<{ service: ServiceDto; branch: BranchDto } | null>(null)
  const toast = useToast()
  // Services belong to the whole clinic: create/edit needs catalog.manage clinic-wide; a branch offer
  // (price/duration) needs catalog.manage on that branch (DECISIONS.md BE1b-2).
  const manageServices = canClinicWide(Perm.catalogManage)
  const manageOffers = can(Perm.catalogManage)
  const saved = () => {
    toast({ title: t('common.saved') })
    services.reload()
  }

  return (
    <div className="stack">
      <PageHeader
        icon={ListChecks}
        title={t('clinicNav.services')}
        subtitle={t('svc.intro')}
        actions={
          manageServices && (
            <button type="button" className="btn" onClick={() => setEditing('new')}>
              <Plus className="icon" aria-hidden="true" />
              {t('svc.add')}
            </button>
          )
        }
      />
      {manageOffers && !manageServices && <p className="muted small">{t('svc.clinicWideOnly')}</p>}
      <QueryView query={services}>
        {(list) =>
          list.length === 0 ? (
            <EmptyState title={t('svc.none')} icon={ListChecks} />
          ) : (
            <ul className="list-plain stack">
              {list.map((s) => {
                const Icon = isServiceCategory(s.category) ? categoryIcon[s.category] : ListChecks
                const tone = isServiceCategory(s.category) ? categoryTone[s.category] : ''
                return (
                  <li key={s.id} className="card stack">
                    <div className="entity">
                      <span className={`icon-tile${tone ? ` icon-tile--${tone}` : ''}`}>
                        <Icon className="icon" aria-hidden="true" />
                      </span>
                      <div className="entity__body">
                        <h2 className="entity__title">
                          <span>{s.name}</span> {!s.isActive && <Badge>{t('common.inactive')}</Badge>}
                        </h2>
                        <p className="muted small">{categoryLabel(t, s.category)}</p>
                      </div>
                      {manageServices && (
                        <div className="entity__actions">
                          <button type="button" className="btn btn--ghost btn--small" onClick={() => setEditing(s)}>
                            <Pencil className="icon" aria-hidden="true" />
                            {t('common.edit')}
                          </button>
                        </div>
                      )}
                    </div>
                    <div className="table-wrap">
                      <table className="table table--compact table--stack">
                        <thead>
                          <tr>
                            <th scope="col">{t('common.branch')}</th>
                            <th scope="col">{t('clinic.price')}</th>
                            <th scope="col">{t('clinic.duration')}</th>
                            <th scope="col">{t('common.status')}</th>
                            {manageOffers && <th scope="col">{t('common.actions')}</th>}
                          </tr>
                        </thead>
                        <tbody>
                          {branches.map((b) => {
                            const o = s.offers.find((x) => x.branchId === b.id)
                            return (
                              <tr key={b.id}>
                                <th scope="row" data-label={t('common.branch')}>
                                  {b.name}
                                </th>
                                <td data-label={t('clinic.price')}>{o ? `${o.priceKind === 'From' ? t('clinic.priceFrom', { price: formatMoney(o.price, locale) }) : formatMoney(o.price, locale)}` : '—'}</td>
                                <td data-label={t('clinic.duration')}>{o ? t('common.minutes', { minutes: o.durationMinutes }) : '—'}</td>
                                <td data-label={t('common.status')}>
                                  {o ? <Badge tone={o.isActive ? 'success' : 'neutral'}>{t(o.isActive ? 'common.active' : 'common.inactive')}</Badge> : <span className="muted">{t('svc.notOffered')}</span>}
                                </td>
                                {manageOffers && (
                                  <td data-label={t('common.actions')}>
                                    <button type="button" className="btn btn--ghost btn--small" onClick={() => setOffer({ service: s, branch: b })}>
                                      {o ? t('common.edit') : t('svc.offer')}
                                    </button>
                                  </td>
                                )}
                              </tr>
                            )
                          })}
                        </tbody>
                      </table>
                    </div>
                  </li>
                )
              })}
            </ul>
          )
        }
      </QueryView>
      {editing && (
        <ServiceDialog
          service={editing === 'new' ? null : editing}
          onDone={() => {
            setEditing(null)
            saved()
          }}
          onClose={() => setEditing(null)}
        />
      )}
      {offer && (
        <OfferDialog
          service={offer.service}
          branch={offer.branch}
          current={offer.service.offers.find((o) => o.branchId === offer.branch.id)}
          onDone={() => {
            setOffer(null)
            saved()
          }}
          onClose={() => setOffer(null)}
        />
      )}
    </div>
  )
}

function ServiceDialog({ service, onDone, onClose }: { service: ServiceDto | null; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [name, setName] = useState(service?.name ?? '')
  const [category, setCategory] = useState<ServiceCategory | ''>(service && isServiceCategory(service.category) ? service.category : '')
  const [isActive, setActive] = useState(service?.isActive ?? true)
  const [touched, setTouched] = useState(false)
  const save = useAction((body: ServiceBody) => (service ? api.clinic.updateService(clinicId, service.id, body) : api.clinic.createService(clinicId, body)))
  const errors = { name: !name.trim() ? t('valid.required') : null, category: !category ? t('valid.required') : null }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.name || !category) return
    const r = await save.run({ name: name.trim(), category, isActive })
    if (r.ok) onDone()
  }

  return (
    <Dialog title={service ? t('svc.edit') : t('svc.add')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('common.name')} error={touched ? errors.name : null}>
          {(p) => <input {...p} maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />}
        </Field>
        <Field label={t('home.category')} error={touched ? errors.category : null}>
          {(p) => (
            <Select
              {...p}
              value={category}
              onChange={(v) => setCategory(v as ServiceCategory | '')}
              options={[{ value: '', label: t('svc.chooseCategory') }, ...serviceCategories.map((c) => ({ value: c, label: t(`category.${c}`) }))]}
            />
          )}
        </Field>
        <Checkbox label={t('common.active')} checked={isActive} onChange={setActive} />
        <FormError error={save.error} />
        <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
      </form>
    </Dialog>
  )
}

function OfferDialog({ service, branch, current, onDone, onClose }: { service: ServiceDto; branch: BranchDto; current?: BranchOfferDto; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [price, setPrice] = useState(current ? amountForInput(current.price.amount) : '')
  const [priceKind, setKind] = useState<PriceKind>(current?.priceKind ?? 'Fixed')
  const [duration, setDuration] = useState(String(current?.durationMinutes ?? 30))
  const [isActive, setActive] = useState(current?.isActive ?? true)
  const [touched, setTouched] = useState(false)
  const save = useAction((body: BranchOfferBody) => api.clinic.setBranchOffer(clinicId, branch.id, service.id, body))
  const parsed = parseAmount(price)
  const minutes = Number(duration)
  const errors = {
    price: !price.trim() ? t('valid.required') : parsed === null ? t('valid.amount') : null,
    duration: !/^\d+$/.test(duration) || minutes < 5 || minutes > 480 ? t('svc.durationInvalid') : null,
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.price || errors.duration || !parsed) return
    const r = await save.run({ price: uzs(parsed), priceKind, durationMinutes: minutes, isActive })
    if (r.ok) onDone()
  }

  return (
    <Dialog title={`${service.name} — ${branch.name}`} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <div className="grid-2">
          <Field label={t('clinic.price')} error={touched ? errors.price : null}>
            {(p) => <input {...p} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />}
          </Field>
          <Field label={t('svc.priceKind')}>
            {(p) => (
              <Select
                {...p}
                value={priceKind}
                onChange={(v) => setKind(v as PriceKind)}
                options={[
                  { value: 'Fixed', label: t('svc.kindFixed') },
                  { value: 'From', label: t('svc.kindFrom') },
                ]}
              />
            )}
          </Field>
        </div>
        <Field label={t('svc.durationMinutes')} hint={t('svc.durationHint')} error={touched ? errors.duration : null}>
          {(p) => <input {...p} type="number" min={5} max={480} step={5} value={duration} onChange={(e) => setDuration(e.target.value)} />}
        </Field>
        <Checkbox label={t('svc.offeredActive')} checked={isActive} onChange={setActive} />
        <FormError error={save.error} />
        <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
      </form>
    </Dialog>
  )
}

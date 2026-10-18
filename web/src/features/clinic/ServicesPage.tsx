import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import type { BranchDto, BranchOfferBody, BranchOfferDto, PriceKind, ServiceBody, ServiceDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { amountForInput, parseAmount, uzs } from '../../lib/money'
import { Dialog } from '../../ui/Dialog'
import { Checkbox, Field, FormError, Notice, SubmitButton } from '../../ui/forms'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'

export function ServicesPage() {
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.services'))
  const api = useApi()
  const { clinicId, branches, can } = useClinic()
  const services = useQuery(`services:${clinicId}`, () => api.clinic.services(clinicId))
  const facets = useQuery('facets', () => api.public.facets())
  const [editing, setEditing] = useState<ServiceDto | 'new' | null>(null)
  const [offer, setOffer] = useState<{ service: ServiceDto; branch: BranchDto } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const manage = can(Perm.catalogManage)
  const categories = facets.data?.categories ?? []
  const categoryName = (key: string) => categories.find((c) => c.key === key)?.name ?? key
  const saved = () => {
    setNotice(t('common.saved'))
    services.reload()
  }

  return (
    <div className="stack">
      <PageHeader
        title={t('clinicNav.services')}
        subtitle={t('svc.intro')}
        actions={
          manage && (
            <button type="button" className="btn" onClick={() => setEditing('new')}>
              {t('svc.add')}
            </button>
          )
        }
      />
      <div aria-live="polite">{notice && <Notice>{notice}</Notice>}</div>
      <QueryView query={services}>
        {(list) =>
          list.length === 0 ? (
            <EmptyState title={t('svc.none')} />
          ) : (
            <ul className="list-plain stack">
              {list.map((s) => (
                <li key={s.id} className="card stack">
                  <div className="row row--between">
                    <div>
                      <strong>{s.name}</strong> <span className="muted">· {categoryName(s.category)}</span> {!s.isActive && <Badge>{t('common.inactive')}</Badge>}
                    </div>
                    {manage && (
                      <button type="button" className="btn btn--ghost btn--small" onClick={() => setEditing(s)}>
                        {t('common.edit')}
                      </button>
                    )}
                  </div>
                  <div className="table-wrap">
                    <table className="table table--compact">
                      <thead>
                        <tr>
                          <th scope="col">{t('common.branch')}</th>
                          <th scope="col">{t('clinic.price')}</th>
                          <th scope="col">{t('clinic.duration')}</th>
                          <th scope="col">{t('common.status')}</th>
                          {manage && <th scope="col">{t('common.actions')}</th>}
                        </tr>
                      </thead>
                      <tbody>
                        {branches.map((b) => {
                          const o = s.offers.find((x) => x.branchId === b.id)
                          return (
                            <tr key={b.id}>
                              <th scope="row">{b.name}</th>
                              <td>{o ? `${o.priceKind === 'From' ? t('clinic.priceFrom', { price: formatMoney(o.price, locale) }) : formatMoney(o.price, locale)}` : '—'}</td>
                              <td>{o ? t('common.minutes', { minutes: o.durationMinutes }) : '—'}</td>
                              <td>{o ? <Badge tone={o.isActive ? 'success' : 'neutral'}>{t(o.isActive ? 'common.active' : 'common.inactive')}</Badge> : <span className="muted">{t('svc.notOffered')}</span>}</td>
                              {manage && (
                                <td>
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
              ))}
            </ul>
          )
        }
      </QueryView>
      {editing && (
        <ServiceDialog
          service={editing === 'new' ? null : editing}
          categories={categories}
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

function ServiceDialog({ service, categories, onDone, onClose }: { service: ServiceDto | null; categories: { key: string; name: string }[]; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [name, setName] = useState(service?.name ?? '')
  const [category, setCategory] = useState(service?.category ?? '')
  const [isActive, setActive] = useState(service?.isActive ?? true)
  const [touched, setTouched] = useState(false)
  const save = useAction((body: ServiceBody) => (service ? api.clinic.updateService(clinicId, service.id, body) : api.clinic.createService(clinicId, body)))
  const errors = { name: !name.trim() ? t('valid.required') : null, category: !category.trim() ? t('valid.required') : null }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.name || errors.category) return
    const r = await save.run({ name: name.trim(), category: category.trim(), isActive })
    if (r.ok) onDone()
  }

  return (
    <Dialog title={service ? t('svc.edit') : t('svc.add')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('common.name')} error={touched ? errors.name : null}>
          {(p) => <input {...p} maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />}
        </Field>
        <Field label={t('home.category')} error={touched ? errors.category : null}>
          {(p) =>
            categories.length > 0 ? (
              <select {...p} value={category} onChange={(e) => setCategory(e.target.value)}>
                <option value="">{t('svc.chooseCategory')}</option>
                {categories.map((c) => (
                  <option key={c.key} value={c.key}>
                    {c.name}
                  </option>
                ))}
              </select>
            ) : (
              <input {...p} maxLength={60} value={category} onChange={(e) => setCategory(e.target.value)} />
            )
          }
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
              <select {...p} value={priceKind} onChange={(e) => setKind(e.target.value as PriceKind)}>
                <option value="Fixed">{t('svc.kindFixed')}</option>
                <option value="From">{t('svc.kindFrom')}</option>
              </select>
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

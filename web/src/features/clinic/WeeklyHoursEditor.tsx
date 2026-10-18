import { weekdays } from '../../api/types'
import { useI18n } from '../../i18n/context'
import { hasOverlap, intervalError, type Interval } from './hours'

/** Weekly template editor (branch-local wall-clock times), several intervals per day allowed. */
export function WeeklyHoursEditor({ value, onChange, legend, showErrors }: { value: Interval[]; onChange: (next: Interval[]) => void; legend: string; showErrors: boolean }) {
  const { t } = useI18n()
  const update = (index: number, patch: Partial<Interval>) => onChange(value.map((v, i) => (i === index ? { ...v, ...patch } : v)))

  return (
    <fieldset className="fieldset hours-editor">
      <legend>{legend}</legend>
      {weekdays.map((d) => {
        const rows = value.map((v, index) => ({ v, index })).filter((r) => r.v.weekday === d)
        return (
          <div key={d} className="hours-editor__day">
            <span className="hours-editor__label">{t(`weekdayLong.${d}`)}</span>
            <div className="hours-editor__rows">
              {rows.length === 0 && <span className="muted small">{t('clinic.closed')}</span>}
              {rows.map(({ v, index }) => {
                const err = showErrors ? intervalError(v) : null
                return (
                  <div key={index} className="hours-editor__row">
                    <label>
                      <span className="visually-hidden">{t('hours.start', { day: t(`weekdayLong.${d}`) })}</span>
                      <input type="time" step={900} value={v.start} aria-invalid={err ? true : undefined} onChange={(e) => update(index, { start: e.target.value })} />
                    </label>
                    <span aria-hidden="true">–</span>
                    <label>
                      <span className="visually-hidden">{t('hours.end', { day: t(`weekdayLong.${d}`) })}</span>
                      <input type="time" step={900} value={v.end} aria-invalid={err ? true : undefined} onChange={(e) => update(index, { end: e.target.value })} />
                    </label>
                    <button type="button" className="btn btn--ghost btn--small" onClick={() => onChange(value.filter((_, i) => i !== index))} aria-label={t('hours.removeInterval', { day: t(`weekdayLong.${d}`) })}>
                      ✕
                    </button>
                    {err && <span className="field__error">{t(err === 'time' ? 'valid.time' : 'valid.timeRange')}</span>}
                  </div>
                )
              })}
              <button type="button" className="btn btn--ghost btn--small" onClick={() => onChange([...value, { weekday: d, start: '09:00', end: '18:00' }])}>
                + {t('hours.addInterval')}
              </button>
            </div>
          </div>
        )
      })}
      {showErrors && hasOverlap(value) && <p className="field__error">{t('hours.overlap')}</p>}
    </fieldset>
  )
}

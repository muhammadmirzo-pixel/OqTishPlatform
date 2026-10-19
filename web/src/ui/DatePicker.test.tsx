import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18n'
import { DateField, DateTimePicker, MonthField, TimeSelect } from './DatePicker'
import { Dialog } from './Dialog'

const wrap = (ui: ReactNode) => render(<I18nProvider initial="en">{ui}</I18nProvider>)

function DateHarness({ initial = '2026-10-19', min, onChange = () => undefined }: { initial?: string; min?: string; onChange?: (v: string) => void }) {
  const [value, setValue] = useState(initial)
  return (
    <DateField
      label="Day"
      value={value}
      min={min}
      today="2026-10-19"
      onChange={(v) => {
        setValue(v)
        onChange(v)
      }}
    />
  )
}

describe('DatePicker', () => {
  it('shows the date in the app language, not the browser format', () => {
    wrap(<DateHarness />)
    expect(screen.getByRole('button', { name: 'Day' })).toHaveTextContent('19 October 2026')
  })

  it('opens on the selected day; arrows, PageDown and Enter choose a date', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<DateHarness onChange={onChange} />)
    await user.click(screen.getByRole('button', { name: 'Day' }))
    expect(screen.getByRole('dialog', { name: 'Choose a date' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '19 October 2026' })).toHaveFocus()
    await user.keyboard('{ArrowRight}{ArrowDown}')
    expect(screen.getByRole('button', { name: '27 October 2026' })).toHaveFocus()
    await user.keyboard('{PageDown}')
    expect(screen.getByText('November 2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '27 November 2026' })).toHaveFocus()
    await user.keyboard('{Enter}')
    expect(onChange).toHaveBeenLastCalledWith('2026-11-27')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByRole('button', { name: 'Day' })).toHaveFocus()
  })

  it('disables days before min and offers Today', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<DateHarness initial="2026-10-25" min="2026-10-19" onChange={onChange} />)
    await user.click(screen.getByRole('button', { name: 'Day' }))
    expect(screen.getByRole('button', { name: '18 October 2026' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Today' }))
    expect(onChange).toHaveBeenLastCalledWith('2026-10-19')
  })

  it('closes on Escape without closing the dialog around it', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    wrap(
      <Dialog title="Test" onClose={onClose}>
        <DateHarness />
      </Dialog>,
    )
    await user.click(screen.getByRole('button', { name: 'Day' }))
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Choose a date' })).toBeNull()
    expect(onClose).not.toHaveBeenCalled()
  })
})

describe('MonthPicker', () => {
  it('chooses a month with the arrows', async () => {
    const user = userEvent.setup()
    function Harness() {
      const [value, setValue] = useState('2026-10')
      return <MonthField label="Month" value={value} thisMonth="2026-10" onChange={setValue} />
    }
    wrap(<Harness />)
    const trigger = screen.getByRole('button', { name: 'Month' })
    expect(trigger).toHaveTextContent('October 2026')
    await user.click(trigger)
    await user.keyboard('{ArrowRight}{ArrowDown}{Enter}')
    expect(trigger).toHaveTextContent('February 2027')
  })
})

describe('TimeSelect and DateTimePicker', () => {
  it('offers a 15-minute grid and jumps by typed digits', async () => {
    const user = userEvent.setup()
    function Harness() {
      const [value, setValue] = useState('09:00')
      return <TimeSelect aria-label="Start" value={value} onChange={setValue} />
    }
    wrap(<Harness />)
    const box = screen.getByRole('combobox', { name: 'Start' })
    box.focus()
    await user.keyboard('14{Enter}')
    expect(box).toHaveTextContent('14:00')
    await user.click(box)
    expect(screen.getAllByRole('option')).toHaveLength(96)
  })

  it('emits a value only when both date and time are chosen, and follows an outside reset', async () => {
    const user = userEvent.setup()
    const seen: string[] = []
    function Harness() {
      const [value, setValue] = useState('')
      return (
        <>
          <DateTimePicker
            aria-label="From"
            value={value}
            onChange={(v) => {
              seen.push(v)
              setValue(v)
            }}
          />
          <button type="button" onClick={() => setValue('')}>
            reset
          </button>
        </>
      )
    }
    wrap(<Harness />)
    await user.click(screen.getByRole('button', { name: 'From' }))
    await user.click(screen.getByRole('button', { name: 'Today' }))
    expect(seen.at(-1)).toBe('')
    const time = screen.getByRole('combobox', { name: 'Choose a time' })
    time.focus()
    await user.keyboard('10{Enter}')
    expect(seen.at(-1)).toMatch(/^\d{4}-\d{2}-\d{2}T10:00$/)
    await user.click(screen.getByRole('button', { name: 'reset' }))
    expect(screen.getByRole('button', { name: 'From' })).toHaveTextContent('Choose a date')
  })
})

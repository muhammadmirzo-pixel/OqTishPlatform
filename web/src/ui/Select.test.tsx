import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../i18n/I18n'
import { Dialog } from './Dialog'
import { Select, SelectField, type SelectOption } from './Select'

const options: SelectOption[] = [
  { value: '', label: 'All' },
  { value: 'a', label: 'Alfa' },
  { value: 'b', label: 'Beta', disabled: true },
  { value: 'c', label: 'Charlie' },
  { value: 'c2', label: 'Chili' },
]

function Harness({ onChange = () => undefined }: { onChange?: (v: string) => void }) {
  const [value, setValue] = useState('')
  return (
    <SelectField
      label="Letter"
      value={value}
      onChange={(v) => {
        setValue(v)
        onChange(v)
      }}
      options={options}
    />
  )
}

describe('Select', () => {
  it('is a labelled combobox showing the current option', () => {
    render(<Harness />)
    const box = screen.getByRole('combobox', { name: 'Letter' })
    expect(box).toHaveTextContent('All')
    expect(box).toHaveAttribute('aria-expanded', 'false')
  })

  it('opens with ArrowDown, skips disabled options and chooses with Enter', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    const box = screen.getByRole('combobox', { name: 'Letter' })
    box.focus()
    await user.keyboard('{ArrowDown}')
    expect(box).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('listbox', { name: 'Letter' })).toBeInTheDocument()
    // Active starts on the selected option ("All"); two steps down skip the disabled "Beta".
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenLastCalledWith('c')
    expect(box).toHaveTextContent('Charlie')
    expect(box).toHaveAttribute('aria-expanded', 'false')
    expect(box).toHaveFocus()
  })

  it('jumps to options by typed letters and cycles on repeats', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const box = screen.getByRole('combobox', { name: 'Letter' })
    box.focus()
    await user.keyboard('c')
    expect(box).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Charlie' }).id)
    await user.keyboard('c')
    expect(box).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Chili' }).id)
  })

  it('chooses with the pointer and marks the selected option', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    await user.click(screen.getByRole('combobox', { name: 'Letter' }))
    await user.click(screen.getByRole('option', { name: 'Alfa' }))
    expect(screen.getByRole('combobox', { name: 'Letter' })).toHaveTextContent('Alfa')
    await user.click(screen.getByRole('combobox', { name: 'Letter' }))
    expect(screen.getByRole('option', { name: 'Alfa' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('option', { name: 'Beta' })).toHaveAttribute('aria-disabled', 'true')
  })

  it('closes on Escape without closing the dialog around it', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(
      <I18nProvider initial="en">
        <Dialog title="Test" onClose={onClose}>
          <Select aria-label="Pick" value="" onChange={() => undefined} options={options} />
        </Dialog>
      </I18nProvider>,
    )
    const box = screen.getByRole('combobox', { name: 'Pick' })
    await user.click(box)
    expect(box).toHaveAttribute('aria-expanded', 'true')
    await user.keyboard('{Escape}')
    expect(box).toHaveAttribute('aria-expanded', 'false')
    expect(onClose).not.toHaveBeenCalled()
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('closes when the pointer goes down outside', async () => {
    const user = userEvent.setup()
    render(
      <>
        <Harness />
        <p>outside</p>
      </>,
    )
    await user.click(screen.getByRole('combobox', { name: 'Letter' }))
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    await user.click(screen.getByText('outside'))
    expect(screen.queryByRole('listbox')).toBeNull()
  })
})

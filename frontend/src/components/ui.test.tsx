import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ActivityTimeline } from './ActivityTimeline'
import { Avatar } from './Avatar'
import { FormErrorSummary } from './FormErrorSummary'
import { KeyValueList } from './KeyValueList'
import { KpiCard } from './KpiCard'
import { Pager } from './Pager'
import { ProgressBar } from './ProgressBar'
import { SegmentedControl } from './SegmentedControl'
import { Stepper } from './Stepper'
import { FilterSelect, SearchField, Toolbar } from './Toolbar'
import { formErrorsTitle, initials, pageItems } from './uiHelpers'

describe('Stepper', () => {
  it('renders an ordered list with the current step marked and status texts', () => {
    render(
      <Stepper
        label="Priebeh ponuky"
        steps={[
          { label: 'Koncept', status: 'done', meta: '28. 10.' },
          { label: 'Odoslaná', status: 'current' },
          { label: 'Prijatá', status: 'upcoming' },
        ]}
      />,
    )
    const items = within(screen.getByRole('list', { name: 'Priebeh ponuky' })).getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(items[0]).toHaveTextContent('Koncept')
    expect(items[0]).toHaveTextContent('dokončené · 28. 10.')
    expect(items[1]).toHaveAttribute('aria-current', 'step')
    expect(items[1]).toHaveTextContent('aktuálny krok')
    expect(items[2]).not.toHaveAttribute('aria-current')
  })

  it('marks a blocked step as current', () => {
    render(<Stepper label="Revízia" steps={[{ label: 'Revízia', status: 'blocked', note: 'Revízia nevyhovela' }]} />)
    const item = screen.getByRole('listitem')
    expect(item).toHaveAttribute('aria-current', 'step')
    expect(item).toHaveTextContent('zastavené')
    expect(item).toHaveTextContent('Revízia nevyhovela')
  })
})

describe('KpiCard', () => {
  it('shows label as a link, value with unit, detail and trend', () => {
    render(
      <MemoryRouter>
        <KpiCard label="Výroba dnes" href="/monitoring" value="1 284" unit="kWh" detail="62 inštalácií" trend={{ text: '+8 % vs. včera', tone: 'success' }} />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Výroba dnes' })).toHaveAttribute('href', '/monitoring')
    expect(screen.getByText('1 284')).toBeInTheDocument()
    expect(screen.getByText('kWh')).toBeInTheDocument()
    expect(screen.getByText('62 inštalácií')).toBeInTheDocument()
    expect(screen.getByText('+8 % vs. včera')).toBeInTheDocument()
  })

  it('shows progress toward a target', () => {
    render(<KpiCard label="Performance ratio" value="0,78" target={{ value: 78, max: 82, label: 'Cieľ 0,82' }} />)
    expect(screen.getByRole('progressbar', { name: 'Cieľ 0,82' })).toHaveAttribute('aria-valuenow', '78')
  })
})

describe('ProgressBar', () => {
  it('exposes value, max and value text', () => {
    render(<ProgressBar label="Lehota" value={6} max={10} valueText="zostáva 4 dni" />)
    const bar = screen.getByRole('progressbar', { name: 'Lehota' })
    expect(bar).toHaveAttribute('aria-valuenow', '6')
    expect(bar).toHaveAttribute('aria-valuemax', '10')
    expect(bar).toHaveAttribute('aria-valuetext', 'zostáva 4 dni')
    expect(screen.getByText('zostáva 4 dni')).toBeInTheDocument()
  })

  it('caps the bar at full width when the value is over the max', () => {
    render(<ProgressBar label="Lehota" value={12} max={10} />)
    const bar = screen.getByRole('progressbar', { name: 'Lehota' })
    expect(bar).toHaveClass('progress--over')
    expect(bar.querySelector('.progress__fill')).toHaveStyle({ width: '100%' })
  })
})

describe('SegmentedControl', () => {
  function Harness({ onChange = () => {} }: { onChange?: (v: string) => void }) {
    const [value, setValue] = useState('kanban')
    return (
      <SegmentedControl
        label="Zobrazenie"
        value={value}
        onChange={(v) => {
          setValue(v)
          onChange(v)
        }}
        options={[
          { value: 'kanban', label: 'Kanban' },
          { value: 'list', label: 'Zoznam', count: 18 },
          { value: 'map', label: 'Mapa' },
        ]}
      />
    )
  }

  it('selects an option on click', async () => {
    render(<Harness />)
    await userEvent.click(screen.getByRole('radio', { name: /Zoznam/ }))
    expect(screen.getByRole('radio', { name: /Zoznam/ })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('radio', { name: 'Kanban' })).toHaveAttribute('aria-checked', 'false')
  })

  it('moves the selection with arrow keys, Home and End (roving tabindex)', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    const group = screen.getByRole('radiogroup', { name: 'Zobrazenie' })
    const kanban = within(group).getByRole('radio', { name: 'Kanban' })
    expect(kanban).toHaveAttribute('tabindex', '0')
    kanban.focus()
    await userEvent.keyboard('{ArrowRight}')
    expect(onChange).toHaveBeenLastCalledWith('list')
    expect(within(group).getByRole('radio', { name: /Zoznam/ })).toHaveFocus()
    await userEvent.keyboard('{End}')
    expect(onChange).toHaveBeenLastCalledWith('map')
    await userEvent.keyboard('{ArrowRight}')
    expect(onChange).toHaveBeenLastCalledWith('kanban')
    await userEvent.keyboard('{Home}')
    expect(within(group).getByRole('radio', { name: 'Kanban' })).toHaveFocus()
  })

  it('shows the count next to the label', () => {
    render(<Harness />)
    expect(screen.getByRole('radio', { name: /Zoznam/ })).toHaveTextContent('Zoznam18')
  })
})

describe('Toolbar', () => {
  it('renders a labelled search field and filter selects', async () => {
    const onSearch = vi.fn()
    const onFilter = vi.fn()
    render(
      <Toolbar aside={<span>3 vybrané</span>}>
        <SearchField label="Hľadať zákazníkov" value="" onChange={onSearch} placeholder="Meno, IČO…" />
        <FilterSelect
          label="Stav"
          value=""
          onChange={onFilter}
          options={[
            { value: '', label: 'Všetky' },
            { value: 'Active', label: 'Aktívni' },
          ]}
        />
      </Toolbar>,
    )
    await userEvent.type(screen.getByRole('searchbox', { name: 'Hľadať zákazníkov' }), 'N')
    expect(onSearch).toHaveBeenCalledWith('N')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Stav' }), 'Active')
    expect(onFilter).toHaveBeenCalledWith('Active')
    expect(screen.getByText('3 vybrané')).toBeInTheDocument()
  })
})

describe('Pager', () => {
  it('builds page items with ellipses around the current page', () => {
    expect(pageItems(1, 1)).toEqual([1])
    expect(pageItems(1, 10)).toEqual([1, 2, 'gap', 10])
    expect(pageItems(5, 10)).toEqual([1, 'gap', 4, 5, 6, 'gap', 10])
    expect(pageItems(10, 10)).toEqual([1, 'gap', 9, 10])
    expect(pageItems(4, 7)).toEqual([1, 2, 3, 4, 5, 6, 7])
  })

  it('shows the range summary and navigates by number and arrows', async () => {
    const onPageChange = vi.fn()
    render(<Pager page={2} pageSize={10} totalCount={35} onPageChange={onPageChange} />)
    const nav = screen.getByRole('navigation', { name: 'Stránkovanie' })
    expect(within(nav).getByText('Zobrazené 11–20 z 35')).toBeInTheDocument()
    expect(within(nav).getByRole('button', { name: 'Strana 2' })).toHaveAttribute('aria-current', 'page')
    await userEvent.click(within(nav).getByRole('button', { name: 'Strana 4' }))
    await userEvent.click(within(nav).getByRole('button', { name: 'Ďalšia' }))
    await userEvent.click(within(nav).getByRole('button', { name: 'Predchádzajúca' }))
    expect(onPageChange.mock.calls).toEqual([[4], [3], [1]])
  })

  it('disables the arrows at the edges', () => {
    render(<Pager page={1} pageSize={10} totalCount={5} onPageChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Predchádzajúca' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Ďalšia' })).toBeDisabled()
    expect(screen.getByText('Zobrazené 1–5 z 5')).toBeInTheDocument()
  })
})

describe('Avatar', () => {
  it('derives initials from names, companies and e-mails', () => {
    expect(initials('Ján Novák')).toBe('JN')
    expect(initials('Solar Agro s.r.o.')).toBe('SA')
    expect(initials('Hotel Tatra Resort a.s.')).toBe('HT')
    expect(initials('demo@solarops.local')).toBe('D')
    expect(initials('lucia.horvathova@solarops.sk')).toBe('LH')
    expect(initials('')).toBe('?')
  })

  it('is decorative so the name is not read twice', () => {
    render(<Avatar name="Ján Novák" />)
    expect(screen.getByText('JN')).toHaveAttribute('aria-hidden', 'true')
  })
})

describe('KeyValueList', () => {
  it('renders terms and definitions', () => {
    render(
      <KeyValueList
        items={[
          { label: 'Typ', value: 'Fyzická osoba' },
          { label: 'E-mail', value: 'jan.novak@gmail.com' },
        ]}
      />,
    )
    expect(screen.getAllByRole('term').map((t) => t.textContent)).toEqual(['Typ', 'E-mail'])
    expect(screen.getAllByRole('definition').map((d) => d.textContent)).toEqual(['Fyzická osoba', 'jan.novak@gmail.com'])
  })
})

describe('ActivityTimeline', () => {
  it('renders a labelled list of events', () => {
    render(
      <ActivityTimeline
        label="Aktivita"
        items={[
          { id: '1', title: 'Ponuka odoslaná', meta: 'Lucia Horváthová · dnes 09:12' },
          { id: '2', title: 'Zákazník vytvorený' },
        ]}
      />,
    )
    const items = within(screen.getByRole('list', { name: 'Aktivita' })).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Ponuka odoslaná')
    expect(items[0]).toHaveTextContent('Lucia Horváthová · dnes 09:12')
  })
})

describe('FormErrorSummary', () => {
  it('uses the right Slovak plural', () => {
    expect(formErrorsTitle(1)).toBe('Formulár obsahuje 1 chybu. Opravte ju a odošlite znova.')
    expect(formErrorsTitle(2)).toBe('Formulár obsahuje 2 chyby. Opravte ich a odošlite znova.')
    expect(formErrorsTitle(5)).toBe('Formulár obsahuje 5 chýb. Opravte ich a odošlite znova.')
  })

  it('announces the errors and renders nothing without errors', () => {
    const { rerender, container } = render(<FormErrorSummary errors={[]} />)
    expect(container).toBeEmptyDOMElement()
    rerender(<FormErrorSummary errors={['IČO musí mať 8 číslic.', 'Vyberte distribučnú spoločnosť.']} />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Formulár obsahuje 2 chyby.')
    expect(within(alert).getAllByRole('listitem')).toHaveLength(2)
  })
})

describe('ConfirmDialog', () => {
  it('shows a decorative warning mark for destructive confirmations', async () => {
    const { ConfirmDialog } = await import('./ConfirmDialog')
    render(<ConfirmDialog title="Zmazať odberné miesto?" message="Túto akciu nie je možné vrátiť." danger onConfirm={() => {}} onCancel={() => {}} />)
    const dialog = screen.getByRole('dialog', { name: 'Zmazať odberné miesto?' })
    expect(within(dialog).getByText('!')).toHaveAttribute('aria-hidden', 'true')
  })
})

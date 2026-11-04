import { useState, type ReactNode } from 'react'
import { ActivityTimeline } from '../components/ActivityTimeline'
import { Avatar } from '../components/Avatar'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { EmptyState } from '../components/EmptyState'
import { FormErrorSummary } from '../components/FormErrorSummary'
import { FormField } from '../components/forms'
import { KeyValueList } from '../components/KeyValueList'
import { KpiCard } from '../components/KpiCard'
import { Pager } from '../components/Pager'
import { ProgressBar } from '../components/ProgressBar'
import { SegmentedControl } from '../components/SegmentedControl'
import { Skeleton } from '../components/Skeleton'
import { Stepper } from '../components/Stepper'
import { FilterSelect, SearchField, Toolbar } from '../components/Toolbar'
import { ProductionAreaChart } from '../components/charts/ProductionAreaChart'
import type { SeriesPoint } from '../components/charts/chartGeometry'
import './showcase.css'

/** Sample day curve: 6:00–20:00 every 30 min, with a dip between 11:00 and 14:00 (like the landing page). */
function sampleDay(dip: boolean): SeriesPoint[] {
  return Array.from({ length: 29 }, (_, i) => {
    const hour = 6 + i / 2
    const predicted = Math.max(0, 6.2 * Math.sin((Math.PI * (hour - 6)) / 14))
    const actual = dip && hour >= 11 && hour <= 14 ? predicted * 0.66 : predicted * (0.97 + ((i * 7) % 5) / 100)
    return { label: `${Math.floor(hour)}:${hour % 1 ? '30' : '00'}`, actual: Math.round(actual * 100) / 100, predicted: Math.round(predicted * 100) / 100 }
  })
}

const SunIcon = (
  <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
    <circle cx="8" cy="8" r="3" />
    <path d="M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1 1M11.6 11.6l1 1M3.4 12.6l1-1M11.6 4.4l1-1" />
  </svg>
)

function Section({ code, title, children }: { code: string; title: string; children: ReactNode }) {
  return (
    <section className="showcase__section" aria-labelledby={`sc-${code}`}>
      <h3 id={`sc-${code}`} className="showcase__label">
        {code} · {title}
      </h3>
      {children}
    </section>
  )
}

/** Dev-only overview of the shared components (wireframe "Knižnica komponentov" K-01 to K-10). Route: /dev/ui. */
export default function UiShowcase() {
  const [view, setView] = useState<'kanban' | 'list'>('kanban')
  const [period, setPeriod] = useState('month')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [confirm, setConfirm] = useState(false)

  return (
    <div className="showcase">
      <header className="page-header">
        <h2 className="page-header__title">Knižnica komponentov</h2>
        <p className="page-header__subtitle">Znovupoužiteľné prvky · K-01 až K-10 · len v dev režime</p>
      </header>

      <Section code="K-01" title="Toolbar, tabuľka a stránkovanie">
        <Toolbar aside={<span>3 vybrané</span>}>
          <SearchField label="Hľadať" value={search} onChange={setSearch} placeholder="Hľadať…" />
          <FilterSelect
            label="Stav"
            value={status}
            onChange={setStatus}
            options={[
              { value: '', label: 'Všetky' },
              { value: 'a', label: 'Aktívne' },
            ]}
          />
        </Toolbar>
        <div className="data-table__scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">Názov</th>
                <th scope="col">Stav</th>
                <th scope="col">Dátum</th>
                <th scope="col">Hodnota</th>
              </tr>
            </thead>
            <tbody>
              {[
                ['Ján Novák', 'Aktívny', 'success', '4. 11. 2026', '11 480 €'],
                ['Hotel Tatra Resort a.s.', 'Na schválenie', 'attention', '3. 11. 2026', '74 900 €'],
                ['Mária Kollárová', 'Koncept', 'muted', '2. 11. 2026', '8 150 €'],
              ].map(([name, label, tone, date, value]) => (
                <tr key={name}>
                  <td>
                    <span className="showcase__person">
                      <Avatar name={name} size="sm" />
                      <strong>{name}</strong>
                    </span>
                  </td>
                  <td>
                    <span className={`badge badge--${tone}`}>{label}</span>
                  </td>
                  <td>{date}</td>
                  <td>{value}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pager page={page} pageSize={10} totalCount={248} onPageChange={setPage} />
      </Section>

      <div className="showcase__grid">
        <Section code="K-02" title="KPI karty">
          <div className="kpi-grid">
            <KpiCard icon={SunIcon} label="Výroba dnes" value="1 284" unit="kWh" detail="62 inštalácií · stav k 11:00" trend={{ text: '+8 % vs. včera', tone: 'success' }} />
            <KpiCard label="Performance ratio" value="0,78" detail="cieľ ≥ 0,82" target={{ value: 78, max: 82, label: 'Plnenie cieľa' }} />
          </div>
        </Section>

        <Section code="K-03" title="Stavové štítky">
          <div className="card showcase__badges">
            <span className="badge badge--info">V riešení</span>
            <span className="badge badge--success">Prijatá</span>
            <span className="badge badge--error">Kritický</span>
            <span className="badge badge--warning">Blíži sa lehota</span>
            <span className="badge badge--attention">Na schválenie</span>
            <span className="badge badge--muted">Neaktívny</span>
            <span className="badge">Odoslaná</span>
            <p className="showcase__note">
              Farba = význam (úspech, info, upozornenie, chyba), obrys = vyžaduje akciu, prerušovaný = neaktívne. Bodka a text, nie len farba.
            </p>
          </div>
        </Section>
      </div>

      <Section code="K-04" title="Stepper a progress">
        <div className="card showcase__stack">
          <Stepper
            label="Workflow inštalácie"
            steps={[
              { label: 'Naplánovaná', status: 'done', meta: '15. 10.' },
              { label: 'Montáž', status: 'done', meta: '4. 11.' },
              { label: 'Revízia', status: 'current', meta: 'prebieha' },
              { label: 'Pripojenie DS', status: 'upcoming' },
              { label: 'Uvedenie do prevádzky', status: 'upcoming' },
              { label: 'Odovzdaná', status: 'upcoming' },
            ]}
          />
          <div className="showcase__progress">
            <ProgressBar label="Lehota ZP-0158" value={60} valueText="zostáva 12 dní" hideLabel={false} />
            <ProgressBar label="Lehota ZP-0151" value={115} valueText="po lehote 4 dni" hideLabel={false} />
          </div>
        </div>
      </Section>

      <div className="showcase__grid">
        <Section code="K-05" title="Prepínače">
          <div className="card showcase__stack">
            <SegmentedControl
              label="Zobrazenie tiketov"
              value={view}
              onChange={setView}
              options={[
                { value: 'kanban', label: 'Kanban' },
                { value: 'list', label: 'Zoznam', count: 18 },
              ]}
            />
            <SegmentedControl
              label="Obdobie"
              size="sm"
              value={period}
              onChange={setPeriod}
              options={[
                { value: 'day', label: 'Deň' },
                { value: 'week', label: 'Týždeň' },
                { value: 'month', label: 'Mesiac' },
                { value: 'year', label: 'Rok' },
              ]}
            />
          </div>
        </Section>

        <Section code="K-06" title="Formulár s chybami">
          <div className="card">
            <FormErrorSummary errors={['IČO musí mať 8 číslic.', 'Vyberte distribučnú spoločnosť.']} />
            <FormField label="IČO" errors={['IČO musí mať 8 číslic.']}>
              {(props) => <input {...props} defaultValue="4512" />}
            </FormField>
            <FormField label="Telefón">{(props) => <input {...props} defaultValue="+421 905 123 456" />}</FormField>
            <div className="btn-row">
              <button type="button" className="btn btn--ghost">
                Zrušiť
              </button>
              <button type="button" className="btn btn--primary" aria-busy="true">
                Ukladám…
              </button>
            </div>
          </div>
        </Section>
      </div>

      <div className="showcase__grid">
        <Section code="K-07" title="Údaje a aktivita">
          <div className="card showcase__stack">
            <KeyValueList
              items={[
                { label: 'Typ', value: 'Fyzická osoba' },
                { label: 'E-mail', value: 'jan.novak@gmail.com' },
                { label: 'Fakturačná adresa', value: 'Hlboká 12, 010 01 Žilina' },
              ]}
            />
            <ActivityTimeline
              label="Aktivita"
              items={[
                { id: '1', title: 'Ponuka CP-2026-0412 odoslaná na interné schválenie', meta: 'Lucia Horváthová · dnes 09:12' },
                { id: '2', title: 'Obhliadka strechy dokončená', meta: 'Peter Baláž · 30. 10. 2026' },
                { id: '3', title: 'Zákazník vytvorený z webového formulára', meta: 'Systém · 12. 10. 2026' },
              ]}
            />
          </div>
        </Section>

        <Section code="K-08" title="Prázdny stav, načítanie a dialóg">
          <div className="showcase__stack">
            <EmptyState
              title="Zatiaľ žiadne servisné tikety"
              description="Keď zákazník nahlási poruchu alebo monitoring vytvorí alarm, tiket sa zobrazí tu."
              action={
                <button type="button" className="btn btn--primary" onClick={() => setConfirm(true)}>
                  Otvoriť dialóg
                </button>
              }
            />
            <div className="card">
              <Skeleton lines={3} label="Načítavam…" />
            </div>
          </div>
        </Section>
      </div>

      <Section code="K-09" title="Graf výroby (svetlý)">
        <div className="card">
          <ProductionAreaChart title="Výroba dnes · RD Novák, Košice" unit="kW" points={sampleDay(true)} summary="Výroba bola od 11:00 do 14:00 pod 85 % predikcie." />
        </div>
      </Section>

      <Section code="K-10" title="Graf výroby (tmavý, landing page)">
        <div className="showcase__dark">
          <ProductionAreaChart title="Výroba dnes · RD Novák, Košice" unit="kW" variant="dark" points={sampleDay(true)} />
        </div>
      </Section>

      {confirm && (
        <ConfirmDialog
          title="Zmazať odberné miesto?"
          message="EIC 24ZSS4198765432B bude odstránené. Túto akciu nie je možné vrátiť."
          confirmLabel="Zmazať"
          danger
          onConfirm={() => setConfirm(false)}
          onCancel={() => setConfirm(false)}
        />
      )}
    </div>
  )
}

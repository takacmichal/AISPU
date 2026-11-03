import './quotes.css'
import { useCallback, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { problemToApiError, type FieldErrors } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { EmptyState } from '../../components/EmptyState'
import { FormField } from '../../components/forms'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { formatCurrency, formatNumber } from '../../lib/format'
import {
  MAX_LINES,
  DEFAULT_VAT,
  calculateTotals,
  compatibilityWarnings,
  lineNet,
  parseQuantity,
  toRequest,
  validateDraft,
  type DraftLine,
} from './quoteForm'
import { TEXT, TYPE_LABELS, UNIT_LABELS, WRITE_ROLES, type CatalogComponent } from './quoteLabels'
import { useCustomerOptions } from './useCustomerNames'

let nextKey = 1

/** Quote configurator (route /quotes/new): pick components from the catalog, see totals, create a draft. */
export default function QuoteConfigurator() {
  const { roles } = useAuth()
  if (!roles.some((r) => WRITE_ROLES.includes(r)))
    return (
      <p role="alert" className="alert">
        Na vytváranie ponúk nemáte oprávnenie.
      </p>
    )
  return <Configurator />
}

function Configurator() {
  const navigate = useNavigate()
  const toast = useToast()
  const [params] = useSearchParams()
  const { customers, loading: customersLoading } = useCustomerOptions()
  const catalog = useLoad<CatalogComponent[]>(async (signal) => {
    const { data, response } = await api.GET('/api/components', { params: { query: { active: true } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'catalog')

  const [customerId, setCustomerId] = useState(params.get('customerId') ?? '')
  const [vatRate, setVatRate] = useState(String(DEFAULT_VAT))
  const [note, setNote] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([])
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const [typeFilter, setTypeFilter] = useState('')
  const [pickedId, setPickedId] = useState('')
  const [pickedQty, setPickedQty] = useState('1')

  const components = (catalog.data ?? []).filter((c) => !typeFilter || c.type === typeFilter)
  const totals = calculateTotals(lines, parseQuantity(vatRate))
  const warnings = compatibilityWarnings(lines)

  const save = useCallback(async () => {
    const result = await api.POST('/api/quotes', { body: toRequest({ customerId, vatRate, note, lines }) })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    toast.success(TEXT.createSuccess)
    navigate(`/quotes/${result.data.id}`)
  }, [customerId, vatRate, note, lines, toast, navigate])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  function clear(field: string) {
    setClientErrors((current) => {
      const rest = { ...current }
      delete rest[field]
      return rest
    })
    clearFieldError(field)
  }

  const pickedComponent = components.find((c) => c.id === pickedId)
  const pickError = errorsFor('picked')

  function addLine() {
    if (!pickedComponent) {
      setClientErrors((c) => ({ ...c, picked: ['Vyberte komponent z katalógu.'] }))
      return
    }
    if (lines.length >= MAX_LINES) {
      setClientErrors((c) => ({ ...c, picked: [`Ponuka môže mať najviac ${MAX_LINES} položiek.`] }))
      return
    }
    clear('picked')
    clear('lines')
    const quantity = parseQuantity(pickedQty)
    setLines((current) => {
      const existing = current.find((l) => l.component.id === pickedComponent.id)
      if (existing && Number.isFinite(quantity))
        return current.map((l) => (l === existing ? { ...l, quantity: String(parseQuantity(l.quantity) + quantity) } : l))
      return [...current, { key: `l${nextKey++}`, component: pickedComponent, quantity: pickedQty }]
    })
    setPickedId('')
    setPickedQty('1')
  }

  function removeLine(key: string) {
    setLines((current) => current.filter((l) => l.key !== key))
    setClientErrors({})
  }

  function setQuantity(key: string, quantity: string) {
    setLines((current) => current.map((l) => (l.key === key ? { ...l, quantity } : l)))
    setClientErrors((current) => Object.fromEntries(Object.entries(current).filter(([k]) => !k.startsWith('lines['))))
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateDraft({ customerId, vatRate, note, lines })
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  if (catalog.loading || customersLoading) return <Skeleton lines={6} label="Načítavam katalóg a zákazníkov…" />
  if (catalog.error)
    return (
      <p role="alert" className="alert">
        Katalóg komponentov sa nepodarilo načítať.
      </p>
    )

  return (
    <section>
      <h2>{TEXT.newQuote}</h2>
      <form onSubmit={onSubmit} noValidate>
        <FormField label="Zákazník" errors={errorsFor('customerId')}>
          {(props) => (
            <select {...props} value={customerId} onChange={(e) => { setCustomerId(e.target.value); clear('customerId') }}>
              <option value="">Vyberte zákazníka</option>
              {customers
                .filter((c) => c.active || c.id === customerId)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
            </select>
          )}
        </FormField>

        <fieldset>
          <legend>Položky ponuky</legend>
          <div className="quote-picker">
            <label>
              Typ
              <select value={typeFilter} onChange={(e) => { setTypeFilter(e.target.value); setPickedId('') }}>
                <option value="">{TEXT.all}</option>
                {Object.entries(TYPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            <label className="quote-picker__component">
              Komponent z katalógu
              <select
                value={pickedId}
                aria-invalid={pickError.length > 0}
                onChange={(e) => { setPickedId(e.target.value); clear('picked') }}
              >
                <option value="">Vyberte komponent</option>
                {components.map((c) => (
                  <option key={c.id} value={c.id}>
                    {`${TYPE_LABELS[c.type] ?? c.type}: ${c.code} – ${c.name} (${formatCurrency(c.unitPrice, c.currency)})`}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {`Množstvo${pickedComponent ? ` (${UNIT_LABELS[pickedComponent.type] ?? ''})` : ''}`}
              <input type="text" inputMode="decimal" value={pickedQty} onChange={(e) => setPickedQty(e.target.value)} />
            </label>
            <button type="button" className="btn btn--ghost" onClick={addLine}>
              Pridať položku
            </button>
          </div>
          {pickError.length > 0 && (
            <p role="alert" className="form-field__error">
              {pickError.join(' ')}
            </p>
          )}

          {lines.length === 0 ? (
            <EmptyState title="Ponuka zatiaľ nemá žiadne položky." description="Vyberte komponent z katalógu a pridajte ho." />
          ) : (
            <div className="data-table__scroll">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Komponent</th>
                    <th scope="col">Množstvo</th>
                    <th scope="col" className="quote-num">Jednotková cena</th>
                    <th scope="col" className="quote-num">Spolu</th>
                    <th scope="col">
                      <span className="sr-only">Akcie</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, index) => {
                    const lineErrors = errorsFor(`lines[${index}].quantity`)
                    const errorId = `qty-error-${line.key}`
                    return (
                      <tr key={line.key}>
                        <td>{`${line.component.code} – ${line.component.name}`}</td>
                        <td>
                          <input
                            className="quote-qty"
                            type="text"
                            inputMode="decimal"
                            aria-label={`Množstvo: ${line.component.name}`}
                            aria-invalid={lineErrors.length > 0}
                            aria-describedby={lineErrors.length > 0 ? errorId : undefined}
                            value={line.quantity}
                            onChange={(e) => setQuantity(line.key, e.target.value)}
                          />{' '}
                          {UNIT_LABELS[line.component.type]}
                          {lineErrors.length > 0 && (
                            <p id={errorId} role="alert" className="form-field__error">
                              {lineErrors.join(' ')}
                            </p>
                          )}
                        </td>
                        <td className="quote-num">{formatCurrency(line.component.unitPrice, line.component.currency)}</td>
                        <td className="quote-num">{formatCurrency(lineNet(line))}</td>
                        <td>
                          <button
                            type="button"
                            className="btn btn--ghost btn--sm"
                            aria-label={`Odobrať položku ${line.component.name}`}
                            onClick={() => removeLine(line.key)}
                          >
                            Odobrať
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
          {errorsFor('lines').length > 0 && (
            <p role="alert" className="form-field__error">
              {errorsFor('lines').join(' ')}
            </p>
          )}
          {warnings.map((warning) => (
            <p key={warning} role="status" className="card--subtle">
              {warning}
            </p>
          ))}
        </fieldset>

        <FormField label="Sadzba DPH (%)" errors={errorsFor('vatRatePercent')}>
          {(props) => (
            <input {...props} type="text" inputMode="decimal" value={vatRate} onChange={(e) => { setVatRate(e.target.value); clear('vatRatePercent') }} />
          )}
        </FormField>
        <FormField label="Poznámka" errors={errorsFor('note')}>
          {(props) => <textarea {...props} value={note} onChange={(e) => { setNote(e.target.value); clear('note') }} />}
        </FormField>

        <dl className="card quote-totals" role="group" aria-label="Súčet ponuky">
          <div>
            <dt>{TEXT.net}</dt>
            <dd>{formatCurrency(totals.net)}</dd>
          </div>
          <div>
            <dt>{`${TEXT.vat} (${formatNumber(parseQuantity(vatRate) || 0)} %)`}</dt>
            <dd>{formatCurrency(totals.vat)}</dd>
          </div>
          <div className="quote-totals__gross">
            <dt>{TEXT.gross}</dt>
            <dd>{formatCurrency(totals.gross)}</dd>
          </div>
        </dl>

        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="btn-row">
          <button type="submit" className="btn btn--primary" disabled={busy}>
            Vytvoriť ponuku
          </button>
          <button type="button" className="btn btn--ghost" onClick={() => navigate('/quotes')}>
            Zrušiť
          </button>
        </div>
      </form>
    </section>
  )
}

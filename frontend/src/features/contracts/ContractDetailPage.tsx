import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError, userMessage, type FieldErrors } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { Dialog } from '../../components/Dialog'
import { EmptyState } from '../../components/EmptyState'
import { FormField } from '../../components/forms'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { MESSAGES } from '../../lib/messages'
import { formatCurrency, formatDate, formatDateTime, formatNumber } from '../../lib/format'
import { ConnectionStepper } from '../connection/ConnectionStepper'
import './contracts.css'
import { parseAmount } from './amount'
import { fetchContractPdf, PdfUnavailableError } from './pdf'
import { AUDIT_ACTION_LABELS, CONTRACT_STATUS_LABELS, CONTRACT_TEXTS as T, DEPOSIT_STATUS_LABELS } from './texts'

type Contract = components['schemas']['ContractResponse']

const WRITE_ROLES = ['Admin', 'Sales']

/** Today as yyyy-mm-dd in local time (value of an <input type="date">). */
function today(): string {
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

type DialogKind = 'sign' | 'deposit' | null

export default function ContractDetailPage() {
  const { id = '' } = useParams()
  const { roles } = useAuth()
  const canWrite = roles.some((role) => WRITE_ROLES.includes(role))
  const [dialog, setDialog] = useState<DialogKind>(null)
  const toast = useToast()

  const contractLoad = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/contracts/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)
  const auditLoad = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/contracts/{id}/audit', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)

  const contract = contractLoad.data
  if (contractLoad.loading) return <Skeleton lines={5} label={T.loading} />
  if (contractLoad.error || !contract)
    return (
      <p role="alert" className="alert">
        {T.loadError}
      </p>
    )

  function saved(message: string) {
    setDialog(null)
    toast.success(message)
    contractLoad.reload()
    auditLoad.reload()
  }

  const signed = contract.status === 'Signed'
  const paid = contract.depositStatus === 'Paid'

  return (
    <section>
      <p>
        <Link to="/contracts">{T.back}</Link>
      </p>
      <div className="toolbar">
        <h2>{T.detail.title(contract.number)}</h2>
        <span>
          <span className="badge badge--info">{CONTRACT_STATUS_LABELS[contract.status] ?? contract.status}</span>{' '}
          <span className={paid ? 'badge badge--success' : 'badge'}>
            {T.detail.deposit}: {DEPOSIT_STATUS_LABELS[contract.depositStatus] ?? contract.depositStatus}
          </span>
        </span>
      </div>

      {canWrite && !signed && (
        <p>
          <button type="button" className="btn btn--primary" onClick={() => setDialog('sign')}>
            {T.sign.open}
          </button>
        </p>
      )}
      {canWrite && signed && !paid && (
        <p>
          <button type="button" className="btn btn--primary" onClick={() => setDialog('deposit')}>
            {T.depositForm.open}
          </button>
        </p>
      )}

      <div className="contract-section">
        <h3>{T.detail.data}</h3>
        <dl className="card">
          <Row label={T.detail.signedOn} value={contract.signedOn ? formatDate(contract.signedOn) : null} />
          <Row label={T.detail.depositPaid} value={paid ? formatCurrency(contract.depositPaidAmount, contract.currency) : null} />
          <Row label={T.detail.depositPaidOn} value={contract.depositPaidOn ? formatDate(contract.depositPaidOn) : null} />
          <Row label={T.detail.netTotal} value={formatCurrency(contract.netTotal, contract.currency)} />
          <Row label={T.detail.vatTotal(formatNumber(contract.vatRatePercent))} value={formatCurrency(contract.vatTotal, contract.currency)} />
          <Row label={T.detail.grossTotal} value={formatCurrency(contract.grossTotal, contract.currency)} />
        </dl>
        {signed && contract.installationId && (
          <p>
            {paid ? T.detail.installationUnlocked : T.detail.installationLocked}{' '}
            <Link to={`/installations/${contract.installationId}`}>{T.detail.installationLink}</Link>
          </p>
        )}
      </div>

      <div className="contract-section">
        <h3>{T.detail.lines}</h3>
        <div className="data-table__scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{T.detail.lineColumns.code}</th>
                <th scope="col">{T.detail.lineColumns.name}</th>
                <th scope="col">{T.detail.lineColumns.unitPrice}</th>
                <th scope="col">{T.detail.lineColumns.quantity}</th>
                <th scope="col">{T.detail.lineColumns.netPrice}</th>
              </tr>
            </thead>
            <tbody>
              {contract.lines.map((line) => (
                <tr key={line.position}>
                  <td className="mono">{line.code}</td>
                  <td>{line.name}</td>
                  <td className="contract-num">{formatCurrency(line.unitPrice, contract.currency)}</td>
                  <td className="contract-num">{formatNumber(line.quantity)}</td>
                  <td className="contract-num">{formatCurrency(line.netPrice, contract.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <PdfSection contractId={contract.id} number={contract.number} />

      {signed && contract.installationId && (
        <div className="contract-section">
          <h3>{T.detail.connection}</h3>
          <ConnectionStepper contractId={contract.id} />
        </div>
      )}

      <div className="contract-section">
        <h3>{T.detail.audit}</h3>
        {auditLoad.loading && <Skeleton lines={2} />}
        {auditLoad.error && (
          <p role="alert" className="alert">
            {T.detail.auditError}
          </p>
        )}
        {auditLoad.data && auditLoad.data.length === 0 && <EmptyState title={T.detail.auditEmpty} />}
        {auditLoad.data && auditLoad.data.length > 0 && (
          <ul className="contract-audit">
            {auditLoad.data.map((entry) => (
              <li key={`${entry.action}-${entry.performedAt}`}>
                <time dateTime={entry.performedAt}>{formatDateTime(entry.performedAt)}</time>
                <span>{AUDIT_ACTION_LABELS[entry.action] ?? entry.action}</span>
                {entry.details && <span>{entry.details}</span>}
              </li>
            ))}
          </ul>
        )}
      </div>

      {dialog === 'sign' && (
        <SignDialog contract={contract} onClose={() => setDialog(null)} onSaved={() => saved(T.sign.success)} />
      )}
      {dialog === 'deposit' && (
        <DepositDialog contract={contract} onClose={() => setDialog(null)} onSaved={() => saved(T.depositForm.success)} />
      )}
    </section>
  )
}

function Row({ label, value }: { label: string; value: string | null }) {
  if (!value) return null
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

type PdfState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ready'; url: string }
  | { kind: 'unavailable' }
  | { kind: 'error'; message: string }

/**
 * PDF preview (iframe) and download. The endpoint may not exist on the server yet (PR #341); then the buttons are
 * disabled with a hint instead of failing.
 */
function PdfSection({ contractId, number }: { contractId: string; number: string }) {
  const [state, setState] = useState<PdfState>({ kind: 'idle' })
  const urlRef = useRef<string | null>(null)

  useEffect(
    () => () => {
      if (urlRef.current) URL.revokeObjectURL(urlRef.current)
    },
    [],
  )

  async function load() {
    setState({ kind: 'loading' })
    try {
      const blob = await fetchContractPdf(contractId)
      if (urlRef.current) URL.revokeObjectURL(urlRef.current)
      urlRef.current = URL.createObjectURL(blob)
      setState({ kind: 'ready', url: urlRef.current })
    } catch (error) {
      setState(error instanceof PdfUnavailableError ? { kind: 'unavailable' } : { kind: 'error', message: userMessage(error) })
    }
  }

  const unavailable = state.kind === 'unavailable'
  return (
    <div className="contract-section">
      <h3>{T.pdf.title}</h3>
      <div className="btn-row">
        <button type="button" className="btn btn--ghost" disabled={unavailable || state.kind === 'loading'} onClick={load}>
          {state.kind === 'loading' ? T.pdf.loading : T.pdf.preview}
        </button>
        {state.kind === 'ready' ? (
          <a className="btn btn--ghost" href={state.url} download={T.pdf.fileName(number)}>
            {T.pdf.download}
          </a>
        ) : (
          <button type="button" className="btn btn--ghost" disabled>
            {T.pdf.download}
          </button>
        )}
      </div>
      {unavailable && <p className="contract-hint">{T.pdf.unavailable}</p>}
      {state.kind === 'error' && (
        <p role="alert" className="form-error">
          {T.pdf.failed} {state.message}
        </p>
      )}
      {state.kind === 'ready' && <iframe className="contract-pdf__frame" title={T.pdf.frameTitle} src={state.url} />}
    </div>
  )
}

function SignDialog({ contract, onClose, onSaved }: { contract: Contract; onClose: () => void; onSaved: () => void }) {
  const [supplyPointId, setSupplyPointId] = useState('')
  const [signedOn, setSignedOn] = useState(today())
  const [installationName, setInstallationName] = useState('')
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const supplyPoints = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/customers/{customerId}/supply-points', {
      params: { path: { customerId: contract.customerId } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, contract.customerId)

  const save = useCallback(async () => {
    const { data, response, error } = await api.POST('/api/contracts/{id}/sign', {
      params: { path: { id: contract.id } },
      body: { supplyPointId, signedOn: signedOn || undefined, installationName: installationName.trim() || undefined },
    })
    if (!data) throw problemToApiError(response.status, error)
    onSaved()
  }, [contract.id, supplyPointId, signedOn, installationName, onSaved])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  function touch(field: string) {
    setClientErrors((current) => ({ ...current, [field]: [] }))
    clearFieldError(field)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors: FieldErrors = {}
    if (!supplyPointId) errors.supplyPointId = [T.sign.required]
    if (signedOn > today()) errors.signedOn = [T.sign.future]
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  return (
    <Dialog title={T.sign.title} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        {supplyPoints.loading && <Skeleton lines={2} />}
        {supplyPoints.error && (
          <p role="alert" className="form-error">
            {T.sign.supplyPointsError}
          </p>
        )}
        {supplyPoints.data && supplyPoints.data.length === 0 && <p className="form-error">{T.sign.noSupplyPoints}</p>}
        <FormField label={T.sign.supplyPoint} errors={errorsFor('supplyPointId')}>
          {(props) => (
            <select
              {...props}
              value={supplyPointId}
              onChange={(e) => {
                setSupplyPointId(e.target.value)
                touch('supplyPointId')
              }}
            >
              <option value="">{T.sign.supplyPointPlaceholder}</option>
              {(supplyPoints.data ?? []).map((sp) => (
                <option key={sp.id} value={sp.id}>
                  {`${sp.name ?? sp.address.street} (${sp.eicCode})`}
                </option>
              ))}
            </select>
          )}
        </FormField>
        <FormField label={T.sign.signedOn} errors={errorsFor('signedOn')}>
          {(props) => (
            <input
              {...props}
              type="date"
              max={today()}
              value={signedOn}
              onChange={(e) => {
                setSignedOn(e.target.value)
                touch('signedOn')
              }}
            />
          )}
        </FormField>
        <FormField label={T.sign.installationName} errors={errorsFor('installationName')}>
          {(props) => (
            <input
              {...props}
              type="text"
              value={installationName}
              onChange={(e) => {
                setInstallationName(e.target.value)
                touch('installationName')
              }}
            />
          )}
        </FormField>
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="dialog__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {MESSAGES.cancel}
          </button>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {T.sign.submit}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

function DepositDialog({ contract, onClose, onSaved }: { contract: Contract; onClose: () => void; onSaved: () => void }) {
  const [amount, setAmount] = useState('')
  const [paidOn, setPaidOn] = useState(today())
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})
  const total = Number(contract.grossTotal)

  const save = useCallback(async () => {
    const { data, response, error } = await api.POST('/api/contracts/{id}/deposit/pay', {
      params: { path: { id: contract.id } },
      body: { amount: parseAmount(amount), paidOn: paidOn || undefined },
    })
    if (!data) throw problemToApiError(response.status, error)
    onSaved()
  }, [contract.id, amount, paidOn, onSaved])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  function touch(field: string) {
    setClientErrors((current) => ({ ...current, [field]: [] }))
    clearFieldError(field)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors: FieldErrors = {}
    const value = parseAmount(amount)
    if (!(value > 0) || Math.round(value * 100) / 100 !== value) errors.amount = [T.depositForm.invalid]
    else if (value > total) errors.amount = [T.depositForm.tooHigh(formatCurrency(total, contract.currency))]
    if (paidOn > today()) errors.paidOn = [T.depositForm.future]
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  return (
    <Dialog title={T.depositForm.title} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        <FormField label={T.depositForm.amount} errors={errorsFor('amount')}>
          {(props) => (
            <input
              {...props}
              type="text"
              inputMode="decimal"
              value={amount}
              onChange={(e) => {
                setAmount(e.target.value)
                touch('amount')
              }}
            />
          )}
        </FormField>
        <FormField label={T.depositForm.paidOn} errors={errorsFor('paidOn')}>
          {(props) => (
            <input
              {...props}
              type="date"
              max={today()}
              value={paidOn}
              onChange={(e) => {
                setPaidOn(e.target.value)
                touch('paidOn')
              }}
            />
          )}
        </FormField>
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="dialog__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {MESSAGES.cancel}
          </button>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {T.depositForm.submit}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

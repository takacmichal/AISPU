import { useCallback, useState, type FormEvent } from 'react'
import { api } from '../../../api/client'
import type { components } from '../../../api/generated/schema'
import { problemToApiError, type FieldErrors } from '../../../api/problem'
import { Dialog } from '../../../components/Dialog'
import { EmptyState } from '../../../components/EmptyState'
import { FormField } from '../../../components/forms'
import { Skeleton } from '../../../components/Skeleton'
import { useToast } from '../../../components/toast'
import { useFormSubmit } from '../../../components/useFormSubmit'
import { useLoad } from '../../../components/useLoad'
import { formatDateTime, formatNumber } from '../../../lib/format'
import { MESSAGES } from '../../../lib/messages'
import { T } from '../messages'

type Device = components['schemas']['DeviceResponse']
type DialogState = { kind: 'replace'; device: Device } | { kind: 'history'; device: Device } | null

export default function DevicesTab({ installationId, canField }: { installationId: string; canField: boolean }) {
  const toast = useToast()
  const [includeReplaced, setIncludeReplaced] = useState(false)
  const [type, setType] = useState('')
  const [dialog, setDialog] = useState<DialogState>(null)

  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{installationId}/devices', {
      params: {
        path: { installationId },
        query: { includeReplaced: includeReplaced || undefined, type: (type || undefined) as 'Panel' | 'Inverter' | undefined },
      },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, JSON.stringify([installationId, includeReplaced, type]))

  return (
    <div>
      <div className="btn-row installations-filters">
        <label>
          {T.devices.type}
          <select value={type} onChange={(e) => setType(e.target.value)}>
            <option value="">{T.devices.allTypes}</option>
            <option value="Panel">{T.devices.types.Panel}</option>
            <option value="Inverter">{T.devices.types.Inverter}</option>
          </select>
        </label>
        <label className="installations-filters__check">
          <input type="checkbox" checked={includeReplaced} onChange={(e) => setIncludeReplaced(e.target.checked)} />
          {T.devices.includeReplaced}
        </label>
      </div>
      {loading && <Skeleton lines={3} label={T.devices.loading} />}
      {error && (
        <p role="alert" className="alert">
          {T.devices.error}
        </p>
      )}
      {data && data.length === 0 && <EmptyState title={T.devices.empty} />}
      {data && data.length > 0 && (
        <div className="data-table__scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">{T.devices.colType}</th>
                <th scope="col">{T.devices.colSerial}</th>
                <th scope="col">{T.devices.colMaker}</th>
                <th scope="col">{T.devices.colPower}</th>
                <th scope="col">{T.devices.colStatus}</th>
                <th scope="col">{T.devices.colActions}</th>
              </tr>
            </thead>
            <tbody>
              {data.map((d) => (
                <tr key={d.id}>
                  <td>{T.devices.types[d.type] ?? d.type}</td>
                  <td className="mono">{d.serialNumber}</td>
                  <td>{`${d.manufacturer} ${d.model}`}</td>
                  <td>{formatNumber(Number(d.powerW))}&nbsp;W</td>
                  <td>
                    <span className={d.status === 'Installed' ? 'badge badge--success' : 'badge'}>
                      {T.devices.statuses[d.status] ?? d.status}
                    </span>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="btn btn--ghost btn--sm"
                      aria-label={`${T.devices.history} ${d.serialNumber}`}
                      onClick={() => setDialog({ kind: 'history', device: d })}
                    >
                      {T.devices.history}
                    </button>{' '}
                    {canField && d.status === 'Installed' && (
                      <button
                        type="button"
                        className="btn btn--ghost btn--sm"
                        aria-label={`${T.devices.replace} ${d.serialNumber}`}
                        onClick={() => setDialog({ kind: 'replace', device: d })}
                      >
                        {T.devices.replace}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {dialog?.kind === 'history' && (
        <HistoryDialog installationId={installationId} device={dialog.device} onClose={() => setDialog(null)} />
      )}
      {dialog?.kind === 'replace' && (
        <ReplaceDialog
          installationId={installationId}
          device={dialog.device}
          onClose={() => setDialog(null)}
          onReplaced={() => {
            setDialog(null)
            toast.success(T.devices.replaced)
            reload()
          }}
        />
      )}
    </div>
  )
}

function HistoryDialog({ installationId, device, onClose }: { installationId: string; device: Device; onClose: () => void }) {
  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{installationId}/devices/{deviceId}/history', {
      params: { path: { installationId, deviceId: device.id } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, device.id)

  return (
    <Dialog title={`${T.devices.historyTitle}: ${device.serialNumber}`} onClose={onClose}>
      {loading && <Skeleton lines={3} />}
      {error && (
        <p role="alert" className="alert">
          {T.devices.historyError}
        </p>
      )}
      {data && (
        <ol className="installation-history">
          {data.map((d) => (
            <li key={d.id}>
              <span className="mono">{d.serialNumber}</span> · {T.devices.statuses[d.status] ?? d.status} · {formatDateTime(d.registeredAt)}
              {d.removedAt && ` · ${T.devices.historyRemoved} ${formatDateTime(d.removedAt)}`}
              {d.removalReason && ` · ${T.devices.historyReason}: ${d.removalReason}`}
            </li>
          ))}
        </ol>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--ghost" onClick={onClose}>
          {MESSAGES.close}
        </button>
      </div>
    </Dialog>
  )
}

interface ReplaceValues {
  serialNumber: string
  manufacturer: string
  model: string
  powerW: string
  reason: string
}

function validateReplace(values: ReplaceValues): FieldErrors {
  const errors: FieldErrors = {}
  for (const key of ['serialNumber', 'manufacturer', 'model', 'reason'] as const) {
    if (!values[key].trim()) errors[key] = [T.devices.required]
  }
  const power = Number(values.powerW)
  if (!values.powerW.trim() || !Number.isInteger(power) || power <= 0) errors.powerW = [T.devices.positive]
  return errors
}

function ReplaceDialog({
  installationId,
  device,
  onClose,
  onReplaced,
}: {
  installationId: string
  device: Device
  onClose: () => void
  onReplaced: () => void
}) {
  const [values, setValues] = useState<ReplaceValues>({
    serialNumber: '',
    manufacturer: device.manufacturer,
    model: device.model,
    powerW: String(Number(device.powerW)),
    reason: '',
  })
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const save = useCallback(async () => {
    const { data, response, error } = await api.POST('/api/installations/{installationId}/devices/{deviceId}/replace', {
      params: { path: { installationId, deviceId: device.id } },
      body: {
        serialNumber: values.serialNumber.trim(),
        manufacturer: values.manufacturer.trim(),
        model: values.model.trim(),
        powerW: Number(values.powerW),
        reason: values.reason.trim(),
      },
    })
    if (!data) throw problemToApiError(response.status, error)
    onReplaced()
  }, [values, installationId, device.id, onReplaced])

  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  function set(name: keyof ReplaceValues, value: string) {
    setValues((current) => ({ ...current, [name]: value }))
    setClientErrors((current) => {
      const rest = { ...current }
      delete rest[name]
      return rest
    })
    clearFieldError(name)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateReplace(values)
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const field = (name: keyof ReplaceValues, label: string, type = 'text', mono = false) => (
    <FormField label={label} errors={[...(clientErrors[name] ?? []), ...(fieldErrors[name] ?? [])]}>
      {(props) => (
        <input {...props} type={type} className={mono ? 'mono' : undefined} value={values[name]} onChange={(e) => set(name, e.target.value)} />
      )}
    </FormField>
  )

  return (
    <Dialog title={T.devices.replaceTitle} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        <p>
          {T.devices.replaceHint(device.serialNumber)}
        </p>
        {field('serialNumber', T.devices.serial, 'text', true)}
        {field('manufacturer', T.devices.manufacturer)}
        {field('model', T.devices.model)}
        {field('powerW', T.devices.powerW, 'number')}
        {field('reason', T.devices.reason)}
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
            {T.devices.replace}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

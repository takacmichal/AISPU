import type { FieldErrors } from '../../api/problem'
import { T } from './messages'
import {
  ALLOWED_CONTENT_TYPES,
  DESCRIPTION_MAX,
  MAX_ATTACHMENTS,
  MAX_ATTACHMENT_BYTES,
  TITLE_MAX,
  type CreateTicketRequest,
  type TicketPriority,
} from './ticketsApi'

export interface TicketFormValues {
  installationId: string
  title: string
  description: string
  /** Empty = backend default (Medium). Only staff may set it. */
  priority: TicketPriority | ''
  files: File[]
}

export const emptyTicketValues: TicketFormValues = { installationId: '', title: '', description: '', priority: '', files: [] }

/** Client-side validation mirroring the backend limits; keys match the FormField error keys. */
export function validateTicket(values: TicketFormValues): FieldErrors {
  const errors: FieldErrors = {}
  const { create } = T
  if (!values.installationId) errors.installationId = [create.required]
  if (!values.title.trim()) errors.title = [create.required]
  else if (values.title.trim().length > TITLE_MAX) errors.title = [create.titleTooLong]
  if (!values.description.trim()) errors.description = [create.required]
  else if (values.description.trim().length > DESCRIPTION_MAX) errors.description = [create.descriptionTooLong]
  const fileErrors = validateFiles(values.files)
  if (fileErrors.length > 0) errors.attachments = fileErrors
  return errors
}

export function validateFiles(files: readonly File[]): string[] {
  const messages: string[] = []
  if (files.length > MAX_ATTACHMENTS) messages.push(T.create.tooMany)
  for (const file of files) {
    if (!(ALLOWED_CONTENT_TYPES as readonly string[]).includes(file.type)) messages.push(T.create.badType(file.name))
    else if (file.size > MAX_ATTACHMENT_BYTES) messages.push(T.create.tooBig(file.name))
  }
  return messages
}

function toBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(new Error(T.create.readFailed(file.name)))
    reader.onload = () => {
      const result = String(reader.result)
      resolve(result.slice(result.indexOf(',') + 1))
    }
    reader.readAsDataURL(file)
  })
}

export async function toTicketRequest(values: TicketFormValues): Promise<CreateTicketRequest> {
  const attachments = await Promise.all(
    values.files.map(async (file) => ({ fileName: file.name, contentType: file.type, contentBase64: await toBase64(file) })),
  )
  return {
    installationId: values.installationId,
    title: values.title.trim(),
    description: values.description.trim(),
    priority: values.priority || undefined,
    attachments: attachments.length > 0 ? attachments : undefined,
  }
}

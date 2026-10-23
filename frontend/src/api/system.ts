import { apiUrl } from './config'

export interface SystemInfo {
  name: string
  version: string
  environment: string
  serverTime: string
}

export async function fetchSystemInfo(signal?: AbortSignal): Promise<SystemInfo> {
  const response = await fetch(`${apiUrl}/api/system/info`, { signal })
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }
  return (await response.json()) as SystemInfo
}

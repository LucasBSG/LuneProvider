import type {
  CreateJobRequest,
  CreateJobResponse,
  JobDetailsResponse,
  TemplateSummary,
} from './types'

const rawBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5180'
const apiBaseUrl = rawBaseUrl.replace(/\/$/, '')

async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    const responseText = await response.text()
    throw new Error(responseText || `Request failed: ${response.status}`)
  }

  return response.json() as Promise<T>
}

export function getApiBaseUrl() {
  return apiBaseUrl
}

export function getTemplates() {
  return apiRequest<TemplateSummary[]>('/templates')
}

export function createJob(payload: CreateJobRequest) {
  return apiRequest<CreateJobResponse>('/jobs', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function approveJob(jobId: string, accessToken: string) {
  return apiRequest<CreateJobResponse>(`/jobs/${jobId}/approve`, {
    method: 'POST',
    headers: {
      'X-Lune-Token': accessToken,
    },
  })
}

export function getJobDetails(jobId: string) {
  return apiRequest<JobDetailsResponse>(`/jobs/${jobId}`)
}

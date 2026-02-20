export type AgentStage = 'Plan' | 'Validate' | 'DryRun' | 'Apply' | 'Output'

export type JobStatus = 'Pending' | 'Running' | 'PendingApproval' | 'Succeeded' | 'Failed'

export interface TemplateSummary {
  id: string
  name: string
  version: string
  createdAtUtc: string
}

export interface CreateJobRequest {
  templateId: string
  userId: string
  environmentId: string
  parameters: {
    name: string
    region: string
    nodeCount: number
  }
}

export interface CreateJobResponse {
  id: string
  status: JobStatus
  currentStage: AgentStage
}

export interface JobEvent {
  sequence: number
  stage: AgentStage
  stream: string
  message: string
  timestampUtc: string
}

export interface JobDetailsResponse {
  id: string
  templateId: string
  userId: string
  environmentId: string
  status: JobStatus
  currentStage: AgentStage
  parameters: Record<string, unknown>
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  lastError: string | null
  approvalRequestedAtUtc: string | null
  approvalGranted: boolean
  approvalGrantedBy: string | null
  approvalGrantedAtUtc: string | null
  events: JobEvent[]
}

export interface JobStatusSignalR {
  jobId: string
  status: JobStatus
  stage: AgentStage
  timestampUtc: string
}

export interface JobLogSignalR {
  jobId: string
  sequence: number
  stage: AgentStage
  stream: string
  message: string
  timestampUtc: string
}

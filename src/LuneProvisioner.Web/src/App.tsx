import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'
import { approveJob, createJob, getApiBaseUrl, getJobDetails, getTemplates } from './api'
import { StageStepper } from './components/StageStepper'
import { TerminalPanel } from './components/TerminalPanel'
import type {
  AgentStage,
  JobDetailsResponse,
  JobLogSignalR,
  JobStatus,
  JobStatusSignalR,
  TemplateSummary,
} from './types'
import './App.css'

const regions = ['us-east-1', 'us-west-2', 'sa-east-1']
const passiveStatuses = new Set<JobStatus>(['Succeeded', 'Failed', 'PendingApproval'])
const streamableStatuses = new Set<JobStatus>(['Pending', 'Running'])

function mergeJobLog(current: JobDetailsResponse | null, incoming: JobLogSignalR) {
  if (!current || current.id !== incoming.jobId) {
    return current
  }

  const alreadyExists = current.events.some((event) => event.sequence === incoming.sequence)
  if (alreadyExists) {
    return current
  }

  return {
    ...current,
    events: [...current.events, incoming].sort((left, right) => left.sequence - right.sequence),
  }
}

function statusTone(status: JobStatus) {
  if (status === 'Succeeded') {
    return 'ok'
  }

  if (status === 'Failed') {
    return 'error'
  }

  if (status === 'Running') {
    return 'running'
  }

  if (status === 'PendingApproval') {
    return 'approval'
  }

  return 'pending'
}

function App() {
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [selectedTemplateId, setSelectedTemplateId] = useState('')

  const [clusterName, setClusterName] = useState('lune-qa')
  const [region, setRegion] = useState('sa-east-1')
  const [nodeCount, setNodeCount] = useState(3)
  const [userId, setUserId] = useState('dev-user')
  const [environmentId, setEnvironmentId] = useState('qa')
  const [approverToken, setApproverToken] = useState(import.meta.env.VITE_APPROVER_TOKEN ?? '')

  const [isSubmitting, setIsSubmitting] = useState(false)
  const [loadingTemplates, setLoadingTemplates] = useState(true)
  const [connectionState, setConnectionState] = useState<'offline' | 'connecting' | 'online' | 'error'>('offline')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [activeJob, setActiveJob] = useState<JobDetailsResponse | null>(null)

  const connectionRef = useRef<HubConnection | null>(null)
  const activeJobIdRef = useRef<string | null>(null)

  const teardownConnection = useCallback(async () => {
    if (connectionRef.current) {
      try {
        if (activeJobIdRef.current) {
          await connectionRef.current.invoke('LeaveJobGroup', activeJobIdRef.current)
        }
        await connectionRef.current.stop()
      } catch {
        // no-op
      }
      connectionRef.current = null
      activeJobIdRef.current = null
      setConnectionState('offline')
    }
  }, [])

  const connectToJobHub = useCallback(
    async (jobId: string) => {
      await teardownConnection()

      const connection = new HubConnectionBuilder()
        .withUrl(`${getApiBaseUrl()}/hubs/jobs`)
        .withAutomaticReconnect()
        .build()

      connection.on('job-status', (payload: JobStatusSignalR) => {
        if (payload.jobId !== jobId) {
          return
        }

        setActiveJob((current) =>
          current
            ? {
                ...current,
                status: payload.status,
                currentStage: payload.stage,
              }
            : current,
        )
      })

      connection.on('job-log', (payload: JobLogSignalR) => {
        setActiveJob((current) => mergeJobLog(current, payload))
      })

      connection.onreconnecting(() => {
        setConnectionState('connecting')
      })

      connection.onreconnected(async () => {
        setConnectionState('online')
        await connection.invoke('JoinJobGroup', jobId)
      })

      connection.onclose(() => {
        setConnectionState('offline')
      })

      setConnectionState('connecting')
      await connection.start()
      await connection.invoke('JoinJobGroup', jobId)
      connectionRef.current = connection
      activeJobIdRef.current = jobId
      setConnectionState('online')
    },
    [teardownConnection],
  )

  const loadJobDetails = useCallback(async (jobId: string) => {
    const details = await getJobDetails(jobId)
    setActiveJob(details)
    return details
  }, [])

  useEffect(() => {
    const loadTemplatesAsync = async () => {
      try {
        const templateList = await getTemplates()
        setTemplates(templateList)
        if (templateList.length > 0) {
          setSelectedTemplateId((current) => current || templateList[0].id)
        }
      } catch (error) {
        setErrorMessage((error as Error).message)
      } finally {
        setLoadingTemplates(false)
      }
    }

    void loadTemplatesAsync()
  }, [])

  useEffect(() => {
    return () => {
      void teardownConnection()
    }
  }, [teardownConnection])

  useEffect(() => {
    if (!activeJob || passiveStatuses.has(activeJob.status)) {
      return
    }

    const timer = window.setInterval(async () => {
      try {
        const details = await loadJobDetails(activeJob.id)
        if (passiveStatuses.has(details.status)) {
          await teardownConnection()
        }
      } catch {
        // poll silently; UI already has live stream when connected
      }
    }, 1200)

    return () => window.clearInterval(timer)
  }, [activeJob, loadJobDetails, teardownConnection])

  const startJob = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault()

      if (!selectedTemplateId) {
        setErrorMessage('Selecione um template antes de iniciar.')
        return
      }

      setErrorMessage(null)
      setIsSubmitting(true)

      try {
        const response = await createJob({
          templateId: selectedTemplateId,
          userId,
          environmentId,
          parameters: {
            name: clusterName,
            region,
            nodeCount,
          },
        })

        const details = await loadJobDetails(response.id)
        if (streamableStatuses.has(details.status)) {
          await connectToJobHub(response.id)
        }
      } catch (error) {
        setErrorMessage((error as Error).message)
      } finally {
        setIsSubmitting(false)
      }
    },
    [
      clusterName,
      connectToJobHub,
      environmentId,
      loadJobDetails,
      nodeCount,
      region,
      selectedTemplateId,
      userId,
    ],
  )

  const approveActiveJob = useCallback(async () => {
    if (!activeJob || activeJob.status !== 'PendingApproval') {
      return
    }

    if (!approverToken.trim()) {
      setErrorMessage('Informe o token de aprovacao para continuar.')
      return
    }

    setErrorMessage(null)
    setIsSubmitting(true)

    try {
      const response = await approveJob(activeJob.id, approverToken.trim())
      const details = await loadJobDetails(response.id)
      if (streamableStatuses.has(details.status)) {
        await connectToJobHub(details.id)
      }
    } catch (error) {
      setErrorMessage((error as Error).message)
    } finally {
      setIsSubmitting(false)
    }
  }, [activeJob, approverToken, connectToJobHub, loadJobDetails])

  const activeTemplate = useMemo(
    () => templates.find((template) => template.id === selectedTemplateId) ?? null,
    [selectedTemplateId, templates],
  )

  return (
    <main className="page">
      <section className="hero">
        <p className="eyebrow">LuneProvisioner / Frontend React</p>
        <h1>Painel de Execucao IaC</h1>
        <p className="subtitle">
          Dispare jobs, acompanhe estagios do agente e visualize logs em tempo real via SignalR.
        </p>
      </section>

      <section className="layout">
        <article className="panel form-panel">
          <header className="panel-header">
            <h2>Novo Job</h2>
            <span className="chip">{loadingTemplates ? 'Carregando templates' : `${templates.length} template(s)`}</span>
          </header>

          <form className="job-form" onSubmit={startJob}>
            <label>
              Template
              <select
                value={selectedTemplateId}
                onChange={(event) => setSelectedTemplateId(event.target.value)}
                disabled={loadingTemplates || templates.length === 0}
              >
                {templates.map((template) => (
                  <option key={template.id} value={template.id}>
                    {template.name} v{template.version}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Nome do cluster
              <input value={clusterName} onChange={(event) => setClusterName(event.target.value)} required />
            </label>

            <label>
              Regiao
              <select value={region} onChange={(event) => setRegion(event.target.value)}>
                {regions.map((currentRegion) => (
                  <option key={currentRegion} value={currentRegion}>
                    {currentRegion}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Quantidade de nos
              <input
                type="number"
                min={1}
                max={20}
                value={nodeCount}
                onChange={(event) => setNodeCount(Number(event.target.value))}
                required
              />
            </label>

            <label>
              UserId
              <input value={userId} onChange={(event) => setUserId(event.target.value)} required />
            </label>

            <label>
              EnvironmentId
              <input value={environmentId} onChange={(event) => setEnvironmentId(event.target.value)} required />
            </label>

            <label>
              Token de aprovacao
              <input
                value={approverToken}
                onChange={(event) => setApproverToken(event.target.value)}
                placeholder="dev-approver-token"
                required
              />
            </label>

            <button type="submit" disabled={isSubmitting || !selectedTemplateId}>
              {isSubmitting ? 'Enfileirando...' : 'Criar e acompanhar job'}
            </button>
          </form>

          {activeTemplate ? (
            <p className="meta">Template ativo: {activeTemplate.name} v{activeTemplate.version}</p>
          ) : null}

          {errorMessage ? <p className="error">{errorMessage}</p> : null}
        </article>

        <article className="panel monitor-panel">
          <header className="panel-header">
            <h2>Execucao</h2>
            <div className="header-badges">
              <span className={`chip ${connectionState}`}>SignalR: {connectionState}</span>
              {activeJob ? <span className={`chip ${statusTone(activeJob.status)}`}>Status: {activeJob.status}</span> : null}
            </div>
          </header>

          {activeJob ? (
            <>
              <div className="job-id">Job: {activeJob.id}</div>
              <StageStepper currentStage={activeJob.currentStage as AgentStage} status={activeJob.status as JobStatus} />
              {activeJob.status === 'PendingApproval' ? (
                <div className="approval-box">
                  <p>
                    Dry-run exige aprovacao delegada para o ambiente <strong>{activeJob.environmentId}</strong>.
                  </p>
                  <button type="button" onClick={() => void approveActiveJob()} disabled={isSubmitting}>
                    {isSubmitting ? 'Aprovando...' : 'Aprovar e continuar apply'}
                  </button>
                </div>
              ) : null}
              <TerminalPanel events={activeJob.events} />
              {activeJob.lastError ? <p className="error">Falha: {activeJob.lastError}</p> : null}
            </>
          ) : (
            <div className="empty-state">Nenhum job monitorado ainda.</div>
          )}
        </article>
      </section>
    </main>
  )
}

export default App

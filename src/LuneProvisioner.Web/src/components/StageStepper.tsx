import type { AgentStage, JobStatus } from '../types'

const stages: AgentStage[] = ['Plan', 'Validate', 'DryRun', 'Apply', 'Output']

type StageStepperProps = {
  currentStage: AgentStage
  status: JobStatus
}

export function StageStepper({ currentStage, status }: StageStepperProps) {
  const currentIndex = stages.indexOf(currentStage)

  return (
    <div className="stepper">
      {stages.map((stage, index) => {
        const isDone = index < currentIndex || (status === 'Succeeded' && stage === 'Output')
        const isCurrent = index === currentIndex
        const isFailed = status === 'Failed' && isCurrent
        const stateClass = isFailed ? 'failed' : isDone ? 'done' : isCurrent ? 'active' : 'pending'

        return (
          <div key={stage} className={`step ${stateClass}`}>
            <div className="step-dot">{index + 1}</div>
            <div className="step-label">{stage}</div>
          </div>
        )
      })}
    </div>
  )
}

import { useEffect, useRef } from 'react'
import type { JobEvent } from '../types'

type TerminalPanelProps = {
  events: JobEvent[]
}

function formatUtcTime(value: string) {
  return new Date(value).toLocaleTimeString()
}

export function TerminalPanel({ events }: TerminalPanelProps) {
  const endRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [events.length])

  if (events.length === 0) {
    return <div className="terminal-empty">Sem logs por enquanto.</div>
  }

  return (
    <div className="terminal">
      {events.map((entry) => (
        <div key={entry.sequence} className={`terminal-line stream-${entry.stream}`}>
          <span className="terminal-time">{formatUtcTime(entry.timestampUtc)}</span>
          <span className="terminal-stage">{entry.stage}</span>
          <span className="terminal-message">{entry.message}</span>
        </div>
      ))}
      <div ref={endRef} />
    </div>
  )
}

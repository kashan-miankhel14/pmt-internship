'use client'

import Chip from '@mui/material/Chip'

const statusConfigMap = {
  // Success states (Green)
  Completed: { bg: 'rgba(34, 197, 94, 0.12)', text: '#16a34a', border: 'rgba(34, 197, 94, 0.25)', label: 'COMPLETED' },
  Done: { bg: 'rgba(34, 197, 94, 0.12)', text: '#16a34a', border: 'rgba(34, 197, 94, 0.25)', label: 'DONE' },
  Resolved: { bg: 'rgba(34, 197, 94, 0.12)', text: '#16a34a', border: 'rgba(34, 197, 94, 0.25)', label: 'RESOLVED' },
  Closed: { bg: 'rgba(100, 116, 139, 0.12)', text: '#475569', border: 'rgba(100, 116, 139, 0.25)', label: 'CLOSED' },

  // Active / In Progress states (Blue)
  Active: { bg: 'rgba(59, 130, 246, 0.12)', text: '#2563eb', border: 'rgba(59, 130, 246, 0.25)', label: 'ACTIVE' },
  InProgress: { bg: 'rgba(59, 130, 246, 0.12)', text: '#2563eb', border: 'rgba(59, 130, 246, 0.25)', label: 'IN PROGRESS' },

  // Review / Warning states (Amber / Purple)
  Review: { bg: 'rgba(168, 85, 247, 0.12)', text: '#9333ea', border: 'rgba(168, 85, 247, 0.25)', label: 'IN REVIEW' },
  OnHold: { bg: 'rgba(245, 158, 11, 0.12)', text: '#d97706', border: 'rgba(245, 158, 11, 0.25)', label: 'ON HOLD' },

  // Blocked / Errors / High Priority (Red / Rose)
  Blocked: { bg: 'rgba(239, 68, 68, 0.12)', text: '#dc2626', border: 'rgba(239, 68, 68, 0.25)', label: 'BLOCKED' },
  Critical: { bg: 'rgba(239, 68, 68, 0.15)', text: '#b91c1c', border: 'rgba(239, 68, 68, 0.35)', label: 'CRITICAL' },
  Highest: { bg: 'rgba(239, 68, 68, 0.12)', text: '#dc2626', border: 'rgba(239, 68, 68, 0.25)', label: 'HIGHEST' },
  High: { bg: 'rgba(249, 115, 22, 0.12)', text: '#ea580c', border: 'rgba(249, 115, 22, 0.25)', label: 'HIGH' },
  Rejected: { bg: 'rgba(239, 68, 68, 0.12)', text: '#dc2626', border: 'rgba(239, 68, 68, 0.25)', label: 'REJECTED' },

  // Medium / Info Priority (Orange / Sky)
  Medium: { bg: 'rgba(245, 158, 11, 0.12)', text: '#d97706', border: 'rgba(245, 158, 11, 0.25)', label: 'MEDIUM' },
  Ready: { bg: 'rgba(14, 165, 233, 0.12)', text: '#0284c7', border: 'rgba(14, 165, 233, 0.25)', label: 'READY' },
  Open: { bg: 'rgba(14, 165, 233, 0.12)', text: '#0284c7', border: 'rgba(14, 165, 233, 0.25)', label: 'OPEN' },

  // Planning / ToDo / Backlog (Slate / Gray)
  Planning: { bg: 'rgba(100, 116, 139, 0.1)', text: '#64748b', border: 'rgba(100, 116, 139, 0.2)', label: 'PLANNING' },
  Backlog: { bg: 'rgba(100, 116, 139, 0.1)', text: '#64748b', border: 'rgba(100, 116, 139, 0.2)', label: 'BACKLOG' },
  ToDo: { bg: 'rgba(100, 116, 139, 0.1)', text: '#475569', border: 'rgba(100, 116, 139, 0.2)', label: 'TO DO' },
  Low: { bg: 'rgba(100, 116, 139, 0.1)', text: '#64748b', border: 'rgba(100, 116, 139, 0.2)', label: 'LOW' },
  Lowest: { bg: 'rgba(100, 116, 139, 0.08)', text: '#94a3b8', border: 'rgba(100, 116, 139, 0.15)', label: 'LOWEST' },
  Cancelled: { bg: 'rgba(100, 116, 139, 0.1)', text: '#64748b', border: 'rgba(100, 116, 139, 0.2)', label: 'CANCELLED' }
}

const humanize = value => String(value || '').replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase()

const StatusChip = ({ value, size = 'small' }) => {
  if (!value) return null

  const key = String(value).replace(/\s+/g, '')
  const config = statusConfigMap[key] || {
    bg: 'rgba(100, 116, 139, 0.1)',
    text: 'var(--mui-palette-text-secondary)',
    border: 'rgba(100, 116, 139, 0.2)',
    label: humanize(value)
  }

  return (
    <span
      className='inline-flex items-center justify-center font-semibold tracking-wider rounded-md select-none transition-all duration-150'
      style={{
        backgroundColor: config.bg,
        color: config.text,
        border: `1px solid ${config.border}`,
        fontSize: size === 'small' ? '0.6875rem' : '0.75rem',
        lineHeight: '1rem',
        padding: size === 'small' ? '2px 7px' : '4px 10px',
        letterSpacing: '0.04em'
      }}
    >
      {config.label}
    </span>
  )
}

export default StatusChip

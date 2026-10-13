import { Badge } from '@fluentui/react-components'
import { statusLabels } from '../../api/resumeBank'
import type { ResumeBankStatus } from '../../api/types'
import { statusBadgeColor } from './status'

export function StatusBadge({ status }: { status: ResumeBankStatus }) {
  return (
    <Badge appearance="tint" color={statusBadgeColor(status)}>
      {statusLabels[status]}
    </Badge>
  )
}

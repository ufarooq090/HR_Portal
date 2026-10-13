import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import { useMe } from '../../api/queries'
import { recruitingKeys, useRejectionReasons } from '../../api/recruiting'
import type { RejectionReason } from '../../api/types'
import { canManageOrganisation } from '../../auth/roles'
import { ActiveBadge, ReferenceDataPage } from '../organisation/ReferenceDataPage'

/** CT-03: the list HR picks rejection reasons from. HR Admins manage it. */
export function RejectionReasonsPage() {
  const { data: me } = useMe()
  const { data, isLoading, error } = useRejectionReasons()
  const queryClient = useQueryClient()
  const refresh = () => queryClient.invalidateQueries({ queryKey: recruitingKeys.rejectionReasons })
  const save = useMutation({
    mutationFn: ({ item, body }: { item: RejectionReason | null; body: Record<string, unknown> }) =>
      item ? api.put(`/api/recruiting/rejection-reasons/${item.id}`, body) : api.post('/api/recruiting/rejection-reasons', body),
    onSuccess: refresh,
  })
  const remove = useMutation({ mutationFn: (item: RejectionReason) => api.delete(`/api/recruiting/rejection-reasons/${item.id}`), onSuccess: refresh })
  const canEdit = canManageOrganisation(me)

  return (
    <ReferenceDataPage<RejectionReason>
      title="Rejection reasons"
      description="The reasons HR chooses from when rejecting a candidate. Deactivate reasons that are no longer used; past rejections keep their reason."
      noun="rejection reason"
      items={data}
      isLoading={isLoading}
      error={error}
      getKey={(r) => r.id}
      getName={(r) => r.name}
      columns={[
        { header: 'Reason', render: (r) => r.name },
        { header: 'Order', render: (r) => r.sortOrder },
        { header: 'Status', render: (r) => <ActiveBadge active={r.isActive} /> },
      ]}
      fields={[
        { name: 'name', label: 'Reason', kind: 'text', required: true },
        { name: 'sortOrder', label: 'Order', kind: 'number', required: true, hint: 'Lower comes first.' },
        { name: 'isActive', label: 'Active', kind: 'active' },
      ]}
      canCreate={canEdit}
      canEdit={() => canEdit}
      onSave={(item, body) => save.mutateAsync({ item, body })}
      onDelete={(item) => remove.mutateAsync(item)}
    />
  )
}

import { type GriffelStyle, tokens } from '@fluentui/react-components'

/**
 * The one responsive grid for forms and filter bars. Columns wrap as the page narrows and never get narrower than
 * the screen; fields align at the top, so labels share a baseline whether or not a neighbour has help text below it.
 */
export const fieldGrid: GriffelStyle = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 220px), 1fr))',
  columnGap: tokens.spacingHorizontalL,
  rowGap: tokens.spacingVerticalM,
  alignItems: 'start',
  // Fluent gives dropdowns a 250px minimum, wider than a narrow column; here the column sets the width instead, so a
  // dropdown never spills over the field next to it.
  '& .fui-Dropdown': { minWidth: 0 },
  '& .fui-Combobox': { minWidth: 0 },
}

/** A full-width row of buttons inside a {@link fieldGrid}, e.g. "Clear filters" or "Upload". */
export const fieldGridActions: GriffelStyle = {
  gridColumn: '1 / -1',
  display: 'flex',
  flexWrap: 'wrap',
  gap: tokens.spacingHorizontalS,
  alignItems: 'center',
}

/** Wide tables scroll inside this container, never the page. */
export const tableWrap: GriffelStyle = { overflowX: 'auto', maxWidth: '100%' }

/** One line, cut with an ellipsis; pair with a tooltip that shows the full text. */
export const truncate: GriffelStyle = { display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', minWidth: 0 }

import {
  Avatar,
  Badge,
  Menu,
  MenuButton,
  MenuGroup,
  MenuGroupHeader,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components'
import {
  BuildingBankRegular,
  CalendarPersonRegular,
  DismissCircleRegular,
  DocumentSearchRegular,
  MailTemplateRegular,
  PersonSearchRegular,
  DocumentAddRegular,
  FlowRegular,
  GlobeRegular,
  HomeRegular,
  LocationRegular,
  OrganizationRegular,
  PeopleRegular,
  RibbonRegular,
  ShieldKeyholeRegular,
  SignOutRegular,
  TaskListLtrRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { NavLink, Outlet } from 'react-router'
import { useMe } from '../api/queries'
import { useAuth } from '../auth/AuthContext'
import { useMyInterviews } from '../api/recruiting'
import { canManageHrAccess, canUseCandidates, canUseRequisitions, canUseResumeBank, canUseTemplates, hasAnyRole, Roles, showsSettings } from '../auth/roles'
import { NotificationBell } from './NotificationBell'

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateRows: '48px 1fr',
    // minmax(0, …): wide content (tables, long names) scrolls or truncates inside the main column instead of
    // stretching the page past the viewport.
    gridTemplateColumns: '220px minmax(0, 1fr)',
    minHeight: '100vh',
    '@media (max-width: 720px)': { gridTemplateColumns: 'minmax(0, 1fr)', gridTemplateRows: '48px auto 1fr' },
  },
  header: {
    gridColumn: '1 / -1',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    minWidth: 0,
    padding: `0 ${tokens.spacingHorizontalL}`,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  userButton: { color: tokens.colorNeutralForegroundOnBrand, maxWidth: '280px', minWidth: 0 },
  userName: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  headerActions: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, minWidth: 0 },
  nav: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    '@media (max-width: 720px)': { flexDirection: 'row', flexWrap: 'wrap', borderRight: 'none' },
  },
  navSection: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS} ${tokens.spacingVerticalXXS}`,
    color: tokens.colorNeutralForeground3,
  },
  // Every item has the same box; only colour and weight change, so the active item never shifts its text.
  link: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    borderLeft: `3px solid transparent`,
    color: tokens.colorNeutralForeground2,
    textDecoration: 'none',
    whiteSpace: 'nowrap',
    ':hover': { backgroundColor: tokens.colorNeutralBackground2Hover, color: tokens.colorNeutralForeground1 },
  },
  // The one active style, for every item and also on the item's sub-pages (e.g. a person's profile under People).
  active: {
    backgroundColor: tokens.colorNeutralBackground2Selected,
    borderLeftColor: tokens.colorBrandStroke1,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    ':hover': { backgroundColor: tokens.colorNeutralBackground2Selected, color: tokens.colorNeutralForeground1 },
  },
  main: { padding: tokens.spacingHorizontalXXL, minWidth: 0 },
})

function NavItem({ to, icon, children }: { to: string; icon: ReactNode; children: ReactNode }) {
  const styles = useStyles()
  return (
    <NavLink to={to} end={to === '/'} className={({ isActive }) => mergeClasses(styles.link, isActive && styles.active)}>
      {icon}
      {children}
    </NavLink>
  )
}

export function AppShell() {
  const styles = useStyles()
  const { data: me } = useMe()
  const { data: myInterviews } = useMyInterviews()
  const { mode, signOut } = useAuth()

  return (
    <div className={styles.root}>
      <header className={styles.header}>
        <Text weight="semibold" size={400}>
          HR Portal
        </Text>
        {mode === 'dev' && (
          <Badge color="warning" appearance="filled">
            DevAuth
          </Badge>
        )}
        <div className={styles.headerActions}>
        <NotificationBell />
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton
              appearance="transparent"
              className={styles.userButton}
              icon={<Avatar name={me?.displayName} size={24} color="colorful" />}
            >
              <span className={styles.userName}>{me?.displayName ?? '…'}</span>
            </MenuButton>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuGroup>
                <MenuGroupHeader>Roles</MenuGroupHeader>
                {me?.roles.map((role) => (
                  <MenuItem key={role} disabled>
                    {role}
                  </MenuItem>
                ))}
              </MenuGroup>
              <MenuItem icon={<SignOutRegular />} onClick={signOut}>
                {mode === 'dev' ? 'Switch user' : 'Sign out'}
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
        </div>
      </header>

      <nav className={styles.nav} aria-label="Main">
        <NavItem to="/" icon={<HomeRegular />}>
          Home
        </NavItem>
        <NavItem to="/people" icon={<PeopleRegular />}>
          People
        </NavItem>
        {canUseRequisitions(me) && (
          <NavItem to="/requisitions" icon={<DocumentAddRegular />}>
            Requisitions
          </NavItem>
        )}
        {canUseCandidates(me) && (
          <NavItem to="/candidates" icon={<PersonSearchRegular />}>
            Candidates
          </NavItem>
        )}
        {canUseResumeBank(me) && (
          <NavItem to="/resume-bank" icon={<DocumentSearchRegular />}>
            Resume Bank
          </NavItem>
        )}
        {canUseTemplates(me) && (
          <NavItem to="/templates" icon={<MailTemplateRegular />}>
            Email templates
          </NavItem>
        )}
        {(hasAnyRole(me, Roles.Interviewer) || (myInterviews?.length ?? 0) > 0) && (
          <NavItem to="/interviews" icon={<CalendarPersonRegular />}>
            My interviews
          </NavItem>
        )}
        {me?.isApprover && (
          <NavItem to="/approvals" icon={<TaskListLtrRegular />}>
            Approvals
          </NavItem>
        )}
        {showsSettings(me) && (
          <>
            <Text size={200} weight="semibold" className={styles.navSection}>
              Settings
            </Text>
            <NavItem to="/settings/countries" icon={<GlobeRegular />}>
              Countries
            </NavItem>
            <NavItem to="/settings/legal-entities" icon={<BuildingBankRegular />}>
              Legal entities
            </NavItem>
            <NavItem to="/settings/locations" icon={<LocationRegular />}>
              Offices
            </NavItem>
            <NavItem to="/settings/departments" icon={<OrganizationRegular />}>
              Departments
            </NavItem>
            <NavItem to="/settings/grades" icon={<RibbonRegular />}>
              Grades
            </NavItem>
            {hasAnyRole(me, Roles.HrAdmin, Roles.HrExecutive) && (
              <NavItem to="/settings/approval-chains" icon={<FlowRegular />}>
                Approval chains
              </NavItem>
            )}
            {hasAnyRole(me, Roles.HrAdmin, Roles.HrExecutive) && (
              <NavItem to="/settings/rejection-reasons" icon={<DismissCircleRegular />}>
                Rejection reasons
              </NavItem>
            )}
            {canManageHrAccess(me) && (
              <NavItem to="/settings/hr-access" icon={<ShieldKeyholeRegular />}>
                HR data access
              </NavItem>
            )}
          </>
        )}
      </nav>

      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  )
}

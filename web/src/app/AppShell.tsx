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
  GlobeRegular,
  HomeRegular,
  LocationRegular,
  OrganizationRegular,
  PeopleRegular,
  RibbonRegular,
  ShieldKeyholeRegular,
  SignOutRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { NavLink, Outlet } from 'react-router'
import { useMe } from '../api/queries'
import { useAuth } from '../auth/AuthContext'
import { canManageHrAccess, showsSettings } from '../auth/roles'

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateRows: '48px 1fr',
    gridTemplateColumns: '220px 1fr',
    minHeight: '100vh',
    '@media (max-width: 720px)': { gridTemplateColumns: '1fr', gridTemplateRows: '48px auto 1fr' },
  },
  header: {
    gridColumn: '1 / -1',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: `0 ${tokens.spacingHorizontalL}`,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  userButton: { color: tokens.colorNeutralForegroundOnBrand },
  nav: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    '@media (max-width: 720px)': { flexDirection: 'row', flexWrap: 'wrap', borderRight: 'none' },
  },
  navSection: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS} ${tokens.spacingVerticalXXS}`,
    color: tokens.colorNeutralForeground3,
  },
  link: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground1,
    textDecoration: 'none',
    ':hover': { backgroundColor: tokens.colorNeutralBackground2Hover },
  },
  active: {
    backgroundColor: tokens.colorNeutralBackground2Selected,
    fontWeight: tokens.fontWeightSemibold,
  },
  main: { padding: tokens.spacingHorizontalXXL, minWidth: 0 },
})

function NavItem({ to, icon, children }: { to: string; icon: ReactNode; children: ReactNode }) {
  const styles = useStyles()
  return (
    <NavLink to={to} end className={({ isActive }) => mergeClasses(styles.link, isActive && styles.active)}>
      {icon}
      {children}
    </NavLink>
  )
}

export function AppShell() {
  const styles = useStyles()
  const { data: me } = useMe()
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
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton
              appearance="transparent"
              className={styles.userButton}
              icon={<Avatar name={me?.displayName} size={24} color="colorful" />}
            >
              {me?.displayName ?? '…'}
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
      </header>

      <nav className={styles.nav} aria-label="Main">
        <NavItem to="/" icon={<HomeRegular />}>
          Home
        </NavItem>
        <NavItem to="/people" icon={<PeopleRegular />}>
          People
        </NavItem>
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

// Starts the API for E2E tests: Test environment, DevAuth on, a freshly recreated `hrportal_e2e`
// database in the docker compose Postgres, and a throwaway field-encryption key generated per run
// (the database is recreated each run, so nothing encrypted with an old key survives).
import { execFileSync, spawn } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { fileURLToPath } from 'node:url'

const repoRoot = fileURLToPath(new URL('../..', import.meta.url))
const database = 'hrportal_e2e'
const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5295'

execFileSync(
  'docker',
  ['compose', 'exec', '-T', 'postgres', 'psql', '-U', 'hrportal', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1',
    '-c', `DROP DATABASE IF EXISTS ${database} WITH (FORCE)`],
  { cwd: repoRoot, stdio: 'inherit' },
)

const api = spawn('dotnet', ['run', '--project', 'src/HrPortal.Api', '--no-launch-profile'], {
  cwd: repoRoot,
  stdio: 'inherit',
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Test',
    ASPNETCORE_URLS: apiUrl,
    DevAuth__Enabled: 'true',
    Database__MigrateOnStartup: 'true',
    ConnectionStrings__HrPortal: `Host=localhost;Port=5432;Database=${database};Username=hrportal`,
    FieldEncryption__ActiveKeyId: 'e2e',
    FieldEncryption__Keys__e2e: randomBytes(32).toString('base64'),
    Storage__ConnectionString: 'UseDevelopmentStorage=true',
    Portal__BaseUrl: process.env.E2E_WEB_URL ?? 'http://127.0.0.1:5273',
    Portal__CompanyName: 'HR Portal E2E',
    Email__Smtp__Host: 'localhost',
    Email__Smtp__Port: '1025',
  },
})

for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => api.kill(signal))
api.on('exit', (code) => process.exit(code ?? 1))

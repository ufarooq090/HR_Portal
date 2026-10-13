# HR Portal — Requirements Specification

Oct 12, 2026 · @Umer Farooq Khan

## Overview

The HR Portal replaces spreadsheets and scattered email with one system covering the employee lifecycle, from job requisition to exit. It is sized for a company of 250–1,000 employees across Pakistan and the US (3 offices, 10 HR users), with headroom to 2,000.

**Goals**

- One source of truth for every candidate and employee record.
- Cut manual HR work: templated emails and letters, automatic reminders, self-service for routine requests.
- Give managers visibility of their own team's hiring, attendance, leave and performance.
- Keep sensitive data restricted by role, with a full audit trail.

**In scope**

The seven modules HR requested (Candidate Tracker, Resume Bank, Onboarding, Attendance, Performance, Email Templates, Outlook Integration), plus these additions: Employee Records, Job Requisitions, Leave Management, Offboarding, Employee Self-Service, Dashboards, Document Expiry Alerts, Asset Tracking, Policy Acknowledgements and Multi-country Support.

**Out of scope (for now)**

- Full payroll processing. The portal exports attendance and leave data for the existing payroll system instead.
- A public careers website. Candidates can be added manually, by CV upload, or through a simple apply link per job.
- Native mobile apps. The web app is responsive and works on phones.

## Users and permissions

Seven internal roles control what each person sees; access is scoped by role first, then by team. A person can hold more than one role (a line manager is also an employee).

| Role | Who | Main purpose |
| --- | --- | --- |
| System Admin | IT | Users, roles, integrations, settings. No access to salary or performance content by default. |
| HR Admin | HR head, senior HR | Full access to all HR data, templates, letters and reports. |
| HR Executive / Recruiter | HR team | Day-to-day recruiting, onboarding, attendance and records. No salary edits unless granted. |
| Hiring Manager | Department heads | Raise requisitions, review candidates for their roles, approve offers. |
| Interviewer | Any employee on a panel | See assigned candidates only; submit scorecards. |
| Line Manager | Anyone with direct reports | Team attendance, leave approvals, performance reviews. |
| Employee | Everyone | Self-service: own profile, leave, documents, payslip downloads, reviews. |

Candidates and new joiners get no login. They use secure, expiring links to upload documents, sign letters and confirm interview slots.

**Access by module**

Full = create, edit, delete. Team = only their direct and indirect reports. Own = only their own record. Assigned = only items assigned to them.

| Module | HR Admin | HR Executive | Hiring Manager | Interviewer | Line Manager | Employee |
| --- | --- | --- | --- | --- | --- | --- |
| Employee Records | Full | Full (no salary) | View team | None | View team | Own |
| Salary and compensation | Full | Granted only | None | None | None | Own |
| Job Requisitions | Full | Full | Create, own dept | None | Create, own dept | None |
| Candidate Tracker | Full | Full | Own requisitions | Assigned | None | None |
| Resume Bank | Full | Full | Search, view | None | None | None |
| Onboarding | Full | Full | View team | None | Assigned tasks | Own checklist |
| Offboarding | Full | Full | View team | None | Assigned tasks | Own checklist |
| Attendance | Full | Full | Team | None | Team | Own |
| Leave | Full | Full | Approve team | None | Approve team | Own |
| Performance | Full | View | Team | None | Team | Own |
| Email Templates | Full | Use | Use (interview only) | None | None | None |
| Dashboards | All | All | Own dept | None | Team | None |
| Audit Log | View | None | None | None | None | None |

## Recommended tech stack and architecture

Recommendation: **ASP.NET Core (.NET 10) API + React frontend + PostgreSQL, hosted on Microsoft Azure.** Outlook, Teams and company login are all Microsoft, and .NET has Microsoft's own, best-supported libraries for Entra ID and the Graph API, so the integration work is smallest and most reliable on this stack.

&#91;embedded content: System architecture · web app, API, data stores, Microsoft 365\]

Staff sign in with their Microsoft account; candidates only ever touch single-purpose links. The API and background jobs share one codebase and call Microsoft Graph for mail, calendar and Teams.

| Layer | Choice | Why |
| --- | --- | --- |
| Frontend | React + TypeScript, Fluent UI component library | Large talent pool; Fluent UI matches the look of Outlook and Teams that staff already use. |
| Backend API | ASP.NET Core (.NET 10 LTS), C# | First-class Entra ID and Microsoft Graph SDKs; strong typing suits leave, attendance and payroll rules. |
| Database | PostgreSQL (Azure Database for PostgreSQL) via Entity Framework Core | Reliable, low cost, and its built-in full-text search covers Resume Bank search at 50,000 CVs. |
| File storage | Azure Blob Storage | CVs, letters and employee documents, encrypted, accessed only through the API. |
| Background jobs | Hangfire | Scheduled reminders, auto-emails, leave accrual, Outlook and biometric sync, with a retry dashboard. |
| Login | Microsoft Entra ID (MSAL / Microsoft.Identity.Web) | Single sign-on and MFA; leavers lose access when IT disables their account. |
| Email and calendar | Microsoft Graph API | Send mail, read replies, create calendar events and Teams links. |
| PDF letters | HTML templates rendered to PDF (Playwright or QuestPDF) | HR-editable templates produce letterhead-quality PDFs. |
| E-signature | Built-in click-to-accept with audit trail, plus scan upload for physically signed copies; DocuSign or Acrobat Sign later only if legal requires it | HR accepts both methods, so no paid e-signature service is needed at launch. |
| Hosting and ops | Azure App Service, Key Vault, Application Insights, GitHub Actions CI/CD | Managed hosting, secrets kept out of code, logging and alerts, automated deployments. |

If your developers are much stronger in JavaScript, Node.js (NestJS) with the same frontend, database and Azure services is a sound alternative. The team's existing skills should outweigh the small integration advantage of .NET.

## Functional requirements

Each requirement has an ID for tracking and test coverage, and the delivery phase it belongs to (see Delivery plan). Modules marked *new* were added beyond HR's original list.

### 1. Employee Records (new)

The employee master record is the core every other module links to.

| ID | Requirement | Phase |
| --- | --- | --- |
| EMP-01 | Store personal details, contact info, emergency contacts, address, date of birth. | 1 |
| EMP-02 | Store job details: employee ID, department, designation, grade, location, reporting manager, employment type (permanent, contract, intern), joining date. | 1 |
| EMP-03 | Store compensation details (CTC, salary band) visible only to permitted roles. | 1 |
| EMP-04 | Track employment status: on probation, confirmed, on notice, resigned, terminated, absconded. | 1 |
| EMP-05 | Keep a dated history of changes to designation, department, manager and salary (promotions, transfers, increments). | 1 |
| EMP-06 | Store employee documents (ID proofs, certificates, contracts, signed letters) against the record. | 1 |
| EMP-07 | Bulk import existing employees from Excel/CSV with validation and an error report. | 1 |
| EMP-08 | Searchable employee directory with filters (department, location, manager, status). | 1 |
| EMP-09 | Org chart generated automatically from reporting-manager links. | 3 |

### 2. Job Requisitions (new)

Every hire starts as an approved requisition, so headcount is controlled and time-to-fill can be measured.

| ID | Requirement | Phase |
| --- | --- | --- |
| REQ-01 | Hiring or line manager raises a requisition: role, department, number of openings, budget range, reason (new / replacement), target date, job description. | 1 |
| REQ-02 | Configurable approval chain; the default is a single CEO approval. Approvers act from the portal or the notification email. | 1 |
| REQ-03 | Approved requisitions become open positions that candidates are tracked against. | 1 |
| REQ-04 | Statuses: draft, pending approval, open, on hold, filled, cancelled. | 1 |
| REQ-05 | Reusable job description library by designation. | 2 |
| REQ-06 | Shareable apply link per open position with a simple application form and CV upload. | 2 |

### 3. Candidate Tracker

Shows each candidate's position in the hiring pipeline from application to hire.

Default pipeline stages (configurable per requisition): Applied → Screening → Interview rounds (1..n) → Offer → Offer accepted → Joined. Exit statuses at any stage: Rejected, Withdrawn, No-show, On hold, Offer declined.

| ID | Requirement | Phase |
| --- | --- | --- |
| CT-01 | Kanban board per requisition with drag-and-drop between stages, plus a list view with filters. | 1 |
| CT-02 | Candidate record: contact details, CV, source (portal, referral, agency, job board, walk-in), current and expected CTC, notice period, current employer. | 1 |
| CT-03 | Every stage change is logged with date, user and optional reason; rejection reasons come from a configurable list. | 1 |
| CT-04 | Schedule interviews: round, panel, date/time, mode (in person, Teams, phone). Creates Outlook calendar invites for panel and candidate. | 2 |
| CT-05 | Show panel members' free/busy times from Outlook when picking a slot. | 2 |
| CT-06 | Structured scorecards per role: criteria, 1–5 rating, comments, overall recommendation (strong hire, hire, no hire). | 1 |
| CT-07 | Interviewers cannot see other panel members' feedback until they submit their own, to avoid bias. | 1 |
| CT-08 | Free-text notes on the candidate with author and timestamp. | 1 |
| CT-09 | Duplicate detection on email and phone; links the new application to the existing Resume Bank record. | 1 |
| CT-10 | Referral tracking: referring employee, referral bonus eligibility date. | 2 |
| CT-11 | CEO approves every offer before it is sent; offers above the requisition budget are flagged to the approver. | 2 |
| CT-12 | On "Joined", convert the candidate into an employee record and start onboarding automatically. | 2 |
| CT-13 | Reminders to interviewers with pending feedback after 24 hours. | 2 |

### 4. Resume Bank

Every candidate ever considered stays searchable for future requisitions. The Resume Bank and Candidate Tracker share one candidate record, so there is no double entry.

| ID | Requirement | Phase |
| --- | --- | --- |
| RB-01 | Central store of all CVs (PDF, DOC, DOCX) linked to the candidate record. | 1 |
| RB-02 | Status categories: Shortlisted, Not shortlisted, No-show, Offer declined, Resigned, Terminated, plus Hired and Blacklisted. | 1 |
| RB-03 | Status is updated automatically from the Candidate Tracker and from employee exits. | 1 |
| RB-04 | Full-text search inside CV content, plus filters: skills, experience range, location, current CTC, notice period, status, last contacted date. | 1 |
| RB-05 | Candidate history: every application, requisition, stage reached, interview feedback and outcome, in one timeline. | 1 |
| RB-06 | Tags and notes (for example "good for future data roles"). | 1 |
| RB-07 | Rehire eligibility flag for ex-employees, set during offboarding. | 2 |
| RB-08 | Add a Resume Bank candidate to an open requisition in one click. | 1 |
| RB-09 | CV parsing: extract name, email, phone, skills, experience and employers from uploaded CVs to pre-fill the record. | 3 |
| RB-10 | Bulk CV upload (zip or multiple files). | 2 |
| RB-11 | Data retention: configurable period after which inactive candidate data is flagged for deletion, and a consent record per candidate. | 2 |
| RB-12 | Import existing candidate data from HR's Excel sheets, with duplicate checks and an error report. | 1 |

### 5. Onboarding

Takes a new joiner from offer acceptance to fully set up, with nothing tracked on paper.

**Offer and appointment letters**

| ID | Requirement | Phase |
| --- | --- | --- |
| ON-01 | Letter templates (Offer Letter, Appointment Letter, and others) built in a rich-text editor with merge fields such as {{candidate\_name}}, {{designation}}, {{ctc}}, {{joining\_date}}. | 2 |
| ON-02 | Generate the letter from the candidate or employee record; HR can edit before sending. | 2 |
| ON-03 | Optional approval step before a letter is released. | 2 |
| ON-04 | Output as PDF on company letterhead, with a salary annexure table generated from the compensation breakup. | 2 |
| ON-05 | Candidate accepts or declines by e-signature through a secure link, or signs a printed copy that HR scans and uploads; either way the signed PDF is stored on the record. | 2 |
| ON-06 | Offer expiry date with automatic reminder and "expired" status. | 2 |
| ON-07 | Every letter version kept; sent letters cannot be edited, only reissued. | 2 |

**Checklist, documents and tasks**

| ID | Requirement | Phase |
| --- | --- | --- |
| ON-08 | Onboarding checklist templates by department, location or employment type. | 2 |
| ON-09 | Tasks split by owner: new joiner, HR, IT (laptop, email account), manager (buddy, first-week plan), admin (ID card, seat). | 2 |
| ON-10 | Each task has an assignee, due date relative to joining date (for example joining date minus 3 days), and status. | 2 |
| ON-11 | Pre-joining document collection through a secure link: ID proof, address proof, education certificates, previous employment letters, bank details, photo. | 2 |
| ON-12 | HR verifies each document (approve / request re-upload with reason). | 2 |
| ON-13 | Completion dashboard: per joiner percentage complete, overdue tasks highlighted, filter by joining date. | 2 |
| ON-14 | Automatic emails: welcome mail before day one, task reminders to owners, overdue escalation to HR. | 2 |
| ON-15 | Probation period set on joining (default 3 months, configurable per country and grade); the end date drives probation review reminders (PM-07). | 2 |

### 6. Offboarding (new)

Mirrors onboarding for exits, so nothing is missed when someone leaves.

| ID | Requirement | Phase |
| --- | --- | --- |
| OFF-01 | Record a resignation or termination: date, reason category, last working day calculated from the notice period (default 30 days, configurable). | 2 |
| OFF-02 | Exit checklist templates with tasks for IT (revoke access), admin (assets, ID card), finance (full and final), manager (handover). | 2 |
| OFF-03 | Asset return tracked against the employee's issued assets. | 2 |
| OFF-04 | Exit interview form; responses reported in aggregate only. | 2 |
| OFF-05 | Generate relieving and experience letters from templates. | 2 |
| OFF-06 | On completion, employee status becomes Resigned or Terminated and the Resume Bank status updates, with a rehire eligibility flag. | 2 |

### 7. Attendance Tracking

Records daily attendance and shows it to HR and managers. Biometric devices are the main source; their export files are imported automatically.

| ID | Requirement | Phase |
| --- | --- | --- |
| ATT-01 | Primary: scheduled import of the biometric devices' export files from all 3 offices, with an error report for unmatched employee IDs. Fallbacks: web check-in/check-out for remote staff, and HR bulk entry or Excel upload. | 3 |
| ATT-02 | Shift definitions: start/end time, grace period, half-day threshold, weekly offs and time zone, per location. | 3 |
| ATT-03 | Daily status per employee: Present, Absent, Half day, Late, On leave, Holiday, Weekly off, Work from home, On duty. | 3 |
| ATT-04 | Leave and holidays apply automatically, so an approved leave never shows as absent. | 3 |
| ATT-05 | Regularisation requests (missed punch, forgot to check in) approved by the line manager. | 3 |
| ATT-06 | Monthly attendance lock by HR; locked months cannot be edited without HR Admin override (logged). | 3 |
| ATT-07 | Manager view: today's team status and monthly calendar per team member. | 3 |
| ATT-08 | Reports: monthly muster, late-coming, absenteeism, overtime; export to Excel and payroll format. | 3 |
| ATT-09 | Alerts to managers for unplanned absence beyond a configurable number of days. | 3 |

### 8. Leave Management (new)

Attendance is only accurate with leave tracked in the same system.

| ID | Requirement | Phase |
| --- | --- | --- |
| LV-01 | Configurable leave types (casual, sick, earned, maternity, paternity, unpaid, comp-off) with rules per type and per country. | 3 |
| LV-02 | Accrual rules: monthly or yearly credit, carry-forward limits, encashment, pro-rata for new joiners, probation restrictions. | 3 |
| LV-03 | Employee applies (full or half day) with reason; sees balance before submitting. | 3 |
| LV-04 | Line manager approves or rejects from the portal or email; HR can override. | 3 |
| LV-05 | Holiday calendar per location. | 3 |
| LV-06 | Team leave calendar for managers to spot overlaps. | 3 |
| LV-07 | Approved leave optionally added to the employee's Outlook calendar as out-of-office. | 3 |
| LV-08 | Year-end leave processing (carry forward, lapse) with a preview before it runs. | 3 |

### 9. Performance Management

Keeps a continuous performance record per employee, plus formal review cycles.

| ID | Requirement | Phase |
| --- | --- | --- |
| PM-01 | Goals or KPIs per employee with weightage, target and due date; set by manager or employee and agreed by both. | 3 |
| PM-02 | Review cycles configured by HR and assigned per team: annual or half-yearly, plus probation reviews. | 3 |
| PM-03 | Review flow: employee self-assessment, then manager review and rating, then optional HR / skip-level calibration, then employee acknowledgement. | 3 |
| PM-04 | Configurable rating scale (for example 1–5) and review form per department or grade. | 3 |
| PM-05 | Continuous feedback: managers log notes or recognition at any time, kept on the record. | 3 |
| PM-06 | Performance Improvement Plans (PIP), detailed in PIP-01 to PIP-09 below. | 3 |
| PM-07 | Probation review form triggered before probation end date; outcome is Confirm, Extend or Terminate and drives the matching email template. | 2 |
| PM-08 | Full performance history per employee across cycles. | 3 |
| PM-09 | Reports: rating distribution by department, review completion status, overdue reviews. | 3 |

**Performance Improvement Plans (PIP)**

A PIP gives an underperforming employee clear goals and support for a fixed period, and gives HR a documented record if the outcome is termination.

| ID | Requirement | Phase |
| --- | --- | --- |
| PIP-01 | Manager or HR starts a PIP from the employee's record, linked to the review ratings or feedback notes that prompted it. HR Admin approves before the employee sees it. | 3 |
| PIP-02 | Plan contents: performance gaps, measurable goals with targets, support offered (training, mentoring), duration (30, 60 or 90 days), check-in schedule, and consequences if goals are not met. | 3 |
| PIP-03 | PIP templates per role or department to keep plans consistent. | 3 |
| PIP-04 | Employee acknowledges the PIP by e-signature or a signed printed copy, and can add their own comments. | 3 |
| PIP-05 | Check-ins scheduled as Outlook calendar invites; manager records progress per goal at each check-in, and the employee can respond. | 3 |
| PIP-06 | Reminders to manager and HR before each check-in and before the end date; missed check-ins escalate to HR. | 3 |
| PIP-07 | Outcomes: Completed successfully, Extended (with a new end date and reason), or Not met. Not met starts the termination workflow in Offboarding, with the PIP attached as evidence. | 3 |
| PIP-08 | Visible only to the employee, their manager and HR; every view is logged in the audit log. | 3 |
| PIP-09 | PIP history stays on the employee's performance record; HR dashboard lists active PIPs by department with days remaining and overdue check-ins. | 3 |

### 10. Email Template System

HR edits templates without developer help; fields fill automatically from the record the email is sent from.

| ID | Requirement | Phase |
| --- | --- | --- |
| ET-01 | Rich-text template editor with subject line, body, and attachments (for example the generated offer letter PDF). | 1 |
| ET-02 | Merge fields picked from a list ({{first\_name}}, {{designation}}, {{interview\_date}}, {{panel\_names}}, {{joining\_date}}, {{manager\_name}} and others); missing values block sending with a warning. | 1 |
| ET-03 | Preview with real data and a send-test-to-myself option. | 1 |
| ET-04 | HR can edit the filled email before sending. | 1 |
| ET-05 | Bulk send (for example rejection to 30 candidates), each personalised. | 1 |
| ET-06 | Template categories, active/inactive, and version history. | 1 |
| ET-07 | Some templates send automatically on an event (see Trigger column), with an on/off switch per template. | 2 |
| ET-08 | Every sent email is logged on the candidate or employee record. | 1 |

Starter templates:

| Template | Trigger | Sent to |
| --- | --- | --- |
| Interview confirmation | Interview scheduled or rescheduled | Candidate (panel gets calendar invite) |
| Interview reminder | 24 hours before interview | Candidate |
| Rejection notification | Status set to Rejected | Candidate |
| Offer mail | Offer letter generated | Candidate |
| Onboarding welcome | Offer accepted / 3 days before joining | New joiner |
| Document request | Onboarding started | New joiner |
| Probation completion notice | Probation review outcome = Confirm | Employee, manager in CC |
| Probation extension notice | Probation review outcome = Extend | Employee, manager in CC |
| Termination communication | Manual only, HR Admin | Employee |
| Resignation acknowledgement | Resignation recorded | Employee |
| Requisition approval request | Requisition submitted | Approver |
| PIP initiation notice | PIP approved by HR Admin | Employee, manager in CC |
| PIP outcome notice | PIP closed as completed or extended (Not met goes through the manual termination email) | Employee, manager in CC |

Termination emails are never sent automatically and require HR Admin role.

### 11. Outlook Integration

Connects through Microsoft Graph API to the company's Microsoft 365 tenant.

| ID | Requirement | Phase |
| --- | --- | --- |
| OL-01 | Single sign-on with Microsoft Entra ID (company Microsoft login); no separate passwords. | 1 |
| OL-02 | Send portal emails from both a shared HR mailbox (for example hr@company.com) and the logged-in user's own mailbox; each template sets a default sender that HR can change before sending. | 2 |
| OL-03 | Sent emails appear in the Outlook Sent folder. | 2 |
| OL-04 | Candidate replies to portal emails are matched to the candidate record and shown in the email history. | 2 |
| OL-05 | Interview scheduling creates Outlook calendar events for the panel, with a Teams meeting link when mode is online. | 2 |
| OL-06 | Reschedule or cancel from the portal updates the Outlook event. | 2 |
| OL-07 | Free/busy lookup for panel members when choosing an interview slot. | 2 |
| OL-08 | Approved leave added to the employee's calendar (optional, per company setting). | 3 |

### 12. Dashboards and Reports (new)

| ID | Requirement | Phase |
| --- | --- | --- |
| DB-01 | HR home dashboard: headcount, joiners and leavers this month, open requisitions, offers pending, today's attendance, pending approvals. | 3 |
| DB-02 | Recruitment metrics: time to hire, time to fill, offer acceptance rate, candidates per stage (funnel), source effectiveness, interviewer feedback turnaround. | 3 |
| DB-03 | Workforce metrics: headcount trend, attrition rate (voluntary and involuntary), tenure distribution, department and location split. | 3 |
| DB-04 | Manager dashboard limited to their team. | 3 |
| DB-05 | All reports filterable by date range, department and location, and exportable to Excel. | 3 |

### 13. Employee Self-Service (new)

| ID | Requirement | Phase |
| --- | --- | --- |
| ESS-01 | View own profile; request changes to personal details (HR approves). | 3 |
| ESS-02 | Apply for leave, view balances, see own attendance and raise regularisation. | 3 |
| ESS-03 | Download own letters and documents (appointment letter, increment letters). | 3 |
| ESS-04 | Complete own onboarding tasks, self-assessments and policy acknowledgements. | 2 |
| ESS-05 | Submit resignation through the portal, which starts offboarding. | 3 |

### 14. Supporting features (new)

| ID | Requirement | Phase |
| --- | --- | --- |
| SUP-01 | Document expiry alerts (contracts, visas, certifications) to HR 30/15/7 days before expiry. | 2 |
| SUP-02 | Asset register: asset type, serial number, issued to, issue and return dates. | 2 |
| SUP-03 | Policy library with version tracking and employee acknowledgement status. | 3 |
| SUP-04 | Announcements to all employees or a department. | 3 |
| SUP-05 | In-app notification centre plus email notifications, with per-user preferences. | 1 |
| SUP-06 | Birthday and work-anniversary reminders to HR and managers. | 3 |
| SUP-07 | Payroll export of attendance, leave, joiners and leavers for the month, in a format matching the current payroll system. | 3 |
| SUP-08 | Global search across employees, candidates and requisitions (respecting permissions). | 1 |

### 15. Multi-country Support (new)

The company operates in Pakistan and the US, so country is a core setting rather than an afterthought.

| ID | Requirement | Phase |
| --- | --- | --- |
| MC-01 | Every employee belongs to a country, legal entity and office location; settings cascade from country to location. | 1 |
| MC-02 | Compensation stored in local currency (PKR or USD); reports can convert to one reporting currency at an HR-set rate. | 1 |
| MC-03 | HR access can be limited by country, so a Pakistan HR user sees only Pakistan employees unless granted more. | 1 |
| MC-04 | Separate letter and email templates per country, picked automatically from the person's country. | 2 |
| MC-05 | Onboarding document checklists per country (for example CNIC copy in Pakistan; Form I-9 and W-4 in the US). | 2 |
| MC-06 | Times shown in each user's local time zone; interview invites carry the correct zone for panel and candidate. | 2 |
| MC-07 | Retention and privacy settings per country. | 2 |
| MC-08 | Leave policies, holiday calendars, work weeks, and probation and notice defaults per country. | 3 |
| MC-09 | Payroll export per country or legal entity. | 3 |

## Non-functional requirements

HR data is among the most sensitive a company holds, so security and auditability are release blockers, not later improvements.

| ID | Area | Requirement |
| --- | --- | --- |
| NFR-01 | Authentication | Microsoft Entra ID single sign-on with MFA enforced by company policy. Sessions expire after 30 minutes idle. |
| NFR-02 | Authorisation | Every API call checks role and data scope on the server, never only in the UI. |
| NFR-03 | Encryption | HTTPS (TLS 1.2+) everywhere; database and file storage encrypted at rest; salary, bank and ID-number fields (CNIC, SSN) additionally encrypted at field level. |
| NFR-04 | Audit log | Log every create, update, delete, export, letter generation, email send and every view of salary data: who, what, when, old and new value. Retained 7 years, not editable. |
| NFR-05 | External links | Candidate and new-joiner links are single-purpose, signed, and expire (default 7 days). |
| NFR-06 | File uploads | Allowed types only (PDF, DOC/DOCX, JPG, PNG), 10 MB limit, malware scan before storage. |
| NFR-07 | Privacy | Consent captured for storing candidate data; configurable retention and deletion; export of a person's data on request. Rules must meet Pakistani and US requirements; confirm with legal counsel in each country. |
| NFR-08 | Performance | Pages load in under 2 seconds and Resume Bank search returns in under 3 seconds for 50,000 CVs. |
| NFR-09 | Scale | Supports 2,000 employees, 100 concurrent users and 50,000 candidate records without redesign. |
| NFR-10 | Availability | 99.5% during business hours; daily backups kept 30 days; restore tested quarterly. |
| NFR-11 | Usability | Responsive on desktop, tablet and phone; works in current Edge, Chrome, Safari and Firefox. |
| NFR-12 | Configurability | Departments, locations, leave types, shifts, pipeline stages, checklists, templates and approval chains are configured by HR Admin, not hard-coded. |
| NFR-13 | Environments | Separate development, test (with masked data) and production environments. |

## Core data model

Two records anchor everything: **Person** (anyone the company has dealt with) and **Employee** (a person while employed). A candidate who joins keeps one Person record, so their hiring history stays attached after they become an employee, and again if they leave and reapply.

| Entity | Holds | Links to |
| --- | --- | --- |
| Person | Name, contacts, CVs, consent, tags, Resume Bank status | Applications, Employee (if hired) |
| Employee | Employee ID, job details, status, probation dates | Person, Manager (Employee), Department, Location, Grade |
| Compensation | CTC, breakup, effective date (history kept) | Employee |
| Country / Location / Department / Grade | Organisation structure, legal entity, currency, time zone, policies | Employees, Shifts, Holiday calendars |
| Requisition | Role, openings, budget, approvals, status | Department, Hiring manager, Applications |
| Application | One person applying to one requisition; current stage; stage history | Person, Requisition, Interviews, Offer |
| Interview | Round, schedule, mode, Outlook event ID | Application, Panel (Employees), Scorecards |
| Scorecard | Ratings per criterion, recommendation, comments | Interview, Interviewer |
| Offer / Letter | Template used, generated PDF, versions, e-signature status | Application or Employee |
| Document | File, type, expiry date, verification status | Person or Employee |
| Checklist / Task | Onboarding or offboarding tasks, owner, due date, status | Employee, Assignee |
| Attendance Day | Date, status, in/out times, source | Employee, Shift |
| Leave Request / Balance | Type, dates, approval, balance ledger | Employee, Approver |
| Review Cycle / Review / Goal | Cycle settings, ratings, feedback, goals | Employee, Reviewer |
| Email Template / Email Log | Template versions; every sent email and reply | Person or Employee |
| Asset | Type, serial, issued and returned dates | Employee |
| Audit Entry | Actor, action, entity, old/new values, timestamp | Any entity |

## Delivery plan

Build in four phases so HR uses recruiting within 3–4 months instead of waiting 7–9 months for everything. Recruiting goes first because it has the most manual work today and no dependency on attendance or leave data.

&#91;embedded content: Delivery roadmap · 4 phases, 3 gates\]

Durations are planning estimates, not commitments; they firm up after Phase 0 answers the open questions. Attendance and leave go live on the first day of a month so balances and payroll exports start clean.

## Testing approach

Every requirement ID above maps to at least one test, and a phase ships only when its tests pass and HR signs off UAT.

| Level | What it covers | Tooling |
| --- | --- | --- |
| Unit | Business rules: leave accrual, notice-period dates, attendance status, merge-field filling, permission checks | xUnit (backend), Vitest (frontend) |
| API / integration | Every endpoint, including permission scoping (a manager calling another team's data must get 403) | xUnit with a test database (Testcontainers) |
| End-to-end | Key journeys per role: requisition to hire, onboarding to first day, leave request to approval, review cycle to acknowledgement | Playwright, run in CI on every merge |
| Outlook / Graph | Calendar invites, mail send, reply matching | Microsoft 365 developer tenant, never the live tenant |
| Security | OWASP Top 10, role-escalation attempts, expired-link access, file upload abuse | Automated scan in CI plus a third-party penetration test before go-live |
| Performance | Resume Bank search and dashboards at target data volumes (NFR-08, NFR-09) | k6 or Playwright-driven load scripts on seeded data |
| Data migration | Imported employee and candidate data matches the source spreadsheets | Reconciliation scripts plus HR spot checks |
| UAT | HR runs real scenarios in the test environment | Scripted UAT checklist per phase |

Role-based E2E tests should log in as each of the seven roles with seeded test users, since most serious HR-system defects are permission leaks rather than broken screens.

## Decisions and remaining questions

HR and IT answered the discovery questions on 12 Oct 2026; the requirements above now reflect these decisions.

| Topic | Decision | Effect on the spec |
| --- | --- | --- |
| Microsoft 365 | Company is on Microsoft 365 with Entra ID; IT can register the app and grant Graph admin consent | SSO and Outlook integration go ahead as planned |
| Sending mailbox | Both: shared HR mailbox and each user's own mailbox | Each template sets its default sender; HR can switch before sending (OL-02) |
| Hosting | Azure approved | Stack unchanged |
| Countries and offices | Pakistan and US, 3 office locations | New Multi-country section (15): per-country leave, holidays, letters, documents, currency and time zones |
| Attendance capture | Biometric devices today; they support file export | Scheduled file import is the primary method; web check-in only as a fallback (ATT-01) |
| Review cycles | Annual or bi-annual, depending on the team | Cycle frequency assigned per team (PM-02) |
| Probation and notice | Probation 3 months; notice period 30 days | Defaults of 3 months (ON-15) and 30 days (OFF-01), configurable per country and grade |
| Approvals | CEO approves requisitions and offers | Single-step CEO approval by default; chain stays configurable (REQ-02, CT-11) |
| Letter signing | E-signature and physical signature both acceptable | Both supported on every letter (ON-05) |
| Existing data | HR maintains Excel sheets | Excel import for employees and candidates (EMP-07, RB-12) |
| HR users | 10 | Well within NFR-09 limits |
| HR country access (13 Oct 2026) | HR Admins see every country by default; HR Executives see only their home country. An HR Admin can narrow or widen this per person, but never for themselves and never beyond their own countries | Reconciles the HR Admin "Full" access in the permission table with MC-03 |
| Compensation for HR Executives (13 Oct 2026) | "Granted only" is a per-person grant set by an HR Admin on the HR data access page | Enforced once compensation records exist (EMP-03) |
| Organisation structure (13 Oct 2026) | Departments are shared across countries; legal entities, office locations and grades each belong to one country | Department scope (e.g. hiring managers) spans both countries (MC-01) |
| Default time zones (13 Oct 2026) | Pakistan: Asia/Karachi. US: America/New_York as a placeholder; offices can override the country default | Revisit once the US state(s) of employment are confirmed |

**Still open**

- [ ] HR to share the current offer letter, appointment letter and email templates.
- [ ] HR to share leave types, accrual and carry-forward rules for Pakistan and the US.
- [ ] HR to confirm the payroll system and its import format.
- [ ] IT to share the biometric device make, model and a sample export file.
- [ ] Rating scale and review form for each team (goals, KPIs or competencies).
- [ ] Which US state(s) employees work in; state law affects leave, final pay, letter wording and the US default time zone.
- [ ] Legal review of letter wording and data retention under Pakistani and US law.
- [ ] Are the Pakistan and US operations separate legal entities? This affects letterhead, payroll export and reports.

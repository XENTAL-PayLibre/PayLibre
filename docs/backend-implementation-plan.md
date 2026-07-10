# PayLibre — Backend Implementation Plan

> Scope: **backend only**. Frontend (school dashboard, parent app) is built against the API here.
> Derived from the attached UI screens + the shared Drive file list, and grounded in the existing
> `PayLibre` skeleton and the **Xental** API that PayLibre consumes.

## 0. Source material & access note

- I can read the Drive folder's **file listing** (link-shared) but **not the individual Google Docs'
  contents** — this environment has no Drive/Docs integration, and the per-file IDs aren't in the
  page markup, so the export URLs aren't reachable. To fold the docs in, export the key ones
  (`XENTAL (PayLibre) PRD`, `PayLibre Merchant Lifecycle`, `XENTAL API Docs`, `USERSTORIES and AC's`)
  to **PDF/Markdown into `PayLibre/docs/`**, or paste them — I'll then tighten this. Points I inferred
  rather than read are marked **⚠️ confirm**.
- Built from: the **UI screenshots**, the **PayLibre skeleton** (Clean Architecture .NET 10 —
  `Domain / Application / Infrastructure / Api`, currently just Health + DI), and the **Xental public
  API** (I have first-hand knowledge of it).

---

## 1. The integration model (the decision that shapes everything)

**PayLibre is a product built *on top of* Xental — a Xental API consumer, exactly like any external
developer.** It is **not** a payments platform and does **not** integrate with Nomba or any bank.

- PayLibre holds **Xental API credentials** (client id + secret) and calls the **Xental public API**.
- Its **only external dependency is Xental.** No Nomba, no direct bank calls, no reconciliation
  engine, no webhook-signature-of-Nomba — **Xental does all of that.**
- PayLibre learns about payments by receiving **Xental's outbound webhook events** (e.g.
  `deposit.reconciled`) at a PayLibre endpoint it registers with Xental.

So PayLibre's backend = **its own school/fee domain + a thin Xental client + a Xental-webhook
receiver + attribution + dashboards + the parent-app API.** Everything money-related is delegated.

```
Parent bank transfer ─▶ Nomba ─▶ Xental (DVA + reconciliation + settlement)
                                     │  (outbound webhook: deposit.reconciled)
                                     ▼
                                  PayLibre  ── attributes credit to a student's fees,
                                     │           updates dashboards, notifies guardian
   School dashboard / Parent app ◀──┘
```

### How PayLibre maps onto Xental primitives

| PayLibre concept | Xental primitive it uses | Xental call |
|---|---|---|
| **School** | **Sub-merchant** (segments customers + routes settlement to the school's bank) | `POST /api/v1/sub-merchants` (+ payout account) — **⚠️ confirm (Q1)** |
| **Student** | **Virtual account** (persistent NUBAN), one per student | `POST /api/v1/virtual-accounts` with `accountRef = student id`, `subMerchantRef = school` |
| **Student's fees** | *(PayLibre-owned — Xental has no "fee" concept)* | tracked in PayLibre's DB |
| **A parent's payment** | reconciled inflow to the student's DVA | received via **Xental webhook** `deposit.reconciled` |
| **Paying the school** | settlement / sub-merchant payout to the school bank | Xental split-settlement / payout — **⚠️ confirm (Q1)** |
| **PayLibre's own cut** | a split leg to PayLibre on settlement | Xental split-settlement — **⚠️ confirm (Q6)** |

This is exactly what Xental's **sub-merchants + split-settlement + reusable DVAs + outbound webhooks**
were built for — PayLibre is a clean, real-world validation of them.

---

## 2. Domain model (PayLibre's own database)

Everything is tenant-owned by `School`. Money is **integer kobo**. PayLibre also stores the **Xental
handles** (ids/refs) it gets back, so it never re-derives them.

| Entity | Key fields | Notes |
|---|---|---|
| **School** (tenant) | name, officialEmail, phone, settlementBankCode, settlementAccountNumber, status, `xentalSubMerchantRef` | On register, PayLibre creates a Xental sub-merchant + payout account and stores the ref. |
| **SchoolUser** | schoolId, email, passwordHash, role (Owner/Admin/Bursar) | Dashboard login (PayLibre's own auth). |
| **AcademicSession / Term** | label (`2026/2027`), term (First/Second/Third), isCurrent | ⚠️ confirm scope (Q7). |
| **Class** | schoolId, name (`SS1`), sessionId | "Add class" step. |
| **Student** | schoolId, admissionNo, fullName, classId, sessionId, guardianName, guardianPhone, guardianEmail, `xentalAccountRef`, `nuban`, `bankName`, `accountName` | On create → PayLibre calls Xental to provision a DVA and caches the NUBAN details for display. |
| **Guardian/Parent** | name, phone, email, passwordHash, [studentLinks] | Parent-app identity. ⚠️ confirm linkage (Q4). |
| **FeeCategory** | schoolId, name (Tuition, PTA…) | Create-Fee "Category". |
| **Fee** (definition) | schoolId, name, categoryId, sessionId, classId, term, amountKobo, dueDateUtc | Applied to a class for a term. |
| **StudentFee** (invoice) | schoolId, feeId, studentId, amountKobo, amountPaidKobo, status (Pending/Partial/Paid/Overdue), dueDateUtc | Per-student instance; **what attribution settles against.** |
| **Payment** | schoolId, studentId, `xentalTransactionRef`, amountKobo, occurredAtUtc, raw event | Mirror of a Xental `deposit.reconciled` event (idempotent on the Xental ref). |
| **FeeAllocation** | paymentId, studentFeeId, amountKobo | Which payment paid which invoice → receipts + audit. |

**Fee fan-out:** creating a `Fee` for `SS1 / First Term` generates one `StudentFee` per student in
that class — the rows behind the Fees summary cards and the per-fee student table.

**No** `VirtualAccount`, `Transaction`-reconciliation, `Settlement`, or Nomba tables of PayLibre's
own — those live in Xental; PayLibre only stores the refs + a cached copy of what it displays.

---

## 3. Money & data flow (sequences)

**Enrolment (CSV import or parent self-enrolment):**
1. Student created in PayLibre → `POST /virtual-accounts` to Xental (`accountRef`, `name`,
   `subMerchantRef` = school). 2. Store returned NUBAN/bank/name on the student. 3. Deliver account
details to the guardian (SMS/email).

**Payment (fully delegated + event-driven):**
1. Parent transfers into the student's NUBAN. 2. Nomba → **Xental** reconciles. 3. Xental fires
`deposit.reconciled` to PayLibre's registered webhook. 4. PayLibre verifies the **Xental** signature,
records a `Payment` (idempotent on the Xental transaction ref), resolves the student by `accountRef`,
and **attributes** the credit to that student's open `StudentFee`s **oldest-due-first**, writing
`FeeAllocation`s and updating invoice status. 5. Emit receipt + guardian notification; update
dashboard.

**Settlement:** Xental settles collected funds to each **school's** bank via sub-merchant
split-settlement (optionally routing PayLibre's fee as a split leg). PayLibre configures this once
per school and otherwise just reports it. **⚠️ confirm (Q1/Q6).**

**Attribution policy = oldest-due-first** (your call): a lump sum cascades across outstanding
invoices by due date; overpayment carries forward as student credit against the next fee.
**⚠️ confirm overpayment handling (Q3).**

---

## 4. PayLibre API surface (its own API — from the UI)

Admin/dashboard plane = PayLibre session auth. Parent app = PayLibre bearer auth. Plus one inbound
endpoint **from Xental**. All money in kobo.

### School onboarding & auth
- `POST /schools/register` — name, email, phone, settlementBank, accountNumber, password → School +
  Owner user, **and provisions the Xental sub-merchant + payout account** *(wizard step 1)*
- `POST /auth/login`, refresh, logout · `GET/PUT /schools/me`

### Classes *(wizard step 2)*
- `POST /classes` · `GET /classes` · `GET/PUT/DELETE /classes/{id}`

### Students *(wizard step 3 + Student Directory)*  — every create provisions a Xental DVA
- `POST /students` · `GET /students` (filter by class/status) · `GET/PUT/DELETE /students/{id}`
- `POST /students/import` — **CSV bulk** (MVP onboarding #1); one Xental DVA per row
- `GET /students/{id}/virtual-account` — cached NUBAN card · `POST /students/{id}/virtual-account/send` (SMS/email)

### Parent self-enrolment *(MVP onboarding #2 — no code on the school's site)*
- `GET /enrol/{schoolCode}` — public school/enrol context · `POST /enrol/{schoolCode}` — parent
  registers a child (name, class, guardian) → student + Xental DVA created, pending school approval
  **⚠️ confirm approval flow (Q2)**

### Fee categories & fees *(wizard step 4 + Fees pages)*
- `GET/POST/PUT/DELETE /fee-categories`
- `POST /fees` (name, category, session, class, term, amount, dueDate) → **fans out StudentFees**
- `GET /fees` (+ collected/outstanding summary) · `GET /fees/{id}` (per-student breakdown) ·
  `PUT/DELETE /fees/{id}` · `GET /fees/summary`

### Payments, receipts
- `GET /payments` · `GET /payments/{ref}` · `GET /students/{id}/payments` · `GET /receipts/{allocationId}`

### Dashboard & reports
- `GET /dashboard/overview` — revenue, total students, collected, outstanding, counts,
  revenue-growth series, recent transactions
- `GET /reports/collections`, `/reports/outstanding`, exports

### Parent app
- `POST /parent/auth/login` · `GET /parent/dashboard` (student(s), DVA card, pending fees)
- `GET /parent/students/{id}/fees` · `GET /parent/fees/{studentFeeId}/payment-details` (bank, amount,
  account no/name) · `POST /parent/fees/{studentFeeId}/mark-paid` (soft nudge; reconciliation is
  automatic via Xental) · `GET /parent/receipts` · `GET /parent/history`

### Inbound from Xental
- `POST /webhooks/xental` — receives `deposit.reconciled` (+ reversal/settlement events); verifies
  the Xental signature; drives attribution. **This replaces any Nomba webhook.**

---

## 5. Xental integration layer (the only external adapter)

A single `XentalClient` in `PayLibre.Infrastructure`:
- **Auth:** exchange `XENTAL_CLIENT_ID` + `XENTAL_CLIENT_SECRET` at `POST /api/v1/auth/token` for a
  bearer token; cache it; refresh on expiry/401 (same pattern as the Xental MCP server).
- **Consumes:** `/sub-merchants` (+ payout), `/virtual-accounts` (create/get), `/webhook-endpoints`
  (register PayLibre's receiver on boot/first-run), split-settlement config, `/transfers/banks` +
  bank lookup (validate a school's settlement account at registration), `/transactions` (backfill/
  reconcile-on-demand if a webhook is missed).
- **Receives:** the outbound webhook at `/webhooks/xental` — verify signature, dedupe on the Xental
  ref, hand to the attribution service.
- **Resilience:** ret/backoff on 5xx, idempotency keys on money-relevant POSTs (Xental transfers are
  idempotent on a caller ref), and a reconcile-on-demand fallback (`GET /transactions`) so a dropped
  webhook never loses a payment.

Config: `XENTAL_API_BASE` (staging vs prod), `XENTAL_CLIENT_ID/SECRET`, `XENTAL_WEBHOOK_SECRET`.

---

## 6. Phased delivery (BE)

- **Phase 0 — Foundations & Xental client:** EF Core + Postgres, multi-tenant scaffolding, PayLibre
  auth, and the `XentalClient` (token auth + bank lookup). `POST /schools/register` → create Xental
  sub-merchant + payout account. Register PayLibre's webhook endpoint with Xental.
- **Phase 1 — Enrolment core:** Classes, Students (+ **Xental DVA per student**), **CSV import**,
  Student Directory + detail, account-details delivery.
- **Phase 2 — Parent self-enrolment:** school code/QR, public enrol endpoint, approval flow.
- **Phase 3 — Fees & billing:** FeeCategories, Fees + **fan-out to StudentFees**, list/detail/summary.
- **Phase 4 — Payments (event-driven):** `/webhooks/xental` receiver → **oldest-due-first
  attribution** → receipts + guardian notifications; payments list; reconcile-on-demand fallback.
- **Phase 5 — Dashboard & reports:** overview aggregation, revenue-growth series, exports.
- **Phase 6 — Parent app API:** auth, dashboard, pending fees, payment details, receipts, history.

Each phase: domain + service + controller + xUnit/SQLite tests (with the `XentalClient` faked), then
ship behind the existing pipeline. **End-to-end proof** uses Xental's **sandbox deposit simulator**
(`POST /sandbox/simulate/deposit` with a test key) to fire a real `deposit.reconciled` at PayLibre
with zero money.

---

## 7. Decisions locked & questions still open

### Locked (your answers)
- **Onboarding MVP:** CSV bulk import **and** parent self-enrolment (both in scope).
- **DVA model:** one reusable Xental DVA **per student**.
- **Attribution:** **oldest-due-first**.
- **Build model:** PayLibre **consumes the Xental API** (gets keys from Xental); no Nomba, no ported
  payment core. Xental owns reconciliation/settlement and its own webhook.

### Still open
- **Q1 — School settlement routing.** Is each school a **Xental sub-merchant** with its own payout
  bank (so Xental settles each school directly — recommended), or does PayLibre collect to one
  account and remit itself? This decides the whole settlement design.
- **Q2 — Parent self-enrolment authority.** Must the school **approve** a self-enrolled student
  before a DVA/fees attach, or is enrolment immediate? Who owns the roster of record?
- **Q3 — Overpayment / credit.** After oldest-due-first, does surplus **carry forward** as student
  credit, sit as an unallocated balance, or get refunded?
- **Q4 — Parent identity.** First-class Parent account (one login, children across schools) vs derived
  from each student's guardian fields? How is a parent linked to their child — OTP to guardian phone,
  invite link, or school-issued code?
- **Q5 — Missed-webhook policy.** Confirm PayLibre should **poll `GET /transactions`** as a fallback
  reconcile (recommended) in case a Xental webhook is dropped.
- **Q6 — Platform fee.** Does PayLibre take a per-transaction cut? If so, model it as a **split leg**
  on settlement (school gets the rest) — who bears it, school or parent?
- **Q7 — Sessions/terms & promotion.** Per-school or global? On session rollover, do students
  auto-promote and do unpaid fees carry over?
- **Q8 — KYB.** Does PayLibre (the Xental merchant) hold the single KYB, with schools as lighter
  sub-merchants, or must each school pass KYB before live collection? (Affects Phase 0 gating.)
- **Q9 — Notifications.** SMS, email, or both for account details, fee reminders, receipts — and does
  PayLibre send these itself or lean on Xental's notifications?

---

## 8. Immediate next steps (once Q1/Q2/Q6 are answered + I can read the Drive docs)
1. Confirm the School→sub-merchant→payout mapping and the fee split.
2. Build Phase 0: `XentalClient` (token auth), `POST /schools/register` → Xental sub-merchant, and
   register PayLibre's webhook endpoint with Xental.
3. Phase 1–3 (enrolment + DVA-per-student + fees fan-out) with the Xental client faked in tests.
4. Phase 4 attribution, proven end-to-end via Xental's sandbox deposit simulator.

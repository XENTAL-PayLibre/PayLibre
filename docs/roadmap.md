# PayLibre — Improvements & New Features Roadmap

_Last updated: 2026-07-11_

PayLibre is a school fee-collection platform built as a **consumer of the Xental API**. Schools onboard as Xental sub-merchants, each student gets a dedicated Xental virtual account, and inbound bank transfers are reconciled automatically via Xental's `deposit.reconciled` webhook and attributed oldest-due-first.

This document is a prioritised backlog of improvements and new features on top of the current baseline. It is a planning artifact — nothing here is committed until we agree scope.

---

## 1. Where we are today (baseline)

Shipped and running on staging + production:

- **Auth** — school registration (auto-provisions Xental sub-merchant + payout), 2-step emailed-OTP sign-in (dashboard + parent), forgot/reset password, rotating refresh tokens, HttpOnly+Secure cookies **and** bearer tokens.
- **Multi-tenancy** — row-level isolation via `ITenantOwned` + global query filter (deny-by-default on empty tenant).
- **Enrolment** — classes, students (each auto-provisioned a Xental virtual account), CSV import, public join-code self-enrolment.
- **Fees** — fee categories, fees fanned out to per-student invoices, statuses (Pending/Partial/Paid/Overdue).
- **Payments** — idempotent reconciliation from Xental webhook (HMAC-verified), oldest-due-first attribution, fee allocations.
- **Dashboard** — collection overview, outstanding, collections-by-class.
- **Parent app** — children, fee account details, pending fees, payment history.
- **Notifications** — Resend email (virtual-account details, welcome, login code, password reset); SMS is config-gated (not yet enabled).
- Full Swagger docs; xUnit + integration test suite (34 tests); CI → GHCR → infra deploy; Traefik + Let's Encrypt TLS.

Everything below is **additive** to this baseline.

---

## 2. Guiding principles

1. **Xental is the only external dependency.** Any new money movement, KYC, or bank feature is added by consuming a Xental capability, not by integrating a third party directly.
2. **Additive & backward-compatible.** New endpoints and columns; no breaking changes to shipped contracts without versioning.
3. **Same security bar as Xental.** HttpOnly+Secure cookies, HMAC-verified webhooks, tenant isolation, rate limiting, no secret leakage.
4. **Test + deploy each phase** staging-first, then prod, before starting the next.

---

## 3. Prioritised roadmap

Each item lists **Value**, **Effort** (S/M/L), and **Dependencies**.

### Priority 0 — Close current gaps (do first)

| # | Feature | Value | Effort | Notes |
|---|---------|-------|--------|-------|
| 0.1 | **Enable SMS** (account number + login OTP fallback) | High | S | Provider creds already plumbed (`PAYLIBRE_SMS_*`); set key + sender ID, flip config, test delivery. |
| 0.2 | **Receipts** — auto-email/SMS a receipt on every reconciled payment | High | S | Hook into `ReconciliationService`; PDF or HTML receipt with school branding. |
| 0.3 | **Rotate leaked secrets** | High | S | Live Xental key + webhook secret that appeared in chat history. Operational, not a feature. |
| 0.4 | **Idempotency + retry hardening on webhook** | Med | S | Dead-letter + replay endpoint for missed/failed `deposit.reconciled` events; audit log of every webhook received. |

### Priority 1 — Revenue & collection lift

| # | Feature | Value | Effort | Notes |
|---|---------|-------|--------|-------|
| 1.1 | **Payment reminders** (scheduled) | High | M | Cron job: email/SMS parents with outstanding fees at T-7/T-1/overdue. Configurable cadence per school. |
| 1.2 | **Instalment / payment plans** | High | L | Split a fee into scheduled instalments; partial-payment attribution already exists — add a schedule + reminder per instalment. |
| 1.3 | **Discounts, scholarships & waivers** | Med | M | Per-student or per-class adjustments applied before invoice fan-out; audit trail of who approved. |
| 1.4 | **Late fees / surcharges** | Med | M | Auto-apply a configurable surcharge after due date; toggle per fee. |
| 1.5 | **Sibling / bulk pay** | Med | M | Parent pays across multiple children in one transfer; attribution splits by outstanding. Needs a "family" grouping. |
| 1.6 | **Card / USSD / QR payment options** | High | M | If Xental exposes non-transfer rails, surface them so parents aren't limited to bank transfer. |

### Priority 2 — School operations & trust

| # | Feature | Value | Effort | Notes |
|---|---------|-------|--------|-------|
| 2.1 | **Settlement / payout reporting** | High | M | Show schools their Xental settlements, platform fee deducted, and payout status; reconcile against collected. |
| 2.2 | **Role & permission expansion** | Med | M | Beyond Owner/Admin/Bursar: read-only accountant, class teacher (own class only), auditor. Fine-grained policies. |
| 2.3 | **Audit log** (who did what) | High | M | Immutable event log for fee changes, refunds, user invites, exports — for disputes and compliance. |
| 2.4 | **Refunds & reversals** | High | L | Bursar-initiated refund via Xental transfer; requires approval workflow + audit + reconciliation entry. |
| 2.5 | **Academic sessions / terms lifecycle** | Med | M | First-class term rollover: carry forward balances, archive prior sessions, promote students to next class. |
| 2.6 | **Bulk operations** | Med | S | Bulk fee assignment, bulk reminders, bulk student status changes, bulk export. |

### Priority 3 — Data, integrations & self-service

| # | Feature | Value | Effort | Notes |
|---|---------|-------|--------|-------|
| 3.1 | **Student data sync / SIS integration** | High | L | The original open question: let schools connect an existing student website without code. Options: (a) scheduled CSV/SFTP import, (b) read-only API + API keys for the school's system to push rosters, (c) a lightweight embeddable widget. Recommend starting with **API keys + push** and **scheduled CSV**. |
| 3.2 | **Public API + API keys for schools** | High | M | Scoped, rate-limited keys so a school's own systems can create students, read balances, and receive PayLibre webhooks. |
| 3.3 | **PayLibre → school webhooks** | Med | M | Forward `payment.received`, `invoice.paid` events to the school's endpoint (HMAC-signed, same pattern we consume from Xental). |
| 3.4 | **Reporting & exports** | High | S | CSV/Excel/PDF exports for collections, outstanding, per-class, per-term; scheduled email of reports to admins. |
| 3.5 | **Analytics dashboard v2** | Med | M | Trends over time, collection-rate benchmarks, forecast of expected collections, cohort/class comparisons. |

### Priority 4 — Parent & mobile experience

| # | Feature | Value | Effort | Notes |
|---|---------|-------|--------|-------|
| 4.1 | **Push notifications** (mobile) | Med | M | Payment confirmed, new fee, reminder — via device tokens. |
| 4.2 | **Parent payment history + downloadable receipts** | Med | S | Extend existing parent payment history with per-payment receipt download. |
| 4.3 | **Multi-guardian per student** | Med | M | Two parents/guardians linked to one child, both can view and pay. |
| 4.4 | **In-app support / dispute a payment** | Low | M | Parent flags a mis-attributed or missing payment → bursar queue. |

### Priority 5 — Platform hardening & scale

| # | Feature | Value | Effort | Notes |
|---|---------|-------|--------|-------|
| 5.1 | **Observability** | High | M | Structured logging, request tracing, metrics (collection volume, webhook lag), health + readiness probes, alerting. |
| 5.2 | **Background job infrastructure** | High | M | Prerequisite for reminders (1.1), reports (3.4), sync (3.1). Durable queue + scheduler (e.g. Hangfire/Quartz or a hosted worker). |
| 5.3 | **Rate-limit & abuse tuning** | Med | S | Per-tenant quotas, self-enrolment abuse protection (join-code throttling, CAPTCHA on public enrol). |
| 5.4 | **Data retention & GDPR/NDPR** | Med | M | Export-my-data, delete-my-account, retention policies, PII minimisation. |
| 5.5 | **Automated backups + restore drills** | High | S | Scheduled Postgres backups, tested restore, point-in-time recovery. |
| 5.6 | **Multi-region / DR readiness** | Low | L | Only if scale demands it. |

---

## 4. Suggested sequencing (quarters are indicative, not committed)

- **Now → next sprint (P0):** SMS, receipts, secret rotation, webhook hardening. Small, high-trust wins that make the money loop feel complete.
- **Then (P1 + platform prereqs):** stand up background jobs (5.2) → payment reminders (1.1) → instalment plans (1.2). Directly lifts collection rates.
- **Then (P2):** settlement reporting (2.1), audit log (2.3), refunds (2.4) — the things schools ask about before they trust a platform with money.
- **Then (P3):** SIS integration + public API (3.1–3.3) — unlocks larger schools with existing systems (the original onboarding question).
- **Ongoing (P5):** observability, backups, rate-limit tuning threaded through every phase.

---

## 5. Open questions (need your input)

1. **SIS integration (3.1):** which of the three connection models do we lead with — push-API, scheduled CSV/SFTP, or embeddable widget? (Affects P3 scope significantly.)
2. **Payment rails (1.6):** does the Xental account/plan expose card/USSD/QR, or is bank-transfer-into-DVA the only rail for now?
3. **Refunds (2.4):** does Xental support sub-merchant-initiated outbound transfers/refunds, and what's the approval bar we want (single bursar vs. dual-control)?
4. **Instalments (1.2):** do schools want fixed schedules (e.g. 3 equal payments) or flexible pay-what-you-can with deadlines?
5. **Branding:** do receipts/reminders need per-school logo + colours, or is a PayLibre-branded template acceptable for v1?

---

## 6. Explicitly out of scope (for now)

- Direct integrations with any provider other than Xental (payments, SMS aggregators are consumed through config, not custom integrations).
- A public marketplace / multi-product billing beyond school fees.
- Native mobile apps beyond the existing parent web/app surface (revisit after 4.1).

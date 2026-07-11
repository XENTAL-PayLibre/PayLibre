# PayLibre backend — Test plan (Phase 0 + 1)

Covers school registration + auth (Phase 0) and enrolment: classes, students with dedicated virtual
accounts, CSV import, account-details delivery (Phase 1). PayLibre talks only to Xental; in automated
tests the Xental client is faked, and the DB is SQLite in-memory.

## 1. Automated tests (`tests/PayLibre.Tests`) — 13 tests, all green

Run:
```bash
cd PayLibre
dotnet test
```

### Unit / service (SQLite in-memory + faked Xental)
| Test | Proves |
|---|---|
| `AuthServiceTests.Register_creates_school_owner_refresh_token_and_links_a_xental_submerchant` | Registration persists School (Active) + Owner + refresh token, and links the Xental sub-merchant with the resolved settlement name |
| `AuthServiceTests.Register_rejects_a_duplicate_email` | Duplicate email → 409 (Conflict) |
| `AuthServiceTests.Register_rolls_back_when_xental_setup_fails` | If Xental setup throws, **nothing** is persisted (atomic registration) |
| `AuthServiceTests.Login_succeeds_with_correct_password_and_fails_otherwise` | Password verify; wrong password → 401 |
| `AuthServiceTests.Refresh_rotates_the_token_and_revokes_the_old_one` | Refresh rotation; the old refresh token can't be reused |
| `EnrolmentServiceTests.Create_class_then_reject_a_duplicate` | Class CRUD + uniqueness (name+session) |
| `EnrolmentServiceTests.Create_student_provisions_a_dedicated_virtual_account` | Student create calls Xental → NUBAN cached on the student |
| `EnrolmentServiceTests.Duplicate_admission_number_conflicts` | Admission-number uniqueness per school |
| `EnrolmentServiceTests.Import_csv_creates_valid_rows_and_reports_the_bad_ones` | CSV import: 2 created, 2 errors (duplicate + unknown class), good rows still commit |
| `EnrolmentServiceTests.Send_account_details_invokes_the_notifier` | Account-details delivery hook |
| `TenantIsolationTests.A_school_cannot_see_another_schools_data` | Row-level tenant isolation via the global query filter |

### End-to-end over real HTTP (`WebApplicationFactory`, SQLite, faked Xental)
| Test | Proves |
|---|---|
| `ApiEndToEndTests.Register_login_and_enrol_a_student_over_http` | Full flow: `POST /auth/register` → **HttpOnly** `plb_access` cookie set → `GET /auth/me` authenticated by cookie → `POST /classes` → `POST /students` (DVA provisioned) → `GET /students` lists the student |
| `ApiEndToEndTests.Unauthenticated_requests_are_rejected` | `/students` and `/classes` return **401** with no session cookie |

Security assertions embedded: the session cookie is asserted **HttpOnly**; unauthenticated access is
**401**; tenant isolation is enforced at the data layer.

## 2. What is intentionally faked (and how it's covered instead)

- **Xental API** — faked in unit + E2E (`FakeXentalClient`). The real `XentalClient` (token auth,
  refresh-on-401, JSON mapping) is covered by the **manual live smoke** below, since it needs real
  Xental credentials.
- **Notifications** — `LoggingNotificationSender` logs; the interface call is asserted. Swap for a
  real SMS/email provider in a later phase.

## 3. Manual live smoke (staging, against real Xental sandbox)

Prereq: a Xental **test-mode** API key (client id + secret) configured on the PayLibre staging service
(`Xental:ClientId` / `Xental:ClientSecret`, `Xental:BaseUrl=https://api.staging.xental.online`).

Use a cookie jar so the HttpOnly session is reused:
```bash
BASE=https://api.paylibre.<staging-domain>
JAR=/tmp/plb.cookies

# 0. Health
curl -s $BASE/health            # 200

# 1. Banks (populates the settlement dropdown; sourced from Xental)
curl -s $BASE/api/v1/banks | head

# 2. Register a school (creates the Xental sub-merchant + payout; sets HttpOnly cookies)
curl -s -c $JAR -X POST $BASE/api/v1/auth/register -H 'Content-Type: application/json' -d '{
  "schoolName":"Demo Academy","officialEmail":"demo@school.test","phone":"08012345678",
  "settlementBankName":"<bank name>","settlementBankCode":"<bank code>",
  "settlementAccountNumber":"<10-digit acct>","password":"password1"}' -i | grep -i set-cookie
#   → expect plb_access + plb_refresh, both HttpOnly + Secure

# 3. Who am I (cookie auth)
curl -s -b $JAR $BASE/api/v1/auth/me

# 4. Create a class
CLASS=$(curl -s -b $JAR -X POST $BASE/api/v1/classes -H 'Content-Type: application/json' \
  -d '{"name":"SS1","session":"2026/2027"}' | python -c 'import sys,json;print(json.load(sys.stdin)["id"])')

# 5. Create a student → provisions a real (sandbox) DVA via Xental
curl -s -b $JAR -X POST $BASE/api/v1/students -H 'Content-Type: application/json' -d "{
  \"admissionNo\":\"ADM-001\",\"fullName\":\"Ada Lovelace\",\"classId\":\"$CLASS\",
  \"guardianName\":\"Mrs Lovelace\",\"guardianEmail\":\"mum@x.com\"}"
#   → hasVirtualAccount:true, nuban populated

# 6. CSV import (save a file first with the header row)
printf 'AdmissionNo,FullName,Class,Session,GuardianName,GuardianPhone,GuardianEmail\nADM-100,Grace Hopper,SS1,2026/2027,Mr Hopper,08000000000,dad@x.com\n' > students.csv
curl -s -b $JAR -X POST $BASE/api/v1/students/import -F file=@students.csv

# 7. Directory + a student's account card
curl -s -b $JAR "$BASE/api/v1/students"
curl -s -b $JAR "$BASE/api/v1/students/<id>/virtual-account"

# 8. Prove end-to-end reconciliation with zero money (Xental sandbox simulator, using the
#    student's accountRef stu_<id>): drive a deposit and confirm Xental reconciles it.
#    (Attribution to fees lands in Phase 4; here we confirm the DVA is live + reconciles.)

# 9. Negative checks
curl -s -o /dev/null -w '%{http_code}\n' $BASE/api/v1/students          # 401 (no cookie)
curl -s -b $JAR -X POST $BASE/api/v1/auth/logout                        # 204, clears cookies
```

Verify in Xental (as the PayLibre merchant): the sub-merchant exists with the school's payout bank,
and one virtual account exists per student under it.

## 4. Regression / CI

`dotnet test` runs on every push (add to the PayLibre build workflow). The suite is hermetic
(SQLite + faked Xental) so it needs no network or database.

## 5. Not yet covered (later phases)
- Parent self-enrolment approval flow (Phase 2).
- Fees fan-out + oldest-due-first attribution (Phase 3/4) — will be driven by the Xental
  `deposit.reconciled` webhook and proven via the sandbox simulator.
- Real SMS/email delivery.

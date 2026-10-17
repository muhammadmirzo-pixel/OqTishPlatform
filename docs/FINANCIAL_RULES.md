# Financial Rules

Status: **Phase 1 proposal**. Items marked OPEN need a business decision before Phase 5.
This document does **not** define the commission contract. It defines how whatever contract is
approved will be represented safely.

## 1. Separate concepts

| Concept | Record | Answers |
|---|---|---|
| Appointment status | appointments.status | Did the visit happen? |
| Service completion | service_completions | What was delivered and at what charged amount? |
| Payment | payments + payment_allocations | What money moved, by which method, collected by whom (clinic or platform)? |
| Ledger | journal_entries + ledger_lines | The append-only financial truth |
| Settlement | settlements + settlement_items | Which platform↔clinic obligations were settled |
| Daily summary | daily_financial_summaries | A rebuildable cache. Never the source of truth |

`Completed` permits a ServiceCompletion but doesn't create money by itself, because the charged
amount has to be confirmed.

## 2. Money

`decimal` / `numeric(19,2)` + currency. v1 is UZS only, and mixed-currency arithmetic is rejected.
A `Money` value object handles arithmetic, and an architecture test bans float/double. Rounding happens
**once per computed line**, and totals are sums of rounded lines. The rounding mode and precision are
OPEN (OD-F5). The proposal is AwayFromZero to 2 decimals.

## 3. Ledger

Double-entry. Each entry has ≥ 2 lines that sum to 0 per currency, enforced by a deferred constraint
trigger and a test.
- **Clinic operational book** (party = Clinic/Patient): PatientReceivable, ClinicCollectedCash/Card,
  ClinicServiceRevenue, ClinicRefundsPaid. The platform keeps this book *for* the clinic and doesn't
  claim the money.
- **Platform book** (party = Platform): PlatformCommissionReceivable, PlatformCommissionRevenue,
  PlatformCollectedFunds and PlatformPayableToClinic (only if the platform collects money, OD-F7),
  SettlementClearing.

Rules:
- **Append-only** via grants + trigger. Corrections use a reversal entry (`reverses_entry_id`) plus a
  new entry, both in the current open period.
- **Idempotent**: `source_event_key` UNIQUE (`payment:{id}:recorded`, `payment:{id}:refund:{rid}`,
  `completion:{id}:commission`). A replay returns the existing entry.
- Payment, allocation, ledger entry, outbox message and audit row are written in one transaction.
- A failed payment is stored with status Failed and posts **no** ledger lines.

## 4. Commission

- Rules carry the clinic, basis, rate or fixed amount, rounding, an effective range that can't overlap,
  a contract reference and the approver.
- **Basis is OPEN (OD-F1)**: booked, confirmed, completed (charged amount), collected payment, new
  patient, or subscription. **No default.** A clinic without an approved rule accrues **0** commission,
  and reports flag "no contract configured".
- Each commission line snapshots `commission_rule_id`, `rate_snapshot` and `basis_amount_snapshot`.
- The rule is chosen by **basis event time** (OD-F2). Refunds reverse commission proportionally in
  the refund period (proposal, OD-F3).

## 5. Recognition, periods, time zone

- Clinic revenue is recognised at ServiceCompletion (accrual). This is a proposal (OD-F4).
- **Reporting day = branch-local date** (IANA zone, default Asia/Tashkent), stored as `posting_date_local`.
- Refunds and corrections post in the period in which they occur. Closed periods are never restated,
  and "prior-period refunds" are shown separately.
- Period lock (OD-F6): the proposal is to lock a month once its settlement is Finalized.

## 6. Settlements

A settlement covers one clinic and one period, with states Draft → Finalized → Paid (or Draft → Voided).
`settlement_items.ledger_line_id` is UNIQUE, and `UNIQUE(clinic_id, period_start) WHERE status <> 'Voided'`
blocks duplicates. Creation locks the clinic row, and a concurrent attempt gets 409. Obligations come
only from approved-rule commission lines and recorded platform-collected funds. Without a contract,
the obligation is 0. Actions require `platform.finance.settle` and are audited.

## 7. Reconciliation

The daily summary is `SUM(ledger_lines)` by account for (branch, local_date), stored with a watermark.
A nightly job recomputes the trailing window, alerts on a mismatch, and overwrites the summary from
the ledger. Month = Σ days = Σ ledger, with exact decimal equality.

## 8. Worked test cases (fixture parameters, not the contract)

Branch Asia/Tashkent (UTC+5), UZS, 2 decimals, AwayFromZero.

| # | Scenario | Expected |
|---|---|---|
| F1 | Completion 500 000.00. Cash 200 000.00, then 300 000.00 | Receivable 300 000.00 → 0.00. Collected 500 000.00 |
| F2 | F1 + partial refund 100 000.00 | Net collected 400 000.00 |
| F3 | Fixture: 10 % of collected payments, F1 + F2 | Commission lines +20 000.00, +30 000.00, −10 000.00. Net **40 000.00**. rate_snapshot 0.1000 on each |
| F4 | Same payment idempotency key ×2, sequential and concurrent | 1 payment, 1 journal entry. The replay returns the original |
| F5 | 7.5 % of 333 333.33 | 24 999.99975 → **25 000.00** |
| F6 | 3 payments of 0.10 at 15 % | Each line 0.015 → 0.02. Total **0.06** (not round(0.30 × 0.15) = 0.05). Sum of lines is the rule |
| F7 | Fixture: 10 % until 2026-03-15 00:00 local, 8 % after. Completion 03-14 23:50, payment 03-15 00:10, basis = collected, 100 000.00 | 8 000.00, the 8 % rule snapshotted, posting date 2026-03-15 |
| F8 | Payment 2026-03-31 18:30 UTC (23:30 local) vs 19:30 UTC (00:30 local) | Posted 2026-03-31 vs **2026-04-01** |
| F9 | Refund in April of a March payment | March unchanged. April shows −refund as a prior-period refund |
| F10 | Failed payment 50 000.00 | Status Failed, 0 ledger lines |
| F11 | Cash 300 000.00 mistakenly entered as 30 000.00 | Reversal −30 000.00 + new 300 000.00. Original untouched. Audit row |
| F12 | Two users create the March settlement concurrently | 201 + 409. Each line appears in ≤ 1 settlement |
| F13 | Σ daily summaries (March) vs Σ ledger (March) | Exactly equal |
| F14 | Clinic without a rule, 10 paid completions | Commission 0.00, obligation 0.00, flagged |
| F15 | Refund > net collected on that payment | 422, nothing written |

## 9. Authorization

Each finance endpoint loads the payment or appointment and checks `payment.record`, `payment.refund`
or `finance.report.view` against **its** branch. Branch filters must lie within the caller's scope.
Settlements are clinic-scoped (owner/finance) or platform-scoped. Every write is audited.

## 10. Open accounting decisions

| Id | Decision | Impact if undecided |
|---|---|---|
| OD-F1 | Commission basis | No commission computable. Platform revenue = 0 |
| OD-F2 | Which event time selects the rule | Rate changes hit the wrong transactions |
| OD-F3 | Refund effect on commission | Over- or under-charging clinics |
| OD-F4 | Accrual vs cash recognition | Changes daily totals and receivables |
| OD-F5 | Rounding mode and precision (tiyin vs whole sum) | Small discrepancies with clinic books |
| OD-F6 | Period close/lock | Late postings silently change closed reports |
| OD-F7 | Does the platform collect money online (Payme/Click/Uzum) in v1? | Payable-to-clinic flows, provider reconciliation, PCI/licensing |
| OD-F8 | Is the quoted price binding? Discounts? | Completion validation and commission basis |
| OD-F9 | VAT/tax on commission | Invoice/settlement amounts. Needs an accountant |
| OD-F10 | Settlement frequency and terms | Scheduling and overdue logic |
| OD-F11 | NoShow fee? | A new charge type |

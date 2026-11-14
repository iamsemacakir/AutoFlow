# ADR 0018: Invoice aggregate — a document that only gets paid

- Status: Accepted
- Date: 2026-11-15

## Context

A completed work order has to be billed: net line totals plus VAT, a number
the tax office accepts, and a record of what the customer has paid. The work
order already knows its lines and has an `Invoiced` status waiting for this
(ADR 0010). Open questions: is an invoice a view on the work order or data of
its own, where VAT is calculated, and how payments are tracked.

## Decision

- **`Invoice` is its own aggregate and a snapshot.** `Invoice.IssueFor`
  copies the work order's lines (renumbered 1…n), the customer's name and a
  description of the vehicle, and keeps the VAT rate of the day. Nothing on
  an invoice changes afterwards — not when the customer is renamed, the car
  is sold or the rate changes — because an issued invoice is a legal
  document. It references the work order and the customer by id.
- **Only a `Completed` work order can be invoiced, once.** The factory
  checks the status; the use case then calls `WorkOrder.MarkInvoiced` in the
  same commit, so a second attempt finds the work order `Invoiced` (422).
- **Amounts.** Lines are net, as on the work order. VAT is calculated once
  on the net total and rounded half away from zero to cents
  (`VatAmount`), not per line, so the printed sum is exactly net + VAT.
  Totals are derived from the lines and never stored.
- **Numbering.** An invoice stores `Year` (UTC calendar year of issue) and
  `Sequence`; the printed `Number` (`2026-00042`) is derived from both.
  Who hands out the sequence is a persistence decision (ADR 0019).
- **Payments are part of the invoice.** `RecordPayment(amount, method,
  receivedAt)` appends an `InvoicePayment` and moves the status
  `Issued → PartiallyPaid → Paid`. A payment must be positive whole cents and
  must not exceed the open `Balance`; a paid invoice takes no more payments.
  `AmountPaid` is kept on the invoice itself so that every payment changes
  the root (one concurrency token for the whole aggregate, as in ADR 0011).
- **Due date and overdue.** `DueDate` is the issue date plus the payment
  term; `IsOverdue(today)` is a question asked with a date, not a stored
  status, so nothing has to run at midnight.
- **An invoice over 0.00** (goodwill work) is `Paid` the moment it is issued.

## Consequences

- Correcting an issued invoice needs a cancellation/credit note, which is
  out of scope for now; work orders stay editable until completion instead.
- The invoice can be printed without loading the work order, customer or
  vehicle.

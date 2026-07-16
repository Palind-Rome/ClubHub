# Issue 137 Award Approval Flow Notes

## Goal

Prepare the implementation work for issue #137: make award applications and
multi-step review records the authoritative source for award scores used by
member evaluations.

## Initial Scope

- Confirm the award application state machine before changing schema.
- Design `AWARD_APPLICATIONS` and `AWARD_REVIEW_RECORDS` with clear foreign key
  ownership and review history.
- Keep existing `EVALUATIONS` award records compatible while the new workflow is
  introduced.
- Plan how member evaluation award scores are calculated from approved and
  publicized award applications.

## State Machine Questions

- Who may submit: member self-application, club leader recommendation, or both.
- Whether rejected applications can be edited and resubmitted.
- Whether repeated review rounds need an explicit `review_round` field.
- Whether publicized applications can be withdrawn or corrected.
- Whether award score is derived from award rules or entered during final review.

## Working Plan

1. Document the agreed workflow and permissions.
2. Add database schema and migration scripts.
3. Extend OpenAPI contracts and backend endpoints.
4. Add frontend application, review, publication, and archive flows.
5. Wire approved award applications into member evaluation award-score
   aggregation.
6. Update verification scripts and add regression coverage for review-state
   transitions.

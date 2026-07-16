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

- Who may submit: both member self-application and club leader recommendation.
- Returned applications keep the same record and advance `review_round` when
  resubmitted.
- Rejected or withdrawn applications stop in history; a new application is needed
  only if the same award scheme is reopened by policy.
- Publicized applications can be corrected by publicity item status, but archived
  applications are the normal source for evaluation award scores.
- Award score and amount are primarily derived from `AWARD_LEVELS`; final review
  snapshots them into `AWARD_APPLICATIONS.final_award_score` and
  `final_amount`.

## School Portal Mapping

The Tongji scholarship portal shape maps into ClubHub as follows:

- `奖项申请` -> `AWARD_SCHEMES` plus `AWARD_LEVELS`, scoped by club,
  academic year and term.
- `我的申请` -> `AWARD_APPLICATIONS`, filtered by applicant and status chips
  such as reviewing, approved, returned and rejected.
- `申请详情` progress bar -> `AWARD_REVIEW_RECORDS` and
  `AWARD_APPLICATIONS.current_step`.
- `奖种详情` fields such as sponsor, amount, term and material notes ->
  `AWARD_SCHEMES` and `AWARD_LEVELS`.
- `奖学金公示` -> `AWARD_PUBLICITY_BATCHES` and `AWARD_PUBLICITY_ITEMS`.
- Member evaluation award score -> archived or publicized
  `AWARD_APPLICATIONS`, with `EVALUATION_AWARD_SOURCES` keeping the score
  provenance for each generated evaluation.

## Working Plan

1. Document the agreed workflow and permissions.
2. Add database schema and migration scripts.
3. Extend OpenAPI contracts and backend endpoints.
4. Add frontend application, review, publication, and archive flows.
5. Wire approved award applications into member evaluation award-score
   aggregation.
6. Update verification scripts and add regression coverage for review-state
   transitions.

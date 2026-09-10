import { beijingStoredDateTimeTimestamp } from "./beijingTime";

export type ActivityStatusInput = {
  status?: string | null;
  startTime?: string | Date | null;
  endTime?: string | Date | null;
  registrationDeadline?: string | Date | null;
};

export type ActivityDisplayStatus =
  | "draft"
  | "pending_review"
  | "published"
  | "registration_closed"
  | "ongoing"
  | "finished"
  | "rejected"
  | "cancelled"
  | "unknown";

const workflowStatuses = new Set<ActivityDisplayStatus>([
  "draft",
  "pending_review",
  "published",
  "registration_closed",
  "ongoing",
  "finished",
  "rejected",
  "cancelled",
]);

function timestamp(value?: string | Date | null) {
  if (value instanceof Date) return value.getTime();
  return beijingStoredDateTimeTimestamp(value);
}

function isFiniteTimestamp(value: number): value is number {
  return Number.isFinite(value);
}

/** Resolve stale `published` rows into the status users can act on today. */
export function resolveActivityDisplayStatus(
  activity: ActivityStatusInput,
  now = Date.now(),
): ActivityDisplayStatus {
  const status = (activity.status ?? "").trim().toLowerCase();
  const start = timestamp(activity.startTime);
  const end = timestamp(activity.endTime);
  const deadline = timestamp(activity.registrationDeadline);

  if (status === "published") {
    if (isFiniteTimestamp(start) && now >= start && (!isFiniteTimestamp(end) || now < end)) {
      return "ongoing";
    }
    if (isFiniteTimestamp(end) && now >= end) return "finished";
    if (isFiniteTimestamp(deadline) && now >= deadline) return "registration_closed";
    return "published";
  }

  if (status === "ongoing" && isFiniteTimestamp(end) && now >= end) return "finished";
  if (workflowStatuses.has(status as ActivityDisplayStatus)) {
    return status as ActivityDisplayStatus;
  }

  return "unknown";
}

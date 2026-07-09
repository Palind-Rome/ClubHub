const BEIJING_TIME_ZONE = "Asia/Shanghai";
const TIME_ZONE_SUFFIX_PATTERN = /(Z|[+-]\d{2}:?\d{2})$/i;
const LOCAL_DATE_TIME_PATTERN =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,3}))?)?$/;

const dateTimeFormatter = new Intl.DateTimeFormat("zh-CN", {
  timeZone: BEIJING_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
});

const dateFormatter = new Intl.DateTimeFormat("zh-CN", {
  timeZone: BEIJING_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

const timeFormatter = new Intl.DateTimeFormat("zh-CN", {
  timeZone: BEIJING_TIME_ZONE,
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
});

function normalizeLocalDateTime(value: string) {
  return value.trim().replace(" ", "T");
}

function formatterParts(formatter: Intl.DateTimeFormat, date: Date) {
  return Object.fromEntries(
    formatter
      .formatToParts(date)
      .filter((part) => part.type !== "literal")
      .map((part) => [part.type, part.value]),
  );
}

function reservationDate(value: string) {
  const normalized = normalizeLocalDateTime(value);
  return new Date(TIME_ZONE_SUFFIX_PATTERN.test(normalized) ? normalized : `${normalized}Z`);
}

export function beijingDateTimeTimestamp(value?: string) {
  if (!value) return Number.NaN;

  const match = LOCAL_DATE_TIME_PATTERN.exec(normalizeLocalDateTime(value));
  if (!match) return Number.NaN;

  const [, year, month, day, hour, minute, second = "0", millisecond = "0"] = match;
  return Date.UTC(
    Number(year),
    Number(month) - 1,
    Number(day),
    Number(hour) - 8,
    Number(minute),
    Number(second),
    Number(millisecond.padEnd(3, "0")),
  );
}

export function beijingDateTimeToUtcIso(value: string) {
  const timestamp = beijingDateTimeTimestamp(value);
  return Number.isFinite(timestamp) ? new Date(timestamp).toISOString() : value;
}

export function beijingCalendarDateTimestamp(date: Date) {
  const parts = formatterParts(dateFormatter, date);
  return beijingDateTimeTimestamp(`${parts.year}-${parts.month}-${parts.day}T00:00:00`);
}

export function beijingTodayStartTimestamp() {
  const parts = formatterParts(dateFormatter, new Date());
  return beijingDateTimeTimestamp(`${parts.year}-${parts.month}-${parts.day}T00:00:00`);
}

export function venueReservationTimestamp(value?: string) {
  if (!value) return Number.NaN;
  return reservationDate(value).getTime();
}

export function formatVenueReservationDateTime(value: string) {
  const date = reservationDate(value);
  if (Number.isNaN(date.getTime())) return "-";

  const parts = formatterParts(dateTimeFormatter, date);
  return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}`;
}

export function formatVenueReservationDate(value: string) {
  const date = reservationDate(value);
  if (Number.isNaN(date.getTime())) return "-";

  const parts = formatterParts(dateFormatter, date);
  return `${parts.year}-${parts.month}-${parts.day}`;
}

export function formatVenueReservationTime(value: string) {
  const date = reservationDate(value);
  if (Number.isNaN(date.getTime())) return "-";

  const parts = formatterParts(timeFormatter, date);
  return `${parts.hour}:${parts.minute}`;
}

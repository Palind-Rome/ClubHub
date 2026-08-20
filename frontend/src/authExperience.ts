export const STUDENT_NO_LENGTH = 7;
export const STAFF_NO_LENGTH = 5;
export const STUDENT_NO_MAX_LENGTH = Math.max(STUDENT_NO_LENGTH, STAFF_NO_LENGTH);

export function resolveIdentityLabel(studentNo?: string | null) {
  const normalized = (studentNo ?? "").trim();
  if (hasDigitLength(normalized, STUDENT_NO_LENGTH)) return "学生";
  if (hasDigitLength(normalized, STAFF_NO_LENGTH)) return "教师";
  return "";
}

export function resolvePostAuthPath(redirect: unknown) {
  if (
    typeof redirect === "string" &&
    redirect.startsWith("/") &&
    !redirect.startsWith("//") &&
    redirect !== "/auth"
  ) {
    return redirect;
  }

  return "/dashboard";
}

function hasDigitLength(value: string, length: number) {
  return value.length === length && /^\d+$/.test(value);
}

export function authRedirectPath(redirect: unknown) {
  return typeof redirect === "string" && redirect.startsWith("/") && !redirect.startsWith("//")
    ? redirect
    : "/dashboard";
}

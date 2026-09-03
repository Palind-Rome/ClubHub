export function newPasswordProblem(currentPassword: string, newPassword: string) {
  if (newPassword.length < 6 || newPassword.length > 128) {
    return "新密码长度为 6 到 128 个字符";
  }
  if (newPassword === currentPassword) {
    return "新密码不能与当前密码相同";
  }
  return null;
}

export function confirmationProblem(newPassword: string, confirmation: string) {
  if (!confirmation) return "请再次输入新密码";
  if (confirmation !== newPassword) return "两次输入的新密码不一致";
  return null;
}

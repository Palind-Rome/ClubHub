<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElMessage } from "element-plus";
import type { FormInstance, FormRules } from "element-plus";
import { ResponseError } from "../api";
import type {
  CaptchaChallenge,
  PermissionDefinition,
  RegisterRequest,
  UserSummary,
} from "../api/models";
import { UpdateUserAccountStatusRequestAccountStatusEnum } from "../api/models";
import { type AuthResponse, type AuthRole, clearSession, readAuth, saveAuth } from "../authSession";
import { apiClient } from "../apiClient";
import { authRedirectPath } from "../authRedirect";
import { confirmationProblem, newPasswordProblem } from "../passwordChange";

const router = useRouter();
const route = useRoute();
const auth = ref<AuthResponse | null>(readAuth());
const mode = ref<"login" | "register">("login");
const loading = ref(false);
const permissionCatalog = ref<PermissionDefinition[]>([]);
const managedUsers = ref<UserSummary[]>([]);
const usersLoading = ref(false);
const loginFormRef = ref<FormInstance>();
const registerFormRef = ref<FormInstance>();
const passwordFormRef = ref<FormInstance>();
const captcha = ref<CaptchaChallenge | null>(null);
const captchaLoading = ref(false);
const passwordDialogVisible = ref(false);
const passwordLoading = ref(false);

const STUDENT_NO_LENGTH = 7;
const STAFF_NO_LENGTH = 5;
const STUDENT_NO_MAX_LENGTH = Math.max(STUDENT_NO_LENGTH, STAFF_NO_LENGTH);
const studentNoRuleMessage = `学工号必须为学生 ${STUDENT_NO_LENGTH} 位或教师 ${STAFF_NO_LENGTH} 位`;
const studentNoPlaceholder = `学生 ${STUDENT_NO_LENGTH} 位，教师 ${STAFF_NO_LENGTH} 位`;
const studentNoHelpMessage = `请输入学生 ${STUDENT_NO_LENGTH} 位或教师 ${STAFF_NO_LENGTH} 位学工号`;
const studentNoIntroText = `学工号学生 ${STUDENT_NO_LENGTH} 位、教师 ${STAFF_NO_LENGTH} 位；可用角色由数据库中的用户角色关系决定。`;

const loginForm = ref({
  username: "",
  password: "",
  captchaToken: "",
  captchaCode: "",
});

const registerForm = ref({
  username: "",
  password: "",
  realName: "",
  studentNo: "",
  gender: "",
  phone: "",
  email: "",
  college: "",
  major: "",
  grade: "",
  captchaToken: "",
  captchaCode: "",
});

const passwordForm = ref({
  currentPassword: "",
  newPassword: "",
  confirmPassword: "",
});

const captchaCodeRules = [
  { required: true, message: "请输入验证码", trigger: "blur" },
  { pattern: /^\d{5}$/, message: "请输入 5 位数字验证码", trigger: "blur" },
];

const loginRules: FormRules = {
  username: [{ required: true, message: "请输入用户名或学工号", trigger: "blur" }],
  password: [{ required: true, message: "请输入密码", trigger: "blur" }],
  captchaCode: captchaCodeRules,
};

const registerRules: FormRules = {
  username: [
    { required: true, message: "请输入用户名", trigger: "blur" },
    { min: 3, max: 50, message: "用户名长度为 3 到 50 个字符", trigger: "blur" },
  ],
  password: [
    { required: true, message: "请输入密码", trigger: "blur" },
    { min: 6, max: 128, message: "密码长度为 6 到 128 个字符", trigger: "blur" },
  ],
  realName: [
    { required: true, message: "请输入姓名", trigger: "blur" },
    { max: 50, message: "姓名最多 50 个字符", trigger: "blur" },
  ],
  studentNo: [
    { required: true, message: "请输入学工号", trigger: "blur" },
    { validator: validateStudentNo, trigger: "blur" },
  ],
  phone: [{ pattern: /^[0-9+\-\s()]{0,20}$/, message: "请输入有效手机号", trigger: "blur" }],
  email: [
    { type: "email", message: "请输入有效邮箱", trigger: "blur" },
    { max: 100, message: "邮箱最多 100 个字符", trigger: "blur" },
  ],
  college: [{ max: 100, message: "学院最多 100 个字符", trigger: "blur" }],
  major: [{ max: 100, message: "专业最多 100 个字符", trigger: "blur" }],
  grade: [{ max: 20, message: "年级最多 20 个字符", trigger: "blur" }],
  captchaCode: captchaCodeRules,
};

const passwordRules: FormRules = {
  currentPassword: [
    { required: true, message: "请输入当前密码", trigger: "blur" },
    { max: 128, message: "当前密码最多 128 个字符", trigger: "blur" },
  ],
  newPassword: [
    { required: true, message: "请输入新密码", trigger: "blur" },
    { validator: validateNewPassword, trigger: "blur" },
  ],
  confirmPassword: [{ validator: validatePasswordConfirmation, trigger: "blur" }],
};

const currentStep = computed(() => {
  if (!auth.value) return mode.value;
  return "account";
});

const permissionNameMap = computed(() => {
  const map: Record<string, string> = {};
  for (const permission of permissionCatalog.value) {
    map[permission.code] = permission.name;
  }
  return map;
});

const registerIdentity = computed(() => identityLabel(registerForm.value.studentNo));
const isSystemAdmin = computed(
  () =>
    auth.value?.permissions.includes("*") ||
    auth.value?.roles.some((role) => role.code === "SYSTEM_ADMIN"),
);

async function requestJson<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { "Content-Type": "application/json", ...(options?.headers ?? {}) },
    ...options,
  });
  const payload = await res.json().catch(() => ({}));
  if (!res.ok) {
    throw new Error(payload.message || `请求失败（${res.status}）`);
  }
  return payload as T;
}

async function loadCaptcha(showError = true) {
  captchaLoading.value = true;
  try {
    const challenge = await requestJson<CaptchaChallenge>("/api/v1/auth/captcha", {
      cache: "no-store",
    });
    captcha.value = challenge;
    loginForm.value.captchaToken = challenge.captchaToken;
    loginForm.value.captchaCode = "";
    registerForm.value.captchaToken = challenge.captchaToken;
    registerForm.value.captchaCode = "";
  } catch (error) {
    captcha.value = null;
    if (showError) {
      ElMessage.error(error instanceof Error ? error.message : "验证码加载失败，请稍后重试");
    }
  } finally {
    captchaLoading.value = false;
  }
}

async function ensureCaptcha() {
  if (captcha.value?.captchaToken) return true;

  await loadCaptcha(true);
  return false;
}

async function login() {
  if (!(await validateForm(loginFormRef.value))) {
    return;
  }
  if (!(await ensureCaptcha())) return;

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/v1/auth/login", {
      method: "POST",
      body: JSON.stringify({
        username: loginForm.value.username.trim(),
        password: loginForm.value.password,
        captchaToken: loginForm.value.captchaToken,
        captchaCode: loginForm.value.captchaCode.trim(),
      }),
    });
    applyAuth(result);
    ElMessage.success("登录成功");
    router.push(authRedirectPath(route.query.redirect));
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "登录失败");
  } finally {
    loading.value = false;
    if (!auth.value) await loadCaptcha(false);
  }
}

async function register() {
  if (!(await validateForm(registerFormRef.value))) {
    return;
  }
  if (!(await ensureCaptcha())) return;

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/v1/auth/register", {
      method: "POST",
      body: JSON.stringify(buildRegisterPayload()),
    });
    applyAuth(result);
    ElMessage.success("注册成功");
    router.push(authRedirectPath(route.query.redirect));
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "注册失败");
  } finally {
    loading.value = false;
    if (!auth.value) await loadCaptcha(false);
  }
}

async function validateForm(form?: FormInstance) {
  if (!form) return false;
  return form.validate().catch(() => false);
}

function validateStudentNo(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (identityLabel(value)) {
    callback();
    return;
  }

  callback(new Error(studentNoRuleMessage));
}

function buildRegisterPayload(): RegisterRequest {
  return {
    username: registerForm.value.username.trim(),
    password: registerForm.value.password,
    realName: registerForm.value.realName.trim(),
    studentNo: registerForm.value.studentNo.trim(),
    gender: optionalText(registerForm.value.gender),
    phone: optionalText(registerForm.value.phone),
    email: optionalText(registerForm.value.email),
    college: optionalText(registerForm.value.college),
    major: optionalText(registerForm.value.major),
    grade: optionalText(registerForm.value.grade),
    captchaToken: registerForm.value.captchaToken,
    captchaCode: registerForm.value.captchaCode.trim(),
  };
}

function optionalText(value: string) {
  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

function applyAuth(nextAuth: AuthResponse) {
  auth.value = nextAuth;
  saveAuth(nextAuth);
}

async function logout() {
  try {
    await apiClient.logoutCurrentSession();
    auth.value = null;
    clearSession();
    mode.value = "login";
    await loadCaptcha(false);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "注销失败，请稍后重试");
  }
}

function openPasswordDialog() {
  passwordForm.value = {
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
  };
  passwordDialogVisible.value = true;
  void nextTick(() => passwordFormRef.value?.clearValidate());
}

function validateNewPassword(_rule: unknown, value: string, callback: (error?: Error) => void) {
  const problem = newPasswordProblem(passwordForm.value.currentPassword, value ?? "");
  callback(problem ? new Error(problem) : undefined);
}

function validatePasswordConfirmation(
  _rule: unknown,
  value: string,
  callback: (error?: Error) => void,
) {
  const problem = confirmationProblem(passwordForm.value.newPassword, value ?? "");
  callback(problem ? new Error(problem) : undefined);
}

async function changePassword() {
  if (!(await validateForm(passwordFormRef.value))) return;

  passwordLoading.value = true;
  try {
    await apiClient.changeCurrentUserPassword({
      changePasswordRequest: {
        currentPassword: passwordForm.value.currentPassword,
        newPassword: passwordForm.value.newPassword,
      },
    });
    passwordDialogVisible.value = false;
    auth.value = null;
    clearSession();
    mode.value = "login";
    ElMessage.success("密码修改成功，请使用新密码重新登录");
    await router.replace("/auth");
    await loadCaptcha(false);
  } catch (error) {
    ElMessage.error(await passwordChangeErrorMessage(error));
  } finally {
    passwordLoading.value = false;
  }
}

async function passwordChangeErrorMessage(error: unknown) {
  if (error instanceof ResponseError) {
    try {
      const payload = (await error.response.clone().json()) as { message?: string };
      if (payload.message) return payload.message;
    } catch {
      // Fall through to the safe generic message.
    }
  }
  return error instanceof Error ? error.message : "密码修改失败，请稍后重试";
}

async function loadManagedUsers() {
  if (!isSystemAdmin.value) return;
  usersLoading.value = true;
  try {
    managedUsers.value = await apiClient.getUsers({});
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "用户列表加载失败");
  } finally {
    usersLoading.value = false;
  }
}

async function toggleUserStatus(user: UserSummary) {
  const disabled = user.accountStatus === "disabled";
  try {
    await apiClient.updateUserAccountStatus({
      userId: user.id,
      updateUserAccountStatusRequest: {
        accountStatus: disabled
          ? UpdateUserAccountStatusRequestAccountStatusEnum.Normal
          : UpdateUserAccountStatusRequestAccountStatusEnum.Disabled,
      },
    });
    ElMessage.success(disabled ? "账号已启用" : "账号已停用并撤销全部会话");
    await loadManagedUsers();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "账号状态更新失败");
  }
}

async function revokeUserSessions(user: UserSummary) {
  try {
    await apiClient.revokeUserSessions({ userId: user.id });
    ElMessage.success(`已强制下线 ${user.displayName}`);
    if (user.id === auth.value?.user.id) {
      auth.value = null;
      clearSession();
      mode.value = "login";
      await router.replace("/auth");
      return;
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "强制下线失败");
  }
}

onMounted(loadManagedUsers);

function scopeLabel(scope: string) {
  return scope === "club" ? "社团范围" : "全局";
}

function roleDisplayName(role: AuthRole) {
  return role.displayName || role.name;
}

function roleKey(role: AuthRole) {
  const clubKey = role.clubId ?? (role.clubIds.length ? role.clubIds.join("-") : "global");
  return `${role.code}:${clubKey}:${roleDisplayName(role)}`;
}

function roleDescription(role: AuthRole) {
  return scopeLabel(role.scope);
}

function permissionLabel(code: string) {
  return permissionNameMap.value[code] || code;
}

function identityLabel(studentNo?: string | null) {
  const normalized = (studentNo ?? "").trim();
  if (hasDigitLength(normalized, STUDENT_NO_LENGTH)) return "学生";
  if (hasDigitLength(normalized, STAFF_NO_LENGTH)) return "教师";
  return "";
}

function hasDigitLength(value: string, length: number) {
  return value.length === length && /^\d+$/.test(value);
}

async function loadPermissionCatalog() {
  try {
    permissionCatalog.value = await requestJson<PermissionDefinition[]>("/api/v1/auth/permissions");
  } catch {
    permissionCatalog.value = [];
  }
}

loadPermissionCatalog();
if (!auth.value) void loadCaptcha(false);
</script>

<template>
  <div class="auth-page">
    <section v-if="currentStep === 'login'" class="auth-shell">
      <div class="intro">
        <h1>ClubHub</h1>
        <p>高校社团运营与协同管理平台</p>
      </div>

      <el-form
        ref="loginFormRef"
        :model="loginForm"
        :rules="loginRules"
        class="auth-panel"
        label-position="top"
      >
        <h2>用户登录</h2>
        <el-form-item label="用户名" prop="username">
          <el-input
            v-model="loginForm.username"
            maxlength="50"
            autocomplete="username"
            placeholder="可输入用户名或学工号"
          />
        </el-form-item>
        <el-form-item label="密码" prop="password">
          <el-input
            v-model="loginForm.password"
            type="password"
            maxlength="128"
            autocomplete="current-password"
            show-password
            @keyup.enter="login"
          />
        </el-form-item>
        <el-form-item label="验证码" prop="captchaCode">
          <div class="captcha-row">
            <el-input
              v-model="loginForm.captchaCode"
              maxlength="5"
              inputmode="numeric"
              autocomplete="off"
              placeholder="5 位数字"
              @keyup.enter="login"
            />
            <button
              type="button"
              class="captcha-preview"
              :disabled="captchaLoading"
              aria-label="刷新验证码"
              @click="loadCaptcha()"
            >
              <img v-if="captcha" :src="captcha.image" alt="验证码图片，点击刷新" />
              <span v-else>{{ captchaLoading ? "加载中…" : "点击加载" }}</span>
            </button>
          </div>
          <div class="field-help">验证码 5 位数字，点击图片刷新</div>
        </el-form-item>
        <el-button type="primary" :loading="loading" class="full-button" @click="login"
          >登录</el-button
        >
        <el-button link type="primary" class="switch-link" @click="mode = 'register'">
          没有账号？立即注册
        </el-button>
      </el-form>
    </section>

    <section v-else-if="currentStep === 'register'" class="auth-shell register-shell">
      <div class="intro">
        <h1>创建账号</h1>
        <p>{{ studentNoIntroText }}</p>
      </div>

      <el-form
        ref="registerFormRef"
        :model="registerForm"
        :rules="registerRules"
        class="auth-panel register-panel"
        label-position="top"
      >
        <div class="form-grid">
          <el-form-item label="用户名" prop="username">
            <el-input v-model="registerForm.username" maxlength="50" />
          </el-form-item>
          <el-form-item label="密码" prop="password">
            <el-input
              v-model="registerForm.password"
              type="password"
              :minlength="6"
              maxlength="128"
              show-password
            />
          </el-form-item>
          <el-form-item label="姓名" prop="realName">
            <el-input v-model="registerForm.realName" maxlength="50" />
          </el-form-item>
          <el-form-item label="学工号" prop="studentNo">
            <el-input
              v-model="registerForm.studentNo"
              :maxlength="STUDENT_NO_MAX_LENGTH"
              :placeholder="studentNoPlaceholder"
            />
            <div class="field-help">
              {{ registerIdentity ? `当前判断为：${registerIdentity}` : studentNoHelpMessage }}
            </div>
          </el-form-item>
          <el-form-item label="性别">
            <el-select v-model="registerForm.gender" clearable>
              <el-option label="男" value="男" />
              <el-option label="女" value="女" />
            </el-select>
          </el-form-item>
          <el-form-item label="手机号" prop="phone">
            <el-input v-model="registerForm.phone" maxlength="20" />
          </el-form-item>
          <el-form-item label="邮箱" prop="email">
            <el-input v-model="registerForm.email" type="email" maxlength="100" />
          </el-form-item>
          <el-form-item label="学院" prop="college">
            <el-input v-model="registerForm.college" maxlength="100" />
          </el-form-item>
          <el-form-item label="专业" prop="major">
            <el-input v-model="registerForm.major" maxlength="100" />
          </el-form-item>
          <el-form-item label="年级" prop="grade">
            <el-input v-model="registerForm.grade" maxlength="20" />
          </el-form-item>
          <el-form-item label="验证码" prop="captchaCode">
            <div class="captcha-row">
              <el-input
                v-model="registerForm.captchaCode"
                maxlength="5"
                inputmode="numeric"
                autocomplete="off"
                placeholder="5 位数字"
                @keyup.enter="register"
              />
              <button
                type="button"
                class="captcha-preview"
                :disabled="captchaLoading"
                aria-label="刷新验证码"
                @click="loadCaptcha()"
              >
                <img v-if="captcha" :src="captcha.image" alt="验证码图片，点击刷新" />
                <span v-else>{{ captchaLoading ? "加载中…" : "点击加载" }}</span>
              </button>
            </div>
            <div class="field-help">验证码 5 位数字，点击图片刷新</div>
          </el-form-item>
        </div>
        <el-button type="primary" :loading="loading" class="full-button" @click="register"
          >注册并继续</el-button
        >
        <el-button link type="primary" class="switch-link" @click="mode = 'login'">
          已有账号？返回登录
        </el-button>
      </el-form>
    </section>

    <section v-else-if="auth" class="account-page">
      <div class="page-title app-page-header">
        <div>
          <h2>当前账号</h2>
          <p>{{ auth.user.realName }}（{{ auth.user.studentNo || auth.user.username }}）</p>
        </div>
        <div class="actions">
          <el-button type="primary" plain @click="openPasswordDialog">修改密码</el-button>
          <el-button type="danger" plain @click="logout">退出登录</el-button>
        </div>
      </div>

      <div class="account-grid">
        <div class="info-panel">
          <h3>账号信息</h3>
          <el-descriptions :column="1" size="small" border>
            <el-descriptions-item label="姓名">{{ auth.user.realName }}</el-descriptions-item>
            <el-descriptions-item label="学工号">{{
              auth.user.studentNo || "未填写"
            }}</el-descriptions-item>
            <el-descriptions-item label="身份">{{
              identityLabel(auth.user.studentNo) || "未判断"
            }}</el-descriptions-item>
            <el-descriptions-item label="学院">{{
              auth.user.college || "未填写"
            }}</el-descriptions-item>
            <el-descriptions-item label="专业">{{
              auth.user.major || "未填写"
            }}</el-descriptions-item>
            <el-descriptions-item label="账号状态">{{
              auth.user.accountStatus
            }}</el-descriptions-item>
          </el-descriptions>
        </div>

        <div class="info-panel">
          <h3>当前角色权限</h3>
          <el-empty
            v-if="auth.roles.length === 0"
            description="当前账号暂无可用角色，请联系管理员分配角色"
          />
          <div v-else class="role-list">
            <div v-for="role in auth.roles" :key="roleKey(role)" class="role-item">
              <div class="role-heading">
                <el-tag type="success">{{ roleDisplayName(role) }}</el-tag>
                <span class="role-scope">{{ roleDescription(role) }}</span>
              </div>
              <div class="permission-tags">
                <el-tag
                  v-for="permission in role.permissions"
                  :key="permission"
                  size="small"
                  effect="plain"
                >
                  {{ permissionLabel(permission) }}
                </el-tag>
              </div>
            </div>
          </div>

          <h3 class="permission-title">本账号全部权限</h3>
          <p class="permission-help">由当前账号的全部角色合并而成，重复权限只显示一次。</p>
          <div class="permission-tags">
            <el-tag
              v-for="permission in auth.permissions"
              :key="permission"
              size="small"
              effect="plain"
            >
              {{ permissionLabel(permission) }}
            </el-tag>
          </div>
        </div>
      </div>

      <el-dialog
        v-model="passwordDialogVisible"
        title="修改密码"
        width="min(460px, 92vw)"
        destroy-on-close
        :close-on-click-modal="!passwordLoading"
        :close-on-press-escape="!passwordLoading"
      >
        <el-alert
          title="修改成功后，当前账号的登录会话将失效，需要使用新密码重新登录。"
          type="info"
          :closable="false"
          show-icon
          class="password-alert"
        />
        <el-form
          ref="passwordFormRef"
          :model="passwordForm"
          :rules="passwordRules"
          label-position="top"
          @submit.prevent="changePassword"
        >
          <el-form-item label="当前密码" prop="currentPassword">
            <el-input
              v-model="passwordForm.currentPassword"
              type="password"
              maxlength="128"
              autocomplete="current-password"
              show-password
            />
          </el-form-item>
          <el-form-item label="新密码" prop="newPassword">
            <el-input
              v-model="passwordForm.newPassword"
              type="password"
              :minlength="6"
              maxlength="128"
              autocomplete="new-password"
              show-password
            />
          </el-form-item>
          <el-form-item label="确认新密码" prop="confirmPassword">
            <el-input
              v-model="passwordForm.confirmPassword"
              type="password"
              :minlength="6"
              maxlength="128"
              autocomplete="new-password"
              show-password
              @keyup.enter="changePassword"
            />
          </el-form-item>
        </el-form>
        <template #footer>
          <el-button :disabled="passwordLoading" @click="passwordDialogVisible = false">
            取消
          </el-button>
          <el-button type="primary" :loading="passwordLoading" @click="changePassword">
            确认修改
          </el-button>
        </template>
      </el-dialog>

      <div v-if="isSystemAdmin" class="info-panel user-admin-panel">
        <div class="panel-heading">
          <div>
            <h3>账号状态与会话</h3>
            <p>停用账号会立即撤销该用户的全部登录会话。</p>
          </div>
          <el-button :loading="usersLoading" @click="loadManagedUsers">刷新</el-button>
        </div>
        <el-table :data="managedUsers" v-loading="usersLoading" stripe>
          <el-table-column prop="displayName" label="用户" min-width="180" />
          <el-table-column prop="studentNo" label="学工号" min-width="120" />
          <el-table-column prop="accountStatus" label="状态" min-width="100">
            <template #default="{ row }">
              <el-tag :type="row.accountStatus === 'disabled' ? 'danger' : 'success'">
                {{ row.accountStatus === "disabled" ? "已停用" : "正常" }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" min-width="220" fixed="right">
            <template #default="{ row }">
              <el-button
                size="small"
                :type="row.accountStatus === 'disabled' ? 'success' : 'danger'"
                plain
                :disabled="row.id === auth.user.id"
                @click="toggleUserStatus(row)"
              >
                {{ row.accountStatus === "disabled" ? "启用" : "停用" }}
              </el-button>
              <el-button size="small" plain @click="revokeUserSessions(row)">强制下线</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </section>
  </div>
</template>

<style scoped>
.auth-page {
  max-width: 1080px;
  margin: 0 auto;
}

.auth-shell {
  min-height: 100vh;
  display: grid;
  grid-template-columns: minmax(260px, 1fr) minmax(320px, 420px);
  gap: 28px;
  align-items: center;
  padding-block: 24px;
}

.register-shell {
  grid-template-columns: minmax(240px, 0.8fr) minmax(520px, 1.2fr);
  padding-block: 28px;
}

.intro h1 {
  margin: 0 0 10px;
  font-size: 36px;
  font-family: "STSong", "Songti SC", "Noto Serif CJK SC", "Source Han Serif SC", serif;
  font-weight: 700;
  letter-spacing: 0.015em;
}

.auth-panel h2 {
  font-family: "STSong", "Songti SC", "Noto Serif CJK SC", "Source Han Serif SC", serif;
  font-weight: 700;
}

.intro p,
.page-title p,
.muted {
  margin: 0;
  color: var(--el-text-color-secondary);
}

.auth-panel,
.info-panel {
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  padding: 20px;
  background: var(--club-bg-elevated);
  box-shadow: var(--club-shadow-sm);
  backdrop-filter: var(--club-glass-blur);
}

.auth-panel h2,
.info-panel h3,
.page-title h2 {
  margin: 0 0 16px;
}

.register-panel {
  padding: 28px;
}

.full-button {
  width: 100%;
}

.switch-link {
  width: 100%;
  margin: 12px 0 0;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(220px, 1fr));
  gap: 0 16px;
}

.field-help {
  width: 100%;
  margin-top: 6px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.4;
}

.captcha-row {
  display: flex;
  align-items: stretch;
  gap: 10px;
}

.captcha-row .el-input {
  min-width: 0;
}

.captcha-preview {
  display: grid;
  flex: 0 0 160px;
  place-items: center;
  min-height: 40px;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--el-border-color);
  border-radius: var(--club-radius-sm);
  color: var(--el-text-color-secondary);
  background: var(--club-bg-muted);
  cursor: pointer;
  transition:
    border-color 180ms ease,
    opacity 180ms ease;
}

.captcha-preview:hover:not(:disabled) {
  border-color: var(--el-color-primary);
}

.captcha-preview:disabled {
  cursor: wait;
  opacity: 0.68;
}

.captcha-preview img {
  display: block;
  width: 100%;
  height: 100%;
  min-height: 40px;
  object-fit: cover;
}

.page-title {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 16px;
}

.role-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.role-item {
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 12px;
}

.role-heading {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.role-scope {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.permission-title {
  margin-top: 30px !important;
}

.permission-help {
  margin: -8px 0 0;
  color: var(--club-text-secondary);
  font-size: 13px;
}

.password-alert {
  margin-bottom: 18px;
}

.account-grid {
  display: grid;
  grid-template-columns: minmax(280px, 360px) 1fr;
  gap: 16px;
}

.actions,
.permission-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.permission-tags {
  margin-top: 14px;
}

@media (max-width: 780px) {
  .auth-shell,
  .register-shell,
  .form-grid,
  .account-grid {
    grid-template-columns: 1fr;
  }

  .page-title {
    flex-direction: column;
  }

  .captcha-preview {
    flex-basis: 136px;
  }
}
</style>

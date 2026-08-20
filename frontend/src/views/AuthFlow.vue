<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElMessage } from "element-plus";
import type { FormInstance, FormRules } from "element-plus";
import {
  ArrowRight,
  Key,
  Lock,
  Monitor,
  Moon,
  Sunny,
  User,
  UserFilled,
} from "@element-plus/icons-vue";
import type { PermissionDefinition, RegisterRequest, UserSummary } from "../api/models";
import { UpdateUserAccountStatusRequestAccountStatusEnum } from "../api/models";
import { type AuthResponse, type AuthRole, clearSession, readAuth, saveAuth } from "../authSession";
import { apiClient } from "../apiClient";
import {
  STAFF_NO_LENGTH,
  STUDENT_NO_LENGTH,
  STUDENT_NO_MAX_LENGTH,
  resolveIdentityLabel,
  resolvePostAuthPath,
} from "../authExperience";
import { AppPageHeader, AppPanel } from "../components/ui";
import { type ThemePreference, useTheme } from "../composables/useTheme";

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
const { theme, setTheme } = useTheme();

const studentNoRuleMessage = `学工号必须为学生 ${STUDENT_NO_LENGTH} 位或教师 ${STAFF_NO_LENGTH} 位`;
const studentNoPlaceholder = `学生 ${STUDENT_NO_LENGTH} 位，教师 ${STAFF_NO_LENGTH} 位`;
const studentNoHelpMessage = `请输入学生 ${STUDENT_NO_LENGTH} 位或教师 ${STAFF_NO_LENGTH} 位学工号`;
const studentNoIntroText = `学工号学生 ${STUDENT_NO_LENGTH} 位、教师 ${STAFF_NO_LENGTH} 位；可用角色由数据库中的用户角色关系决定。`;

const loginForm = ref({
  username: "",
  password: "",
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
});

const loginRules: FormRules = {
  username: [{ required: true, message: "请输入用户名或学工号", trigger: "blur" }],
  password: [{ required: true, message: "请输入密码", trigger: "blur" }],
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
const themeLabel = computed(() => {
  if (theme.value === "light") return "浅色";
  if (theme.value === "dark") return "深色";
  return "跟随系统";
});

function changeTheme(command: string | number | object) {
  setTheme(command as ThemePreference);
}

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

async function login() {
  if (!(await validateForm(loginFormRef.value))) {
    return;
  }

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({
        username: loginForm.value.username.trim(),
        password: loginForm.value.password,
      }),
    });
    applyAuth(result);
    ElMessage.success("登录成功");
    router.push(authRedirectPath());
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "登录失败");
  } finally {
    loading.value = false;
  }
}

async function register() {
  if (!(await validateForm(registerFormRef.value))) {
    return;
  }

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/auth/register", {
      method: "POST",
      body: JSON.stringify(buildRegisterPayload()),
    });
    applyAuth(result);
    ElMessage.success("注册成功");
    router.push(authRedirectPath());
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "注册失败");
  } finally {
    loading.value = false;
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
  };
}

function optionalText(value: string) {
  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

function authRedirectPath() {
  return resolvePostAuthPath(route.query.redirect);
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
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "注销失败，请稍后重试");
  }
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
  return resolveIdentityLabel(studentNo);
}

async function loadPermissionCatalog() {
  try {
    permissionCatalog.value = await requestJson<PermissionDefinition[]>("/api/auth/permissions");
  } catch {
    permissionCatalog.value = [];
  }
}

loadPermissionCatalog();
</script>

<template>
  <div class="auth-page">
    <el-dropdown v-if="!auth" class="public-theme-switch" trigger="click" @command="changeTheme">
      <el-button class="public-theme-button" :aria-label="`当前主题：${themeLabel}`">
        <el-icon>
          <Sunny v-if="theme === 'light'" />
          <Moon v-else-if="theme === 'dark'" />
          <Monitor v-else />
        </el-icon>
        {{ themeLabel }}
      </el-button>
      <template #dropdown>
        <el-dropdown-menu>
          <el-dropdown-item command="system" :disabled="theme === 'system'">
            <el-icon><Monitor /></el-icon>跟随系统
          </el-dropdown-item>
          <el-dropdown-item command="light" :disabled="theme === 'light'">
            <el-icon><Sunny /></el-icon>浅色模式
          </el-dropdown-item>
          <el-dropdown-item command="dark" :disabled="theme === 'dark'">
            <el-icon><Moon /></el-icon>深色模式
          </el-dropdown-item>
        </el-dropdown-menu>
      </template>
    </el-dropdown>

    <section v-if="currentStep === 'login'" class="auth-shell login-shell">
      <div class="intro auth-intro">
        <div class="brand-lockup">
          <img src="/favicon.svg" alt="" aria-hidden="true" />
          <div><strong>ClubHub</strong><span>社团协作中心</span></div>
        </div>
        <span class="intro-eyebrow">Connected campus</span>
        <h1>让每一次社团协作<br />清晰而有序</h1>
        <p>统一管理身份、活动、项目与通知，随时掌握与你相关的校园动态。</p>
      </div>

      <el-form
        ref="loginFormRef"
        :model="loginForm"
        :rules="loginRules"
        class="auth-panel login-panel"
        label-position="top"
      >
        <span class="panel-eyebrow">Welcome back</span>
        <h2>登录 ClubHub</h2>
        <p class="panel-description">使用用户名、学号或工号继续访问你的社团空间。</p>
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
        <el-button type="primary" :loading="loading" class="full-button" @click="login"
          >登录</el-button
        >
        <div class="auth-divider"><span>首次使用 ClubHub</span></div>
        <el-button link type="primary" class="switch-link" @click="mode = 'register'">
          没有账号？立即注册
          <el-icon class="el-icon--right"><ArrowRight /></el-icon>
        </el-button>
      </el-form>
    </section>

    <section v-else-if="currentStep === 'register'" class="auth-shell register-shell">
      <div class="intro auth-intro register-intro">
        <div class="brand-lockup">
          <img src="/favicon.svg" alt="" aria-hidden="true" />
          <div><strong>ClubHub</strong><span>社团协作中心</span></div>
        </div>
        <span class="intro-eyebrow">Create your space</span>
        <h1>创建你的<br />校园协作身份</h1>
        <p>{{ studentNoIntroText }}</p>
        <div class="identity-hint">
          <el-icon><UserFilled /></el-icon>
          <div><strong>身份自动识别</strong><span>系统将根据学工号识别学生或教师身份。</span></div>
        </div>
      </div>

      <el-form
        ref="registerFormRef"
        :model="registerForm"
        :rules="registerRules"
        class="auth-panel register-panel"
        label-position="top"
      >
        <span class="panel-eyebrow">Join ClubHub</span>
        <h2>注册新账号</h2>
        <p class="panel-description">先填写必要身份信息，其他资料可按需补充。</p>
        <div class="form-section-title"><span>1</span>登录与身份</div>
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
        </div>
        <div class="form-section-title"><span>2</span>个人资料（选填）</div>
        <div class="form-grid">
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
      <AppPageHeader
        class="app-page-header"
        eyebrow="Account & permissions"
        title="账号与权限"
        description="查看当前身份、角色范围和可用权限，管理账号会话状态。"
      >
        <template #meta>
          <el-tag effect="plain">{{ identityLabel(auth.user.studentNo) || "校园用户" }}</el-tag>
          <el-tag type="success" effect="plain">{{
            auth.user.accountStatus === "disabled" ? "已停用" : "状态正常"
          }}</el-tag>
        </template>
        <template #actions>
          <el-button type="primary" plain @click="router.push('/dashboard')">返回工作台</el-button>
          <el-button type="danger" plain @click="logout">退出登录</el-button>
        </template>
      </AppPageHeader>

      <section class="account-identity" aria-label="当前账号摘要">
        <div class="account-avatar" aria-hidden="true">{{ auth.user.realName.slice(0, 1) }}</div>
        <div class="account-identity-copy">
          <span>当前登录账号</span>
          <strong>{{ auth.user.realName }}</strong>
          <p>
            {{ auth.user.studentNo || auth.user.username }} ·
            {{ auth.user.college || "学院信息未填写" }}
          </p>
        </div>
        <div class="account-stats">
          <div>
            <el-icon><User /></el-icon><strong>{{ auth.roles.length }}</strong
            ><span>角色</span>
          </div>
          <div>
            <el-icon><Key /></el-icon><strong>{{ auth.permissions.length }}</strong
            ><span>权限</span>
          </div>
        </div>
      </section>

      <div class="account-grid">
        <AppPanel title="账号档案" description="用于校内身份识别的基础资料。">
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
        </AppPanel>

        <AppPanel title="角色与权限" description="权限为当前全部角色授权能力的并集。">
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

          <h3 class="permission-title">权限并集</h3>
          <div class="permission-tags">
            <el-tag
              v-for="permission in auth.permissions"
              :key="permission"
              size="small"
              effect="plain"
            >
              {{ permissionLabel(permission) }}
            </el-tag>
            <span v-if="auth.permissions.length === 0" class="muted">暂无可用权限</span>
          </div>
        </AppPanel>
      </div>

      <div v-if="isSystemAdmin" class="info-panel user-admin-panel">
        <div class="panel-heading">
          <div>
            <div class="admin-title">
              <el-icon><Lock /></el-icon>
              <h3>账号状态与会话</h3>
            </div>
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
  min-width: 0;
}

.public-theme-switch {
  position: fixed;
  z-index: 30;
  top: 20px;
  right: 20px;
}

.public-theme-button {
  border-color: var(--club-border);
  color: var(--club-text-secondary);
  background: var(--club-surface);
  box-shadow: var(--club-shadow-sm);
  backdrop-filter: var(--club-glass-blur);
  -webkit-backdrop-filter: var(--club-glass-blur);
}

.auth-shell {
  display: grid;
  width: min(1180px, calc(100% - 40px));
  min-height: 100vh;
  align-items: center;
  grid-template-columns: minmax(320px, 1fr) minmax(340px, 440px);
  gap: clamp(40px, 7vw, 96px);
  margin: 0 auto;
  padding: 40px 0;
}

.register-shell {
  grid-template-columns: minmax(280px, 0.72fr) minmax(560px, 1.28fr);
  gap: clamp(32px, 5vw, 72px);
}

.auth-intro {
  position: relative;
}

.brand-lockup {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: clamp(44px, 8vh, 84px);
}

.brand-lockup img {
  width: 48px;
  height: 48px;
  object-fit: contain;
}

.brand-lockup div {
  display: flex;
  flex-direction: column;
}

.brand-lockup strong {
  color: var(--club-text);
  font-size: 20px;
  line-height: 1.2;
}

.brand-lockup span {
  margin-top: 3px;
  color: var(--club-text-muted);
  font-size: 12px;
}

.intro-eyebrow,
.panel-eyebrow {
  display: block;
  color: var(--club-primary-strong);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}

.auth-intro h1 {
  margin: 12px 0 18px;
  color: var(--club-text);
  font-size: clamp(38px, 5vw, 58px);
  line-height: 1.12;
  letter-spacing: -0.045em;
}

.auth-intro > p {
  max-width: 560px;
  margin: 0;
  color: var(--club-text-secondary);
  font-size: 16px;
  line-height: 1.8;
}

.identity-hint {
  display: flex;
  align-items: center;
  gap: 12px;
  max-width: 420px;
  margin-top: 32px;
  padding: 16px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  background: var(--club-surface);
}

.identity-hint > .el-icon {
  width: 38px;
  height: 38px;
  flex: 0 0 auto;
  border-radius: 13px;
  color: var(--club-primary);
  background: var(--club-primary-soft);
  font-size: 20px;
}

.identity-hint div {
  display: flex;
  flex-direction: column;
}

.identity-hint strong {
  color: var(--club-text);
  font-size: 13px;
}

.identity-hint span {
  margin-top: 3px;
  color: var(--club-text-muted);
  font-size: 12px;
  line-height: 1.5;
}

.auth-panel,
.info-panel {
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-xl);
  padding: clamp(24px, 3.5vw, 40px);
  background: var(--club-bg-elevated);
  box-shadow: var(--club-shadow-md);
  backdrop-filter: var(--club-glass-blur);
}

.auth-panel h2 {
  margin: 8px 0 0;
  color: var(--club-text);
  font-size: 28px;
  letter-spacing: -0.03em;
}

.panel-description {
  margin: 8px 0 28px;
  color: var(--club-text-secondary);
  font-size: 13px;
  line-height: 1.6;
}

.auth-panel :deep(.el-form-item__label) {
  color: var(--club-text-secondary);
  font-weight: 650;
}

.auth-panel :deep(.el-input__wrapper),
.auth-panel :deep(.el-select__wrapper) {
  min-height: 42px;
  background: color-mix(in srgb, var(--club-surface-solid) 74%, transparent);
}

.full-button {
  width: 100%;
  min-height: 42px;
  margin-top: 4px;
  font-weight: 700;
}

.switch-link {
  width: 100%;
  margin: 2px 0 0;
}

.auth-divider {
  display: flex;
  align-items: center;
  gap: 12px;
  margin: 24px 0 6px;
  color: var(--club-text-muted);
  font-size: 11px;
}

.auth-divider::before,
.auth-divider::after {
  height: 1px;
  flex: 1;
  background: var(--club-border);
  content: "";
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(210px, 1fr));
  gap: 0 18px;
}

.form-section-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 26px 0 16px;
  color: var(--club-text);
  font-size: 13px;
  font-weight: 750;
}

.form-section-title span {
  display: grid;
  width: 22px;
  height: 22px;
  place-items: center;
  border-radius: 8px;
  color: var(--club-primary-strong);
  background: var(--club-primary-soft);
  font-size: 11px;
}

.field-help {
  width: 100%;
  margin-top: 6px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.4;
}

.account-page {
  display: grid;
  gap: var(--club-space-6);
}

.account-page :deep(.page-header) {
  margin-bottom: 0;
}

.account-identity {
  display: grid;
  align-items: center;
  gap: var(--club-space-5);
  padding: clamp(20px, 3vw, 32px);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-xl);
  grid-template-columns: auto minmax(0, 1fr) auto;
  background: linear-gradient(
    120deg,
    color-mix(in srgb, var(--club-primary) 13%, transparent),
    var(--club-surface) 55%,
    color-mix(in srgb, var(--club-primary) 6%, transparent)
  );
  box-shadow: var(--club-shadow-sm);
}

.account-avatar {
  display: grid;
  width: 64px;
  height: 64px;
  place-items: center;
  border-radius: 22px;
  color: #fff;
  background: linear-gradient(135deg, var(--club-primary), var(--club-primary-strong));
  font-size: 26px;
  font-weight: 800;
}

.account-identity-copy {
  min-width: 0;
}

.account-identity-copy > span {
  color: var(--club-primary-strong);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.1em;
}

.account-identity-copy strong {
  display: block;
  margin-top: 4px;
  overflow: hidden;
  color: var(--club-text);
  font-size: 22px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.account-identity-copy p,
.muted {
  margin: 4px 0 0;
  color: var(--club-text-secondary);
  font-size: 13px;
}

.account-stats {
  display: grid;
  grid-template-columns: repeat(2, minmax(92px, 1fr));
  gap: var(--club-space-3);
}

.account-stats > div {
  display: grid;
  grid-template-columns: auto 1fr;
  align-items: center;
  gap: 2px 8px;
  padding: 13px 15px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  background: color-mix(in srgb, var(--club-surface-solid) 72%, transparent);
}

.account-stats .el-icon {
  grid-row: 1 / 3;
  color: var(--club-primary);
  font-size: 19px;
}

.account-stats strong {
  color: var(--club-text);
  font-size: 18px;
  line-height: 1;
}

.account-stats span {
  color: var(--club-text-muted);
  font-size: 11px;
}

.role-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.role-item {
  padding: 14px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  background: color-mix(in srgb, var(--club-surface-solid) 48%, transparent);
}

.role-heading {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.role-scope {
  color: var(--club-text-secondary);
  font-size: 13px;
}

.permission-title {
  margin-top: 18px;
}

.account-grid {
  display: grid;
  grid-template-columns: minmax(300px, 0.8fr) minmax(0, 1.2fr);
  gap: var(--club-space-5);
}

.permission-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.permission-tags {
  margin-top: 14px;
}

.info-panel h3 {
  margin: 0;
  color: var(--club-text);
}

.user-admin-panel {
  padding: var(--club-space-6);
  border-radius: var(--club-radius-lg);
  box-shadow: var(--club-shadow-sm);
}

.panel-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--club-space-4);
  margin-bottom: var(--club-space-5);
}

.panel-heading p {
  margin: 5px 0 0;
  color: var(--club-text-secondary);
  font-size: 13px;
}

.admin-title {
  display: flex;
  align-items: center;
  gap: 9px;
}

.admin-title .el-icon {
  color: var(--club-primary);
}

@media (max-width: 920px) {
  .auth-shell,
  .register-shell {
    grid-template-columns: 1fr;
  }

  .auth-shell {
    max-width: 680px;
    gap: 36px;
  }

  .brand-lockup {
    margin-bottom: 36px;
  }

  .auth-intro h1 br {
    display: none;
  }

  .register-intro {
    display: none;
  }
}

@media (max-width: 760px) {
  .account-identity,
  .account-grid {
    grid-template-columns: 1fr;
  }

  .account-identity {
    grid-template-columns: auto minmax(0, 1fr);
  }

  .account-stats {
    grid-column: 1 / -1;
  }
}

@media (max-width: 560px) {
  .auth-shell {
    width: min(100% - 24px, 680px);
    padding: 24px 0;
  }

  .public-theme-switch {
    top: 12px;
    right: 12px;
  }

  .auth-intro h1 {
    font-size: 34px;
  }

  .form-grid {
    grid-template-columns: 1fr;
  }

  .account-identity {
    grid-template-columns: 1fr;
  }

  .account-stats {
    grid-column: auto;
  }

  .panel-heading {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>

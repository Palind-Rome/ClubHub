<script setup lang="ts">
import { computed, ref } from "vue";
import { useRouter } from "vue-router";
import { ElMessage } from "element-plus";
import {
  type AuthResponse,
  type AuthRole,
  clearSession,
  readAuth,
  saveAuth,
} from "../authSession";

interface PermissionDefinition {
  code: string;
  name: string;
  description: string;
}

const router = useRouter();
const auth = ref<AuthResponse | null>(readAuth());
const mode = ref<"login" | "register">("login");
const loading = ref(false);
const permissionCatalog = ref<PermissionDefinition[]>([]);

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
  if (!loginForm.value.username || !loginForm.value.password) {
    ElMessage.warning("请输入用户名或学工号和密码");
    return;
  }

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify(loginForm.value),
    });
    applyAuth(result);
    ElMessage.success("登录成功");
    router.push("/clubs");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "登录失败");
  } finally {
    loading.value = false;
  }
}

async function register() {
  if (
    !registerForm.value.username ||
    !registerForm.value.password ||
    !registerForm.value.realName ||
    !registerForm.value.studentNo
  ) {
    ElMessage.warning("请填写用户名、密码、姓名和学工号");
    return;
  }

  if (!identityLabel(registerForm.value.studentNo)) {
    ElMessage.warning("学工号必须为学生 7 位或教师 5 位");
    return;
  }

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/auth/register", {
      method: "POST",
      body: JSON.stringify(registerForm.value),
    });
    applyAuth(result);
    ElMessage.success("注册成功");
    router.push("/clubs");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "注册失败");
  } finally {
    loading.value = false;
  }
}

function applyAuth(nextAuth: AuthResponse) {
  auth.value = nextAuth;
  saveAuth(nextAuth);
}

function logout() {
  auth.value = null;
  clearSession();
  mode.value = "login";
}

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
  if (/^\d{7}$/.test(normalized)) return "学生";
  if (/^\d{5}$/.test(normalized)) return "教师";
  return "";
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
    <section v-if="currentStep === 'login'" class="auth-shell">
      <div class="intro">
        <h1>ClubHub</h1>
        <p>高校社团运营与协同管理平台</p>
      </div>

      <el-form class="auth-panel" label-position="top">
        <h2>用户登录</h2>
        <el-form-item label="用户名" required>
          <el-input v-model="loginForm.username" autocomplete="username" placeholder="可输入用户名或学工号" />
        </el-form-item>
        <el-form-item label="密码" required>
          <el-input
            v-model="loginForm.password"
            type="password"
            autocomplete="current-password"
            show-password
            @keyup.enter="login"
          />
        </el-form-item>
        <el-button type="primary" :loading="loading" class="full-button" @click="login">登录</el-button>
        <el-button link type="primary" class="switch-link" @click="mode = 'register'">
          没有账号？立即注册
        </el-button>
      </el-form>
    </section>

    <section v-else-if="currentStep === 'register'" class="auth-shell register-shell">
      <div class="intro">
        <h1>创建账号</h1>
        <p>学工号学生 7 位、教师 5 位；可用角色由数据库中的用户角色关系决定。</p>
      </div>

      <el-form class="auth-panel register-panel" label-position="top">
        <div class="form-grid">
          <el-form-item label="用户名" required>
            <el-input v-model="registerForm.username" />
          </el-form-item>
          <el-form-item label="密码" required>
            <el-input v-model="registerForm.password" type="password" show-password />
          </el-form-item>
          <el-form-item label="姓名" required>
            <el-input v-model="registerForm.realName" />
          </el-form-item>
          <el-form-item label="学工号" required>
            <el-input v-model="registerForm.studentNo" maxlength="7" placeholder="学生 7 位，教师 5 位" />
            <div class="field-help">
              {{ registerIdentity ? `当前判断为：${registerIdentity}` : "请输入学生 7 位或教师 5 位学工号" }}
            </div>
          </el-form-item>
          <el-form-item label="性别">
            <el-select v-model="registerForm.gender" clearable>
              <el-option label="男" value="男" />
              <el-option label="女" value="女" />
            </el-select>
          </el-form-item>
          <el-form-item label="手机号">
            <el-input v-model="registerForm.phone" />
          </el-form-item>
          <el-form-item label="邮箱">
            <el-input v-model="registerForm.email" />
          </el-form-item>
          <el-form-item label="学院">
            <el-input v-model="registerForm.college" />
          </el-form-item>
          <el-form-item label="专业">
            <el-input v-model="registerForm.major" />
          </el-form-item>
          <el-form-item label="年级">
            <el-input v-model="registerForm.grade" />
          </el-form-item>
        </div>
        <el-button type="primary" :loading="loading" class="full-button" @click="register">注册并继续</el-button>
        <el-button link type="primary" class="switch-link" @click="mode = 'login'">
          已有账号？返回登录
        </el-button>
      </el-form>
    </section>

    <section v-else-if="auth" class="account-page">
      <div class="page-title">
        <div>
          <h2>当前账号</h2>
          <p>{{ auth.user.realName }}（{{ auth.user.studentNo || auth.user.username }}）</p>
        </div>
        <div class="actions">
          <el-button type="danger" plain @click="logout">退出登录</el-button>
        </div>
      </div>

      <div class="account-grid">
        <div class="info-panel">
          <h3>账号信息</h3>
          <el-descriptions :column="1" size="small" border>
            <el-descriptions-item label="姓名">{{ auth.user.realName }}</el-descriptions-item>
            <el-descriptions-item label="学工号">{{ auth.user.studentNo || "未填写" }}</el-descriptions-item>
            <el-descriptions-item label="身份">{{ identityLabel(auth.user.studentNo) || "未判断" }}</el-descriptions-item>
            <el-descriptions-item label="学院">{{ auth.user.college || "未填写" }}</el-descriptions-item>
            <el-descriptions-item label="专业">{{ auth.user.major || "未填写" }}</el-descriptions-item>
            <el-descriptions-item label="账号状态">{{ auth.user.accountStatus }}</el-descriptions-item>
          </el-descriptions>
        </div>

        <div class="info-panel">
          <h3>当前角色</h3>
          <el-empty v-if="auth.roles.length === 0" description="当前账号暂无可用角色，请联系管理员分配角色" />
          <div v-else class="role-list">
            <div v-for="role in auth.roles" :key="roleKey(role)" class="role-item">
              <div class="role-heading">
                <el-tag type="success">{{ roleDisplayName(role) }}</el-tag>
                <span class="role-scope">{{ roleDescription(role) }}</span>
              </div>
              <div class="permission-tags">
                <el-tag v-for="permission in role.permissions" :key="permission" size="small" effect="plain">
                  {{ permissionLabel(permission) }}
                </el-tag>
              </div>
            </div>
          </div>

          <h3 class="permission-title">权限并集</h3>
          <div class="permission-tags">
            <el-tag v-for="permission in auth.permissions" :key="permission" size="small" effect="plain">
              {{ permissionLabel(permission) }}
            </el-tag>
          </div>
        </div>
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
  min-height: 520px;
  display: grid;
  grid-template-columns: minmax(260px, 1fr) minmax(320px, 420px);
  gap: 28px;
  align-items: center;
}

.register-shell {
  grid-template-columns: minmax(240px, 0.8fr) minmax(520px, 1.2fr);
}

.intro h1 {
  margin: 0 0 10px;
  font-size: 36px;
}

.intro p,
.page-title p,
.muted {
  margin: 0;
  color: var(--el-text-color-secondary);
}

.auth-panel,
.info-panel {
  background: #fff;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 20px;
}

.auth-panel h2,
.info-panel h3,
.page-title h2 {
  margin: 0 0 16px;
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
  margin-top: 18px;
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
}
</style>

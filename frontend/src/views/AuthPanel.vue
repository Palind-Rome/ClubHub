<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ElMessage } from "element-plus";

interface AuthUser {
  id: number;
  username: string;
  realName: string;
  studentNo: string | null;
  gender: string | null;
  phone: string | null;
  email: string | null;
  college: string | null;
  major: string | null;
  grade: string | null;
  accountStatus: string;
}

interface AuthRole {
  id: number;
  code: string;
  name: string;
  scope: "system" | "club";
  clubId: number | null;
  permissions: string[];
  permissionDesc: string | null;
}

interface AuthResponse {
  token: string;
  user: AuthUser;
  roles: AuthRole[];
  permissions: string[];
}

interface RoleDefinition {
  code: string;
  name: string;
  scope: "system" | "club";
  description: string;
  permissions: string[];
}

interface PermissionDefinition {
  code: string;
  name: string;
  description: string;
}

interface PermissionCheckResult {
  userId: number;
  permission: string;
  clubId: number | null;
  allowed: boolean;
  matchedRoles: AuthRole[];
  message: string;
}

interface RoleAssignmentResult {
  targetUserId: number;
  role: AuthRole;
  alreadyExists: boolean;
  message: string;
}

const activeTab = ref("login");
const loading = ref(false);
const roleDefinitions = ref<RoleDefinition[]>([]);
const permissionCatalog = ref<PermissionDefinition[]>([]);
const auth = ref<AuthResponse | null>(readStoredAuth());

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

const permissionForm = ref({
  userId: auth.value?.user.id ?? undefined,
  permission: "public:view",
  clubId: undefined as number | undefined,
});

const assignForm = ref({
  operatorUserId: auth.value?.user.id ?? undefined,
  targetUserId: undefined as number | undefined,
  roleCode: "CLUB_MEMBER",
  clubId: undefined as number | undefined,
});

const permissionResult = ref<PermissionCheckResult | null>(null);
const assignmentResult = ref<RoleAssignmentResult | null>(null);

const permissionNameMap = computed(() => {
  const map: Record<string, string> = {};
  for (const permission of permissionCatalog.value) {
    map[permission.code] = permission.name;
  }
  return map;
});

const selectedAssignRole = computed(() =>
  roleDefinitions.value.find((role) => role.code === assignForm.value.roleCode),
);

function readStoredAuth(): AuthResponse | null {
  const raw = localStorage.getItem("clubhub-auth");
  if (!raw) return null;
  try {
    return JSON.parse(raw) as AuthResponse;
  } catch {
    localStorage.removeItem("clubhub-auth");
    return null;
  }
}

function storeAuth(nextAuth: AuthResponse) {
  auth.value = nextAuth;
  permissionForm.value.userId = nextAuth.user.id;
  assignForm.value.operatorUserId = nextAuth.user.id;
  localStorage.setItem("clubhub-auth", JSON.stringify(nextAuth));
}

function clearAuth() {
  auth.value = null;
  permissionResult.value = null;
  assignmentResult.value = null;
  localStorage.removeItem("clubhub-auth");
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

async function loadCatalogs() {
  try {
    const [roles, permissions] = await Promise.all([
      requestJson<RoleDefinition[]>("/api/auth/roles"),
      requestJson<PermissionDefinition[]>("/api/auth/permissions"),
    ]);
    roleDefinitions.value = roles;
    permissionCatalog.value = permissions;
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "基础权限加载失败");
  }
}

async function login() {
  if (!loginForm.value.username || !loginForm.value.password) {
    ElMessage.warning("请输入用户名和密码");
    return;
  }

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify(loginForm.value),
    });
    storeAuth(result);
    ElMessage.success("登录成功");
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
    ElMessage.warning("请填写用户名、密码、姓名和学号");
    return;
  }

  loading.value = true;
  try {
    const result = await requestJson<AuthResponse>("/api/auth/register", {
      method: "POST",
      body: JSON.stringify(registerForm.value),
    });
    storeAuth(result);
    ElMessage.success("注册成功，已自动登录");
    activeTab.value = "login";
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "注册失败");
  } finally {
    loading.value = false;
  }
}

async function checkPermission() {
  if (!permissionForm.value.userId || !permissionForm.value.permission) {
    ElMessage.warning("请选择用户和权限");
    return;
  }

  const params = new URLSearchParams({
    userId: String(permissionForm.value.userId),
    permission: permissionForm.value.permission,
  });
  if (permissionForm.value.clubId) {
    params.set("clubId", String(permissionForm.value.clubId));
  }

  try {
    permissionResult.value = await requestJson<PermissionCheckResult>(
      `/api/auth/permissions/check?${params.toString()}`,
    );
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "权限检查失败");
  }
}

async function assignRole() {
  if (!assignForm.value.operatorUserId || !assignForm.value.targetUserId || !assignForm.value.roleCode) {
    ElMessage.warning("请填写操作人、目标用户和角色");
    return;
  }

  if (selectedAssignRole.value?.scope === "club" && !assignForm.value.clubId) {
    ElMessage.warning("社团范围角色需要填写社团 ID");
    return;
  }

  try {
    assignmentResult.value = await requestJson<RoleAssignmentResult>("/api/auth/roles/assign", {
      method: "POST",
      body: JSON.stringify({
        operatorUserId: assignForm.value.operatorUserId,
        targetUserId: assignForm.value.targetUserId,
        roleCode: assignForm.value.roleCode,
        clubId: selectedAssignRole.value?.scope === "club" ? assignForm.value.clubId : null,
      }),
    });
    ElMessage.success(assignmentResult.value.message);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "角色分配失败");
  }
}

function permissionLabel(code: string) {
  return permissionNameMap.value[code] ? `${permissionNameMap.value[code]}（${code}）` : code;
}

function scopeLabel(scope: string) {
  return scope === "club" ? "社团范围" : "全局";
}

onMounted(loadCatalogs);
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <h2>账号与权限</h2>
      <el-button v-if="auth" type="danger" plain @click="clearAuth">退出登录</el-button>
    </div>

    <el-tabs v-model="activeTab">
      <el-tab-pane label="登录" name="login">
        <div class="grid">
          <el-form class="panel" label-position="top">
            <el-form-item label="用户名" required>
              <el-input v-model="loginForm.username" autocomplete="username" />
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
            <el-button type="primary" :loading="loading" @click="login">登录</el-button>
          </el-form>

          <div v-if="auth" class="panel">
            <div class="summary">
              <div>
                <h3>{{ auth.user.realName }}</h3>
                <p>{{ auth.user.username }} · {{ auth.user.accountStatus }}</p>
              </div>
              <el-tag type="success">已登录</el-tag>
            </div>
            <el-descriptions :column="1" size="small" border>
              <el-descriptions-item label="用户 ID">{{ auth.user.id }}</el-descriptions-item>
              <el-descriptions-item label="学号">{{ auth.user.studentNo || "未填写" }}</el-descriptions-item>
              <el-descriptions-item label="学院">{{ auth.user.college || "未填写" }}</el-descriptions-item>
              <el-descriptions-item label="专业">{{ auth.user.major || "未填写" }}</el-descriptions-item>
              <el-descriptions-item label="年级">{{ auth.user.grade || "未填写" }}</el-descriptions-item>
            </el-descriptions>

            <div class="role-list">
              <el-tag v-for="role in auth.roles" :key="`${role.code}-${role.clubId ?? 'system'}`">
                {{ role.name }}{{ role.clubId ? ` · 社团 ${role.clubId}` : "" }}
              </el-tag>
            </div>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane label="注册" name="register">
        <el-form class="panel wide-form" label-position="top">
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
            <el-form-item label="学号" required>
              <el-input v-model="registerForm.studentNo" />
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
          <el-button type="primary" :loading="loading" @click="register">注册</el-button>
        </el-form>
      </el-tab-pane>

      <el-tab-pane label="权限判断" name="permission">
        <div class="grid">
          <el-form class="panel" label-position="top">
            <el-form-item label="用户 ID" required>
              <el-input-number v-model="permissionForm.userId" :min="1" />
            </el-form-item>
            <el-form-item label="权限" required>
              <el-select v-model="permissionForm.permission" filterable>
                <el-option
                  v-for="permission in permissionCatalog"
                  :key="permission.code"
                  :label="permissionLabel(permission.code)"
                  :value="permission.code"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="社团 ID">
              <el-input-number v-model="permissionForm.clubId" :min="1" />
            </el-form-item>
            <el-button type="primary" @click="checkPermission">检查权限</el-button>
          </el-form>

          <div v-if="permissionResult" class="panel">
            <div class="summary">
              <div>
                <h3>{{ permissionResult.allowed ? "允许" : "拒绝" }}</h3>
                <p>{{ permissionResult.message }}</p>
              </div>
              <el-tag :type="permissionResult.allowed ? 'success' : 'danger'">
                {{ permissionResult.allowed ? "通过" : "无权限" }}
              </el-tag>
            </div>
            <el-table :data="permissionResult.matchedRoles" empty-text="暂无匹配角色">
              <el-table-column prop="name" label="匹配角色" />
              <el-table-column label="范围" width="100">
                <template #default="{ row }">{{ scopeLabel(row.scope) }}</template>
              </el-table-column>
              <el-table-column prop="clubId" label="社团 ID" width="100" />
            </el-table>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane label="角色分配" name="assign">
        <div class="grid">
          <el-form class="panel" label-position="top">
            <el-form-item label="操作人用户 ID" required>
              <el-input-number v-model="assignForm.operatorUserId" :min="1" />
            </el-form-item>
            <el-form-item label="目标用户 ID" required>
              <el-input-number v-model="assignForm.targetUserId" :min="1" />
            </el-form-item>
            <el-form-item label="角色" required>
              <el-select v-model="assignForm.roleCode" filterable>
                <el-option
                  v-for="role in roleDefinitions"
                  :key="role.code"
                  :label="`${role.name}（${role.code}）`"
                  :value="role.code"
                />
              </el-select>
            </el-form-item>
            <el-form-item v-if="selectedAssignRole?.scope === 'club'" label="社团 ID" required>
              <el-input-number v-model="assignForm.clubId" :min="1" />
            </el-form-item>
            <el-button type="primary" @click="assignRole">分配角色</el-button>
          </el-form>

          <div v-if="assignmentResult" class="panel">
            <div class="summary">
              <div>
                <h3>{{ assignmentResult.role.name }}</h3>
                <p>{{ assignmentResult.message }}</p>
              </div>
              <el-tag :type="assignmentResult.alreadyExists ? 'info' : 'success'">
                {{ assignmentResult.alreadyExists ? "已存在" : "已分配" }}
              </el-tag>
            </div>
            <el-descriptions :column="1" size="small" border>
              <el-descriptions-item label="目标用户 ID">
                {{ assignmentResult.targetUserId }}
              </el-descriptions-item>
              <el-descriptions-item label="角色编码">
                {{ assignmentResult.role.code }}
              </el-descriptions-item>
              <el-descriptions-item label="范围">
                {{ scopeLabel(assignmentResult.role.scope) }}
              </el-descriptions-item>
              <el-descriptions-item label="社团 ID">
                {{ assignmentResult.role.clubId || "无" }}
              </el-descriptions-item>
            </el-descriptions>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane label="角色矩阵" name="matrix">
        <el-table :data="roleDefinitions" stripe empty-text="暂无角色数据">
          <el-table-column prop="name" label="角色" width="120" />
          <el-table-column prop="code" label="编码" width="150" />
          <el-table-column label="范围" width="100">
            <template #default="{ row }">{{ scopeLabel(row.scope) }}</template>
          </el-table-column>
          <el-table-column label="基础权限">
            <template #default="{ row }">
              <div class="permission-tags">
                <el-tag v-for="permission in row.permissions" :key="permission" size="small" effect="plain">
                  {{ permissionNameMap[permission] || permission }}
                </el-tag>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.page {
  max-width: 1080px;
  margin: 0 auto;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.toolbar h2,
.summary h3 {
  margin: 0;
}

.grid {
  display: grid;
  grid-template-columns: minmax(280px, 380px) 1fr;
  gap: 16px;
  align-items: start;
}

.panel {
  background: #fff;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 16px;
}

.wide-form {
  max-width: 880px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(220px, 1fr));
  gap: 0 16px;
}

.summary {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: flex-start;
  margin-bottom: 14px;
}

.summary p {
  margin: 6px 0 0;
  color: var(--el-text-color-secondary);
}

.role-list,
.permission-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.role-list {
  margin-top: 14px;
}

@media (max-width: 760px) {
  .grid,
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>

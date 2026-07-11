<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import {
  AddProjectMemberRequestMemberRoleEnum,
  ProjectMemberMemberRoleEnum,
  ProjectMemberMemberStatusEnum,
  ResponseError,
  type ProjectMember,
  type ProjectMemberCandidate,
} from "../api";
import { apiClient as api } from "../apiClient";
import { onSessionChange, readAuth, type AuthRole } from "../authSession";

const props = defineProps<{
  projectId: number;
  clubId: number;
  leaderUserId?: number | null;
}>();

type AddMemberForm = {
  userId: number | null;
  memberRole: AddProjectMemberRequestMemberRoleEnum;
  remark: string;
};

const principalRoleCodes = new Set(["club_president", "club_leader", "club_manager", "president"]);
const officerRoleCodes = new Set(["club_officer", "officer", "club_manager"]);

const members = ref<ProjectMember[]>([]);
const candidates = ref<ProjectMemberCandidate[]>([]);
const auth = ref(readAuth());
const loading = ref(false);
const candidateLoading = ref(false);
const saving = ref(false);
const removingId = ref<number | null>(null);
const showHistory = ref(false);
const addDialogVisible = ref(false);
const loadError = ref("");
const addFormRef = ref<FormInstance>();
let stopSessionChange: (() => void) | undefined;

const addForm = reactive<AddMemberForm>({
  userId: null as number | null,
  memberRole: AddProjectMemberRequestMemberRoleEnum.Member,
  remark: "",
});

const canManage = computed(() => {
  const currentUserId = auth.value?.user.id;
  if (!currentUserId) return false;
  if (currentUserId === props.leaderUserId) return true;

  return (auth.value?.roles ?? []).some((role) =>
    roleAllowsProjectMemberManagement(role, props.clubId),
  );
});

const addRules: FormRules<AddMemberForm> = {
  userId: [{ required: true, message: "请选择要加入项目的社团成员", trigger: "change" }],
  memberRole: [{ required: true, message: "请选择项目角色", trigger: "change" }],
};

const activeCount = computed(
  () =>
    members.value.filter((member) => member.memberStatus === ProjectMemberMemberStatusEnum.Active)
      .length,
);
const historicalCount = computed(
  () =>
    members.value.filter((member) => member.memberStatus !== ProjectMemberMemberStatusEnum.Active)
      .length,
);

const roleLabel: Record<string, string> = {
  [ProjectMemberMemberRoleEnum.Leader]: "负责人",
  [ProjectMemberMemberRoleEnum.Member]: "普通成员",
  [ProjectMemberMemberRoleEnum.Mentor]: "导师",
};

const statusLabel: Record<string, string> = {
  [ProjectMemberMemberStatusEnum.Active]: "参与中",
  [ProjectMemberMemberStatusEnum.Removed]: "已移除",
  [ProjectMemberMemberStatusEnum.Quit]: "已退出",
};

const statusType: Record<string, "success" | "warning" | "info"> = {
  [ProjectMemberMemberStatusEnum.Active]: "success",
  [ProjectMemberMemberStatusEnum.Removed]: "warning",
  [ProjectMemberMemberStatusEnum.Quit]: "info",
};

async function loadMembers() {
  loading.value = true;
  loadError.value = "";
  try {
    members.value = await api.getProjectMembers({
      projectId: props.projectId,
      includeInactive: showHistory.value,
    });
  } catch (error) {
    members.value = [];
    loadError.value = await toErrorMessage(error, "项目成员加载失败");
  } finally {
    loading.value = false;
  }
}

async function loadCandidates() {
  candidateLoading.value = true;
  try {
    candidates.value = await api.getProjectMemberCandidates({ projectId: props.projectId });
  } catch (error) {
    candidates.value = [];
    ElMessage.error(await toErrorMessage(error, "项目成员候选人加载失败"));
  } finally {
    candidateLoading.value = false;
  }
}

async function handleHistoryChange() {
  await loadMembers();
}

async function openAddDialog() {
  if (!canManage.value) {
    ElMessage.warning("当前账号没有项目成员维护权限。");
    return;
  }

  addForm.userId = null;
  addForm.memberRole = AddProjectMemberRequestMemberRoleEnum.Member;
  addForm.remark = "";
  addDialogVisible.value = true;
  await nextTick();
  addFormRef.value?.clearValidate();
  await loadCandidates();
}

async function addMember() {
  const valid = await addFormRef.value?.validate().catch(() => false);
  if (!valid || !addForm.userId) {
    return;
  }

  saving.value = true;
  try {
    await api.addProjectMember({
      projectId: props.projectId,
      addProjectMemberRequest: {
        userId: addForm.userId,
        memberRole: addForm.memberRole,
        remark: normalizeOptionalText(addForm.remark),
      },
    });
    ElMessage.success("项目成员已添加或恢复");
    addDialogVisible.value = false;
    await loadMembers();
  } catch (error) {
    ElMessage.error(await toErrorMessage(error, "项目成员添加失败"));
  } finally {
    saving.value = false;
  }
}

async function removeMember(member: ProjectMember) {
  if (member.userId === props.leaderUserId) {
    ElMessage.warning("当前项目负责人不能被移除，请先调整项目负责人。");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确认将“${memberDisplayName(member)}”移出当前项目？成员历史记录会保留。`,
      "移除项目成员",
      {
        type: "warning",
        confirmButtonText: "确认移除",
        cancelButtonText: "取消",
      },
    );
  } catch {
    return;
  }

  removingId.value = member.projectMemberId;
  try {
    await api.removeProjectMember({
      projectId: props.projectId,
      projectMemberId: member.projectMemberId,
    });
    ElMessage.success("项目成员已移除");
    await loadMembers();
  } catch (error) {
    ElMessage.error(await toErrorMessage(error, "项目成员移除失败"));
  } finally {
    removingId.value = null;
  }
}

function roleAllowsProjectMemberManagement(role: AuthRole, clubId: number) {
  const permissions = role.permissions ?? [];
  if (permissions.includes("*")) return true;
  if (permissions.includes("project:task:manage")) {
    const scope = normalizeRoleText(role.scope);
    if (scope !== "club" || roleCoversClub(role, clubId)) return true;
  }

  const roleCode = normalizeRoleText(role.code);
  return (
    (principalRoleCodes.has(roleCode) || officerRoleCodes.has(roleCode)) &&
    roleCoversClub(role, clubId)
  );
}

function roleCoversClub(role: AuthRole, clubId: number) {
  return role.clubId === clubId || Boolean(role.clubIds?.includes(clubId));
}

function normalizeRoleText(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
}

function memberDisplayName(member: ProjectMember) {
  const name = member.realName?.trim() || `用户 #${member.userId}`;
  return member.studentNo ? `${name}（${member.studentNo}）` : name;
}

function formatDateTime(value?: Date | null) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(value);
}

function normalizeOptionalText(value: string) {
  const normalized = value.trim();
  return normalized || undefined;
}

async function toErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ResponseError) {
    try {
      const body = (await error.response.clone().json()) as { message?: string };
      if (body.message) return `${fallback}：${body.message}`;
    } catch {
      return `${fallback}（HTTP ${error.response.status}）`;
    }
  }

  return fallback;
}

watch(
  () => props.projectId,
  () => {
    showHistory.value = false;
    void loadMembers();
  },
);

watch(canManage, (allowed) => {
  if (!allowed && showHistory.value) {
    showHistory.value = false;
    void loadMembers();
  }
});

onMounted(() => {
  stopSessionChange = onSessionChange(() => {
    auth.value = readAuth();
  });
  void loadMembers();
});

onUnmounted(() => {
  stopSessionChange?.();
});
</script>

<template>
  <section class="members-panel" aria-labelledby="project-members-heading">
    <div class="panel-toolbar">
      <div>
        <div class="panel-title-row">
          <h2 id="project-members-heading">项目成员</h2>
          <span class="member-metric">当前 {{ activeCount }}</span>
          <span v-if="showHistory" class="member-metric muted">历史 {{ historicalCount }}</span>
        </div>
        <p>任务执行人将从这里的有效成员中选择，负责人关系由项目负责人分配流程维护。</p>
      </div>

      <div class="panel-actions">
        <el-switch
          v-if="canManage"
          v-model="showHistory"
          active-text="包含历史成员"
          @change="handleHistoryChange"
        />
        <el-button :loading="loading" @click="loadMembers">刷新</el-button>
        <el-button v-if="canManage" type="primary" @click="openAddDialog">添加成员</el-button>
      </div>
    </div>

    <el-alert
      v-if="loadError"
      class="load-alert"
      type="error"
      :title="loadError"
      show-icon
      :closable="false"
    >
      <template #default>
        <el-button link type="primary" @click="loadMembers">重新加载</el-button>
      </template>
    </el-alert>

    <div class="table-wrap">
      <el-table
        v-loading="loading"
        class="members-table"
        :data="members"
        row-key="projectMemberId"
        :empty-text="showHistory ? '暂无项目成员记录' : '暂无有效项目成员'"
      >
        <el-table-column label="成员" min-width="180" fixed="left">
          <template #default="{ row }">
            <div class="member-identity">
              <strong>{{ row.realName || `用户 #${row.userId}` }}</strong>
              <span>{{ row.studentNo || "暂无学工号" }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="项目角色" width="110">
          <template #default="{ row }">
            {{ roleLabel[row.memberRole] || row.memberRole }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="105">
          <template #default="{ row }">
            <el-tag :type="statusType[row.memberStatus] || 'info'" size="small" effect="plain">
              {{ statusLabel[row.memberStatus] || row.memberStatus }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="加入 / 恢复时间" min-width="170">
          <template #default="{ row }">{{ formatDateTime(row.joinedAt) }}</template>
        </el-table-column>
        <el-table-column v-if="showHistory" label="离开时间" min-width="170">
          <template #default="{ row }">{{ formatDateTime(row.leftAt) }}</template>
        </el-table-column>
        <el-table-column prop="remark" label="备注" min-width="170" show-overflow-tooltip>
          <template #default="{ row }">{{ row.remark || "—" }}</template>
        </el-table-column>
        <el-table-column v-if="canManage" label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-tooltip
              :disabled="row.userId !== leaderUserId"
              content="当前负责人不能移除"
              placement="top"
            >
              <span
                :tabindex="row.userId === leaderUserId ? 0 : undefined"
                :aria-label="row.userId === leaderUserId ? '当前负责人不能移除' : undefined"
              >
                <el-button
                  v-if="row.memberStatus === ProjectMemberMemberStatusEnum.Active"
                  text
                  type="danger"
                  size="small"
                  :disabled="row.userId === leaderUserId"
                  :loading="removingId === row.projectMemberId"
                  @click="removeMember(row)"
                >
                  移除
                </el-button>
                <span v-else class="no-action">已留档</span>
              </span>
            </el-tooltip>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <el-dialog
      v-model="addDialogVisible"
      title="添加项目成员"
      width="min(480px, calc(100vw - 32px))"
    >
      <el-form
        ref="addFormRef"
        :model="addForm"
        :rules="addRules"
        label-position="top"
        @submit.prevent
      >
        <el-form-item label="所属社团有效成员" prop="userId">
          <el-select
            v-model="addForm.userId"
            filterable
            :loading="candidateLoading"
            no-data-text="暂无可添加成员"
            placeholder="按姓名或学工号搜索"
          >
            <el-option
              v-for="candidate in candidates"
              :key="candidate.userId"
              :label="candidate.displayName"
              :value="candidate.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="项目角色" prop="memberRole">
          <el-radio-group v-model="addForm.memberRole">
            <el-radio-button :value="AddProjectMemberRequestMemberRoleEnum.Member">
              普通成员
            </el-radio-button>
            <el-radio-button :value="AddProjectMemberRequestMemberRoleEnum.Mentor">
              导师
            </el-radio-button>
          </el-radio-group>
          <div class="field-hint">负责人不能在此指定，请使用项目列表中的负责人分配功能。</div>
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="addForm.remark"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit
            placeholder="可填写成员职责、加入说明等"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="addDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="addMember">确认添加</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.members-panel {
  padding-top: 6px;
}

.panel-toolbar {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 24px;
  padding: 12px 0 18px;
}

.panel-title-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.panel-title-row h2 {
  margin: 0;
  font-size: 20px;
}

.members-panel p {
  margin: 7px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 14px;
  line-height: 1.6;
}

.member-metric {
  padding: 3px 7px;
  border: 1px solid var(--el-color-success-light-5);
  color: var(--el-color-success);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.member-metric.muted {
  border-color: var(--el-border-color);
  color: var(--el-text-color-secondary);
}

.panel-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 10px;
}

.load-alert {
  margin-bottom: 14px;
}

.table-wrap {
  width: 100%;
}

.member-identity {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.member-identity strong {
  color: var(--el-text-color-primary);
  font-weight: 600;
}

.member-identity span,
.no-action,
.field-hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.field-hint {
  width: 100%;
  margin-top: 8px;
  line-height: 1.5;
}

@media (max-width: 720px) {
  .panel-toolbar {
    display: block;
  }

  .panel-actions {
    justify-content: flex-start;
    margin-top: 14px;
  }
}
</style>

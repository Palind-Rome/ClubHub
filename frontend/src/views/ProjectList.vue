<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from "vue";
import { RouterLink } from "vue-router";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import {
  ProjectProjectStatusEnum,
  ReviewProjectRequestProjectStatusEnum,
  type CancelProjectRequest,
  type Club,
  type Project,
  type UserSummary,
} from "../api";
import { apiClient as api } from "../apiClient";
import { onSessionChange, readAuth, type AuthRole } from "../authSession";

const projectReviewPermission = "project:review";
const projectTaskManagePermission = "project:task:manage";
const principalRoleCodes = new Set(["club_president", "club_leader", "club_manager", "president"]);
const officerRoleCodes = new Set(["club_officer", "officer", "club_manager"]);
const advisorRoleCodes = new Set(["advisor", "club_advisor", "teacher_advisor"]);

type ProjectForm = {
  clubId: number | null;
  projectName: string;
  description: string;
  leaderUserId: number | null;
  startDate: Date | null;
  endDate: Date | null;
};

const clubs = ref<Club[]>([]);
const projects = ref<Project[]>([]);
const leaderCandidates = ref<UserSummary[]>([]);
const leaderCandidatesByClub = ref<Record<number, UserSummary[]>>({});
const auth = ref(readAuth());
const loading = ref(false);
const saving = ref(false);
const leaderCandidateLoading = ref(false);
const cancelSavingId = ref<number | null>(null);
const leaderSavingId = ref<number | null>(null);
const reviewSavingId = ref<number | null>(null);
const createDialogVisible = ref(false);
const leaderDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const createFormRef = ref<FormInstance>();
const leaderFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();
let stopSessionChange: (() => void) | undefined;
let leaderCandidateClubId: number | null = null;

const filters = reactive({
  clubId: undefined as number | undefined,
  page: 1,
  pageSize: 10,
});

const createForm = reactive<ProjectForm>({
  clubId: null,
  projectName: "",
  description: "",
  leaderUserId: null,
  startDate: null,
  endDate: null,
});

const leaderForm = reactive({
  projectId: 0,
  leaderUserId: null as number | null,
});

const reviewForm = reactive({
  projectId: 0,
  projectStatus: ReviewProjectRequestProjectStatusEnum.Running,
  reviewComment: "",
});

const statusLabel: Record<string, string> = {
  [ProjectProjectStatusEnum.Pending]: "待审核",
  [ProjectProjectStatusEnum.Running]: "执行中",
  [ProjectProjectStatusEnum.Finished]: "已完成",
  [ProjectProjectStatusEnum.Delayed]: "已延期",
  [ProjectProjectStatusEnum.Closed]: "已关闭",
};

const statusType: Record<string, "success" | "warning" | "info" | "danger" | "primary"> = {
  [ProjectProjectStatusEnum.Pending]: "warning",
  [ProjectProjectStatusEnum.Running]: "success",
  [ProjectProjectStatusEnum.Finished]: "primary",
  [ProjectProjectStatusEnum.Delayed]: "danger",
  [ProjectProjectStatusEnum.Closed]: "info",
};

const numericStatusMap: Record<string, ProjectProjectStatusEnum> = {
  "1": ProjectProjectStatusEnum.Pending,
  "2": ProjectProjectStatusEnum.Running,
  "3": ProjectProjectStatusEnum.Finished,
  "4": ProjectProjectStatusEnum.Delayed,
  "5": ProjectProjectStatusEnum.Closed,
};

const clubNameMap = computed(() => {
  const map = new Map<number, string>();
  clubs.value.forEach((club) => map.set(club.id, club.name));
  return map;
});

const currentUserId = computed(() => auth.value?.user.id ?? null);
const currentUserName = computed(
  () => auth.value?.user.realName || auth.value?.user.username || "当前用户",
);
const currentRoles = computed(() => auth.value?.roles ?? []);
const creatableClubs = computed(() =>
  clubs.value.filter((club) => canCreateProjectForClub(club.id)),
);
const leaderUserMap = computed(() => {
  const map = new Map<number, UserSummary>();
  Object.values(leaderCandidatesByClub.value).forEach((users) => {
    users.forEach((user) => map.set(user.id, user));
  });
  return map;
});

const createRules: FormRules<ProjectForm> = {
  clubId: [{ required: true, message: "请选择立项社团", trigger: "change" }],
  projectName: [
    {
      validator: (_rule, value: string, callback) => {
        if (!value || value.trim().length === 0) {
          callback(new Error("请输入项目名称"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
    { min: 2, max: 80, message: "项目名称长度应为 2 到 80 个字符", trigger: "blur" },
  ],
  leaderUserId: [
    {
      type: "number",
      min: 1,
      message: "请选择有效项目负责人",
      trigger: "change",
    },
  ],
  startDate: [{ required: true, message: "请选择开始日期", trigger: "change" }],
  endDate: [
    {
      validator: (_rule, value: Date | null, callback) => {
        if (value && createForm.startDate && value < createForm.startDate) {
          callback(new Error("结束日期不能早于开始日期"));
          return;
        }
        callback();
      },
      trigger: "change",
    },
  ],
};

const leaderRules: FormRules<typeof leaderForm> = {
  leaderUserId: [
    { required: true, message: "请选择项目负责人", trigger: "change" },
    { type: "number", min: 1, message: "请选择有效项目负责人", trigger: "change" },
  ],
};

const reviewRules: FormRules<typeof reviewForm> = {
  projectStatus: [{ required: true, message: "请选择审核结果", trigger: "change" }],
};

async function validateForm(form?: FormInstance) {
  if (!form) return false;
  return form.validate().catch(() => false);
}

async function loadClubs() {
  try {
    clubs.value = await api.getClubs();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "社团列表加载失败"));
  }
}

async function loadProjects() {
  loading.value = true;
  try {
    projects.value = await api.getProjects({
      clubId: filters.clubId,
      page: filters.page,
      pageSize: filters.pageSize,
    });
    await loadProjectLeaderNames(projects.value);
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目列表加载失败"));
  } finally {
    loading.value = false;
  }
}

async function loadProjectLeaderNames(projectList: Project[]) {
  if (!currentUserId.value) return;

  const clubIds = Array.from(
    new Set(
      projectList
        .filter((project) => project.leaderUserId && !leaderUserMap.value.has(project.leaderUserId))
        .map((project) => project.clubId),
    ),
  );

  await Promise.all(clubIds.map((clubId) => loadLeaderCandidates(clubId, { silent: true })));
}

async function loadLeaderCandidates(clubId?: number | null, options: { silent?: boolean } = {}) {
  if (!clubId || !currentUserId.value) {
    leaderCandidates.value = [];
    leaderCandidateClubId = null;
    return;
  }

  if (leaderCandidateClubId === clubId && leaderCandidates.value.length > 0) return;

  leaderCandidateLoading.value = true;
  try {
    leaderCandidates.value = await api.getUsers({ clubId });
    leaderCandidatesByClub.value = {
      ...leaderCandidatesByClub.value,
      [clubId]: leaderCandidates.value,
    };
    leaderCandidateClubId = clubId;
  } catch (error) {
    leaderCandidates.value = [];
    leaderCandidateClubId = null;
    if (!options.silent) {
      ElMessage.error(toErrorMessage(error, "负责人候选人加载失败"));
    }
  } finally {
    leaderCandidateLoading.value = false;
  }
}

async function changePage(nextPage: number) {
  filters.page = Math.max(1, nextPage);
  await loadProjects();
}

async function openCreateDialog() {
  if (!currentUserId.value) {
    ElMessage.warning("请先登录后再提交项目立项申请。");
    return;
  }

  if (creatableClubs.value.length === 0) {
    ElMessage.warning("当前账号没有可提交立项申请的社团。");
    return;
  }

  const selectedClubId =
    filters.clubId && canCreateProjectForClub(filters.clubId)
      ? filters.clubId
      : creatableClubs.value[0]?.id;

  createForm.clubId = selectedClubId ?? null;
  createForm.projectName = "";
  createForm.description = "";
  createForm.leaderUserId = null;
  createForm.startDate = null;
  createForm.endDate = null;
  await loadLeaderCandidates(createForm.clubId);
  createDialogVisible.value = true;
}

async function handleCreateClubChange(clubId?: number) {
  createForm.leaderUserId = null;
  await loadLeaderCandidates(clubId);
}

async function createProject() {
  if (
    !(await validateForm(createFormRef.value)) ||
    !createForm.clubId ||
    !createForm.startDate ||
    !currentUserId.value
  ) {
    return;
  }

  saving.value = true;
  try {
    await api.createProject({
      createProjectRequest: {
        currentUserId: currentUserId.value,
        clubId: createForm.clubId,
        projectName: createForm.projectName.trim(),
        description: normalizeOptionalText(createForm.description),
        leaderUserId: createForm.leaderUserId || undefined,
        startDate: createForm.startDate,
        endDate: createForm.endDate || undefined,
      },
    });
    ElMessage.success("项目立项申请已提交");
    createDialogVisible.value = false;
    await loadProjects();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目立项申请提交失败"));
  } finally {
    saving.value = false;
  }
}

function openLeaderDialog(project: Project) {
  if (!canAssignProjectLeader(project)) {
    ElMessage.warning("只有本社团负责人或干部可以分配项目负责人。");
    return;
  }

  leaderForm.projectId = project.id;
  leaderForm.leaderUserId = project.leaderUserId ?? null;
  void loadLeaderCandidates(project.clubId);
  leaderDialogVisible.value = true;
}

async function assignLeader() {
  if (
    !(await validateForm(leaderFormRef.value)) ||
    !leaderForm.leaderUserId ||
    !currentUserId.value
  ) {
    return;
  }

  leaderSavingId.value = leaderForm.projectId;
  try {
    await api.assignProjectLeader({
      projectId: leaderForm.projectId,
      assignProjectLeaderRequest: {
        currentUserId: currentUserId.value,
        leaderUserId: leaderForm.leaderUserId,
      },
    });
    ElMessage.success("项目负责人已更新");
    leaderDialogVisible.value = false;
    await loadProjects();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目负责人更新失败"));
  } finally {
    leaderSavingId.value = null;
  }
}

function openReviewDialog(project: Project) {
  if (!canReviewProject(project)) {
    ElMessage.warning("只有本社团指导老师可以审核项目。");
    return;
  }

  reviewForm.projectId = project.id;
  reviewForm.projectStatus = ReviewProjectRequestProjectStatusEnum.Running;
  reviewForm.reviewComment = "";
  reviewDialogVisible.value = true;
}

async function reviewProject() {
  if (!(await validateForm(reviewFormRef.value)) || !currentUserId.value) {
    return;
  }

  reviewSavingId.value = reviewForm.projectId;
  try {
    await api.reviewProject({
      projectId: reviewForm.projectId,
      reviewProjectRequest: {
        currentUserId: currentUserId.value,
        projectStatus: reviewForm.projectStatus,
        reviewComment: normalizeOptionalText(reviewForm.reviewComment),
      },
    });
    ElMessage.success("项目审核结果已保存");
    reviewDialogVisible.value = false;
    await loadProjects();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目审核失败"));
  } finally {
    reviewSavingId.value = null;
  }
}

function normalizeProjectStatus(status: Project["projectStatus"] | number | string) {
  const statusText = String(status);
  return numericStatusMap[statusText] ?? statusText;
}

function isPendingProject(project: Project) {
  return normalizeProjectStatus(project.projectStatus) === ProjectProjectStatusEnum.Pending;
}

function canCreateProjectForClub(clubId: number) {
  if (hasSystemAdminRole() || hasPlatformAdminRole()) return false;

  return hasScopedRole(clubId, principalRoleCodes) || canAdvisorActForClub(clubId);
}

function canAssignProjectLeader(project: Project) {
  if (hasSystemAdminRole() || hasPlatformAdminRole()) return false;

  return (
    hasClubPermission(projectTaskManagePermission, project.clubId) ||
    hasScopedRole(project.clubId, principalRoleCodes) ||
    hasScopedRole(project.clubId, officerRoleCodes)
  );
}

function canReviewProject(project: Project) {
  return isPendingProject(project) && canAdvisorActForClub(project.clubId);
}

function canCancelProject(project: Project) {
  const status = normalizeProjectStatus(project.projectStatus);
  if (hasSystemAdminRole())
    return (
      status === ProjectProjectStatusEnum.Pending || status === ProjectProjectStatusEnum.Running
    );
  if (hasPlatformAdminRole()) return status === ProjectProjectStatusEnum.Running;
  return (
    status === ProjectProjectStatusEnum.Pending && hasScopedRole(project.clubId, principalRoleCodes)
  );
}

async function cancelProject(project: Project) {
  if (!canCancelProject(project) || !currentUserId.value) {
    ElMessage.warning("仅系统管理员、本社团负责人或校级社团管理员可撤销符合条件的项目。");
    return;
  }

  let cancelReason = "";
  try {
    const result = await ElMessageBox.prompt(
      `确认以“${currentUserName.value}”身份撤销“${project.projectName}”？`,
      "撤销立项申请",
      {
        confirmButtonText: "确认撤销",
        cancelButtonText: "取消",
        inputType: "textarea",
        inputPlaceholder: "可填写撤销原因",
        inputValidator: (value) =>
          String(value ?? "").length <= 200 || "撤销原因不能超过 200 个字符",
      },
    );
    cancelReason = String(result.value ?? "");
  } catch {
    return;
  }

  const cancelProjectRequest: CancelProjectRequest = {
    currentUserId: currentUserId.value,
    cancelReason: normalizeOptionalText(cancelReason),
  };

  cancelSavingId.value = project.id;
  try {
    await api.cancelProject({
      projectId: project.id,
      cancelProjectRequest,
    });
    ElMessage.success("项目立项申请已撤销");
    await loadProjects();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目立项申请撤销失败"));
  } finally {
    cancelSavingId.value = null;
  }
}

function hasClubPermission(permission: string, clubId: number) {
  return currentRoles.value.some((role) => roleAllowsClubPermission(role, permission, clubId));
}

function roleAllowsClubPermission(role: AuthRole, permission: string, clubId: number) {
  const permissions = role.permissions ?? [];
  if (!permissions.includes("*") && !permissions.includes(permission)) return false;
  if (permissions.includes("*")) return true;

  const scope = normalizeRoleText(role.scope);
  return scope !== "club" || roleCoversClub(role, clubId);
}

function hasScopedRole(clubId: number, roleCodes: ReadonlySet<string>) {
  return currentRoles.value.some(
    (role) => roleCodes.has(normalizeRoleText(role.code)) && roleCoversClub(role, clubId),
  );
}

function canAdvisorActForClub(clubId: number) {
  if (hasSystemAdminRole() || hasPlatformAdminRole()) return false;

  return (
    hasScopedRole(clubId, advisorRoleCodes) ||
    hasClubPermission(projectReviewPermission, clubId) ||
    isNamedAdvisorForClub(clubId)
  );
}

function isNamedAdvisorForClub(clubId: number) {
  const user = auth.value?.user;
  if (!user) return false;

  const club = clubs.value.find((item) => item.id === clubId);
  if (!club) return false;
  if (club.advisorUserId === user.id) return true;

  const advisorName = (club.advisorName ?? "").trim();
  const realName = (user.realName ?? "").trim();
  const studentNo = (user.studentNo ?? "").trim();
  return (
    Boolean(advisorName && realName && advisorName.includes(realName)) && studentNo.length === 5
  );
}

function hasSystemAdminRole() {
  return currentRoles.value.some((role) =>
    ["system_admin", "sysadmin"].includes(normalizeRoleText(role.code)),
  );
}

function hasPlatformAdminRole() {
  return currentRoles.value.some((role) =>
    ["platform_admin", "club_admin", "admin", "club_reviewer"].includes(
      normalizeRoleText(role.code),
    ),
  );
}

function roleCoversClub(role: AuthRole, clubId: number) {
  return role.clubId === clubId || Boolean(role.clubIds?.includes(clubId));
}

function normalizeRoleText(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
}

function leaderCandidateLabel(user: UserSummary) {
  const name = user.realName || user.username || "未知用户";
  const identity = user.studentNo || user.username;
  return identity ? `${name}（${identity}）` : name;
}

function leaderDisplayName(leaderUserId?: number | null) {
  if (!leaderUserId) return "未分配";

  const user = leaderUserMap.value.get(leaderUserId);
  return user ? leaderCandidateLabel(user) : "未知负责人";
}

function formatDate(value?: Date | null) {
  if (!value) return "未填写";
  return value.toLocaleDateString("zh-CN");
}

function normalizeOptionalText(value: string) {
  const text = value.trim();
  return text.length > 0 ? text : undefined;
}

function toErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message) return `${fallback}：${error.message}`;
  return fallback;
}

onMounted(async () => {
  stopSessionChange = onSessionChange(() => {
    auth.value = readAuth();
  });
  await loadClubs();
  await loadProjects();
});

onUnmounted(() => {
  stopSessionChange?.();
});
</script>

<template>
  <div class="page project-page">
    <div class="toolbar">
      <div>
        <h2>项目管理</h2>
        <p class="subtitle">演示社团项目立项申请、负责人分配和立项审核流程。</p>
      </div>
      <el-button type="primary" :disabled="!currentUserId" @click="openCreateDialog">
        提交立项申请
      </el-button>
    </div>

    <el-card class="filter-card" shadow="never">
      <el-form inline label-position="left">
        <el-form-item label="所属社团">
          <el-select
            v-model="filters.clubId"
            clearable
            filterable
            placeholder="全部社团"
            style="width: 220px"
            @change="loadProjects"
            @clear="loadProjects"
          >
            <el-option v-for="club in clubs" :key="club.id" :label="club.name" :value="club.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="每页数量">
          <el-input-number v-model="filters.pageSize" :min="1" :max="100" @change="loadProjects" />
        </el-form-item>
        <el-form-item>
          <el-button :loading="loading" @click="loadProjects">刷新列表</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-table v-loading="loading" :data="projects" stripe empty-text="暂无项目数据">
      <el-table-column prop="projectName" label="项目名称" min-width="180" show-overflow-tooltip />
      <el-table-column label="所属社团" min-width="140">
        <template #default="{ row }">
          {{ clubNameMap.get(row.clubId) || "未知社团" }}
        </template>
      </el-table-column>
      <el-table-column label="负责人" min-width="150" show-overflow-tooltip>
        <template #default="{ row }">
          {{ leaderDisplayName(row.leaderUserId) }}
        </template>
      </el-table-column>
      <el-table-column label="计划时间" min-width="180">
        <template #default="{ row }">
          {{ formatDate(row.startDate) }} 至 {{ formatDate(row.endDate) }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag
            :type="statusType[normalizeProjectStatus(row.projectStatus)] || 'info'"
            size="small"
          >
            {{
              statusLabel[normalizeProjectStatus(row.projectStatus)] ||
              normalizeProjectStatus(row.projectStatus)
            }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column
        prop="reviewComment"
        label="审核意见"
        min-width="160"
        show-overflow-tooltip
      />
      <el-table-column label="操作" width="300" fixed="right">
        <template #default="{ row }">
          <RouterLink :to="`/projects/${row.id}/workspace`" custom v-slot="{ href, navigate }">
            <el-link
              class="workspace-action"
              :href="href"
              type="primary"
              underline="never"
              @click="navigate"
            >
              项目空间
            </el-link>
          </RouterLink>
          <el-button
            v-if="canAssignProjectLeader(row)"
            size="small"
            text
            type="primary"
            @click="openLeaderDialog(row)"
          >
            分配负责人
          </el-button>
          <el-button
            v-if="canReviewProject(row)"
            size="small"
            text
            type="success"
            @click="openReviewDialog(row)"
          >
            审核
          </el-button>
          <el-button
            v-if="canCancelProject(row)"
            size="small"
            text
            type="danger"
            :loading="cancelSavingId === row.id"
            @click="cancelProject(row)"
          >
            撤销
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-button :disabled="filters.page <= 1 || loading" @click="changePage(filters.page - 1)">
        上一页
      </el-button>
      <span>第 {{ filters.page }} 页</span>
      <el-button
        :disabled="projects.length < filters.pageSize || loading"
        @click="changePage(filters.page + 1)"
      >
        下一页
      </el-button>
    </div>

    <el-dialog v-model="createDialogVisible" title="提交项目立项申请" width="560px">
      <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-position="top">
        <el-form-item label="所属社团" prop="clubId">
          <el-select
            v-model="createForm.clubId"
            filterable
            placeholder="请选择社团"
            @change="handleCreateClubChange"
          >
            <el-option
              v-for="club in creatableClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="项目名称" prop="projectName">
          <el-input v-model="createForm.projectName" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="项目简介">
          <el-input
            v-model="createForm.description"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="请输入项目背景、目标或预期成果"
          />
        </el-form-item>
        <el-form-item label="项目负责人（可选）" prop="leaderUserId">
          <el-select
            v-model="createForm.leaderUserId"
            clearable
            filterable
            :loading="leaderCandidateLoading"
            placeholder="可稍后分配；按姓名或学工号搜索"
          >
            <el-option
              v-for="user in leaderCandidates"
              :key="user.id"
              :label="leaderCandidateLabel(user)"
              :value="user.id"
            />
          </el-select>
        </el-form-item>
        <div class="date-row">
          <el-form-item label="开始日期" prop="startDate">
            <el-date-picker
              v-model="createForm.startDate"
              type="date"
              placeholder="请选择开始日期"
            />
          </el-form-item>
          <el-form-item label="结束日期" prop="endDate">
            <el-date-picker v-model="createForm.endDate" type="date" placeholder="请选择结束日期" />
          </el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="createProject">提交申请</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="leaderDialogVisible" title="分配项目负责人" width="420px">
      <el-form ref="leaderFormRef" :model="leaderForm" :rules="leaderRules" label-position="top">
        <el-form-item label="项目负责人" prop="leaderUserId">
          <el-select
            v-model="leaderForm.leaderUserId"
            filterable
            :loading="leaderCandidateLoading"
            placeholder="按姓名或学工号搜索"
          >
            <el-option
              v-for="user in leaderCandidates"
              :key="user.id"
              :label="leaderCandidateLabel(user)"
              :value="user.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="leaderDialogVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="leaderSavingId === leaderForm.projectId"
          @click="assignLeader"
        >
          保存负责人
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="审核项目立项申请" width="460px">
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-position="top">
        <el-form-item label="审核结果" prop="projectStatus">
          <el-radio-group v-model="reviewForm.projectStatus">
            <el-radio-button :label="ReviewProjectRequestProjectStatusEnum.Running">
              审批通过
            </el-radio-button>
            <el-radio-button :label="ReviewProjectRequestProjectStatusEnum.Closed">
              关闭申请
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="审核意见">
          <el-input
            v-model="reviewForm.reviewComment"
            type="textarea"
            :rows="3"
            maxlength="300"
            show-word-limit
            placeholder="可填写审批说明"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="reviewSavingId === reviewForm.projectId"
          @click="reviewProject"
        >
          保存审核结果
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.project-page {
  max-width: 1120px;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 12px;
}

.toolbar h2 {
  margin: 0;
}

.subtitle {
  margin: 6px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 14px;
}

.filter-card {
  margin-bottom: 12px;
}

.filter-card :deep(.el-card__body) {
  padding-bottom: 0;
}

.pager {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 12px;
  margin-top: 12px;
  color: var(--el-text-color-regular);
}

.date-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.workspace-action {
  margin-right: 12px;
  font-size: 12px;
}

@media (max-width: 720px) {
  .toolbar,
  .date-row {
    display: block;
  }

  .toolbar .el-button {
    margin-top: 12px;
  }
}
</style>

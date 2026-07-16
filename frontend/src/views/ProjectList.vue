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
const apiBasePath = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");
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

type ProjectTask = {
  id: number;
  projectId: number;
  assignees: Array<{ userId: number; displayName: string }>;
  assigneeUserId?: number | null;
  assigneeDisplayName?: string | null;
  title: string;
  content?: string | null;
  priority?: string | null;
  startDate?: Date | null;
  dueDate?: Date | null;
  finishDate?: Date | null;
  progress?: number | null;
  taskStatus: string;
  delayReason?: string | null;
  deliverableTitle?: string | null;
  deliverableDesc?: string | null;
  deliverableUrl?: string | null;
  deliverableStatus: string;
  reviewerUserId?: number | null;
  reviewerDisplayName?: string | null;
  reviewComment?: string | null;
  deliverableSubmitterId?: number | null;
  deliverableSubmitterDisplayName?: string | null;
  deliverableSubmittedAt?: Date | null;
  deliverableReviewedAt?: Date | null;
};

type ProjectTaskApi = Omit<
  ProjectTask,
  "startDate" | "dueDate" | "finishDate" | "deliverableSubmittedAt" | "deliverableReviewedAt"
> & {
  startDate?: string | null;
  dueDate?: string | null;
  finishDate?: string | null;
  deliverableSubmittedAt?: string | null;
  deliverableReviewedAt?: string | null;
};

type DeliverableForm = {
  deliverableTitle: string;
  deliverableDesc: string;
  deliverableUrl: string;
};

type DeliverableReviewForm = {
  approved: boolean;
  reviewComment: string;
};

type ProjectWithTaskAccess = Project & {
  canViewTasks?: boolean | null;
};

const clubs = ref<Club[]>([]);
const projects = ref<ProjectWithTaskAccess[]>([]);
const projectTasks = ref<ProjectTask[]>([]);
const leaderCandidates = ref<UserSummary[]>([]);
const leaderCandidatesByClub = ref<Record<number, UserSummary[]>>({});
const auth = ref(readAuth());
const loading = ref(false);
const saving = ref(false);
const taskLoading = ref(false);
const leaderCandidateLoading = ref(false);
const cancelSavingId = ref<number | null>(null);
const leaderSavingId = ref<number | null>(null);
const reviewSavingId = ref<number | null>(null);
const taskSavingId = ref<number | null>(null);
const createDialogVisible = ref(false);
const leaderDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const taskDialogVisible = ref(false);
const deliverableDialogVisible = ref(false);
const deliverableReviewDialogVisible = ref(false);
const createFormRef = ref<FormInstance>();
const leaderFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();
const deliverableFormRef = ref<FormInstance>();
const deliverableReviewFormRef = ref<FormInstance>();
const selectedProject = ref<ProjectWithTaskAccess | null>(null);
const activeTask = ref<ProjectTask | null>(null);
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

const deliverableForm = reactive<DeliverableForm>({
  deliverableTitle: "",
  deliverableDesc: "",
  deliverableUrl: "",
});

const deliverableReviewForm = reactive<DeliverableReviewForm>({
  approved: true,
  reviewComment: "",
});

const statusLabel: Record<string, string> = {
  [ProjectProjectStatusEnum.Pending]: "待立项审核",
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

const taskStatusLabel: Record<string, string> = {
  pending: "待开始",
  in_progress: "进行中",
  completed: "已完成",
  delayed: "已延期",
};

const taskStatusType: Record<string, "success" | "warning" | "info" | "danger" | "primary"> = {
  pending: "info",
  in_progress: "primary",
  completed: "success",
  delayed: "danger",
};

const deliverableStatusLabel: Record<string, string> = {
  not_submitted: "未提交",
  pending: "待成果审核",
  approved: "已通过",
  rejected: "已驳回",
};

const deliverableStatusType: Record<string, "success" | "warning" | "info" | "danger"> = {
  not_submitted: "info",
  pending: "warning",
  approved: "success",
  rejected: "danger",
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

const deliverableRules: FormRules<DeliverableForm> = {
  deliverableTitle: [
    {
      validator: (_rule, value: string, callback) => {
        if (!value || value.trim().length === 0) {
          callback(new Error("请输入成果标题"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
    { min: 1, max: 100, message: "成果标题不能超过 100 个字符", trigger: "blur" },
  ],
  deliverableDesc: [{ max: 4000, message: "成果说明不能超过 4000 个字符", trigger: "blur" }],
  deliverableUrl: [
    { max: 255, message: "成果链接不能超过 255 个字符", trigger: "blur" },
    {
      validator: (_rule, value: string, callback) => {
        if (value && !/^https?:\/\/.+/i.test(value.trim())) {
          callback(new Error("成果链接必须是 http 或 https 地址"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
  ],
};

const deliverableReviewRules: FormRules<DeliverableReviewForm> = {
  approved: [{ required: true, message: "请选择审核结果", trigger: "change" }],
  reviewComment: [
    { max: 255, message: "审核意见不能超过 255 个字符", trigger: "blur" },
    {
      validator: (_rule, value: string, callback) => {
        if (deliverableReviewForm.approved === false && (!value || value.trim().length === 0)) {
          callback(new Error("驳回成果时必须填写审核意见"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
  ],
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

async function loadProjectLeaderNames(projectList: ProjectWithTaskAccess[]) {
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
    leaderCandidates.value = await projectApiRequest<UserSummary[]>(
      `/api/users?clubId=${encodeURIComponent(String(clubId))}`,
    );
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
  if (!(await validateForm(createFormRef.value)) || !createForm.clubId || !createForm.startDate) {
    return;
  }

  saving.value = true;
  try {
    await api.createProject({
      createProjectRequest: {
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

function openLeaderDialog(project: ProjectWithTaskAccess) {
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
  if (!(await validateForm(leaderFormRef.value)) || !leaderForm.leaderUserId) {
    return;
  }

  leaderSavingId.value = leaderForm.projectId;
  try {
    await api.assignProjectLeader({
      projectId: leaderForm.projectId,
      assignProjectLeaderRequest: {
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

function openReviewDialog(project: ProjectWithTaskAccess) {
  if (!canReviewProject(project)) {
    ElMessage.warning("只有本社团指导老师可以进行立项审核。");
    return;
  }

  reviewForm.projectId = project.id;
  reviewForm.projectStatus = ReviewProjectRequestProjectStatusEnum.Running;
  reviewForm.reviewComment = "";
  reviewDialogVisible.value = true;
}

async function reviewProject() {
  if (!(await validateForm(reviewFormRef.value))) {
    return;
  }

  reviewSavingId.value = reviewForm.projectId;
  try {
    await api.reviewProject({
      projectId: reviewForm.projectId,
      reviewProjectRequest: {
        projectStatus: reviewForm.projectStatus,
        reviewComment: normalizeOptionalText(reviewForm.reviewComment),
      },
    });
    ElMessage.success("立项审核结果已保存");
    reviewDialogVisible.value = false;
    await loadProjects();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "立项审核失败"));
  } finally {
    reviewSavingId.value = null;
  }
}

async function openTaskDialog(project: ProjectWithTaskAccess) {
  selectedProject.value = project;
  projectTasks.value = [];
  taskDialogVisible.value = true;
  await loadProjectTasks();
}

async function loadProjectTasks() {
  if (!selectedProject.value) return;

  taskLoading.value = true;
  try {
    const tasks = await projectApiRequest<ProjectTaskApi[]>(
      `/api/projects/${selectedProject.value.id}/tasks`,
    );
    projectTasks.value = tasks.map(mapProjectTask);
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目任务加载失败"));
  } finally {
    taskLoading.value = false;
  }
}

function openDeliverableDialog(task: ProjectTask) {
  if (!selectedProject.value || !canSubmitTaskDeliverable(selectedProject.value, task)) {
    ElMessage.warning("只有任务负责人或项目负责人可以提交任务成果。");
    return;
  }

  activeTask.value = task;
  deliverableForm.deliverableTitle = task.deliverableTitle ?? task.title;
  deliverableForm.deliverableDesc = task.deliverableDesc ?? "";
  deliverableForm.deliverableUrl = task.deliverableUrl ?? "";
  deliverableDialogVisible.value = true;
}

async function submitTaskDeliverable() {
  if (
    !(await validateForm(deliverableFormRef.value)) ||
    !selectedProject.value ||
    !activeTask.value
  ) {
    return;
  }

  taskSavingId.value = activeTask.value.id;
  try {
    const task = await projectApiRequest<ProjectTaskApi>(
      `/api/projects/${selectedProject.value.id}/tasks/${activeTask.value.id}/deliverable`,
      {
        method: "POST",
        body: JSON.stringify({
          deliverableTitle: deliverableForm.deliverableTitle.trim(),
          deliverableDesc: normalizeOptionalText(deliverableForm.deliverableDesc),
          deliverableUrl: normalizeOptionalText(deliverableForm.deliverableUrl),
        }),
      },
    );
    upsertProjectTask(mapProjectTask(task));
    ElMessage.success("任务成果已提交，等待审核");
    deliverableDialogVisible.value = false;
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "任务成果提交失败"));
  } finally {
    taskSavingId.value = null;
  }
}

function openDeliverableReviewDialog(task: ProjectTask) {
  if (!selectedProject.value || !canReviewTaskDeliverable(selectedProject.value, task)) {
    ElMessage.warning("只有指导老师或校级社团管理员可以审核任务成果。");
    return;
  }

  activeTask.value = task;
  deliverableReviewForm.approved = true;
  deliverableReviewForm.reviewComment = "";
  deliverableReviewDialogVisible.value = true;
}

async function reviewTaskDeliverable() {
  if (
    !(await validateForm(deliverableReviewFormRef.value)) ||
    !selectedProject.value ||
    !activeTask.value
  ) {
    return;
  }

  taskSavingId.value = activeTask.value.id;
  try {
    const task = await projectApiRequest<ProjectTaskApi>(
      `/api/projects/${selectedProject.value.id}/tasks/${activeTask.value.id}/deliverable/review`,
      {
        method: "POST",
        body: JSON.stringify({
          approved: deliverableReviewForm.approved,
          reviewComment: normalizeOptionalText(deliverableReviewForm.reviewComment),
        }),
      },
    );
    upsertProjectTask(mapProjectTask(task));
    ElMessage.success("任务成果审核结果已保存");
    deliverableReviewDialogVisible.value = false;
    await loadProjects();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "任务成果审核失败"));
  } finally {
    taskSavingId.value = null;
  }
}

function normalizeProjectStatus(status: Project["projectStatus"] | number | string) {
  const statusText = String(status);
  return numericStatusMap[statusText] ?? statusText;
}

function isPendingProject(project: ProjectWithTaskAccess) {
  return normalizeProjectStatus(project.projectStatus) === ProjectProjectStatusEnum.Pending;
}

function canCreateProjectForClub(clubId: number) {
  if (hasSystemAdminRole() || hasPlatformAdminRole()) return false;

  return hasScopedRole(clubId, principalRoleCodes) || canAdvisorActForClub(clubId);
}

function canAssignProjectLeader(project: ProjectWithTaskAccess) {
  if (hasSystemAdminRole() || hasPlatformAdminRole()) return false;
  if (normalizeProjectStatus(project.projectStatus) === ProjectProjectStatusEnum.Closed)
    return false;

  return (
    hasClubPermission(projectTaskManagePermission, project.clubId) ||
    hasScopedRole(project.clubId, principalRoleCodes) ||
    hasScopedRole(project.clubId, officerRoleCodes)
  );
}

function canReviewProject(project: ProjectWithTaskAccess) {
  return isPendingProject(project) && canAdvisorActForClub(project.clubId);
}

function canCancelProject(project: ProjectWithTaskAccess) {
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

function isActiveExecutionProject(project: ProjectWithTaskAccess) {
  const status = normalizeProjectStatus(project.projectStatus);
  return status === ProjectProjectStatusEnum.Running || status === ProjectProjectStatusEnum.Delayed;
}

function canViewProjectTasks(project: ProjectWithTaskAccess) {
  const status = normalizeProjectStatus(project.projectStatus);
  const statusAllowsWorkspace =
    status === ProjectProjectStatusEnum.Running ||
    status === ProjectProjectStatusEnum.Delayed ||
    status === ProjectProjectStatusEnum.Finished;
  if (!statusAllowsWorkspace) return false;

  return project.canViewTasks === true;
}

function canSubmitTaskDeliverable(project: ProjectWithTaskAccess, task: ProjectTask) {
  if (!currentUserId.value || !isActiveExecutionProject(project)) return false;
  if (["pending", "approved"].includes(normalizeDeliverableStatus(task.deliverableStatus))) {
    return false;
  }

  return (
    task.assignees.some((assignee) => assignee.userId === currentUserId.value) ||
    task.assigneeUserId === currentUserId.value ||
    project.leaderUserId === currentUserId.value
  );
}

function canReviewTaskDeliverable(project: ProjectWithTaskAccess, task: ProjectTask) {
  const status = normalizeProjectStatus(project.projectStatus);
  if (status !== ProjectProjectStatusEnum.Running && status !== ProjectProjectStatusEnum.Delayed) {
    return false;
  }

  return (
    normalizeDeliverableStatus(task.deliverableStatus) === "pending" &&
    (canAdvisorActForClub(project.clubId) || hasPlatformAdminRole())
  );
}

function hasProjectActions(project: ProjectWithTaskAccess) {
  return (
    canViewProjectTasks(project) ||
    canAssignProjectLeader(project) ||
    canReviewProject(project) ||
    canCancelProject(project)
  );
}

async function cancelProject(project: ProjectWithTaskAccess) {
  if (!canCancelProject(project)) {
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
    hasScopedRole(clubId, advisorRoleCodes) || hasClubPermission(projectReviewPermission, clubId)
  );
}

function hasSystemAdminRole() {
  return currentRoles.value.some((role) =>
    ["system_admin", "sysadmin"].includes(normalizeRoleText(role.code)),
  );
}

function hasPlatformAdminRole() {
  return currentRoles.value.some((role) => isSystemScopedPlatformAdminRole(role));
}

function isSystemScopedPlatformAdminRole(role: AuthRole) {
  return (
    isSystemScope(role.scope) &&
    role.clubId == null &&
    (role.clubIds ?? []).length === 0 &&
    ["platform_admin", "club_admin", "admin", "club_reviewer"].includes(
      normalizeRoleText(role.code),
    )
  );
}

function isSystemScope(scope?: string | null) {
  const normalizedScope = normalizeRoleText(scope);
  return normalizedScope === "system" || normalizedScope === "平台";
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

function taskUserLabel(task: ProjectTask) {
  if (task.assignees.length > 0) {
    return task.assignees.map((assignee) => assignee.displayName).join("、");
  }

  return task.assigneeDisplayName || "未分配";
}

function normalizeTaskStatus(status?: string | null) {
  return (status ?? "in_progress").trim().toLowerCase();
}

function normalizeDeliverableStatus(status?: string | null) {
  return (status ?? "not_submitted").trim().toLowerCase();
}

function formatDate(value?: Date | null) {
  if (!value) return "未填写";
  return value.toLocaleDateString("zh-CN");
}

function formatDateTime(value?: Date | null) {
  if (!value) return "未填写";
  return value.toLocaleString("zh-CN");
}

function normalizeOptionalText(value: string) {
  const text = value.trim();
  return text.length > 0 ? text : undefined;
}

function mapProjectTask(task: ProjectTaskApi): ProjectTask {
  return {
    ...task,
    assignees: task.assignees ?? [],
    startDate: parseOptionalDate(task.startDate),
    dueDate: parseOptionalDate(task.dueDate),
    finishDate: parseOptionalDate(task.finishDate),
    taskStatus: normalizeTaskStatus(task.taskStatus),
    deliverableStatus: normalizeDeliverableStatus(task.deliverableStatus),
    deliverableSubmittedAt: parseOptionalDate(task.deliverableSubmittedAt),
    deliverableReviewedAt: parseOptionalDate(task.deliverableReviewedAt),
  };
}

function parseOptionalDate(value?: string | null) {
  return value ? new Date(value) : undefined;
}

function upsertProjectTask(task: ProjectTask) {
  const index = projectTasks.value.findIndex((item) => item.id === task.id);
  if (index >= 0) {
    projectTasks.value.splice(index, 1, task);
    if (activeTask.value?.id === task.id) {
      activeTask.value = task;
    }
    return;
  }

  projectTasks.value.push(task);
}

async function projectApiRequest<T>(path: string, init: RequestInit = {}) {
  const token = readAuth()?.token;
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`${apiBasePath}${path}`, {
    ...init,
    headers,
  });
  if (!response.ok) {
    const message = await parseErrorMessage(response);
    throw new Error(message || `请求失败（${response.status}）`);
  }

  return (await response.json()) as T;
}

async function parseErrorMessage(response: Response) {
  const text = await response.text();
  if (!text) return "";

  try {
    const body = JSON.parse(text) as { message?: string };
    return body.message?.trim() || text;
  } catch {
    return text;
  }
}

function toErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message) {
    if (/response returned an error code/i.test(error.message)) return fallback;
    return `${fallback}：${error.message}`;
  }
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
      <el-table-column label="操作" width="340" fixed="right">
        <template #default="{ row }">
          <div v-if="hasProjectActions(row)" class="project-action-groups">
            <div class="project-action-group">
              <span class="action-group-label">项目入口</span>
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
            </div>
            <div
              v-if="canAssignProjectLeader(row) || canReviewProject(row) || canCancelProject(row)"
              class="project-action-group"
            >
              <span class="action-group-label">项目流程</span>
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
                立项审核
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
            </div>
            <div v-if="canViewProjectTasks(row)" class="project-action-group">
              <span class="action-group-label">任务成果</span>
              <el-button size="small" text type="primary" @click="openTaskDialog(row)">
                查看任务成果
              </el-button>
            </div>
          </div>
          <span v-else class="action-muted">无可用操作</span>
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
            maxlength="4000"
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

    <el-dialog v-model="reviewDialogVisible" title="立项审核" width="460px">
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-position="top">
        <el-form-item label="立项审核结果" prop="projectStatus">
          <el-radio-group v-model="reviewForm.projectStatus">
            <el-radio-button :label="ReviewProjectRequestProjectStatusEnum.Running">
              立项通过
            </el-radio-button>
            <el-radio-button :label="ReviewProjectRequestProjectStatusEnum.Closed">
              驳回立项
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="立项审核意见">
          <el-input
            v-model="reviewForm.reviewComment"
            type="textarea"
            :rows="3"
            maxlength="300"
            show-word-limit
            placeholder="可填写立项审批说明"
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
          保存立项审核结果
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="taskDialogVisible"
      :title="selectedProject ? `任务成果审核 - ${selectedProject.projectName}` : '任务成果审核'"
      width="920px"
    >
      <el-table v-loading="taskLoading" :data="projectTasks" stripe empty-text="暂无项目任务">
        <el-table-column prop="title" label="任务" min-width="150" show-overflow-tooltip />
        <el-table-column label="负责人" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">
            {{ taskUserLabel(row) }}
          </template>
        </el-table-column>
        <el-table-column label="任务状态" width="100">
          <template #default="{ row }">
            <el-tag
              :type="taskStatusType[normalizeTaskStatus(row.taskStatus)] || 'info'"
              size="small"
            >
              {{
                taskStatusLabel[normalizeTaskStatus(row.taskStatus)] ||
                normalizeTaskStatus(row.taskStatus)
              }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="成果状态" width="100">
          <template #default="{ row }">
            <el-tag
              :type="
                deliverableStatusType[normalizeDeliverableStatus(row.deliverableStatus)] || 'info'
              "
              size="small"
            >
              {{
                deliverableStatusLabel[normalizeDeliverableStatus(row.deliverableStatus)] ||
                normalizeDeliverableStatus(row.deliverableStatus)
              }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="截止时间" min-width="120">
          <template #default="{ row }">
            {{ formatDate(row.dueDate) }}
          </template>
        </el-table-column>
        <el-table-column label="成果" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <a
              v-if="row.deliverableUrl"
              :href="row.deliverableUrl"
              target="_blank"
              rel="noreferrer"
            >
              {{ row.deliverableTitle || "查看成果" }}
            </a>
            <span v-else>{{ row.deliverableTitle || "未提交" }}</span>
          </template>
        </el-table-column>
        <el-table-column label="审核意见" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.reviewComment || "暂无" }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="selectedProject && canSubmitTaskDeliverable(selectedProject, row)"
              size="small"
              text
              type="primary"
              :loading="taskSavingId === row.id"
              @click="openDeliverableDialog(row)"
            >
              提交成果
            </el-button>
            <el-button
              v-if="selectedProject && canReviewTaskDeliverable(selectedProject, row)"
              size="small"
              text
              type="success"
              :loading="taskSavingId === row.id"
              @click="openDeliverableReviewDialog(row)"
            >
              审核成果
            </el-button>
            <span
              v-if="
                !selectedProject ||
                (!canSubmitTaskDeliverable(selectedProject, row) &&
                  !canReviewTaskDeliverable(selectedProject, row))
              "
              class="action-muted"
            >
              无可用操作
            </span>
          </template>
        </el-table-column>
      </el-table>

      <template #footer>
        <el-button @click="taskDialogVisible = false">关闭</el-button>
        <el-button :loading="taskLoading" @click="loadProjectTasks">刷新任务</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="deliverableDialogVisible" title="提交任务成果" width="520px">
      <el-form
        ref="deliverableFormRef"
        :model="deliverableForm"
        :rules="deliverableRules"
        label-position="top"
      >
        <el-form-item label="成果标题" prop="deliverableTitle">
          <el-input v-model="deliverableForm.deliverableTitle" maxlength="100" show-word-limit />
        </el-form-item>
        <el-form-item label="成果说明">
          <el-input
            v-model="deliverableForm.deliverableDesc"
            type="textarea"
            :rows="3"
            maxlength="4000"
            show-word-limit
            placeholder="可填写成果摘要、完成情况或交付物说明"
          />
        </el-form-item>
        <el-form-item label="成果链接" prop="deliverableUrl">
          <el-input
            v-model="deliverableForm.deliverableUrl"
            maxlength="255"
            show-word-limit
            placeholder="粘贴文档、网盘或仓库链接"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="deliverableDialogVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="taskSavingId === activeTask?.id"
          @click="submitTaskDeliverable"
        >
          提交成果
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="deliverableReviewDialogVisible" title="任务成果审核" width="520px">
      <div v-if="activeTask" class="task-summary">
        <div class="task-summary-title">{{ activeTask.deliverableTitle || activeTask.title }}</div>
        <div class="task-summary-meta">
          提交人：{{ activeTask.deliverableSubmitterDisplayName || "未记录" }} · 提交时间：{{
            formatDateTime(activeTask.deliverableSubmittedAt)
          }}
        </div>
        <p v-if="activeTask.deliverableDesc">{{ activeTask.deliverableDesc }}</p>
        <a
          v-if="activeTask.deliverableUrl"
          :href="activeTask.deliverableUrl"
          target="_blank"
          rel="noreferrer"
        >
          打开成果链接
        </a>
      </div>
      <el-form
        ref="deliverableReviewFormRef"
        :model="deliverableReviewForm"
        :rules="deliverableReviewRules"
        label-position="top"
      >
        <el-form-item label="成果审核结果" prop="approved">
          <el-radio-group v-model="deliverableReviewForm.approved">
            <el-radio-button :label="true">通过</el-radio-button>
            <el-radio-button :label="false">驳回修改</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="成果审核意见" prop="reviewComment">
          <el-input
            v-model="deliverableReviewForm.reviewComment"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit
            placeholder="可填写验收意见或修改建议"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="deliverableReviewDialogVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="taskSavingId === activeTask?.id"
          @click="reviewTaskDeliverable"
        >
          保存成果审核结果
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

.project-action-groups {
  display: grid;
  gap: 4px;
}

.project-action-group {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 2px;
}

.action-group-label {
  min-width: 56px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.task-summary {
  margin-bottom: 16px;
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  background: var(--el-fill-color-lighter);
}

.task-summary-title {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.task-summary-meta {
  margin-top: 4px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
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

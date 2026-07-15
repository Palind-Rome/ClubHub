<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import {
  CreateProjectTaskRequestPriorityEnum,
  ProjectMemberMemberStatusEnum,
  ProjectProjectStatusEnum,
  ProjectTaskTaskStatusEnum,
  ResponseError,
  UpdateProjectTaskProgressRequestTaskStatusEnum,
  type ProjectMember,
  type ProjectTask,
  type ProjectTaskProgressReport,
  type UpdateProjectTaskProgressRequestTaskStatusEnum as UpdateTaskStatus,
} from "../api";
import { apiClient as api } from "../apiClient";
import { readAuth } from "../authSession";

const props = defineProps<{
  projectId: number;
  clubId: number;
  leaderUserId?: number | null;
  projectStatus?: ProjectProjectStatusEnum | null;
}>();

type ProjectTaskWithDeliverable = ProjectTask & {
  deliverableTitle?: string | null;
  deliverableDesc?: string | null;
  deliverableUrl?: string | null;
  deliverableStatus?: string | null;
  reviewerDisplayName?: string | null;
  reviewComment?: string | null;
  deliverableSubmitterDisplayName?: string | null;
  deliverableSubmittedAt?: Date | null;
  deliverableReviewedAt?: Date | null;
};

const apiBasePath = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");
const platformAdminRoleCodes = new Set(["platform_admin", "club_admin", "admin", "club_reviewer"]);
const tasks = ref<ProjectTaskWithDeliverable[]>([]);
const members = ref<ProjectMember[]>([]);
const loading = ref(false);
const showingCompleted = ref(false);
const saving = ref(false);
const loadError = ref("");
const createVisible = ref(false);
const updateVisible = ref(false);
const deliverableVisible = ref(false);
const reviewVisible = ref(false);
const reportsVisible = ref(false);
const reportsLoading = ref(false);
const reportsError = ref("");
const progressReports = ref<ProjectTaskProgressReport[]>([]);
const createFormRef = ref<FormInstance>();
const updateFormRef = ref<FormInstance>();
const deliverableFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();
const selectedTask = ref<ProjectTaskWithDeliverable | null>(null);
let requestVersion = 0;
let reportsRequestVersion = 0;

const auth = computed(() => readAuth());
const currentUserId = computed(() => auth.value?.user.id ?? null);
const isLeader = computed(
  () => currentUserId.value !== null && currentUserId.value === props.leaderUserId,
);
const canCreate = computed(
  () => isLeader.value && props.projectStatus === ProjectProjectStatusEnum.Running,
);
const canReviewDeliverables = computed(() => {
  return (auth.value?.roles ?? []).some((role) => {
    const rolePermissions = role.permissions ?? [];
    if (!rolePermissions.includes("*") && !rolePermissions.includes("project:review")) return false;

    const code = normalizeRoleText(role.code);
    const scope = normalizeRoleText(role.scope);
    if (
      scope === "system" &&
      role.clubId == null &&
      (role.clubIds ?? []).length === 0 &&
      platformAdminRoleCodes.has(code)
    ) {
      return true;
    }

    return role.clubId === props.clubId || Boolean(role.clubIds?.includes(props.clubId));
  });
});
const activeMembers = computed(() =>
  members.value.filter((member) => member.memberStatus === ProjectMemberMemberStatusEnum.Active),
);

const createForm = reactive({
  assigneeUserIds: [] as number[],
  title: "",
  content: "",
  priority: CreateProjectTaskRequestPriorityEnum.Medium,
  dueDate: "",
});
const updateForm = reactive<{
  progress: number;
  taskStatus: UpdateTaskStatus;
  delayReason: string;
  reportContent: string;
}>({
  progress: 0,
  taskStatus: UpdateProjectTaskProgressRequestTaskStatusEnum.Pending,
  delayReason: "",
  reportContent: "",
});
const deliverableForm = reactive({
  deliverableTitle: "",
  deliverableDesc: "",
  deliverableUrl: "",
});
const reviewForm = reactive({
  approved: true,
  reviewComment: "",
});
const createRules: FormRules = {
  assigneeUserIds: [{ required: true, message: "请至少选择一名任务执行人", trigger: "change" }],
  title: [{ required: true, message: "请输入任务标题", trigger: "blur" }],
  dueDate: [
    { required: true, message: "请选择截止时间", trigger: "change" },
    {
      validator: (_rule, value: string, callback) => {
        if (value && new Date(value) <= new Date()) {
          callback(new Error("截止时间必须晚于当前时间"));
          return;
        }
        callback();
      },
      trigger: "change",
    },
  ],
};
const deliverableRules: FormRules = {
  deliverableTitle: [
    { required: true, message: "请输入成果标题", trigger: "blur" },
    { max: 100, message: "成果标题不能超过 100 个字符", trigger: "blur" },
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
const reviewRules: FormRules = {
  reviewComment: [
    { max: 255, message: "审核意见不能超过 255 个字符", trigger: "blur" },
    {
      validator: (_rule, value: string, callback) => {
        if (reviewForm.approved === false && (!value || value.trim().length === 0)) {
          callback(new Error("驳回成果时必须填写审核意见"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
  ],
};

function normalizeRoleText(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
}

const priorityLabel: Record<string, string> = {
  low: "低",
  medium: "中",
  high: "高",
  urgent: "紧急",
};
const statusLabel: Record<string, string> = {
  pending: "待开始",
  in_progress: "进行中",
  completed: "已完成",
  delayed: "已延期",
};
const statusType: Record<string, "info" | "primary" | "success" | "danger"> = {
  pending: "info",
  in_progress: "primary",
  completed: "success",
  delayed: "danger",
};
const deliverableStatusLabel: Record<string, string> = {
  not_submitted: "未提交",
  pending: "待成果审核",
  approved: "成果通过",
  rejected: "成果驳回",
};
const deliverableStatusType: Record<string, "info" | "warning" | "success" | "danger"> = {
  not_submitted: "info",
  pending: "warning",
  approved: "success",
  rejected: "danger",
};

async function loadTasks() {
  const version = ++requestVersion;
  loading.value = true;
  loadError.value = "";
  try {
    const [nextTasks, nextMembers] = await Promise.all([
      api.getProjectTasks({ projectId: props.projectId, completedOnly: showingCompleted.value }),
      isLeader.value
        ? api.getProjectMembers({ projectId: props.projectId, includeInactive: false })
        : Promise.resolve([]),
    ]);
    if (version !== requestVersion) return;
    tasks.value = nextTasks as ProjectTaskWithDeliverable[];
    members.value = nextMembers;
  } catch (error) {
    if (version !== requestVersion) return;
    tasks.value = [];
    loadError.value = await toErrorMessage(error, "项目任务加载失败");
  } finally {
    if (version === requestVersion) loading.value = false;
  }
}

async function openCreate() {
  if (!canCreate.value) {
    ElMessage.warning(
      props.projectStatus === ProjectProjectStatusEnum.Running
        ? "当前账号不是项目负责人。"
        : "只有执行中的项目可以创建任务。",
    );
    return;
  }
  createForm.assigneeUserIds = [];
  createForm.title = "";
  createForm.content = "";
  createForm.priority = CreateProjectTaskRequestPriorityEnum.Medium;
  createForm.dueDate = "";
  createVisible.value = true;
  await loadTasks();
}

async function toggleCompletedTasks() {
  showingCompleted.value = !showingCompleted.value;
  await loadTasks();
}

async function createTask() {
  const valid = await createFormRef.value?.validate().catch(() => false);
  if (!valid || createForm.assigneeUserIds.length === 0 || !createForm.dueDate) return;
  saving.value = true;
  try {
    await api.createProjectTask({
      projectId: props.projectId,
      createProjectTaskRequest: {
        assigneeUserIds: new Set(createForm.assigneeUserIds),
        title: createForm.title.trim(),
        content: optional(createForm.content),
        priority: createForm.priority,
        dueDate: new Date(createForm.dueDate),
      },
    });
    ElMessage.success("项目任务已创建");
    createVisible.value = false;
    await loadTasks();
  } catch (error) {
    ElMessage.error(await toErrorMessage(error, "项目任务创建失败"));
  } finally {
    saving.value = false;
  }
}

function openUpdate(task: ProjectTaskWithDeliverable) {
  selectedTask.value = task;
  updateForm.progress = task.progress;
  updateForm.taskStatus = task.taskStatus as UpdateProjectTaskProgressRequestTaskStatusEnum;
  updateForm.delayReason = task.delayReason ?? "";
  updateForm.reportContent = "";
  updateVisible.value = true;
}

async function openReports(task: ProjectTaskWithDeliverable) {
  const version = ++reportsRequestVersion;
  selectedTask.value = task;
  reportsVisible.value = true;
  reportsLoading.value = true;
  reportsError.value = "";
  progressReports.value = [];
  try {
    const reports = await api.getProjectTaskProgressReports({
      projectId: props.projectId,
      taskId: task.id,
    });
    if (version !== reportsRequestVersion) return;
    progressReports.value = reports;
  } catch (error) {
    if (version !== reportsRequestVersion) return;
    reportsError.value = await toErrorMessage(error, "进度记录加载失败");
  } finally {
    if (version === reportsRequestVersion) reportsLoading.value = false;
  }
}

async function updateTask() {
  const task = selectedTask.value;
  if (!task) return;
  const valid = await updateFormRef.value?.validate().catch(() => false);
  if (!valid) return;
  const completed =
    updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Completed;
  const delayed = updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Delayed;
  if (delayed && !optional(updateForm.delayReason)) {
    ElMessage.warning("已延期任务必须填写延期原因。");
    return;
  }
  if (
    updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.InProgress &&
    !optional(updateForm.reportContent)
  ) {
    ElMessage.warning("进行中任务请填写本次进度汇报。");
    return;
  }
  saving.value = true;
  try {
    await api.updateProjectTaskProgress({
      projectId: props.projectId,
      taskId: task.id,
      updateProjectTaskProgressRequest: {
        progress: updateForm.progress,
        taskStatus: updateForm.taskStatus,
        delayReason: completed ? undefined : optional(updateForm.delayReason),
        reportContent: optional(updateForm.reportContent),
      },
    });
    ElMessage.success("任务进度已更新");
    updateVisible.value = false;
    await loadTasks();
  } catch (error) {
    ElMessage.error(await toErrorMessage(error, "任务进度更新失败"));
  } finally {
    saving.value = false;
  }
}

function openDeliverable(task: ProjectTaskWithDeliverable) {
  if (!canSubmitDeliverable(task)) {
    ElMessage.warning("只有项目负责人或任务执行人可以提交待审核成果。");
    return;
  }
  selectedTask.value = task;
  deliverableForm.deliverableTitle = task.deliverableTitle ?? task.title;
  deliverableForm.deliverableDesc = task.deliverableDesc ?? "";
  deliverableForm.deliverableUrl = task.deliverableUrl ?? "";
  deliverableVisible.value = true;
}

async function submitDeliverable() {
  const task = selectedTask.value;
  if (!task) return;
  const valid = await deliverableFormRef.value?.validate().catch(() => false);
  if (!valid) return;

  saving.value = true;
  try {
    await projectTaskRequest<ProjectTaskWithDeliverable>(
      `/api/projects/${props.projectId}/tasks/${task.id}/deliverable`,
      {
        method: "POST",
        body: JSON.stringify({
          deliverableTitle: deliverableForm.deliverableTitle.trim(),
          deliverableDesc: optional(deliverableForm.deliverableDesc),
          deliverableUrl: optional(deliverableForm.deliverableUrl),
        }),
      },
    );
    ElMessage.success("任务成果已提交，等待审核");
    deliverableVisible.value = false;
    await loadTasks();
  } catch (error) {
    ElMessage.error(await toErrorMessage(error, "任务成果提交失败"));
  } finally {
    saving.value = false;
  }
}

function openDeliverableReview(task: ProjectTaskWithDeliverable) {
  if (!canReviewDeliverable(task)) {
    ElMessage.warning("只有本社团指导老师或校级社团管理员可以审核待审核成果。");
    return;
  }
  selectedTask.value = task;
  reviewForm.approved = true;
  reviewForm.reviewComment = "";
  reviewVisible.value = true;
}

async function reviewDeliverable() {
  const task = selectedTask.value;
  if (!task) return;
  const valid = await reviewFormRef.value?.validate().catch(() => false);
  if (!valid) return;

  saving.value = true;
  try {
    await projectTaskRequest<ProjectTaskWithDeliverable>(
      `/api/projects/${props.projectId}/tasks/${task.id}/deliverable/review`,
      {
        method: "POST",
        body: JSON.stringify({
          approved: reviewForm.approved,
          reviewComment: optional(reviewForm.reviewComment),
        }),
      },
    );
    ElMessage.success("任务成果审核结果已保存");
    reviewVisible.value = false;
    await loadTasks();
  } catch (error) {
    ElMessage.error(await toErrorMessage(error, "任务成果审核失败"));
  } finally {
    saving.value = false;
  }
}

async function deleteTask(task: ProjectTaskWithDeliverable) {
  try {
    await ElMessageBox.confirm(
      `确定删除“${task.title}”吗？关联的进度记录也会一并删除，且无法恢复。`,
      "删除项目任务",
      { confirmButtonText: "删除", cancelButtonText: "取消", type: "warning" },
    );
    saving.value = true;
    await api.deleteProjectTask({ projectId: props.projectId, taskId: task.id });
    ElMessage.success("项目任务已删除");
    await loadTasks();
  } catch (error) {
    if (error !== "cancel" && error !== "close") {
      ElMessage.error(await toErrorMessage(error, "项目任务删除失败"));
    }
  } finally {
    saving.value = false;
  }
}

function canUpdate(task: ProjectTaskWithDeliverable) {
  return task.assignees.some((assignee) => assignee.userId === currentUserId.value);
}
function canSubmitDeliverable(task: ProjectTaskWithDeliverable) {
  if (
    !isActiveExecutionProject() ||
    ["pending", "approved"].includes(taskDeliverableStatus(task))
  ) {
    return false;
  }
  return isLeader.value || canUpdate(task);
}
function canReviewDeliverable(task: ProjectTaskWithDeliverable) {
  return (
    isActiveExecutionProject() &&
    taskDeliverableStatus(task) === "pending" &&
    canReviewDeliverables.value
  );
}
function isActiveExecutionProject() {
  return (
    props.projectStatus === ProjectProjectStatusEnum.Running ||
    props.projectStatus === ProjectProjectStatusEnum.Delayed
  );
}
function taskDeliverableStatus(task: ProjectTaskWithDeliverable) {
  return (task.deliverableStatus ?? "not_submitted").trim().toLowerCase();
}
function assigneeNames(task: ProjectTaskWithDeliverable) {
  return task.assignees.map((assignee) => assignee.displayName).join("、");
}
function memberText(member: ProjectMember) {
  return member.studentNo
    ? `${member.realName || `用户 #${member.userId}`}（${member.studentNo}）`
    : member.realName || `用户 #${member.userId}`;
}
function formatDate(value?: Date | null) {
  return value
    ? new Intl.DateTimeFormat("zh-CN", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
      }).format(value)
    : "—";
}
function optional(value: string) {
  const text = value.trim();
  return text || undefined;
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
  if (error instanceof Error && error.message) return `${fallback}：${error.message}`;
  return fallback;
}

async function projectTaskRequest<T>(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  const token = readAuth()?.token;
  headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`${apiBasePath}${path}`, { ...init, headers });
  if (!response.ok) {
    throw new Error(await readApiError(response));
  }

  return (await response.json()) as T;
}

async function readApiError(response: Response) {
  try {
    const body = (await response.clone().json()) as { message?: string };
    return body.message || `HTTP ${response.status}`;
  } catch {
    return (await response.text()) || `HTTP ${response.status}`;
  }
}

watch(
  () => props.projectId,
  () => void loadTasks(),
);
watch(isLeader, () => void loadTasks());
onMounted(() => void loadTasks());
</script>

<template>
  <section class="tasks-panel" aria-labelledby="project-tasks-heading">
    <div class="panel-toolbar">
      <div>
        <h2 id="project-tasks-heading">项目任务</h2>
        <p>
          {{
            showingCompleted
              ? "已完成任务及其历史进度记录。"
              : "负责人可分配任务；成员只能查看并更新分配给自己的任务。"
          }}
        </p>
      </div>
      <div class="panel-actions">
        <el-button :loading="loading" @click="loadTasks">刷新</el-button
        ><el-button @click="toggleCompletedTasks">{{
          showingCompleted ? "返回进行中任务" : "查看已完成任务"
        }}</el-button
        ><el-button type="primary" :disabled="!canCreate" @click="openCreate">创建任务</el-button>
      </div>
    </div>
    <el-alert v-if="loadError" type="error" :title="loadError" show-icon :closable="false" />
    <div class="table-wrap">
      <el-table
        v-loading="loading"
        :data="tasks"
        row-key="id"
        :empty-text="showingCompleted ? '暂无已完成项目任务' : '暂无可查看的项目任务'"
        ><el-table-column prop="title" label="任务" min-width="190"
          ><template #default="{ row }"
            ><strong>{{ row.title }}</strong
            ><span v-if="row.content" class="task-content">{{ row.content }}</span
            ><span
              v-if="row.taskStatus === ProjectTaskTaskStatusEnum.Delayed && row.delayReason"
              class="task-delay-reason"
              >延期原因：{{ row.delayReason }}</span
            ></template
          ></el-table-column
        ><el-table-column label="执行人" min-width="150"
          ><template #default="{ row }">{{ assigneeNames(row) }}</template></el-table-column
        ><el-table-column label="优先级" width="90"
          ><template #default="{ row }"
            ><el-tag effect="plain">{{
              priorityLabel[row.priority] || row.priority
            }}</el-tag></template
          ></el-table-column
        ><el-table-column label="进度" min-width="150"
          ><template #default="{ row }"
            ><el-progress
              :percentage="row.progress"
              :status="
                row.taskStatus === ProjectTaskTaskStatusEnum.Completed ? 'success' : undefined
              " /></template></el-table-column
        ><el-table-column label="状态" width="100"
          ><template #default="{ row }"
            ><el-tag :type="statusType[row.taskStatus]" effect="plain">{{
              statusLabel[row.taskStatus] || row.taskStatus
            }}</el-tag></template
          ></el-table-column
        ><el-table-column label="成果" min-width="170"
          ><template #default="{ row }"
            ><div class="deliverable-cell">
              <el-tag :type="deliverableStatusType[taskDeliverableStatus(row)]" effect="plain">{{
                deliverableStatusLabel[taskDeliverableStatus(row)] || taskDeliverableStatus(row)
              }}</el-tag>
              <a
                v-if="row.deliverableUrl"
                class="deliverable-link"
                :href="row.deliverableUrl"
                target="_blank"
                rel="noopener noreferrer"
                >{{ row.deliverableTitle || "查看成果" }}</a
              ><span v-else class="deliverable-title">{{
                row.deliverableTitle || "暂未提交成果"
              }}</span>
            </div></template
          ></el-table-column
        ><el-table-column label="截止时间" min-width="145"
          ><template #default="{ row }">{{ formatDate(row.dueDate) }}</template></el-table-column
        ><el-table-column label="操作" width="150" fixed="right"
          ><template #default="{ row }"
            ><div class="task-actions">
              <el-button
                v-if="canUpdate(row) && !showingCompleted"
                link
                type="primary"
                @click="openUpdate(row)"
                >更新进度</el-button
              ><el-button
                v-if="canSubmitDeliverable(row)"
                link
                type="success"
                @click="openDeliverable(row)"
                >提交成果</el-button
              ><el-button
                v-if="canReviewDeliverable(row)"
                link
                type="warning"
                @click="openDeliverableReview(row)"
                >审核成果</el-button
              ><el-button link @click="openReports(row)">进度记录</el-button
              ><el-button
                v-if="isLeader"
                link
                type="danger"
                :loading="saving"
                @click="deleteTask(row)"
                >删除任务</el-button
              >
            </div></template
          ></el-table-column
        ></el-table
      >
    </div>

    <el-dialog v-model="createVisible" title="创建项目任务" width="min(620px, 92vw)"
      ><el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="92px"
        ><el-form-item label="执行人" prop="assigneeUserIds"
          ><el-select
            v-model="createForm.assigneeUserIds"
            placeholder="选择正在参与项目的成员（可多选）"
            filterable
            multiple
            ><el-option
              v-for="member in activeMembers"
              :key="member.userId"
              :label="memberText(member)"
              :value="member.userId" /></el-select></el-form-item
        ><el-form-item label="任务标题" prop="title"
          ><el-input v-model="createForm.title" maxlength="120" show-word-limit /></el-form-item
        ><el-form-item label="任务说明"
          ><el-input
            v-model="createForm.content"
            type="textarea"
            :rows="3"
            maxlength="4000"
            show-word-limit /></el-form-item
        ><el-form-item label="优先级"
          ><el-select v-model="createForm.priority"
            ><el-option label="低" :value="CreateProjectTaskRequestPriorityEnum.Low" /><el-option
              label="中"
              :value="CreateProjectTaskRequestPriorityEnum.Medium" /><el-option
              label="高"
              :value="CreateProjectTaskRequestPriorityEnum.High" /><el-option
              label="紧急"
              :value="CreateProjectTaskRequestPriorityEnum.Urgent" /></el-select></el-form-item
        ><el-form-item label="截止时间" prop="dueDate"
          ><el-date-picker
            v-model="createForm.dueDate"
            type="datetime"
            format="YYYY-MM-DD HH:mm"
            value-format="YYYY-MM-DDTHH:mm"
            :show-seconds="false" /></el-form-item></el-form
      ><template #footer
        ><el-button @click="createVisible = false">取消</el-button
        ><el-button type="primary" :loading="saving" @click="createTask">创建</el-button></template
      ></el-dialog
    >
    <el-dialog v-model="updateVisible" title="更新任务进度" width="min(560px, 92vw)"
      ><el-form ref="updateFormRef" :model="updateForm" label-width="96px"
        ><el-form-item label="任务"
          ><span>{{ selectedTask?.title }}</span></el-form-item
        ><el-form-item label="完成进度"
          ><el-input-number v-model="updateForm.progress" :min="0" :max="100" /></el-form-item
        ><el-form-item label="任务状态"
          ><el-select v-model="updateForm.taskStatus"
            ><el-option
              label="待开始"
              :value="UpdateProjectTaskProgressRequestTaskStatusEnum.Pending" /><el-option
              label="进行中"
              :value="UpdateProjectTaskProgressRequestTaskStatusEnum.InProgress" /><el-option
              label="已完成"
              :value="UpdateProjectTaskProgressRequestTaskStatusEnum.Completed" /><el-option
              label="已延期"
              :value="
                UpdateProjectTaskProgressRequestTaskStatusEnum.Delayed
              " /></el-select></el-form-item
        ><el-alert
          v-if="updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Completed"
          title="完成时间将以本次提交的服务器时间自动记录。"
          type="success"
          :closable="false"
          show-icon />
        <el-form-item
          v-if="updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.InProgress"
          label="进度汇报"
          ><el-input
            v-model="updateForm.reportContent"
            type="textarea"
            :rows="3"
            maxlength="1000"
            placeholder="说明本次已完成的工作和下一步计划"
            show-word-limit /></el-form-item
        ><el-form-item
          v-if="updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Delayed"
          label="延期原因"
          ><el-input
            v-model="updateForm.delayReason"
            type="textarea"
            maxlength="255"
            show-word-limit /></el-form-item></el-form
      ><template #footer
        ><el-button @click="updateVisible = false">取消</el-button
        ><el-button type="primary" :loading="saving" @click="updateTask">保存</el-button></template
      ></el-dialog
    >
    <el-dialog v-model="deliverableVisible" title="提交任务成果" width="min(560px, 92vw)"
      ><el-form
        ref="deliverableFormRef"
        :model="deliverableForm"
        :rules="deliverableRules"
        label-width="96px"
        ><el-form-item label="任务"
          ><span>{{ selectedTask?.title }}</span></el-form-item
        ><el-form-item label="成果标题" prop="deliverableTitle"
          ><el-input
            v-model="deliverableForm.deliverableTitle"
            maxlength="100"
            show-word-limit /></el-form-item
        ><el-form-item label="成果说明" prop="deliverableDesc"
          ><el-input
            v-model="deliverableForm.deliverableDesc"
            type="textarea"
            :rows="3"
            maxlength="4000"
            show-word-limit /></el-form-item
        ><el-form-item label="成果链接" prop="deliverableUrl"
          ><el-input
            v-model="deliverableForm.deliverableUrl"
            maxlength="255"
            placeholder="可填写网盘、文档或仓库链接"
            show-word-limit /></el-form-item></el-form
      ><template #footer
        ><el-button @click="deliverableVisible = false">取消</el-button
        ><el-button type="primary" :loading="saving" @click="submitDeliverable"
          >提交成果</el-button
        ></template
      ></el-dialog
    >
    <el-dialog v-model="reviewVisible" title="审核任务成果" width="min(560px, 92vw)"
      ><div v-if="selectedTask" class="deliverable-review-summary">
        <strong>{{ selectedTask.deliverableTitle || selectedTask.title }}</strong>
        <p>
          提交人：{{ selectedTask.deliverableSubmitterDisplayName || "未记录" }} · 提交时间：{{
            formatDate(selectedTask.deliverableSubmittedAt)
          }}
        </p>
        <p v-if="selectedTask.deliverableDesc">{{ selectedTask.deliverableDesc }}</p>
        <a
          v-if="selectedTask.deliverableUrl"
          :href="selectedTask.deliverableUrl"
          target="_blank"
          rel="noopener noreferrer"
          >打开成果链接</a
        >
      </div>
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="96px"
        ><el-form-item label="审核结果"
          ><el-radio-group v-model="reviewForm.approved"
            ><el-radio-button :label="true">通过</el-radio-button
            ><el-radio-button :label="false">驳回</el-radio-button></el-radio-group
          ></el-form-item
        ><el-form-item label="审核意见" prop="reviewComment"
          ><el-input
            v-model="reviewForm.reviewComment"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit /></el-form-item></el-form
      ><template #footer
        ><el-button @click="reviewVisible = false">取消</el-button
        ><el-button type="primary" :loading="saving" @click="reviewDeliverable"
          >保存审核结果</el-button
        ></template
      ></el-dialog
    >
    <el-drawer
      v-model="reportsVisible"
      :title="`${selectedTask?.title || '任务'} · 进度记录`"
      size="min(480px, 92vw)"
      ><el-alert
        v-if="reportsError"
        type="error"
        :title="reportsError"
        show-icon
        :closable="false"
      />
      <div v-else v-loading="reportsLoading" class="reports-drawer">
        <el-empty
          v-if="!reportsLoading && progressReports.length === 0"
          description="暂未提交进度记录"
        />
        <el-timeline v-else
          ><el-timeline-item
            v-for="report in progressReports"
            :key="report.id"
            :timestamp="formatDate(report.submittedAt)"
            placement="top"
            ><div class="report-entry">
              <div class="report-entry__heading">
                <strong>{{ report.reporterName }}</strong
                ><el-tag :type="statusType[report.taskStatus]" effect="plain">{{
                  statusLabel[report.taskStatus] || report.taskStatus
                }}</el-tag>
              </div>
              <el-progress :percentage="report.progress" :stroke-width="8" />
              <p v-if="report.reportContent">{{ report.reportContent }}</p>
              <p v-if="report.delayReason" class="report-entry__delay">
                延期原因：{{ report.delayReason }}
              </p>
            </div></el-timeline-item
          ></el-timeline
        >
      </div></el-drawer
    >
  </section>
</template>

<style scoped>
.panel-toolbar {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  align-items: flex-start;
  padding: 20px 0;
}
.panel-toolbar h2 {
  margin: 0;
  font-size: 20px;
}
.panel-toolbar p {
  margin: 8px 0 0;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
}
.panel-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
.table-wrap {
  overflow: auto;
}
.task-content {
  display: block;
  margin-top: 4px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.45;
}

.task-delay-reason {
  display: block;
  margin-top: 4px;
  color: var(--el-color-danger);
  font-size: 12px;
  line-height: 1.45;
}
.deliverable-cell {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
}
.deliverable-link,
.deliverable-title {
  color: var(--el-text-color-regular);
  font-size: 12px;
  line-height: 1.45;
  word-break: break-word;
}
.deliverable-link {
  color: var(--el-color-primary);
}
.tasks-panel :deep(.el-progress) {
  min-width: 110px;
}
.task-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
}
.task-actions .el-button {
  margin-left: 0;
}
.deliverable-review-summary {
  margin-bottom: 18px;
  padding: 14px;
  border: 1px solid var(--el-border-color-lighter);
  background: var(--el-fill-color-light);
}
.deliverable-review-summary p {
  margin: 8px 0 0;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
  white-space: pre-wrap;
}
.deliverable-review-summary a {
  display: inline-block;
  margin-top: 8px;
  color: var(--el-color-primary);
}
.reports-drawer {
  min-height: 180px;
  padding: 8px 4px;
}
.report-entry {
  border-top: 1px solid var(--el-border-color-lighter);
  padding-top: 10px;
}
.report-entry__heading {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  margin-bottom: 10px;
}
.report-entry p {
  margin: 10px 0 0;
  color: var(--el-text-color-regular);
  line-height: 1.6;
  white-space: pre-wrap;
}
.report-entry .report-entry__delay {
  color: var(--el-color-danger);
}
@media (max-width: 640px) {
  .panel-toolbar {
    flex-direction: column;
  }
  .panel-actions {
    width: 100%;
  }
  .panel-actions .el-button {
    flex: 1;
  }
}
</style>

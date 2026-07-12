<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import {
  CreateProjectTaskRequestPriorityEnum,
  ProjectMemberMemberStatusEnum,
  ProjectProjectStatusEnum,
  ProjectTaskTaskStatusEnum,
  ResponseError,
  UpdateProjectTaskProgressRequestTaskStatusEnum,
  type ProjectMember,
  type ProjectTask,
  type UpdateProjectTaskProgressRequestTaskStatusEnum as UpdateTaskStatus,
} from "../api";
import { apiClient as api } from "../apiClient";
import { readAuth } from "../authSession";

const props = defineProps<{
  projectId: number;
  leaderUserId?: number | null;
  projectStatus?: ProjectProjectStatusEnum | null;
}>();

const tasks = ref<ProjectTask[]>([]);
const members = ref<ProjectMember[]>([]);
const loading = ref(false);
const saving = ref(false);
const loadError = ref("");
const createVisible = ref(false);
const updateVisible = ref(false);
const createFormRef = ref<FormInstance>();
const updateFormRef = ref<FormInstance>();
const selectedTask = ref<ProjectTask | null>(null);
let requestVersion = 0;

const auth = computed(() => readAuth());
const currentUserId = computed(() => auth.value?.user.id ?? null);
const isLeader = computed(
  () => currentUserId.value !== null && currentUserId.value === props.leaderUserId,
);
const canCreate = computed(
  () => isLeader.value && props.projectStatus === ProjectProjectStatusEnum.Running,
);
const activeMembers = computed(() =>
  members.value.filter((member) => member.memberStatus === ProjectMemberMemberStatusEnum.Active),
);

const createForm = reactive({
  assigneeUserId: undefined as number | undefined,
  title: "",
  content: "",
  priority: CreateProjectTaskRequestPriorityEnum.Medium,
  dueDate: "",
});
const updateForm = reactive<{
  progress: number;
  taskStatus: UpdateTaskStatus;
  finishDate: string;
  delayReason: string;
}>({
  progress: 0,
  taskStatus: UpdateProjectTaskProgressRequestTaskStatusEnum.Pending,
  finishDate: "",
  delayReason: "",
});
const createRules: FormRules = {
  assigneeUserId: [{ required: true, message: "请选择任务执行人", trigger: "change" }],
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

async function loadTasks() {
  const version = ++requestVersion;
  loading.value = true;
  loadError.value = "";
  try {
    const [nextTasks, nextMembers] = await Promise.all([
      api.getProjectTasks({ projectId: props.projectId }),
      isLeader.value
        ? api.getProjectMembers({ projectId: props.projectId, includeInactive: false })
        : Promise.resolve([]),
    ]);
    if (version !== requestVersion) return;
    tasks.value = nextTasks;
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
  createForm.assigneeUserId = undefined;
  createForm.title = "";
  createForm.content = "";
  createForm.priority = CreateProjectTaskRequestPriorityEnum.Medium;
  createForm.dueDate = "";
  createVisible.value = true;
  await loadTasks();
}

async function createTask() {
  const valid = await createFormRef.value?.validate().catch(() => false);
  if (!valid || !createForm.assigneeUserId || !createForm.dueDate) return;
  saving.value = true;
  try {
    await api.createProjectTask({
      projectId: props.projectId,
      createProjectTaskRequest: {
        assigneeUserId: createForm.assigneeUserId,
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

function openUpdate(task: ProjectTask) {
  selectedTask.value = task;
  updateForm.progress = task.progress;
  updateForm.taskStatus = task.taskStatus as UpdateProjectTaskProgressRequestTaskStatusEnum;
  updateForm.finishDate = task.finishDate ? toInputDateTime(task.finishDate) : "";
  updateForm.delayReason = task.delayReason ?? "";
  updateVisible.value = true;
}

async function updateTask() {
  const task = selectedTask.value;
  if (!task) return;
  const valid = await updateFormRef.value?.validate().catch(() => false);
  if (!valid) return;
  const completed =
    updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Completed;
  const delayedOrOverdue =
    updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Delayed ||
    (!completed && task.dueDate < new Date());
  if (completed && !updateForm.finishDate) {
    ElMessage.warning("完成任务时请填写完成日期。");
    return;
  }
  if (delayedOrOverdue && !optional(updateForm.delayReason)) {
    ElMessage.warning("延期或逾期未完成的任务必须填写延期原因。");
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
        finishDate:
          completed && updateForm.finishDate ? new Date(updateForm.finishDate) : undefined,
        delayReason: completed ? undefined : optional(updateForm.delayReason),
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

function canUpdate(task: ProjectTask) {
  return task.assigneeUserId === currentUserId.value;
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
function toInputDateTime(value: Date) {
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
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
  return fallback;
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
        <p>负责人可分配任务；成员只能查看并更新分配给自己的任务。</p>
      </div>
      <div class="panel-actions">
        <el-button :loading="loading" @click="loadTasks">刷新</el-button
        ><el-button type="primary" :disabled="!canCreate" @click="openCreate">创建任务</el-button>
      </div>
    </div>
    <el-alert v-if="loadError" type="error" :title="loadError" show-icon :closable="false" />
    <div class="table-wrap">
      <el-table v-loading="loading" :data="tasks" row-key="id" empty-text="暂无可查看的项目任务"
        ><el-table-column prop="title" label="任务" min-width="190"
          ><template #default="{ row }"
            ><strong>{{ row.title }}</strong
            ><span v-if="row.content" class="task-content">{{ row.content }}</span></template
          ></el-table-column
        ><el-table-column prop="assigneeName" label="执行人" min-width="110" /><el-table-column
          label="优先级"
          width="90"
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
        ><el-table-column label="截止时间" min-width="145"
          ><template #default="{ row }">{{ formatDate(row.dueDate) }}</template></el-table-column
        ><el-table-column label="操作" width="100" fixed="right"
          ><template #default="{ row }"
            ><el-button v-if="canUpdate(row)" link type="primary" @click="openUpdate(row)"
              >更新进度</el-button
            ><span v-else>—</span></template
          ></el-table-column
        ></el-table
      >
    </div>

    <el-dialog v-model="createVisible" title="创建项目任务" width="min(620px, 92vw)"
      ><el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="92px"
        ><el-form-item label="执行人" prop="assigneeUserId"
          ><el-select
            v-model="createForm.assigneeUserId"
            placeholder="选择正在参与项目的成员"
            filterable
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
            value-format="YYYY-MM-DDTHH:mm" /></el-form-item></el-form
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
        ><el-form-item
          v-if="updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Completed"
          label="完成日期"
          ><el-date-picker
            v-model="updateForm.finishDate"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm" /></el-form-item
        ><el-form-item
          v-if="
            updateForm.taskStatus === UpdateProjectTaskProgressRequestTaskStatusEnum.Delayed ||
            (selectedTask &&
              updateForm.taskStatus !== UpdateProjectTaskProgressRequestTaskStatusEnum.Completed &&
              selectedTask.dueDate < new Date())
          "
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
.tasks-panel :deep(.el-progress) {
  min-width: 110px;
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

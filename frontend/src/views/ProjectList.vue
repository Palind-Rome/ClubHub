<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import {
  DefaultApi,
  ProjectProjectStatusEnum,
  ReviewProjectRequestProjectStatusEnum,
  type Club,
  type Project,
} from "../api";

const api = new DefaultApi();

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
const loading = ref(false);
const saving = ref(false);
const leaderSavingId = ref<number | null>(null);
const reviewSavingId = ref<number | null>(null);
const createDialogVisible = ref(false);
const leaderDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const createFormRef = ref<FormInstance>();
const leaderFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();

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
  reviewerUserId: null as number | null,
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

const clubNameMap = computed(() => {
  const map = new Map<number, string>();
  clubs.value.forEach((club) => map.set(club.id, club.name));
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
      message: "负责人用户 ID 必须大于 0",
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
    { required: true, message: "请输入负责人用户 ID", trigger: "blur" },
    { type: "number", min: 1, message: "负责人用户 ID 必须大于 0", trigger: "change" },
  ],
};

const reviewRules: FormRules<typeof reviewForm> = {
  projectStatus: [{ required: true, message: "请选择审核结果", trigger: "change" }],
  reviewerUserId: [
    { required: true, message: "请输入审核人用户 ID", trigger: "blur" },
    { type: "number", min: 1, message: "审核人用户 ID 必须大于 0", trigger: "change" },
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
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "项目列表加载失败"));
  } finally {
    loading.value = false;
  }
}

async function changePage(nextPage: number) {
  filters.page = Math.max(1, nextPage);
  await loadProjects();
}

function openCreateDialog() {
  createForm.clubId = filters.clubId ?? null;
  createForm.projectName = "";
  createForm.description = "";
  createForm.leaderUserId = null;
  createForm.startDate = null;
  createForm.endDate = null;
  createDialogVisible.value = true;
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

function openLeaderDialog(project: Project) {
  leaderForm.projectId = project.id;
  leaderForm.leaderUserId = project.leaderUserId ?? null;
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

function openReviewDialog(project: Project) {
  reviewForm.projectId = project.id;
  reviewForm.projectStatus = ReviewProjectRequestProjectStatusEnum.Running;
  reviewForm.reviewerUserId = null;
  reviewForm.reviewComment = "";
  reviewDialogVisible.value = true;
}

async function reviewProject() {
  if (!(await validateForm(reviewFormRef.value)) || !reviewForm.reviewerUserId) {
    return;
  }

  reviewSavingId.value = reviewForm.projectId;
  try {
    await api.reviewProject({
      projectId: reviewForm.projectId,
      reviewProjectRequest: {
        projectStatus: reviewForm.projectStatus,
        reviewerUserId: reviewForm.reviewerUserId,
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
  await loadClubs();
  await loadProjects();
});
</script>

<template>
  <div class="page project-page">
    <div class="toolbar">
      <div>
        <h2>项目管理</h2>
        <p class="subtitle">演示社团项目立项申请、负责人分配和立项审核流程。</p>
      </div>
      <el-button type="primary" @click="openCreateDialog">提交立项申请</el-button>
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
      <el-table-column prop="id" label="ID" width="70" />
      <el-table-column prop="projectName" label="项目名称" min-width="180" show-overflow-tooltip />
      <el-table-column label="所属社团" min-width="140">
        <template #default="{ row }">
          {{ clubNameMap.get(row.clubId) || `社团 ${row.clubId}` }}
        </template>
      </el-table-column>
      <el-table-column label="负责人" width="110">
        <template #default="{ row }">
          {{ row.leaderUserId ?? "未分配" }}
        </template>
      </el-table-column>
      <el-table-column label="计划时间" min-width="180">
        <template #default="{ row }">
          {{ formatDate(row.startDate) }} 至 {{ formatDate(row.endDate) }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="statusType[row.projectStatus] || 'info'" size="small">
            {{ statusLabel[row.projectStatus] || row.projectStatus }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column
        prop="reviewComment"
        label="审核意见"
        min-width="160"
        show-overflow-tooltip
      />
      <el-table-column label="操作" width="190" fixed="right">
        <template #default="{ row }">
          <el-button size="small" text type="primary" @click="openLeaderDialog(row)">
            分配负责人
          </el-button>
          <el-button
            size="small"
            text
            type="success"
            :disabled="row.projectStatus !== ProjectProjectStatusEnum.Pending"
            @click="openReviewDialog(row)"
          >
            审核
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
          <el-select v-model="createForm.clubId" filterable placeholder="请选择社团">
            <el-option v-for="club in clubs" :key="club.id" :label="club.name" :value="club.id" />
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
        <el-form-item label="项目负责人用户 ID" prop="leaderUserId">
          <el-input-number v-model="createForm.leaderUserId" :min="1" placeholder="可稍后分配" />
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
        <el-form-item label="负责人用户 ID" prop="leaderUserId">
          <el-input-number v-model="leaderForm.leaderUserId" :min="1" />
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
        <el-form-item label="审核人用户 ID" prop="reviewerUserId">
          <el-input-number v-model="reviewForm.reviewerUserId" :min="1" />
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

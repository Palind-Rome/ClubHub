<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { Check, Close, Edit, Plus, Refresh, Search, User } from "@element-plus/icons-vue";
import { type AuthResponse, type AuthRole, onSessionChange, readAuth } from "../authSession";

type RecruitmentStatus = "draft" | "published" | "closed";
type ApplicationStatus = "pending" | "accepted" | "rejected";
type ReviewDecision = "accepted" | "rejected";
type RecruitmentFormMode = "create" | "edit";

interface Club {
  id: number;
  name: string;
  status: string | null;
  statusText: string;
}

interface Recruitment {
  id: number;
  clubId: number;
  clubName: string;
  title: string;
  description: string | null;
  startAt: string | null;
  endAt: string | null;
  quota: number | null;
  requirements: string | null;
  recruitStatus: RecruitmentStatus;
  recruitStatusText: string;
  createdAt: string;
  applicationCount: number;
  acceptedCount: number;
  currentUserApplicationId: number | null;
  currentUserApplicationStatus: ApplicationStatus | null;
  currentUserApplicationStatusText: string | null;
  canManage: boolean;
}

interface RecruitmentApplication {
  id: number;
  recruitId: number;
  recruitTitle: string;
  clubId: number;
  clubName: string;
  userId: number;
  applicantName: string;
  studentNo: string | null;
  applicationReason: string;
  interviewScore: number | null;
  applicationStatus: ApplicationStatus;
  applicationStatusText: string;
  reviewerUserId: number | null;
  reviewerName: string | null;
  submittedAt: string | null;
  reviewedAt: string | null;
}

interface ApiError {
  message?: string;
  title?: string;
}

const recruitmentManagePermission = "recruitment:manage";
const recruitmentApplyPermission = "recruitment:apply";

const auth = ref<AuthResponse | null>(readAuth());
const recruitments = ref<Recruitment[]>([]);
const clubs = ref<Club[]>([]);
const applications = ref<RecruitmentApplication[]>([]);
const loading = ref(false);
const clubLoading = ref(false);
const applicationLoading = ref(false);
const saving = ref(false);
const applying = ref(false);
const reviewing = ref(false);
const error = ref("");
const selectedRecruitment = ref<Recruitment | null>(null);

const filters = reactive({
  status: "",
  clubId: undefined as number | undefined,
});

const recruitmentDialogVisible = ref(false);
const recruitmentFormRef = ref<FormInstance>();
const recruitmentFormMode = ref<RecruitmentFormMode>("create");
const recruitmentTarget = ref<Recruitment | null>(null);
const recruitmentForm = reactive({
  clubId: undefined as number | undefined,
  title: "",
  description: "",
  startAt: "",
  endAt: "",
  quota: 10,
  requirements: "",
  recruitStatus: "published" as RecruitmentStatus,
});

const applicationDialogVisible = ref(false);
const applicationFormRef = ref<FormInstance>();
const applicationTarget = ref<Recruitment | null>(null);
const applicationForm = reactive({
  applicationReason: "",
});

const reviewDialogVisible = ref(false);
const reviewFormRef = ref<FormInstance>();
const reviewTarget = ref<RecruitmentApplication | null>(null);
const reviewForm = reactive({
  decision: "accepted" as ReviewDecision,
  interviewScore: null as number | null,
});

const recruitmentRules: FormRules = {
  clubId: [{ required: true, message: "请选择发布社团", trigger: "change" }],
  title: [{ required: true, message: "请填写纳新标题", trigger: "blur" }],
  startAt: [{ required: true, message: "请选择开始时间", trigger: "change" }],
  endAt: [{ required: true, message: "请选择结束时间", trigger: "change" }],
  quota: [{ required: true, message: "请填写计划人数", trigger: "blur" }],
  requirements: [{ required: true, message: "请填写纳新要求", trigger: "blur" }],
  recruitStatus: [{ required: true, message: "请选择纳新状态", trigger: "change" }],
};

const applicationRules: FormRules = {
  applicationReason: [{ required: true, message: "请填写入社理由", trigger: "blur" }],
};

const reviewRules: FormRules = {
  decision: [{ required: true, message: "请选择筛选结果", trigger: "change" }],
};

let stopSessionListener: (() => void) | null = null;
let recruitmentRequestId = 0;
let applicationRequestId = 0;

const currentUserId = computed(() => auth.value?.user.id);
const isStudent = computed(() =>
  (auth.value?.roles ?? []).some((role) => (role.code ?? "").toLowerCase() === "student"),
);
const canApply = computed(
  () => isStudent.value && hasPermission(recruitmentApplyPermission) && !hasPermission("*"),
);
const hasManageAccess = computed(
  () => hasPermission("*") || hasPermission(recruitmentManagePermission),
);
const manageableClubIdSet = computed(() => {
  if (hasPermission("*")) return null;

  const clubIds = new Set<number>();
  (auth.value?.roles ?? [])
    .filter((role) => roleHasPermission(role, recruitmentManagePermission))
    .forEach((role) => {
      const ids = role.clubIds?.length ? role.clubIds : role.clubId ? [role.clubId] : [];
      ids.forEach((clubId) => clubIds.add(clubId));
    });
  return clubIds;
});
const manageableClubs = computed(() => {
  const clubIds = manageableClubIdSet.value;
  return clubs.value.filter((club) => {
    if (club.status !== "active") return false;
    return clubIds === null || clubIds.has(club.id);
  });
});
const canCreateRecruitment = computed(() => manageableClubs.value.length > 0);

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    let message = `请求失败（${res.status}）`;
    try {
      const body = (await res.json()) as ApiError;
      message = body.message || body.title || message;
    } catch {
      /* 保留默认错误信息 */
    }
    throw new Error(message);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

async function validateForm(form: FormInstance | undefined) {
  if (!form) return false;

  try {
    await form.validate();
    return true;
  } catch {
    return false;
  }
}

async function loadClubs() {
  if (!currentUserId.value || !hasManageAccess.value) {
    clubs.value = [];
    return;
  }

  clubLoading.value = true;
  try {
    clubs.value = await requestJson<Club[]>(`/api/clubs?viewerUserId=${currentUserId.value}`);
    syncSelectedClubFilter();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "社团加载失败");
    clubs.value = [];
  } finally {
    clubLoading.value = false;
  }
}

async function loadRecruitments() {
  const requestId = ++recruitmentRequestId;
  if (!currentUserId.value) {
    recruitments.value = [];
    loading.value = false;
    return;
  }

  loading.value = true;
  error.value = "";
  try {
    const query = new URLSearchParams({ viewerUserId: String(currentUserId.value) });
    if (filters.status) query.set("status", filters.status);
    if (filters.clubId) query.set("clubId", String(filters.clubId));
    const data = await requestJson<Recruitment[]>(`/api/recruitments?${query.toString()}`);
    if (requestId !== recruitmentRequestId) return;
    recruitments.value = data;
    if (selectedRecruitment.value) {
      const refreshed = data.find((item) => item.id === selectedRecruitment.value?.id) ?? null;
      selectedRecruitment.value = refreshed?.canManage ? refreshed : null;
      if (selectedRecruitment.value) await loadApplications(selectedRecruitment.value);
      else applications.value = [];
    }
  } catch (e) {
    if (requestId === recruitmentRequestId) {
      error.value = e instanceof Error ? e.message : "纳新信息加载失败";
      recruitments.value = [];
      applications.value = [];
    }
  } finally {
    if (requestId === recruitmentRequestId) loading.value = false;
  }
}

async function loadApplications(row: Recruitment) {
  const requestId = ++applicationRequestId;
  if (!currentUserId.value) return;

  applicationLoading.value = true;
  try {
    const data = await requestJson<RecruitmentApplication[]>(
      `/api/recruitments/${row.id}/applications?viewerUserId=${currentUserId.value}`,
    );
    if (requestId === applicationRequestId) applications.value = data;
  } catch (e) {
    if (requestId === applicationRequestId) {
      applications.value = [];
      ElMessage.error(e instanceof Error ? e.message : "入社申请列表加载失败");
    }
  } finally {
    if (requestId === applicationRequestId) applicationLoading.value = false;
  }
}

async function refreshAll() {
  await loadClubs();
  await loadRecruitments();
}

function syncSelectedClubFilter() {
  if (!filters.clubId) return;
  if (!manageableClubs.value.some((club) => club.id === filters.clubId)) {
    filters.clubId = undefined;
  }
}

function resetFilters() {
  filters.status = "";
  filters.clubId = undefined;
  void loadRecruitments();
}

function resetRecruitmentForm() {
  const start = new Date();
  start.setHours(start.getHours() + 1, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + 14);

  recruitmentForm.clubId = filters.clubId ?? manageableClubs.value[0]?.id;
  recruitmentForm.title = "";
  recruitmentForm.description = "";
  recruitmentForm.startAt = dateTimeInput(start);
  recruitmentForm.endAt = dateTimeInput(end);
  recruitmentForm.quota = 10;
  recruitmentForm.requirements = "";
  recruitmentForm.recruitStatus = "published";
  recruitmentFormRef.value?.clearValidate();
}

function openCreateDialog() {
  if (!canCreateRecruitment.value) {
    ElMessage.warning("当前账号没有可发布纳新的运营中社团。");
    return;
  }

  recruitmentFormMode.value = "create";
  recruitmentTarget.value = null;
  resetRecruitmentForm();
  recruitmentDialogVisible.value = true;
}

function openEditDialog(row: Recruitment) {
  if (!row.canManage) {
    ElMessage.warning("当前账号不能维护该纳新项目。");
    return;
  }

  recruitmentFormMode.value = "edit";
  recruitmentTarget.value = row;
  recruitmentForm.clubId = row.clubId;
  recruitmentForm.title = row.title;
  recruitmentForm.description = row.description ?? "";
  recruitmentForm.startAt = dateTimeInput(row.startAt);
  recruitmentForm.endAt = dateTimeInput(row.endAt);
  recruitmentForm.quota = row.quota ?? 1;
  recruitmentForm.requirements = row.requirements ?? "";
  recruitmentForm.recruitStatus = row.recruitStatus;
  recruitmentFormRef.value?.clearValidate();
  recruitmentDialogVisible.value = true;
}

async function submitRecruitment() {
  if (!recruitmentFormRef.value || !currentUserId.value) return;
  if (!(await validateForm(recruitmentFormRef.value))) return;
  if (!recruitmentForm.clubId) {
    ElMessage.warning("请选择发布社团");
    return;
  }

  saving.value = true;
  try {
    const payload = {
      currentUserId: currentUserId.value,
      clubId: recruitmentForm.clubId,
      title: recruitmentForm.title,
      description: emptyToNull(recruitmentForm.description),
      startAt: recruitmentForm.startAt,
      endAt: recruitmentForm.endAt,
      quota: recruitmentForm.quota,
      requirements: recruitmentForm.requirements,
      recruitStatus: recruitmentForm.recruitStatus,
    };

    if (recruitmentFormMode.value === "create") {
      await requestJson<Recruitment>("/api/recruitments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      ElMessage.success("纳新已发布");
    } else if (recruitmentTarget.value) {
      await requestJson<Recruitment>(`/api/recruitments/${recruitmentTarget.value.id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ...payload,
          clubId: undefined,
        }),
      });
      ElMessage.success("纳新已保存");
    }

    recruitmentDialogVisible.value = false;
    await loadRecruitments();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "保存失败");
  } finally {
    saving.value = false;
  }
}

function openApplicationDialog(row: Recruitment) {
  const reason = applyDisabledReason(row);
  if (reason) {
    ElMessage.warning(reason);
    return;
  }

  applicationTarget.value = row;
  applicationForm.applicationReason = "";
  applicationFormRef.value?.clearValidate();
  applicationDialogVisible.value = true;
}

async function submitApplication() {
  if (!applicationFormRef.value || !applicationTarget.value || !currentUserId.value) return;
  if (!(await validateForm(applicationFormRef.value))) return;

  applying.value = true;
  try {
    await requestJson<RecruitmentApplication>(
      `/api/recruitments/${applicationTarget.value.id}/applications`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          currentUserId: currentUserId.value,
          applicationReason: applicationForm.applicationReason,
        }),
      },
    );
    ElMessage.success("入社申请已提交");
    applicationDialogVisible.value = false;
    await loadRecruitments();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "提交申请失败");
  } finally {
    applying.value = false;
  }
}

async function openApplicationWorkbench(row: Recruitment) {
  if (!row.canManage) {
    ElMessage.warning("当前账号不能查看该纳新申请。");
    return;
  }

  selectedRecruitment.value = row;
  await loadApplications(row);
}

function openReviewDialog(row: RecruitmentApplication, decision: ReviewDecision) {
  if (row.applicationStatus !== "pending") {
    ElMessage.warning("只有待筛选申请可以录入结果。");
    return;
  }

  reviewTarget.value = row;
  reviewForm.decision = decision;
  reviewForm.interviewScore = row.interviewScore;
  reviewFormRef.value?.clearValidate();
  reviewDialogVisible.value = true;
}

async function submitReview() {
  if (!reviewFormRef.value || !reviewTarget.value || !currentUserId.value) return;
  if (!(await validateForm(reviewFormRef.value))) return;

  const actionText = reviewForm.decision === "accepted" ? "录取" : "拒绝";
  try {
    await ElMessageBox.confirm(
      `确认${actionText}“${reviewTarget.value.applicantName}”？`,
      "筛选确认",
      {
        type: reviewForm.decision === "accepted" ? "success" : "warning",
        confirmButtonText: `确认${actionText}`,
        cancelButtonText: "取消",
      },
    );
  } catch {
    return;
  }

  reviewing.value = true;
  try {
    await requestJson<RecruitmentApplication>(
      `/api/recruitments/applications/${reviewTarget.value.id}/review`,
      {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          currentUserId: currentUserId.value,
          decision: reviewForm.decision,
          interviewScore: reviewForm.interviewScore,
        }),
      },
    );
    ElMessage.success(`申请已${actionText}`);
    reviewDialogVisible.value = false;
    await loadRecruitments();
    if (selectedRecruitment.value) await loadApplications(selectedRecruitment.value);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "筛选失败");
  } finally {
    reviewing.value = false;
  }
}

function applyDisabledReason(row: Recruitment) {
  if (!canApply.value) return "当前账号不能提交入社申请，请切换到普通学生账号。";
  if (row.currentUserApplicationId) return "你已经提交过该纳新申请。";
  if (row.recruitStatus !== "published") return "该纳新当前不接受申请。";
  if (row.quota !== null && row.acceptedCount >= row.quota) return "纳新名额已满。";
  const now = Date.now();
  if (row.startAt && new Date(row.startAt).getTime() > now) return "纳新尚未开始。";
  if (row.endAt && new Date(row.endAt).getTime() < now) return "纳新已结束。";
  return "";
}

function hasPermission(permission: string) {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("*") || permissions.includes(permission);
}

function roleHasPermission(role: AuthRole, permission: string) {
  return role.permissions?.includes("*") || role.permissions?.includes(permission);
}

function statusTagType(status: RecruitmentStatus) {
  if (status === "published") return "success";
  if (status === "closed") return "info";
  return "warning";
}

function recruitmentStatusText(status: RecruitmentStatus) {
  if (status === "draft") return "草稿";
  if (status === "published") return "申请中";
  return "已结束";
}

function applicationTagType(status: ApplicationStatus | null) {
  if (status === "accepted") return "success";
  if (status === "rejected") return "danger";
  if (status === "pending") return "warning";
  return "info";
}

function formatDateTime(value: string | null | undefined) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function dateTimeInput(value: Date | string | null | undefined) {
  const date = typeof value === "string" ? new Date(value) : value;
  if (!date || Number.isNaN(date.getTime())) return "";
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  const hour = String(date.getHours()).padStart(2, "0");
  const minute = String(date.getMinutes()).padStart(2, "0");
  const second = String(date.getSeconds()).padStart(2, "0");
  return `${year}-${month}-${day}T${hour}:${minute}:${second}`;
}

function emptyToNull(value: string) {
  return value.trim() ? value.trim() : null;
}

function refreshSession() {
  auth.value = readAuth();
  void refreshAll();
}

watch(
  () => filters.clubId,
  () => {
    void loadRecruitments();
  },
);

onMounted(() => {
  stopSessionListener = onSessionChange(refreshSession);
  void refreshAll();
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h2>社团纳新</h2>
        <p>发布纳新、提交入社申请、筛选录取</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" :loading="loading" @click="refreshAll">刷新</el-button>
        <el-button
          v-if="hasManageAccess"
          type="primary"
          :icon="Plus"
          :disabled="!canCreateRecruitment"
          @click="openCreateDialog"
        >
          发布纳新
        </el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <div class="filters">
      <el-select
        v-model="filters.status"
        clearable
        placeholder="纳新状态"
        class="filter-control"
        @change="loadRecruitments"
      >
        <el-option label="草稿" value="draft" />
        <el-option label="申请中" value="published" />
        <el-option label="已结束" value="closed" />
      </el-select>
      <el-select
        v-if="hasManageAccess"
        v-model="filters.clubId"
        clearable
        filterable
        placeholder="管理社团"
        class="filter-control"
        :loading="clubLoading"
      >
        <el-option
          v-for="club in manageableClubs"
          :key="club.id"
          :label="club.name"
          :value="club.id"
        />
      </el-select>
      <el-button :icon="Search" @click="loadRecruitments">查询</el-button>
      <el-button @click="resetFilters">重置</el-button>
    </div>

    <el-table v-loading="loading" :data="recruitments" stripe empty-text="暂无纳新数据">
      <el-table-column label="纳新信息" min-width="260">
        <template #default="{ row }">
          <div class="title-line">{{ row.title }}</div>
          <div class="muted">{{ row.clubName }}</div>
          <div v-if="row.description" class="description">{{ row.description }}</div>
        </template>
      </el-table-column>
      <el-table-column label="时间范围" min-width="210">
        <template #default="{ row }">
          <div>{{ formatDateTime(row.startAt) }}</div>
          <div class="muted">至 {{ formatDateTime(row.endAt) }}</div>
        </template>
      </el-table-column>
      <el-table-column label="人数" width="110">
        <template #default="{ row }">
          <span>{{ row.acceptedCount }} / {{ row.quota ?? "不限" }}</span>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.recruitStatus)" size="small">
            {{ recruitmentStatusText(row.recruitStatus) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="我的申请" width="120">
        <template #default="{ row }">
          <el-tag
            v-if="row.currentUserApplicationStatus"
            :type="applicationTagType(row.currentUserApplicationStatus)"
            size="small"
          >
            {{ row.currentUserApplicationStatusText }}
          </el-tag>
          <span v-else class="muted">未申请</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="250" fixed="right">
        <template #default="{ row }">
          <el-button
            v-if="!applyDisabledReason(row)"
            type="primary"
            link
            :icon="User"
            @click="openApplicationDialog(row)"
          >
            申请加入
          </el-button>
          <el-tooltip
            v-else-if="canApply && !row.currentUserApplicationId"
            :content="applyDisabledReason(row)"
          >
            <span>
              <el-button link disabled :icon="User">申请加入</el-button>
            </span>
          </el-tooltip>
          <el-button
            v-if="row.canManage"
            type="primary"
            link
            :icon="Search"
            @click="openApplicationWorkbench(row)"
          >
            申请管理
          </el-button>
          <el-button
            v-if="row.canManage"
            type="primary"
            link
            :icon="Edit"
            @click="openEditDialog(row)"
          >
            编辑
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <div v-if="selectedRecruitment" class="applications-panel">
      <div class="panel-header">
        <div>
          <h3>{{ selectedRecruitment.title }} 申请管理</h3>
          <p>
            {{ selectedRecruitment.clubName }} / {{ selectedRecruitment.applicationCount }} 份申请
          </p>
        </div>
        <el-button
          :icon="Refresh"
          :loading="applicationLoading"
          @click="loadApplications(selectedRecruitment)"
        >
          刷新申请
        </el-button>
      </div>

      <el-table
        v-loading="applicationLoading"
        :data="applications"
        stripe
        empty-text="暂无申请数据"
      >
        <el-table-column prop="applicantName" label="学生" min-width="130" />
        <el-table-column prop="studentNo" label="学号" width="110" />
        <el-table-column
          prop="applicationReason"
          label="入社理由"
          min-width="260"
          show-overflow-tooltip
        />
        <el-table-column label="面试分" width="100">
          <template #default="{ row }">
            {{ row.interviewScore ?? "-" }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="applicationTagType(row.applicationStatus)" size="small">
              {{ row.applicationStatusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="提交时间" width="170">
          <template #default="{ row }">
            {{ formatDateTime(row.submittedAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button
              type="success"
              link
              :icon="Check"
              :disabled="row.applicationStatus !== 'pending'"
              @click="openReviewDialog(row, 'accepted')"
            >
              录取
            </el-button>
            <el-button
              type="danger"
              link
              :icon="Close"
              :disabled="row.applicationStatus !== 'pending'"
              @click="openReviewDialog(row, 'rejected')"
            >
              拒绝
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <el-dialog
      v-model="recruitmentDialogVisible"
      :title="recruitmentFormMode === 'create' ? '发布纳新' : '编辑纳新'"
      width="640px"
    >
      <el-form
        ref="recruitmentFormRef"
        :model="recruitmentForm"
        :rules="recruitmentRules"
        label-width="96px"
      >
        <el-form-item label="发布社团" prop="clubId">
          <el-select
            v-model="recruitmentForm.clubId"
            filterable
            :disabled="recruitmentFormMode === 'edit'"
            placeholder="请选择社团"
          >
            <el-option
              v-for="club in manageableClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="纳新标题" prop="title">
          <el-input v-model="recruitmentForm.title" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="纳新说明">
          <el-input
            v-model="recruitmentForm.description"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="时间范围" required>
          <div class="date-row">
            <el-form-item prop="startAt">
              <el-date-picker
                v-model="recruitmentForm.startAt"
                type="datetime"
                value-format="YYYY-MM-DDTHH:mm:ss"
                placeholder="开始时间"
              />
            </el-form-item>
            <el-form-item prop="endAt">
              <el-date-picker
                v-model="recruitmentForm.endAt"
                type="datetime"
                value-format="YYYY-MM-DDTHH:mm:ss"
                placeholder="结束时间"
              />
            </el-form-item>
          </div>
        </el-form-item>
        <el-form-item label="计划人数" prop="quota">
          <el-input-number v-model="recruitmentForm.quota" :min="1" :max="999" />
        </el-form-item>
        <el-form-item label="纳新要求" prop="requirements">
          <el-input
            v-model="recruitmentForm.requirements"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="纳新状态" prop="recruitStatus">
          <el-radio-group v-model="recruitmentForm.recruitStatus">
            <el-radio-button label="draft">草稿</el-radio-button>
            <el-radio-button label="published">申请中</el-radio-button>
            <el-radio-button label="closed">已结束</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="recruitmentDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitRecruitment">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="applicationDialogVisible" title="提交入社申请" width="560px">
      <el-form
        ref="applicationFormRef"
        :model="applicationForm"
        :rules="applicationRules"
        label-width="96px"
      >
        <el-form-item label="申请项目">
          <span>{{ applicationTarget?.title }}</span>
        </el-form-item>
        <el-form-item label="入社理由" prop="applicationReason">
          <el-input
            v-model="applicationForm.applicationReason"
            type="textarea"
            :rows="5"
            maxlength="800"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="applicationDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="applying" @click="submitApplication"
          >提交申请</el-button
        >
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="录入筛选结果" width="520px">
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="96px">
        <el-form-item label="申请学生">
          <span>{{ reviewTarget?.applicantName }}</span>
        </el-form-item>
        <el-form-item label="面试分">
          <el-input-number v-model="reviewForm.interviewScore" :min="0" :max="100" :precision="1" />
        </el-form-item>
        <el-form-item label="筛选结果" prop="decision">
          <el-radio-group v-model="reviewForm.decision">
            <el-radio-button label="accepted">录取</el-radio-button>
            <el-radio-button label="rejected">拒绝</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="reviewing" @click="submitReview">保存结果</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  max-width: 1180px;
  margin: 0 auto;
}

.page-header,
.filters,
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.page-header {
  margin-bottom: 16px;
}

.page-header h2,
.panel-header h3 {
  margin: 0;
}

.page-header p,
.panel-header p {
  margin: 4px 0 0;
  color: var(--el-text-color-secondary);
}

.header-actions,
.filters {
  flex-wrap: wrap;
}

.filters {
  justify-content: flex-start;
  margin: 16px 0;
}

.filter-control {
  width: 180px;
}

.title-line {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.muted,
.description {
  color: var(--el-text-color-secondary);
}

.description {
  margin-top: 4px;
  line-height: 1.5;
}

.applications-panel {
  margin-top: 24px;
  padding-top: 20px;
  border-top: 1px solid var(--el-border-color-light);
}

.panel-header {
  margin-bottom: 12px;
}

.date-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  width: 100%;
}

.date-row :deep(.el-form-item) {
  margin-bottom: 0;
}

.date-row :deep(.el-date-editor) {
  width: 100%;
}

@media (max-width: 760px) {
  .page-header,
  .panel-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .date-row {
    grid-template-columns: 1fr;
  }
}
</style>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from "vue";
import { ElMessage } from "element-plus";
import { onSessionChange, readAuth } from "../authSession";

interface Activity {
  id: number;
  title: string;
  activityType: string | null;
  description: string | null;
  clubName: string;
  clubId: number;
  creatorUserId: number | null;
  startTime: string | null;
  endTime: string | null;
  location: string | null;
  status: string | null;
  maxParticipants: number | null;
  registrationDeadline: string | null;
  reviewerUserId: number | null;
  reviewComment: string | null;
  budgetAmount: number | null;
  budgetPurpose: string | null;
  budgetDetail: string | null;
  budgetStatus: string | null;
  budgetReviewerId: number | null;
  budgetComment: string | null;
  publishedAt: string | null;
  checkinStartAt: string | null;
  checkinEndAt: string | null;
  checkoutStartAt: string | null;
  checkoutEndAt: string | null;
  currentParticipants: number;
  isRegistered: boolean;
}

interface ActivityParticipation {
  id: number;
  userId: number;
  registerStatus: string | null;
  registeredAt: string | null;
  checkinAt: string | null;
  checkoutAt: string | null;
  signStatus: string | null;
  remark: string | null;
}

interface CreateActivityForm {
  clubId: number;
  creatorUserId: number | null;
  title: string;
  activityType: string;
  description: string;
  location: string;
  startTime: string;
  endTime: string;
  maxParticipants: number;
  registrationDeadline: string;
}

interface ReviewActivityForm {
  approved: boolean;
  reviewerUserId: number | null;
  comment: string;
}

interface BudgetForm {
  applicantUserId: number;
  budgetAmount: number;
  budgetPurpose: string;
  budgetDetail: string;
}

interface BudgetReviewForm {
  approved: boolean;
  reviewerUserId: number;
  comment: string;
}

const activities = ref<Activity[]>([]);
const auth = ref(readAuth());
const statusFilter = ref("all");
const loading = ref(true);
const error = ref("");
const saving = ref(false);
const registeringActivityId = ref<number | null>(null);
let stopSessionListener: (() => void) | undefined;

const createDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const budgetDialogVisible = ref(false);
const budgetReviewDialogVisible = ref(false);
const settingsDialogVisible = ref(false);
const signDialogVisible = ref(false);
const participationsDialogVisible = ref(false);

const currentActivity = ref<Activity | null>(null);
const participations = ref<ActivityParticipation[]>([]);
const participationLoading = ref(false);

const currentUserId = computed(() => auth.value?.user.id ?? null);

const createForm = ref<CreateActivityForm>({
  clubId: 1,
  creatorUserId: null,
  title: "",
  activityType: "",
  description: "",
  location: "",
  startTime: "",
  endTime: "",
  maxParticipants: 50,
  registrationDeadline: "",
});

const reviewForm = ref<ReviewActivityForm>({
  approved: true,
  reviewerUserId: null,
  comment: "",
});

const budgetForm = ref<BudgetForm>({
  applicantUserId: 1,
  budgetAmount: 1000,
  budgetPurpose: "",
  budgetDetail: "",
});

const budgetReviewForm = ref<BudgetReviewForm>({
  approved: true,
  reviewerUserId: 1,
  comment: "",
});

const settingsForm = ref({
  checkinCode: "",
  checkinStartAt: "",
  checkinEndAt: "",
  checkoutCode: "",
  checkoutStartAt: "",
  checkoutEndAt: "",
});

const signForm = ref({
  type: "checkin",
  // TODO(#81): 默认 userId 仅为 demo 联调；正式版应由后端根据登录态确定，前端移除该输入。
  userId: 1,
  code: "",
});

const statusFilterOptions = [
  { value: "all", label: "全部状态" },
  { value: "pending_review", label: "待审核" },
  { value: "published", label: "报名中" },
  { value: "ongoing", label: "进行中" },
  { value: "rejected", label: "已驳回" },
];

const filteredActivities = computed(() => {
  if (statusFilter.value === "all") {
    return activities.value;
  }
  return activities.value.filter((activity) => activity.status === statusFilter.value);
});

const statusLabel: Record<string, string> = {
  draft: "草稿",
  pending_review: "待审核",
  published: "报名中",
  rejected: "已驳回",
  ongoing: "进行中",
  finished: "已结束",
  cancelled: "已取消",
};

const statusType: Record<string, string> = {
  draft: "info",
  pending_review: "warning",
  published: "success",
  rejected: "danger",
  ongoing: "",
  finished: "info",
  cancelled: "danger",
};

const signStatusLabel: Record<string, string> = {
  registered: "已登记",
  checked_in: "已签到",
  checked_out: "已签退",
};

const budgetStatusLabel: Record<string, string> = {
  pending: "待审批",
  approved: "已通过",
  rejected: "已驳回",
};

const budgetStatusType: Record<string, string> = {
  pending: "warning",
  approved: "success",
  rejected: "danger",
};

const CHECKIN_WINDOW_MINUTES = 5;
const SIGN_CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

onMounted(async () => {
  stopSessionListener = onSessionChange(() => {
    auth.value = readAuth();
    void loadActivities();
  });
  await loadActivities();
});

onUnmounted(() => {
  stopSessionListener?.();
});

function formatDateTimeForPicker(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

function formatTime(value: string | null) {
  return value ? new Date(value).toLocaleString() : "-";
}

function formatActivityTimeRange(activity: Activity) {
  if (!activity.startTime || !activity.endTime) {
    return "未设置活动时间";
  }
  return `${formatTime(activity.startTime)} ~ ${formatTime(activity.endTime)}`;
}

function formatMoney(value: number | null) {
  if (value == null) return "未填写";
  return new Intl.NumberFormat("zh-CN", {
    style: "currency",
    currency: "CNY",
    minimumFractionDigits: 2,
  }).format(value);
}

function buildDefaultCreateTimes() {
  const start = new Date();
  start.setDate(start.getDate() + 1);
  start.setHours(14, 0, 0, 0);

  const end = new Date(start);
  end.setHours(17, 0, 0, 0);

  const deadline = new Date(start);
  deadline.setDate(deadline.getDate() - 1);
  deadline.setHours(23, 59, 0, 0);

  return {
    startTime: formatDateTimeForPicker(start),
    endTime: formatDateTimeForPicker(end),
    registrationDeadline: formatDateTimeForPicker(deadline),
  };
}

function getCurrentUserId() {
  return currentUserId.value ?? 1;
}

function generateSignCode() {
  return Array.from(
    { length: 6 },
    () => SIGN_CODE_CHARS[Math.floor(Math.random() * SIGN_CODE_CHARS.length)],
  ).join("");
}

function hasExistingCheckinSettings(activity: Activity) {
  return Boolean(
    activity.checkinStartAt ||
    activity.checkinEndAt ||
    activity.checkoutStartAt ||
    activity.checkoutEndAt,
  );
}

function buildRecommendedCheckinSettings(activity: Activity) {
  if (!activity.startTime || !activity.endTime) {
    return {
      checkinStartAt: "",
      checkinEndAt: "",
      checkoutStartAt: "",
      checkoutEndAt: "",
    };
  }

  const start = new Date(activity.startTime);
  const end = new Date(activity.endTime);
  const checkinEnd = new Date(start.getTime() + CHECKIN_WINDOW_MINUTES * 60 * 1000);
  const effectiveCheckinEnd = checkinEnd > end ? end : checkinEnd;

  return {
    checkinStartAt: formatDateTimeForPicker(start),
    checkinEndAt: formatDateTimeForPicker(effectiveCheckinEnd),
    checkoutStartAt: formatDateTimeForPicker(effectiveCheckinEnd),
    checkoutEndAt: formatDateTimeForPicker(end),
  };
}

function emptyToNull(value: string) {
  return value ? value : null;
}

async function loadActivities() {
  loading.value = true;
  error.value = "";
  try {
    const query = new URLSearchParams();
    if (currentUserId.value) {
      query.set("currentUserId", String(currentUserId.value));
    }

    const url = query.toString() ? `/api/activities?${query}` : "/api/activities";
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = (await res.json()) as Activity[];
    activities.value = data.map((activity) => ({
      ...activity,
      currentParticipants: activity.currentParticipants ?? 0,
      isRegistered: activity.isRegistered ?? false,
    }));
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
}

function canRegister(activity: Activity) {
  const deadlinePassed =
    activity.registrationDeadline != null &&
    new Date(activity.registrationDeadline).getTime() < Date.now();

  return (
    Boolean(currentUserId.value) &&
    activity.status === "published" &&
    !activity.isRegistered &&
    !deadlinePassed &&
    (activity.maxParticipants == null || activity.currentParticipants < activity.maxParticipants)
  );
}

function registerButtonText(activity: Activity) {
  if (activity.isRegistered) return "已报名";
  return canRegister(activity) ? "报名" : "不可报名";
}

function openCreate() {
  const defaults = buildDefaultCreateTimes();
  createForm.value = {
    clubId: 1,
    creatorUserId: null,
    title: "",
    activityType: "",
    description: "",
    location: "",
    startTime: defaults.startTime,
    endTime: defaults.endTime,
    maxParticipants: 50,
    registrationDeadline: defaults.registrationDeadline,
  };
  createDialogVisible.value = true;
}

async function createActivity() {
  if (!createForm.value.title || !createForm.value.startTime || !createForm.value.endTime) {
    error.value = "请填写活动标题、开始时间和结束时间。";
    ElMessage.error(error.value);
    return;
  }

  saving.value = true;
  try {
    const res = await fetch("/api/activities", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        clubId: createForm.value.clubId,
        creatorUserId: createForm.value.creatorUserId,
        title: createForm.value.title,
        activityType: emptyToNull(createForm.value.activityType),
        description: emptyToNull(createForm.value.description),
        location: emptyToNull(createForm.value.location),
        startTime: createForm.value.startTime,
        endTime: createForm.value.endTime,
        maxParticipants: createForm.value.maxParticipants,
        registrationDeadline: emptyToNull(createForm.value.registrationDeadline),
      }),
    });
    if (!res.ok) throw new Error(await res.text());
    createDialogVisible.value = false;
    ElMessage.success("活动已提交审核");
    await loadActivities();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "创建活动失败";
    ElMessage.error(error.value);
  } finally {
    saving.value = false;
  }
}

async function registerActivity(activity: Activity) {
  const userId = currentUserId.value;
  if (!userId) {
    ElMessage.warning("请先登录后再报名");
    return;
  }

  registeringActivityId.value = activity.id;
  try {
    const res = await fetch(`/api/activities/${activity.id}/registrations`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ userId }),
    });
    if (!res.ok) throw new Error(await readErrorMessage(res));

    const result = (await res.json()) as { currentParticipants?: number; message?: string };
    activity.currentParticipants = result.currentParticipants ?? activity.currentParticipants + 1;
    activity.isRegistered = true;
    ElMessage.success(result.message || "报名成功");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "报名失败");
  } finally {
    registeringActivityId.value = null;
  }
}

function openReview(activity: Activity) {
  currentActivity.value = activity;
  reviewForm.value = {
    approved: true,
    reviewerUserId: null,
    comment: "",
  };
  reviewDialogVisible.value = true;
}

async function reviewActivity() {
  if (!currentActivity.value) return;

  saving.value = true;
  try {
    const res = await fetch(`/api/activities/${currentActivity.value.id}/review`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(reviewForm.value),
    });
    if (!res.ok) throw new Error(await res.text());
    reviewDialogVisible.value = false;
    ElMessage.success("审核结果已保存");
    await loadActivities();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "审核活动失败";
    ElMessage.error(error.value);
  } finally {
    saving.value = false;
  }
}

function openBudget(activity: Activity) {
  currentActivity.value = activity;
  budgetForm.value = {
    applicantUserId: getCurrentUserId(),
    budgetAmount: activity.budgetAmount ?? 1000,
    budgetPurpose: activity.budgetPurpose ?? "",
    budgetDetail: activity.budgetDetail ?? "",
  };
  budgetDialogVisible.value = true;
}

async function applyBudget() {
  if (!currentActivity.value) return;
  if (!budgetForm.value.budgetPurpose.trim() || budgetForm.value.budgetAmount <= 0) {
    error.value = "请填写大于 0 的预算金额和经费用途。";
    ElMessage.error(error.value);
    return;
  }

  saving.value = true;
  try {
    const res = await fetch(`/api/activities/${currentActivity.value.id}/budget`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        applicantUserId: budgetForm.value.applicantUserId,
        budgetAmount: budgetForm.value.budgetAmount,
        budgetPurpose: budgetForm.value.budgetPurpose,
        budgetDetail: emptyToNull(budgetForm.value.budgetDetail),
      }),
    });
    if (!res.ok) throw new Error(await readErrorMessage(res));
    budgetDialogVisible.value = false;
    ElMessage.success("经费预算已提交审批");
    await loadActivities();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "提交经费预算失败";
    ElMessage.error(error.value);
  } finally {
    saving.value = false;
  }
}

function openBudgetReview(activity: Activity) {
  currentActivity.value = activity;
  budgetReviewForm.value = {
    approved: true,
    reviewerUserId: getCurrentUserId(),
    comment: "",
  };
  budgetReviewDialogVisible.value = true;
}

async function reviewBudget() {
  if (!currentActivity.value) return;

  saving.value = true;
  try {
    const res = await fetch(`/api/activities/${currentActivity.value.id}/budget/review`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(budgetReviewForm.value),
    });
    if (!res.ok) throw new Error(await readErrorMessage(res));
    budgetReviewDialogVisible.value = false;
    ElMessage.success("经费审批结果已保存");
    await loadActivities();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "审批经费失败";
    ElMessage.error(error.value);
  } finally {
    saving.value = false;
  }
}

function openSettings(activity: Activity) {
  currentActivity.value = activity;
  const recommended = buildRecommendedCheckinSettings(activity);
  const useRecommended = !hasExistingCheckinSettings(activity);

  settingsForm.value = {
    checkinCode: generateSignCode(),
    checkinStartAt: useRecommended ? recommended.checkinStartAt : (activity.checkinStartAt ?? ""),
    checkinEndAt: useRecommended ? recommended.checkinEndAt : (activity.checkinEndAt ?? ""),
    checkoutCode: generateSignCode(),
    checkoutStartAt: useRecommended
      ? recommended.checkoutStartAt
      : (activity.checkoutStartAt ?? ""),
    checkoutEndAt: useRecommended ? recommended.checkoutEndAt : (activity.checkoutEndAt ?? ""),
  };
  settingsDialogVisible.value = true;
}

function applyRecommendedTimes() {
  if (!currentActivity.value) return;

  const recommended = buildRecommendedCheckinSettings(currentActivity.value);
  settingsForm.value.checkinStartAt = recommended.checkinStartAt;
  settingsForm.value.checkinEndAt = recommended.checkinEndAt;
  settingsForm.value.checkoutStartAt = recommended.checkoutStartAt;
  settingsForm.value.checkoutEndAt = recommended.checkoutEndAt;
}

function onCheckinEndChange(value: string) {
  if (value) {
    settingsForm.value.checkoutStartAt = value;
  }
}

function regenerateCheckinCode() {
  settingsForm.value.checkinCode = generateSignCode();
}

function regenerateCheckoutCode() {
  settingsForm.value.checkoutCode = generateSignCode();
}

async function saveCheckinSettings() {
  if (!currentActivity.value) return;
  if (
    !settingsForm.value.checkinCode ||
    !settingsForm.value.checkoutCode ||
    !settingsForm.value.checkinStartAt ||
    !settingsForm.value.checkinEndAt ||
    !settingsForm.value.checkoutStartAt ||
    !settingsForm.value.checkoutEndAt
  ) {
    error.value = "请完整填写签到签退码和时间窗口。";
    ElMessage.error(error.value);
    return;
  }

  saving.value = true;
  try {
    const res = await fetch(`/api/activities/${currentActivity.value.id}/checkin-settings`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(settingsForm.value),
    });
    if (!res.ok) throw new Error(await res.text());
    settingsDialogVisible.value = false;
    ElMessage.success("签到签退设置已保存");
    await loadActivities();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "保存签到设置失败";
    ElMessage.error(error.value);
  } finally {
    saving.value = false;
  }
}

function openSign(activity: Activity, type: "checkin" | "checkout") {
  currentActivity.value = activity;
  signForm.value = {
    type,
    userId: getCurrentUserId(),
    code: "",
  };
  signDialogVisible.value = true;
}

async function submitSign() {
  if (!currentActivity.value) return;

  saving.value = true;
  try {
    const res = await fetch(`/api/activities/${currentActivity.value.id}/${signForm.value.type}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        userId: signForm.value.userId,
        code: signForm.value.code,
      }),
    });
    if (!res.ok) throw new Error(await res.text());
    signDialogVisible.value = false;
    ElMessage.success(signForm.value.type === "checkin" ? "签到成功" : "签退成功");
    await loadActivities();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "签到签退失败";
    ElMessage.error(error.value);
  } finally {
    saving.value = false;
  }
}

async function openParticipations(activity: Activity) {
  currentActivity.value = activity;
  participationsDialogVisible.value = true;
  participationLoading.value = true;
  try {
    const res = await fetch(`/api/activities/${activity.id}/participations`);
    if (!res.ok) throw new Error(await res.text());
    participations.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载参与记录失败";
    ElMessage.error(error.value);
  } finally {
    participationLoading.value = false;
  }
}

async function readErrorMessage(res: Response) {
  const body = await res.text();
  if (!body) {
    return `请求失败（${res.status}）`;
  }

  try {
    const parsed = JSON.parse(body) as { message?: string };
    return parsed.message || `请求失败（${res.status}）`;
  } catch {
    return body;
  }
}
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <h2>活动管理</h2>
      <div class="toolbar-actions">
        <el-select v-model="statusFilter" placeholder="筛选状态" class="status-filter">
          <el-option
            v-for="option in statusFilterOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
        <el-button type="primary" @click="openCreate">创建活动</el-button>
      </div>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <el-table
      v-loading="loading"
      :data="filteredActivities"
      stripe
      border
      empty-text="暂无活动数据"
      class="activity-table"
    >
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="title" label="标题" min-width="180" show-overflow-tooltip />
      <el-table-column prop="clubName" label="主办社团" width="120" />
      <el-table-column label="时间" min-width="260">
        <template #default="{ row }">
          <span class="time-range">{{ formatActivityTimeRange(row) }}</span>
        </template>
      </el-table-column>
      <el-table-column prop="location" label="地点" width="120" show-overflow-tooltip />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag v-if="row.status" :type="statusType[row.status] || 'info'" size="small">
            {{ statusLabel[row.status] || row.status }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="报名情况" width="120" align="center">
        <template #default="{ row }">
          <span class="quota-text"
            >{{ row.currentParticipants }} / {{ row.maxParticipants ?? "不限" }}</span
          >
        </template>
      </el-table-column>
      <el-table-column label="经费" width="160">
        <template #default="{ row }">
          <div class="budget-cell">
            <el-tag
              v-if="row.budgetStatus"
              :type="budgetStatusType[row.budgetStatus] || 'info'"
              size="small"
            >
              {{ budgetStatusLabel[row.budgetStatus] || row.budgetStatus }}
            </el-tag>
            <el-tag v-else type="info" size="small">未申请</el-tag>
            <span>{{ formatMoney(row.budgetAmount) }}</span>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="520" align="center">
        <template #default="{ row }">
          <div class="action-buttons">
            <el-button
              v-if="row.status === 'published'"
              size="small"
              type="primary"
              :disabled="!canRegister(row)"
              :loading="registeringActivityId === row.id"
              @click="registerActivity(row)"
              >{{ registerButtonText(row) }}</el-button
            >
            <el-button
              v-if="row.status === 'pending_review'"
              size="small"
              type="primary"
              plain
              @click="openReview(row)"
              >审核</el-button
            >
            <el-button
              size="small"
              type="primary"
              plain
              :disabled="row.budgetStatus === 'approved'"
              @click="openBudget(row)"
              >经费申请</el-button
            >
            <el-button
              v-if="row.budgetStatus === 'pending'"
              size="small"
              type="warning"
              plain
              @click="openBudgetReview(row)"
              >经费审批</el-button
            >
            <el-button
              v-if="row.status === 'published' || row.status === 'ongoing'"
              size="small"
              type="primary"
              plain
              @click="openSettings(row)"
              >签到设置</el-button
            >
            <el-button
              v-if="row.status === 'published' || row.status === 'ongoing'"
              size="small"
              type="success"
              plain
              @click="openSign(row, 'checkin')"
              >签到</el-button
            >
            <el-button
              v-if="row.status === 'published' || row.status === 'ongoing'"
              size="small"
              type="warning"
              plain
              @click="openSign(row, 'checkout')"
              >签退</el-button
            >
            <el-button size="small" plain @click="openParticipations(row)">记录</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createDialogVisible" title="创建活动" width="620px">
      <el-form label-position="top">
        <el-form-item label="社团 ID" required>
          <el-input-number v-model="createForm.clubId" :min="1" />
        </el-form-item>
        <el-form-item label="创建人用户 ID（可留空）">
          <el-input-number v-model="createForm.creatorUserId" :min="1" placeholder="可留空" />
        </el-form-item>
        <el-form-item label="活动标题" required>
          <el-input v-model="createForm.title" placeholder="请输入活动标题" />
        </el-form-item>
        <el-form-item label="活动类型">
          <el-input v-model="createForm.activityType" placeholder="如：讲座、比赛、志愿服务" />
        </el-form-item>
        <el-form-item label="活动地点">
          <el-input v-model="createForm.location" placeholder="请输入活动地点" />
        </el-form-item>
        <el-form-item label="开始时间" required>
          <el-date-picker
            v-model="createForm.startTime"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择开始时间"
          />
        </el-form-item>
        <el-form-item label="结束时间" required>
          <el-date-picker
            v-model="createForm.endTime"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择结束时间"
          />
        </el-form-item>
        <el-form-item label="人数上限">
          <el-input-number v-model="createForm.maxParticipants" :min="1" />
        </el-form-item>
        <el-form-item label="报名截止时间">
          <el-date-picker
            v-model="createForm.registrationDeadline"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="可选"
          />
        </el-form-item>
        <el-form-item label="活动说明">
          <el-input
            v-model="createForm.description"
            type="textarea"
            :rows="3"
            placeholder="请输入活动内容"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="createActivity">提交审核</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="活动审核" width="520px">
      <el-descriptions
        v-if="currentActivity"
        :column="1"
        border
        size="small"
        class="review-summary"
      >
        <el-descriptions-item label="活动标题">{{ currentActivity.title }}</el-descriptions-item>
        <el-descriptions-item label="主办社团">{{ currentActivity.clubName }}</el-descriptions-item>
        <el-descriptions-item label="活动时间">{{
          formatActivityTimeRange(currentActivity)
        }}</el-descriptions-item>
        <el-descriptions-item label="活动地点">{{
          currentActivity.location || "未填写"
        }}</el-descriptions-item>
        <el-descriptions-item label="人数上限">{{
          currentActivity.maxParticipants ?? "不限"
        }}</el-descriptions-item>
        <el-descriptions-item label="活动说明">{{
          currentActivity.description || "未填写"
        }}</el-descriptions-item>
      </el-descriptions>
      <el-form label-position="top" class="review-form">
        <el-form-item label="审核结果">
          <el-radio-group v-model="reviewForm.approved">
            <el-radio :value="true">通过并发布</el-radio>
            <el-radio :value="false">驳回</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="审核人用户 ID（可留空）">
          <el-input-number v-model="reviewForm.reviewerUserId" :min="1" placeholder="可留空" />
        </el-form-item>
        <el-form-item label="审核意见">
          <el-input
            v-model="reviewForm.comment"
            type="textarea"
            :rows="3"
            placeholder="请输入审核意见"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="reviewActivity">保存审核结果</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="budgetDialogVisible" title="活动经费预算申请" width="560px">
      <el-descriptions
        v-if="currentActivity"
        :column="1"
        border
        size="small"
        class="review-summary"
      >
        <el-descriptions-item label="活动标题">{{ currentActivity.title }}</el-descriptions-item>
        <el-descriptions-item label="主办社团">{{ currentActivity.clubName }}</el-descriptions-item>
        <el-descriptions-item label="当前经费状态">
          {{
            currentActivity.budgetStatus
              ? budgetStatusLabel[currentActivity.budgetStatus]
              : "未申请"
          }}
        </el-descriptions-item>
        <el-descriptions-item label="审批意见">{{
          currentActivity.budgetComment || "暂无"
        }}</el-descriptions-item>
      </el-descriptions>
      <el-form label-position="top" class="review-form">
        <el-form-item label="申请人用户 ID" required>
          <el-input-number v-model="budgetForm.applicantUserId" :min="1" />
        </el-form-item>
        <el-form-item label="预算金额" required>
          <el-input-number
            v-model="budgetForm.budgetAmount"
            :min="0.01"
            :precision="2"
            :step="100"
          />
        </el-form-item>
        <el-form-item label="经费用途" required>
          <el-input v-model="budgetForm.budgetPurpose" placeholder="如：物料采购、场地布置" />
        </el-form-item>
        <el-form-item label="预算明细">
          <el-input
            v-model="budgetForm.budgetDetail"
            type="textarea"
            :rows="4"
            placeholder="请填写预算条目、单价、数量或补充说明"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="budgetDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="applyBudget">提交审批</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="budgetReviewDialogVisible" title="活动经费审批" width="560px">
      <el-descriptions
        v-if="currentActivity"
        :column="1"
        border
        size="small"
        class="review-summary"
      >
        <el-descriptions-item label="活动标题">{{ currentActivity.title }}</el-descriptions-item>
        <el-descriptions-item label="预算金额">{{
          formatMoney(currentActivity.budgetAmount)
        }}</el-descriptions-item>
        <el-descriptions-item label="经费用途">{{
          currentActivity.budgetPurpose || "未填写"
        }}</el-descriptions-item>
        <el-descriptions-item label="预算明细">{{
          currentActivity.budgetDetail || "未填写"
        }}</el-descriptions-item>
      </el-descriptions>
      <el-form label-position="top" class="review-form">
        <el-form-item label="审批结果">
          <el-radio-group v-model="budgetReviewForm.approved">
            <el-radio :value="true">通过</el-radio>
            <el-radio :value="false">驳回</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="审批人用户 ID" required>
          <el-input-number v-model="budgetReviewForm.reviewerUserId" :min="1" />
        </el-form-item>
        <el-form-item label="审批意见">
          <el-input
            v-model="budgetReviewForm.comment"
            type="textarea"
            :rows="3"
            placeholder="请输入经费审批意见"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="budgetReviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="reviewBudget">保存审批结果</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="settingsDialogVisible" title="签到签退设置" width="560px">
      <el-alert
        v-if="currentActivity"
        :title="`${currentActivity.title}：${formatActivityTimeRange(currentActivity)}`"
        :description="`地点：${currentActivity.location || '未填写'}。默认签到窗口 ${CHECKIN_WINDOW_MINUTES} 分钟，可按需调整。`"
        type="info"
        :closable="false"
        show-icon
        class="settings-hint"
      />
      <div class="settings-actions">
        <el-button size="small" @click="applyRecommendedTimes">恢复推荐时间</el-button>
      </div>
      <el-form label-position="top">
        <el-form-item label="签到码" required>
          <div class="code-field">
            <el-input v-model="settingsForm.checkinCode" placeholder="请输入签到码" />
            <el-button @click="regenerateCheckinCode">重新生成</el-button>
          </div>
        </el-form-item>
        <el-form-item label="签到开始时间" required>
          <el-date-picker
            v-model="settingsForm.checkinStartAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择签到开始时间"
          />
        </el-form-item>
        <el-form-item label="签到结束时间" required>
          <el-date-picker
            v-model="settingsForm.checkinEndAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择签到结束时间"
            @change="onCheckinEndChange"
          />
        </el-form-item>
        <el-form-item label="签退码" required>
          <div class="code-field">
            <el-input v-model="settingsForm.checkoutCode" placeholder="请输入签退码" />
            <el-button @click="regenerateCheckoutCode">重新生成</el-button>
          </div>
        </el-form-item>
        <el-form-item label="签退开始时间" required>
          <el-date-picker
            v-model="settingsForm.checkoutStartAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择签退开始时间"
          />
        </el-form-item>
        <el-form-item label="签退结束时间" required>
          <el-date-picker
            v-model="settingsForm.checkoutEndAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择签退结束时间"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="settingsDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveCheckinSettings"
          >保存设置</el-button
        >
      </template>
    </el-dialog>

    <el-dialog
      v-model="signDialogVisible"
      :title="signForm.type === 'checkin' ? '活动签到' : '活动签退'"
      width="420px"
    >
      <el-form label-position="top">
        <el-form-item label="用户 ID" required>
          <el-input-number v-model="signForm.userId" :min="1" />
        </el-form-item>
        <el-form-item :label="signForm.type === 'checkin' ? '签到码' : '签退码'" required>
          <el-input v-model="signForm.code" placeholder="请输入验证码" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="signDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitSign">
          {{ signForm.type === "checkin" ? "签到" : "签退" }}
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="participationsDialogVisible" title="活动参与记录" width="760px">
      <el-table
        v-loading="participationLoading"
        :data="participations"
        stripe
        empty-text="暂无参与记录"
      >
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column prop="userId" label="用户 ID" width="90" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            {{ signStatusLabel[row.signStatus] || row.signStatus || "-" }}
          </template>
        </el-table-column>
        <el-table-column label="签到时间" width="180">
          <template #default="{ row }">{{ formatTime(row.checkinAt) }}</template>
        </el-table-column>
        <el-table-column label="签退时间" width="180">
          <template #default="{ row }">{{ formatTime(row.checkoutAt) }}</template>
        </el-table-column>
        <el-table-column prop="remark" label="备注" show-overflow-tooltip />
      </el-table>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  width: 100%;
  max-width: 1280px;
  margin: 0 auto;
  padding: 0 12px;
  box-sizing: border-box;
}
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.toolbar-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}
.status-filter {
  width: 140px;
}
.toolbar h2 {
  margin: 0;
}
.activity-table {
  width: 100%;
}
.time-range {
  display: inline-block;
  line-height: 1.4;
  white-space: normal;
  word-break: break-word;
}
.review-summary {
  margin-bottom: 16px;
}
.review-form {
  margin-top: 4px;
}
.quota-text {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.budget-cell {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  font-variant-numeric: tabular-nums;
}
.action-buttons {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 6px;
}
.action-buttons :deep(.el-button) {
  margin-left: 0;
}
.settings-hint {
  margin-bottom: 12px;
}
.settings-actions {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 12px;
}
.code-field {
  display: flex;
  gap: 8px;
  width: 100%;
}
.code-field .el-input {
  flex: 1;
}
</style>

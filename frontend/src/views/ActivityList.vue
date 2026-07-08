<script setup lang="ts">
import { ref, onMounted } from "vue";
import { ElMessage } from "element-plus";

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
  publishedAt: string | null;
  checkinStartAt: string | null;
  checkinEndAt: string | null;
  checkoutStartAt: string | null;
  checkoutEndAt: string | null;
  currentParticipants: number;
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

const activities = ref<Activity[]>([]);
const loading = ref(true);
const error = ref("");
const saving = ref(false);

const createDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const settingsDialogVisible = ref(false);
const signDialogVisible = ref(false);
const participationsDialogVisible = ref(false);

const currentActivity = ref<Activity | null>(null);
const participations = ref<ActivityParticipation[]>([]);
const participationLoading = ref(false);

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
  userId: 1,
  code: "",
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

function formatTime(value: string | null) {
  return value ? new Date(value).toLocaleString() : "-";
}

function emptyToNull(value: string) {
  return value ? value : null;
}

async function loadActivities() {
  loading.value = true;
  error.value = "";
  try {
    const res = await fetch("/api/activities");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    activities.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
}

function openCreate() {
  createForm.value = {
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

function openSettings(activity: Activity) {
  currentActivity.value = activity;
  settingsForm.value = {
    checkinCode: "",
    checkinStartAt: activity.checkinStartAt ?? "",
    checkinEndAt: activity.checkinEndAt ?? "",
    checkoutCode: "",
    checkoutStartAt: activity.checkoutStartAt ?? "",
    checkoutEndAt: activity.checkoutEndAt ?? "",
  };
  settingsDialogVisible.value = true;
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
    userId: 1,
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

onMounted(loadActivities);
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <h2>活动管理</h2>
      <el-button type="primary" @click="openCreate">创建活动</el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <el-table
      v-loading="loading"
      :data="activities"
      stripe
      border
      empty-text="暂无活动数据"
      class="activity-table"
    >
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="title" label="标题" min-width="180" show-overflow-tooltip />
      <el-table-column prop="clubName" label="主办社团" width="120" />
      <el-table-column label="时间" width="190">
        <template #default="{ row }">
          <span>{{ formatTime(row.startTime) }}</span>
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
      <el-table-column label="操作" width="330" align="center">
        <template #default="{ row }">
          <div class="action-buttons">
            <el-button
              v-if="row.status === 'pending_review'"
              size="small"
              type="primary"
              plain
              @click="openReview(row)"
              >审核</el-button
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

    <el-dialog v-model="reviewDialogVisible" title="活动审核" width="480px">
      <el-form label-position="top">
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

    <el-dialog v-model="settingsDialogVisible" title="签到签退设置" width="560px">
      <el-form label-position="top">
        <el-form-item label="签到码" required>
          <el-input v-model="settingsForm.checkinCode" placeholder="请输入签到码" />
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
          />
        </el-form-item>
        <el-form-item label="签退码" required>
          <el-input v-model="settingsForm.checkoutCode" placeholder="请输入签退码" />
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
.toolbar h2 {
  margin: 0;
}
.activity-table {
  width: 100%;
}
.quota-text {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
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
</style>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { readAuth } from "../authSession";

interface Venue {
  id: number;
  name: string;
  building?: string | null;
  roomNo?: string | null;
  capacity: number;
  status: string;
  managerUserId?: number | null;
  createdAt: string;
}

interface VenueReservation {
  id: number;
  venueName: string;
  startTime: string;
  endTime: string;
  status: string;
}

interface ApiErrorPayload {
  message?: string;
  detail?: string | null;
}

interface ReservationForm {
  venueId?: number;
  clubId?: number;
  activityId?: number;
  startTime: string;
  endTime: string;
  purpose: string;
}

const venues = ref<Venue[]>([]);
const loading = ref(false);
const error = ref("");
const dialogVisible = ref(false);
const submitting = ref(false);
const latestReservation = ref<VenueReservation | null>(null);
const reservationFormRef = ref<FormInstance>();
const auth = ref(readAuth());

const firstClubId = computed(() => {
  const roleClubIds = auth.value?.roles.flatMap((role) => role.clubIds ?? []) ?? [];
  return roleClubIds.find((clubId) => Number.isFinite(clubId));
});

const form = reactive<ReservationForm>({
  venueId: undefined,
  clubId: firstClubId.value,
  activityId: undefined,
  startTime: "",
  endTime: "",
  purpose: "",
});

const selectedVenue = computed(() => venues.value.find((venue) => venue.id === form.venueId));
const applicantName = computed(() => auth.value?.user.realName ?? "未登录");
const canSubmit = computed(() => Boolean(auth.value?.user.id));

const statusLabel: Record<string, string> = {
  available: "可预约",
  maintenance: "维护中",
  disabled: "停用",
};

const statusType: Record<string, "success" | "warning" | "info"> = {
  available: "success",
  maintenance: "warning",
  disabled: "info",
};

const rules: FormRules<ReservationForm> = {
  clubId: [{ required: true, message: "请输入申请社团 ID", trigger: "blur" }],
  startTime: [{ required: true, message: "请选择开始时间", trigger: "change" }],
  endTime: [
    { required: true, message: "请选择结束时间", trigger: "change" },
    {
      validator: (_rule, value, callback) => {
        if (!value || !form.startTime) {
          callback();
          return;
        }
        if (new Date(value).getTime() <= new Date(form.startTime).getTime()) {
          callback(new Error("结束时间必须晚于开始时间"));
          return;
        }
        callback();
      },
      trigger: "change",
    },
  ],
  purpose: [
    { required: true, message: "请输入预约用途", trigger: "blur" },
    { min: 2, max: 200, message: "用途需为 2-200 个字符", trigger: "blur" },
  ],
};

async function readErrorMessage(res: Response) {
  try {
    const payload = (await res.json()) as ApiErrorPayload;
    return payload.message || payload.detail || `请求失败：HTTP ${res.status}`;
  } catch {
    return `请求失败：HTTP ${res.status}`;
  }
}

async function loadVenues() {
  loading.value = true;
  error.value = "";
  try {
    const res = await fetch("/api/venues?status=available");
    if (!res.ok) throw new Error(await readErrorMessage(res));
    venues.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载场地失败";
  } finally {
    loading.value = false;
  }
}

function openReservation(venue: Venue) {
  latestReservation.value = null;
  form.venueId = venue.id;
  form.clubId = firstClubId.value;
  form.activityId = undefined;
  form.startTime = "";
  form.endTime = "";
  form.purpose = "";
  dialogVisible.value = true;
}

async function submitReservation() {
  if (!reservationFormRef.value) return;
  await reservationFormRef.value.validate();

  const applicantUserId = auth.value?.user.id;
  if (!applicantUserId || !form.venueId || !form.clubId) {
    ElMessage.error("缺少申请人、场地或社团信息，无法提交预约。");
    return;
  }

  submitting.value = true;
  try {
    const res = await fetch("/api/venue-reservations", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        venueId: form.venueId,
        clubId: form.clubId,
        activityId: form.activityId || null,
        applicantUserId,
        startTime: new Date(form.startTime).toISOString(),
        endTime: new Date(form.endTime).toISOString(),
        purpose: form.purpose.trim(),
      }),
    });

    if (!res.ok) throw new Error(await readErrorMessage(res));
    latestReservation.value = await res.json();
    dialogVisible.value = false;
    ElMessage.success("预约申请已提交，等待管理员审批。");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "提交预约失败");
  } finally {
    submitting.value = false;
  }
}

function formatLocation(venue: Venue) {
  return [venue.building, venue.roomNo].filter(Boolean).join(" / ") || "未填写";
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString();
}

onMounted(loadVenues);
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <div>
        <h2>场地预约</h2>
        <p class="subtitle">选择可用场地，提交预约申请后等待管理员审批。</p>
      </div>
      <el-button :loading="loading" @click="loadVenues">刷新场地</el-button>
    </div>

    <el-alert
      v-if="!canSubmit"
      title="当前未读取到登录用户，请先完成登录后再提交预约。"
      type="warning"
      show-icon
      class="notice"
    />

    <el-alert
      v-if="latestReservation"
      :title="`已提交预约 #${latestReservation.id}：${latestReservation.venueName}`"
      type="success"
      show-icon
      closable
      class="notice"
      @close="latestReservation = null"
    >
      <template #default>
        {{ formatDateTime(latestReservation.startTime) }} 至
        {{ formatDateTime(latestReservation.endTime) }}，状态：待审批
      </template>
    </el-alert>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <el-table v-loading="loading" :data="venues" stripe empty-text="暂无可预约场地">
      <el-table-column prop="id" label="ID" width="70" />
      <el-table-column prop="name" label="场地名称" min-width="160" />
      <el-table-column label="位置" min-width="180">
        <template #default="{ row }">
          {{ formatLocation(row) }}
        </template>
      </el-table-column>
      <el-table-column prop="capacity" label="容量" width="90" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="statusType[row.status] || 'info'" size="small">
            {{ statusLabel[row.status] || row.status }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="managerUserId" label="管理员 ID" width="110" />
      <el-table-column label="操作" width="120" fixed="right">
        <template #default="{ row }">
          <el-button
            type="primary"
            size="small"
            :disabled="row.status !== 'available' || !canSubmit"
            @click="openReservation(row)"
          >
            申请预约
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" title="提交场地预约申请" width="520px">
      <el-form
        ref="reservationFormRef"
        :model="form"
        :rules="rules"
        label-position="top"
        status-icon
      >
        <el-form-item label="场地">
          <el-input :model-value="selectedVenue?.name ?? ''" disabled />
        </el-form-item>
        <el-form-item label="申请人">
          <el-input :model-value="applicantName" disabled />
        </el-form-item>
        <el-form-item label="申请社团 ID" prop="clubId">
          <el-input-number v-model="form.clubId" :min="1" :controls="false" />
          <span v-if="firstClubId" class="field-hint">已默认使用当前角色关联社团。</span>
        </el-form-item>
        <el-form-item label="关联活动 ID（可选）" prop="activityId">
          <el-input-number v-model="form.activityId" :min="1" :controls="false" />
        </el-form-item>
        <el-form-item label="开始时间" prop="startTime">
          <el-date-picker
            v-model="form.startTime"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            format="YYYY-MM-DD HH:mm"
            placeholder="选择开始时间"
            class="full-width"
          />
        </el-form-item>
        <el-form-item label="结束时间" prop="endTime">
          <el-date-picker
            v-model="form.endTime"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            format="YYYY-MM-DD HH:mm"
            placeholder="选择结束时间"
            class="full-width"
          />
        </el-form-item>
        <el-form-item label="用途" prop="purpose">
          <el-input
            v-model="form.purpose"
            type="textarea"
            maxlength="200"
            show-word-limit
            placeholder="例如：社团招新宣讲、项目例会、活动彩排"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitReservation"
          >提交申请</el-button
        >
      </template>
    </el-dialog>
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
}
.notice {
  margin-bottom: 12px;
}
.full-width {
  width: 100%;
}
.field-hint {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
</style>

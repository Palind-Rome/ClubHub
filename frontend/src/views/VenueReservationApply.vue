<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { readAuth } from "../authSession";
import {
  createVenueSearchIndex,
  formatVenueLocation,
  matchesVenueSearch,
  type VenueSearchIndex,
} from "../venueSearch";
import VenueReservationReview from "./VenueReservationReview.vue";

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
  venueId: number;
  venueName: string;
  clubId?: number;
  startTime: string;
  endTime: string;
  status: string;
}

interface VenueRow {
  venue: Venue;
  searchIndex: VenueSearchIndex;
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
const reviewDialogVisible = ref(false);
const approvedSlotsVisible = ref(false);
const submitting = ref(false);
const latestReservation = ref<VenueReservation | null>(null);
const approvedReservations = ref<VenueReservation[]>([]);
const approvedSlotsVenue = ref<Venue | null>(null);
const reservationFormRef = ref<FormInstance>();
const auth = ref(readAuth());
const venueSearch = ref("");
const approvedSlotSearch = ref("");

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
const selectedVenueApprovedSlots = computed(() => {
  if (!approvedSlotsVenue.value) return [];
  return approvedReservationsForVenue(approvedSlotsVenue.value.id);
});
const filteredSelectedVenueApprovedSlots = computed(() => {
  return selectedVenueApprovedSlots.value.filter((slot) =>
    matchesVenueSearch(createApprovedSlotIndex(slot), approvedSlotSearch.value),
  );
});
const approvedSlotEmptyText = computed(() => {
  return approvedSlotSearch.value.trim() ? "未找到匹配时段" : "该场地暂无已预约时段";
});
const approvedSlotSearchSummary = computed(() => {
  if (!approvedSlotSearch.value.trim()) {
    return `共 ${selectedVenueApprovedSlots.value.length} 个已预约时段`;
  }
  return `已匹配 ${filteredSelectedVenueApprovedSlots.value.length} / ${selectedVenueApprovedSlots.value.length} 个时段`;
});
const venueRows = computed<VenueRow[]>(() => {
  return venues.value.map((venue) => ({
    venue,
    searchIndex: createVenueIndex(venue),
  }));
});
const filteredVenues = computed(() => {
  return venueRows.value
    .filter((row) => matchesVenueSearch(row.searchIndex, venueSearch.value))
    .map((row) => row.venue);
});
const venueTableEmptyText = computed(() => {
  return venueSearch.value.trim() ? "未找到匹配场地" : "暂无可预约场地";
});
const venueSearchSummary = computed(() => {
  if (!venueSearch.value.trim()) return `共 ${venues.value.length} 个可预约场地`;
  return `已匹配 ${filteredVenues.value.length} / ${venues.value.length} 个场地`;
});
const applicantName = computed(() => auth.value?.user.realName ?? "未登录");
const canSubmit = computed(() => Boolean(auth.value?.user.id));
const canReview = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("venue:review") || permissions.includes("*");
});

const statusLabel: Record<string, string> = {
  available: "可预约",
  occupied: "占用中",
  maintenance: "维护中",
  disabled: "停用",
};

const statusType: Record<string, "success" | "warning" | "info" | "danger"> = {
  available: "success",
  occupied: "danger",
  maintenance: "warning",
  disabled: "info",
};

const rules: FormRules<ReservationForm> = {
  clubId: [{ required: true, message: "请输入申请社团 ID", trigger: "blur" }],
  startTime: [
    { required: true, message: "请选择开始时间", trigger: "change" },
    {
      validator: (_rule, value, callback) => {
        if (isBeforeNow(value)) {
          callback(new Error("开始时间不能早于当前时间"));
          return;
        }
        if (
          form.venueId &&
          form.endTime &&
          hasApprovedConflict(form.venueId, value, form.endTime)
        ) {
          callback(new Error("申请时段不能覆盖当前已被预约时段"));
          return;
        }
        callback();
      },
      trigger: "change",
    },
  ],
  endTime: [
    { required: true, message: "请选择结束时间", trigger: "change" },
    {
      validator: (_rule, value, callback) => {
        if (isBeforeNow(value)) {
          callback(new Error("结束时间不能早于当前时间"));
          return;
        }
        if (!value || !form.startTime) {
          callback();
          return;
        }
        if (new Date(value).getTime() <= new Date(form.startTime).getTime()) {
          callback(new Error("结束时间必须晚于开始时间"));
          return;
        }
        if (form.venueId && hasApprovedConflict(form.venueId, form.startTime, value)) {
          callback(new Error("申请时段不能覆盖当前已被预约时段"));
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

function isBeforeNow(value?: string) {
  return value ? new Date(value).getTime() < Date.now() : false;
}

function disablePastDate(date: Date) {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return date.getTime() < today.getTime();
}

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

async function loadApprovedReservations() {
  try {
    const res = await fetch("/api/venue-reservations?status=approved");
    if (!res.ok) throw new Error(await readErrorMessage(res));
    const reservations = (await res.json()) as VenueReservation[];
    approvedReservations.value = reservations.filter(isActiveApprovedReservation);
  } catch {
    approvedReservations.value = [];
  }
}

async function refreshVenueData() {
  await Promise.all([loadVenues(), loadApprovedReservations()]);
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

function openApprovedSlots(venue: Venue) {
  approvedSlotsVenue.value = venue;
  approvedSlotSearch.value = "";
  approvedSlotsVisible.value = true;
}

async function submitReservation() {
  if (!reservationFormRef.value) return;
  const valid = await reservationFormRef.value.validate().catch(() => false);
  if (!valid) return;

  const applicantUserId = auth.value?.user.id;
  if (!applicantUserId || !form.venueId || !form.clubId) {
    ElMessage.error("缺少申请人、场地或社团信息，无法提交预约。");
    return;
  }

  submitting.value = true;
  try {
    await loadApprovedReservations();
    if (hasApprovedConflict(form.venueId, form.startTime, form.endTime)) {
      ElMessage.error("申请时段不能覆盖当前已被预约时段。");
      submitting.value = false;
      return;
    }

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
  return formatVenueLocation(venue.building, venue.roomNo) || "未填写";
}

function createVenueIndex(venue: Venue) {
  const location = formatVenueLocation(venue.building, venue.roomNo);
  return createVenueSearchIndex({
    ids: [venue.id],
    texts: [venue.name, venue.building, venue.roomNo, location],
  });
}

function createApprovedSlotIndex(slot: VenueReservation) {
  const venue = approvedSlotsVenue.value;
  const location = venue ? formatVenueLocation(venue.building, venue.roomNo) : "";

  return createVenueSearchIndex({
    ids: [slot.id, slot.venueId],
    texts: [slot.venueName, venue?.name, venue?.building, venue?.roomNo, location],
  });
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString();
}

function formatDateOnly(value: string) {
  return new Date(value).toLocaleDateString();
}

function formatTimeOnly(value: string) {
  return new Date(value).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatSlotRange(slot: VenueReservation) {
  return `${formatTimeOnly(slot.startTime)} - ${formatTimeOnly(slot.endTime)}`;
}

function formatSlotDuration(slot: VenueReservation) {
  const minutes = Math.max(
    0,
    Math.round((new Date(slot.endTime).getTime() - new Date(slot.startTime).getTime()) / 60000),
  );
  if (minutes < 60) return `${minutes} 分钟`;

  const hours = Math.floor(minutes / 60);
  const restMinutes = minutes % 60;
  return restMinutes === 0 ? `${hours} 小时` : `${hours} 小时 ${restMinutes} 分钟`;
}

function openReviewDialog() {
  reviewDialogVisible.value = true;
}

function findVenueOccupancy(venue: Venue) {
  return approvedReservations.value
    .filter(
      (reservation) =>
        reservation.venueId === venue.id && isCurrentApprovedReservation(reservation),
    )
    .sort((a, b) => new Date(a.endTime).getTime() - new Date(b.endTime).getTime())[0];
}

function approvedReservationsForVenue(venueId: number) {
  return approvedReservations.value
    .filter(
      (reservation) => reservation.venueId === venueId && isActiveApprovedReservation(reservation),
    )
    .slice()
    .sort((a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());
}

function isActiveApprovedReservation(reservation: VenueReservation) {
  return reservation.status === "approved" && new Date(reservation.endTime).getTime() > Date.now();
}

function isCurrentApprovedReservation(reservation: VenueReservation) {
  const now = Date.now();
  const startTime = new Date(reservation.startTime).getTime();
  const endTime = new Date(reservation.endTime).getTime();
  return reservation.status === "approved" && startTime <= now && endTime > now;
}

function hasApprovedConflict(venueId: number, startValue?: string, endValue?: string) {
  if (!startValue || !endValue) return false;
  const startTime = new Date(startValue).getTime();
  const endTime = new Date(endValue).getTime();
  if (!Number.isFinite(startTime) || !Number.isFinite(endTime)) return false;

  return approvedReservationsForVenue(venueId).some((reservation) => {
    const approvedStart = new Date(reservation.startTime).getTime();
    const approvedEnd = new Date(reservation.endTime).getTime();
    return approvedStart < endTime && approvedEnd > startTime;
  });
}

function venueStatus(venue: Venue) {
  return findVenueOccupancy(venue) ? "occupied" : venue.status;
}

function venueStatusLabel(venue: Venue) {
  const status = venueStatus(venue);
  return statusLabel[status] || status;
}

function venueStatusType(venue: Venue) {
  return statusType[venueStatus(venue)] || "info";
}

onMounted(refreshVenueData);
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <div>
        <h2>场地预约</h2>
        <p class="subtitle">选择可用场地，提交预约申请后等待管理员审批。</p>
      </div>
      <div class="toolbar-actions">
        <el-button v-if="canReview" type="primary" plain @click="openReviewDialog">
          进入预约审批
        </el-button>
        <el-button :loading="loading" @click="refreshVenueData">刷新场地</el-button>
      </div>
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

    <div class="search-row">
      <el-input
        v-model="venueSearch"
        clearable
        placeholder="搜索 ID、场地名称或位置"
        class="search-input"
      />
      <span class="search-summary">{{ venueSearchSummary }}</span>
    </div>

    <el-table v-loading="loading" :data="filteredVenues" stripe :empty-text="venueTableEmptyText">
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
          <el-tag :type="venueStatusType(row)" size="small">
            {{ venueStatusLabel(row) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="managerUserId" label="管理员 ID" width="110" />
      <el-table-column label="操作" width="210" fixed="right">
        <template #default="{ row }">
          <div class="row-actions">
            <el-button size="small" @click="openApprovedSlots(row)">
              已预约时段
              <span class="slot-count">{{ approvedReservationsForVenue(row.id).length }}</span>
            </el-button>
            <el-button
              type="primary"
              size="small"
              :disabled="row.status !== 'available' || !canSubmit"
              @click="openReservation(row)"
            >
              申请预约
            </el-button>
          </div>
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
            :disabled-date="disablePastDate"
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
            :disabled-date="disablePastDate"
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

    <el-dialog
      v-model="approvedSlotsVisible"
      :title="`${approvedSlotsVenue?.name ?? '场地'}已预约时段`"
      width="760px"
    >
      <el-alert
        title="新申请的开始和结束时间不能覆盖以下任何已预约时段。"
        type="info"
        show-icon
        class="notice"
      />
      <div class="search-row">
        <el-input
          v-model="approvedSlotSearch"
          clearable
          placeholder="搜索预约 ID、场地名称或位置"
          class="search-input"
        />
        <span class="search-summary">{{ approvedSlotSearchSummary }}</span>
      </div>
      <el-empty
        v-if="filteredSelectedVenueApprovedSlots.length === 0"
        :description="approvedSlotEmptyText"
      />
      <div v-else class="slot-timeline">
        <div v-for="slot in filteredSelectedVenueApprovedSlots" :key="slot.id" class="slot-card">
          <div class="slot-date">{{ formatDateOnly(slot.startTime) }}</div>
          <div class="slot-bar">
            <span class="slot-dot"></span>
            <div>
              <div class="slot-range">{{ formatSlotRange(slot) }}</div>
              <div class="muted">持续 {{ formatSlotDuration(slot) }}</div>
            </div>
          </div>
        </div>
      </div>
    </el-dialog>

    <VenueReservationReview v-model="reviewDialogVisible" @reviewed="loadApprovedReservations" />
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
.toolbar-actions {
  display: flex;
  gap: 8px;
}
.subtitle {
  margin: 6px 0 0;
  color: var(--el-text-color-secondary);
}
.notice {
  margin-bottom: 12px;
}
.search-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}
.search-input {
  max-width: 360px;
}
.search-summary {
  flex: 1;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.full-width {
  width: 100%;
}
.field-hint {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.row-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.slot-count {
  margin-left: 4px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.slot-timeline {
  display: grid;
  gap: 10px;
}
.slot-card {
  display: grid;
  grid-template-columns: 120px 1fr;
  gap: 14px;
  align-items: stretch;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: #fff;
}
.slot-date {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 58px;
  border-radius: 6px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-regular);
  font-weight: 600;
}
.slot-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  border-left: 3px solid var(--el-color-primary-light-5);
  padding-left: 14px;
}
.slot-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--el-color-primary);
  box-shadow: 0 0 0 4px var(--el-color-primary-light-9);
}
.slot-range {
  font-size: 18px;
  font-weight: 700;
  color: var(--el-text-color-primary);
}
.muted {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
@media (max-width: 720px) {
  .search-row {
    align-items: stretch;
    flex-direction: column;
  }

  .search-input {
    max-width: none;
  }

  .slot-card {
    grid-template-columns: 1fr;
  }

  .slot-date {
    justify-content: flex-start;
    min-height: auto;
    padding: 8px 10px;
  }
}
</style>

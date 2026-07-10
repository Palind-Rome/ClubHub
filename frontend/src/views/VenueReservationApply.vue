<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { readAuth } from "../authSession";
import { requestJson } from "../composables/useApiRequest";
import {
  beijingCalendarDateTimestamp,
  beijingDateTimeTimestamp,
  beijingDateTimeToUtcIso,
  beijingTodayStartTimestamp,
  formatVenueReservationDate,
  formatVenueReservationDateTime,
  venueReservationTimestamp,
} from "../beijingTime";
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
  maintenanceUntil?: string | null;
}

interface VenueReservation {
  id: number;
  venueId: number;
  venueName: string;
  clubId?: number;
  clubName?: string;
  activityTitle?: string | null;
  applicantUserId: number;
  applicantName?: string | null;
  startTime: string;
  endTime: string;
  purpose?: string;
  status: string;
  reviewComment?: string | null;
  createdAt?: string;
}

interface VenueOccupiedSlotPayload {
  reservationId: number;
  venueId: number;
  venueName: string;
  startTime: string;
  endTime: string;
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

interface ClubOption {
  id: number;
  name: string;
}

interface ActivityOption {
  id: number;
  title: string;
  clubId: number;
  clubName: string;
  startTime?: string | null;
  status?: string | null;
}

const venues = ref<Venue[]>([]);
const clubs = ref<ClubOption[]>([]);
const activities = ref<ActivityOption[]>([]);
const loading = ref(false);
const error = ref("");
const dialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const approvedSlotsVisible = ref(false);
const myReservationsVisible = ref(false);
const showExpiredMyReservations = ref(false);
const submitting = ref(false);
const myReservationsLoading = ref(false);
const deletingReservationId = ref<number>();
const latestReservation = ref<VenueReservation | null>(null);
const approvedReservations = ref<VenueReservation[]>([]);
const myReservations = ref<VenueReservation[]>([]);
const approvedSlotsVenue = ref<Venue | null>(null);
const reservationFormRef = ref<FormInstance>();
const auth = ref(readAuth());
const venueSearch = ref("");
const approvedSlotSearch = ref("");

const reserveClubIds = computed(() => {
  const roles = auth.value?.roles ?? [];
  return [
    ...new Set(
      roles
        .filter(
          (role) => role.permissions.includes("venue:reserve") || role.permissions.includes("*"),
        )
        .flatMap((role) => role.clubIds ?? [])
        .filter((clubId) => Number.isFinite(clubId)),
    ),
  ].sort((a, b) => a - b);
});
const firstClubId = computed(() => reserveClubIds.value[0]);

const form = reactive<ReservationForm>({
  venueId: undefined,
  clubId: firstClubId.value,
  activityId: undefined,
  startTime: "",
  endTime: "",
  purpose: "",
});

const selectedVenue = computed(() => venues.value.find((venue) => venue.id === form.venueId));
const selectableActivities = computed(() =>
  activities.value.filter((activity) => activity.clubId === form.clubId),
);
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
  return venueSearch.value.trim() ? "未找到匹配场地" : "暂无场地";
});
const venueSearchSummary = computed(() => {
  if (!venueSearch.value.trim()) return `共 ${venues.value.length} 个场地`;
  return `已匹配 ${filteredVenues.value.length} / ${venues.value.length} 个场地`;
});
const applicantName = computed(() => auth.value?.user.realName ?? "未登录");
const canSubmit = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("venue:reserve") || permissions.includes("*");
});
const canReview = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("venue:review") || permissions.includes("*");
});
const activeMyReservations = computed(() => {
  return myReservations.value
    .filter((reservation) => !isExpiredReservation(reservation))
    .slice()
    .sort(compareActiveReservationTime);
});
const expiredMyReservations = computed(() => {
  return myReservations.value
    .filter(isExpiredReservation)
    .slice()
    .sort(compareExpiredReservationTime);
});
const displayedMyReservations = computed(() =>
  showExpiredMyReservations.value ? expiredMyReservations.value : activeMyReservations.value,
);
const myReservationEmptyText = computed(() =>
  showExpiredMyReservations.value ? "暂无过期预约记录" : "暂无当前预约记录",
);

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

const reservationStatusLabel: Record<string, string> = {
  pending: "待审批",
  approved: "已通过",
  rejected: "已拒绝",
  cancelled: "已取消",
};

const reservationStatusType: Record<string, "success" | "warning" | "info" | "danger"> = {
  pending: "warning",
  approved: "success",
  rejected: "danger",
  cancelled: "info",
};

const rules: FormRules<ReservationForm> = {
  clubId: [{ required: true, message: "请选择申请社团", trigger: "change" }],
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
        if (beijingDateTimeTimestamp(value) <= beijingDateTimeTimestamp(form.startTime)) {
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
  const timestamp = beijingDateTimeTimestamp(value);
  return Number.isFinite(timestamp) && timestamp < Date.now();
}

function disablePastDate(date: Date) {
  return beijingCalendarDateTimestamp(date) < beijingTodayStartTimestamp();
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
    const res = await fetch("/api/venues");
    if (!res.ok) throw new Error(await readErrorMessage(res));
    venues.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载场地失败";
  } finally {
    loading.value = false;
  }
}

async function loadReservationOptions() {
  const userId = auth.value?.user.id;
  if (!userId) {
    clubs.value = [];
    activities.value = [];
    return;
  }

  try {
    const [clubRes, activityRes] = await Promise.all([
      fetch(`/api/clubs?viewerUserId=${userId}`),
      fetch(`/api/activities?currentUserId=${userId}`),
    ]);
    if (!clubRes.ok) throw new Error(await readErrorMessage(clubRes));
    if (!activityRes.ok) throw new Error(await readErrorMessage(activityRes));
    clubs.value = await clubRes.json();
    activities.value = await activityRes.json();
  } catch (e) {
    clubs.value = [];
    activities.value = [];
    ElMessage.error(e instanceof Error ? e.message : "预约选项加载失败");
  }
}

async function loadApprovedReservations() {
  try {
    const slots = await requestJson<VenueOccupiedSlotPayload[]>(
      "/api/venue-reservations/occupied-slots",
    );
    approvedReservations.value = slots.map((slot) => ({
      id: slot.reservationId,
      venueId: slot.venueId,
      venueName: slot.venueName,
      applicantUserId: 0,
      startTime: slot.startTime,
      endTime: slot.endTime,
      status: "approved",
    }));
  } catch {
    approvedReservations.value = [];
  }
}

async function loadMyReservations() {
  const applicantUserId = auth.value?.user.id;
  if (!applicantUserId) {
    myReservations.value = [];
    return;
  }

  myReservationsLoading.value = true;
  try {
    myReservations.value = await requestJson<VenueReservation[]>(
      `/api/venue-reservations?applicantUserId=${applicantUserId}`,
    );
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "加载我的预约失败");
    myReservations.value = [];
  } finally {
    myReservationsLoading.value = false;
  }
}

async function refreshVenueData() {
  await Promise.all([loadVenues(), loadApprovedReservations(), loadReservationOptions()]);
}

function openReservation(venue: Venue) {
  latestReservation.value = null;
  form.venueId = venue.id;
  form.clubId = clubs.value[0]?.id ?? firstClubId.value;
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

async function openMyReservations() {
  if (!auth.value?.user.id) {
    ElMessage.error("当前未读取到登录用户，无法查看我的预约。");
    return;
  }

  showExpiredMyReservations.value = false;
  myReservationsVisible.value = true;
  await loadMyReservations();
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

    latestReservation.value = await requestJson<VenueReservation>("/api/venue-reservations", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        venueId: form.venueId,
        clubId: form.clubId,
        activityId: form.activityId || null,
        startTime: beijingDateTimeToUtcIso(form.startTime),
        endTime: beijingDateTimeToUtcIso(form.endTime),
        purpose: form.purpose.trim(),
      }),
    });
    if (myReservationsVisible.value) await loadMyReservations();
    dialogVisible.value = false;
    ElMessage.success("预约申请已提交，等待管理员审批。");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "提交预约失败");
  } finally {
    submitting.value = false;
  }
}

async function deleteReservation(reservation: VenueReservation) {
  const operatorUserId = auth.value?.user.id;
  if (!operatorUserId) {
    ElMessage.error("当前未读取到登录用户，无法删除预约。");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定删除「${reservation.venueName}」在 ${formatDateTime(reservation.startTime)} 的预约吗？删除后不可恢复。`,
      "确认删除预约",
      {
        confirmButtonText: "确定删除",
        cancelButtonText: "取消",
        type: "warning",
        confirmButtonClass: "el-button--danger",
      },
    );
  } catch {
    return;
  }

  deletingReservationId.value = reservation.id;
  try {
    await requestJson<void>(`/api/venue-reservations/${reservation.id}`, {
      method: "DELETE",
    });

    if (latestReservation.value?.id === reservation.id) {
      latestReservation.value = null;
    }
    approvedReservations.value = approvedReservations.value.filter(
      (item) => item.id !== reservation.id,
    );
    myReservations.value = myReservations.value.filter((item) => item.id !== reservation.id);
    ElMessage.success("预约已删除。");
    await loadApprovedReservations();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "删除预约失败");
  } finally {
    deletingReservationId.value = undefined;
  }
}

function formatLocation(venue: Venue) {
  return formatVenueLocation(venue.building, venue.roomNo) || "未填写";
}

function createVenueIndex(venue: Venue) {
  const location = formatVenueLocation(venue.building, venue.roomNo);
  return createVenueSearchIndex({
    texts: [venue.name, venue.building, venue.roomNo, location],
  });
}

function createApprovedSlotIndex(slot: VenueReservation) {
  const venue = approvedSlotsVenue.value;
  const location = venue ? formatVenueLocation(venue.building, venue.roomNo) : "";

  return createVenueSearchIndex({
    texts: [slot.venueName, venue?.name, venue?.building, venue?.roomNo, location],
  });
}

function activityOptionLabel(activity: ActivityOption) {
  const time = activity.startTime ? ` · ${formatDateTime(activity.startTime)}` : "";
  return `${activity.title}（${activity.clubName || "未知社团"}${time}）`;
}

function formatDateTime(value: string) {
  return formatVenueReservationDateTime(value);
}

function formatDateOnly(value: string) {
  return formatVenueReservationDate(value);
}

function formatSlotRange(slot: VenueReservation) {
  return `起始 ${formatDateTime(slot.startTime)} · 终止 ${formatDateTime(slot.endTime)}`;
}

function formatSlotDuration(slot: VenueReservation) {
  const minutes = Math.max(
    0,
    Math.round(
      (venueReservationTimestamp(slot.endTime) - venueReservationTimestamp(slot.startTime)) / 60000,
    ),
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
    .sort((a, b) => venueReservationTimestamp(a.endTime) - venueReservationTimestamp(b.endTime))[0];
}

function approvedReservationsForVenue(venueId: number) {
  return approvedReservations.value
    .filter(
      (reservation) => reservation.venueId === venueId && isActiveApprovedReservation(reservation),
    )
    .slice()
    .sort(
      (a, b) => venueReservationTimestamp(a.startTime) - venueReservationTimestamp(b.startTime),
    );
}

function isActiveApprovedReservation(reservation: VenueReservation) {
  return (
    reservation.status === "approved" && venueReservationTimestamp(reservation.endTime) > Date.now()
  );
}

function isCurrentApprovedReservation(reservation: VenueReservation) {
  const now = Date.now();
  const startTime = venueReservationTimestamp(reservation.startTime);
  const endTime = venueReservationTimestamp(reservation.endTime);
  return reservation.status === "approved" && startTime <= now && endTime > now;
}

function hasApprovedConflict(venueId: number, startValue?: string, endValue?: string) {
  if (!startValue || !endValue) return false;
  const startTime = beijingDateTimeTimestamp(startValue);
  const endTime = beijingDateTimeTimestamp(endValue);
  if (!Number.isFinite(startTime) || !Number.isFinite(endTime)) return false;

  return approvedReservationsForVenue(venueId).some((reservation) => {
    const approvedStart = venueReservationTimestamp(reservation.startTime);
    const approvedEnd = venueReservationTimestamp(reservation.endTime);
    return approvedStart < endTime && approvedEnd > startTime;
  });
}

function venueStatus(venue: Venue) {
  if (venue.status !== "available") return venue.status;
  return findVenueOccupancy(venue) ? "occupied" : venue.status;
}

function venueStatusLabel(venue: Venue) {
  const status = venueStatus(venue);
  return statusLabel[status] || status;
}

function venueStatusType(venue: Venue) {
  return statusType[venueStatus(venue)] || "info";
}

function maintenanceUntilText(venue: Venue) {
  if (venue.status !== "maintenance") return "";
  return venue.maintenanceUntil
    ? `维护至 ${formatDateTime(venue.maintenanceUntil)}`
    : "维护至 未知";
}

function canReserveVenue(venue: Venue) {
  return venue.status === "available" && canSubmit.value;
}

function reserveDisabledReason(venue: Venue) {
  if (!canSubmit.value) return "仅社团干部或负责人可以提交场地预约";
  if (venue.status === "maintenance") return maintenanceUntilText(venue) || "场地维护中";
  if (venue.status === "disabled") return "场地已停用";
  return "";
}

function canDeleteReservation(reservation: VenueReservation) {
  return canOperateReservation(reservation) && !isInProgressApprovedReservation(reservation);
}

function canOperateReservation(reservation: VenueReservation) {
  const userId = auth.value?.user.id;
  return canReview.value || reservation.applicantUserId === userId;
}

function deleteReservationDisabledReason(reservation: VenueReservation) {
  return isInProgressApprovedReservation(reservation)
    ? "已开始且未结束的已通过预约暂时无法删除"
    : "";
}

function isInProgressApprovedReservation(reservation: VenueReservation) {
  if (reservation.status !== "approved") return false;

  const now = Date.now();
  const startTime = venueReservationTimestamp(reservation.startTime);
  const endTime = venueReservationTimestamp(reservation.endTime);
  return startTime <= now && endTime > now;
}

function isExpiredReservation(reservation: VenueReservation) {
  return venueReservationTimestamp(reservation.endTime) < Date.now();
}

function compareActiveReservationTime(a: VenueReservation, b: VenueReservation) {
  return activeReservationDistance(a) - activeReservationDistance(b) || a.id - b.id;
}

function compareExpiredReservationTime(a: VenueReservation, b: VenueReservation) {
  return venueReservationTimestamp(b.endTime) - venueReservationTimestamp(a.endTime) || b.id - a.id;
}

function activeReservationDistance(reservation: VenueReservation) {
  const now = Date.now();
  const startTime = venueReservationTimestamp(reservation.startTime);
  return startTime <= now ? 0 : startTime - now;
}

function reservationStatusName(status: string) {
  return reservationStatusLabel[status] || status;
}

function reservationStatusTagType(status: string) {
  return reservationStatusType[status] || "info";
}

function myReservationRowClass({ row }: { row: VenueReservation }) {
  return isExpiredReservation(row) ? "expired-reservation-row" : "";
}

watch(
  () => form.clubId,
  () => {
    if (!selectableActivities.value.some((activity) => activity.id === form.activityId)) {
      form.activityId = undefined;
    }
  },
);

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
        <el-button :disabled="!canSubmit" @click="openMyReservations">我的预约</el-button>
        <el-button v-if="canReview" type="primary" plain @click="openReviewDialog">
          进入预约审批
        </el-button>
        <el-button :loading="loading" @click="refreshVenueData">刷新场地</el-button>
      </div>
    </div>

    <el-alert
      v-if="!canSubmit"
      title="当前账号没有场地预约申请权限，仅社团干部或负责人可以为本社团提交申请。"
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
        <div class="alert-content">
          <span>
            {{ formatDateTime(latestReservation.startTime) }} 至
            {{ formatDateTime(latestReservation.endTime) }}，状态：待审批
          </span>
          <el-tooltip
            v-if="canOperateReservation(latestReservation)"
            :disabled="canDeleteReservation(latestReservation)"
            :content="deleteReservationDisabledReason(latestReservation)"
            placement="top"
          >
            <span>
              <el-button
                type="danger"
                size="small"
                plain
                :disabled="!canDeleteReservation(latestReservation)"
                :loading="deletingReservationId === latestReservation.id"
                @click="deleteReservation(latestReservation)"
              >
                删除申请
              </el-button>
            </span>
          </el-tooltip>
        </div>
      </template>
    </el-alert>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <div class="search-row">
      <el-input
        v-model="venueSearch"
        clearable
        placeholder="搜索场地名称或位置"
        class="search-input"
      />
      <span class="search-summary">{{ venueSearchSummary }}</span>
    </div>

    <el-table v-loading="loading" :data="filteredVenues" stripe :empty-text="venueTableEmptyText">
      <el-table-column prop="name" label="场地名称" min-width="160" />
      <el-table-column label="位置" min-width="180">
        <template #default="{ row }">
          {{ formatLocation(row) }}
        </template>
      </el-table-column>
      <el-table-column prop="capacity" label="容量" width="90" />
      <el-table-column label="状态" width="150">
        <template #default="{ row }">
          <el-tag :type="venueStatusType(row)" size="small">
            {{ venueStatusLabel(row) }}
          </el-tag>
          <div v-if="maintenanceUntilText(row)" class="muted status-note">
            {{ maintenanceUntilText(row) }}
          </div>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="210" fixed="right">
        <template #default="{ row }">
          <div class="row-actions">
            <el-button size="small" @click="openApprovedSlots(row)">
              已预约时段
              <span class="slot-count">{{ approvedReservationsForVenue(row.id).length }}</span>
            </el-button>
            <el-tooltip
              :disabled="canReserveVenue(row)"
              :content="reserveDisabledReason(row)"
              placement="top"
            >
              <span>
                <el-button
                  type="primary"
                  size="small"
                  :disabled="!canReserveVenue(row)"
                  @click="openReservation(row)"
                >
                  申请预约
                </el-button>
              </span>
            </el-tooltip>
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
        <el-form-item label="申请社团" prop="clubId">
          <el-select
            v-if="reserveClubIds.length > 0"
            v-model="form.clubId"
            filterable
            placeholder="选择有预约权限的社团"
            class="full-width"
          >
            <el-option
              v-for="club in clubs.filter((c) => reserveClubIds.includes(c.id))"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
          <span v-if="firstClubId" class="field-hint">仅显示当前角色有权预约的社团。</span>
        </el-form-item>
        <el-form-item label="关联活动（可选）" prop="activityId">
          <el-select
            v-model="form.activityId"
            clearable
            filterable
            placeholder="请选择本社团活动"
            class="full-width"
          >
            <el-option
              v-for="activity in selectableActivities"
              :key="activity.id"
              :label="activityOptionLabel(activity)"
              :value="activity.id"
            />
          </el-select>
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
          placeholder="搜索场地名称或位置"
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
            <div class="slot-content">
              <div class="slot-range">{{ formatSlotRange(slot) }}</div>
              <div class="muted">持续 {{ formatSlotDuration(slot) }}</div>
            </div>
          </div>
          <el-tooltip
            v-if="canOperateReservation(slot)"
            :disabled="canDeleteReservation(slot)"
            :content="deleteReservationDisabledReason(slot)"
            placement="top"
          >
            <span>
              <el-button
                type="danger"
                size="small"
                text
                :disabled="!canDeleteReservation(slot)"
                :loading="deletingReservationId === slot.id"
                @click="deleteReservation(slot)"
              >
                删除
              </el-button>
            </span>
          </el-tooltip>
        </div>
      </div>
    </el-dialog>

    <el-dialog v-model="myReservationsVisible" title="我的预约" width="920px">
      <div class="my-reservation-toolbar">
        <div>
          <div class="panel-title">
            {{ showExpiredMyReservations ? "过期预约记录" : "当前预约记录" }}
          </div>
          <div class="muted">
            {{
              showExpiredMyReservations
                ? `共 ${expiredMyReservations.length} 条过期记录`
                : `共 ${activeMyReservations.length} 条当前记录`
            }}
          </div>
        </div>
        <div class="toolbar-actions">
          <el-button @click="showExpiredMyReservations = !showExpiredMyReservations">
            {{
              showExpiredMyReservations
                ? "返回当前预约"
                : `查看过期记录（${expiredMyReservations.length}）`
            }}
          </el-button>
          <el-button :loading="myReservationsLoading" @click="loadMyReservations">刷新</el-button>
        </div>
      </div>
      <el-table
        v-loading="myReservationsLoading"
        :data="displayedMyReservations"
        stripe
        :empty-text="myReservationEmptyText"
        :row-class-name="myReservationRowClass"
      >
        <el-table-column label="场地/社团" min-width="180">
          <template #default="{ row }">
            <div class="primary-text">{{ row.venueName }}</div>
            <div class="muted">{{ row.clubName || "未知社团" }}</div>
          </template>
        </el-table-column>
        <el-table-column label="预约时间" min-width="220">
          <template #default="{ row }">
            <div>{{ formatDateTime(row.startTime) }}</div>
            <div class="muted">至 {{ formatDateTime(row.endTime) }}</div>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="reservationStatusTagType(row.status)" size="small">
              {{ reservationStatusName(row.status) }}
            </el-tag>
            <div v-if="isExpiredReservation(row)" class="muted status-note">已过期</div>
          </template>
        </el-table-column>
        <el-table-column prop="purpose" label="用途" min-width="180" show-overflow-tooltip />
        <el-table-column label="操作" width="90" fixed="right">
          <template #default="{ row }">
            <el-tooltip
              v-if="canOperateReservation(row)"
              :disabled="canDeleteReservation(row)"
              :content="deleteReservationDisabledReason(row)"
              placement="top"
            >
              <span>
                <el-button
                  type="danger"
                  size="small"
                  text
                  :disabled="!canDeleteReservation(row)"
                  :loading="deletingReservationId === row.id"
                  @click="deleteReservation(row)"
                >
                  删除
                </el-button>
              </span>
            </el-tooltip>
          </template>
        </el-table-column>
      </el-table>
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
.my-reservation-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 12px;
}
.panel-title {
  font-weight: 600;
}
.primary-text {
  font-weight: 600;
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
.alert-content {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
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
  grid-template-columns: 120px 1fr auto;
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
.slot-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.muted {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.status-note {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.35;
}
:deep(.expired-reservation-row) {
  color: var(--el-text-color-placeholder);
}
:deep(.expired-reservation-row .primary-text),
:deep(.expired-reservation-row .muted) {
  color: var(--el-text-color-placeholder);
}
@media (max-width: 720px) {
  .search-row,
  .my-reservation-toolbar {
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

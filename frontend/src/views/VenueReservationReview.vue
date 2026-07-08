<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { readAuth } from "../authSession";
import { formatVenueReservationDateTime, venueReservationTimestamp } from "../beijingTime";
import {
  createVenueSearchIndex,
  formatVenueLocation,
  matchesVenueSearch,
  type VenueSearchIndex,
} from "../venueSearch";

interface VenueReservation {
  id: number;
  venueId: number;
  venueName: string;
  clubId: number;
  clubName: string;
  activityId?: number | null;
  activityTitle?: string | null;
  applicantUserId: number;
  applicantName?: string | null;
  startTime: string;
  endTime: string;
  purpose: string;
  status: string;
  reviewerUserId?: number | null;
  reviewerName?: string | null;
  reviewComment?: string | null;
  createdAt: string;
}

interface Venue {
  id: number;
  name: string;
  building?: string | null;
  roomNo?: string | null;
}

interface ApiErrorPayload {
  message?: string;
  detail?: string | null;
}

interface VenueGroup {
  venueId: number;
  venueName: string;
  pendingCount: number;
  nextStartTime: string;
}

interface VenueGroupRow {
  group: VenueGroup;
  searchIndex: VenueSearchIndex;
}

interface VenueReservationRow {
  reservation: VenueReservation;
  searchIndex: VenueSearchIndex;
}

interface ReviewForm {
  approved: boolean;
  reviewComment: string;
}

const auth = ref(readAuth());
const props = defineProps<{ modelValue: boolean }>();
const emit = defineEmits<{
  (event: "update:modelValue", value: boolean): void;
  (event: "reviewed", reservation: VenueReservation): void;
}>();
const pendingReservations = ref<VenueReservation[]>([]);
const historyReservations = ref<VenueReservation[]>([]);
const venues = ref<Venue[]>([]);
const loading = ref(false);
const historyLoading = ref(false);
const reviewing = ref(false);
const error = ref("");
const historyError = ref("");
const selectedVenueId = ref<number>();
const reviewDialogVisible = ref(false);
const historyVisible = ref(false);
const reviewTarget = ref<VenueReservation | null>(null);
const historyStatus = ref<"all" | "approved" | "rejected">("all");
const venueSearch = ref("");
const selectedReservationSearch = ref("");
const historySearch = ref("");

const reviewForm = reactive<ReviewForm>({
  approved: true,
  reviewComment: "",
});

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit("update:modelValue", value),
});

const canReview = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("venue:review") || permissions.includes("*");
});

const reviewerUserId = computed(() => auth.value?.user.id);
const selectedVenueName = computed(() => {
  return venueGroups.value.find((venue) => venue.venueId === selectedVenueId.value)?.venueName;
});
const venueLookup = computed(() => {
  return new Map(venues.value.map((venue) => [venue.id, venue]));
});

const venueGroups = computed<VenueGroup[]>(() => {
  const map = new Map<number, VenueGroup>();

  for (const reservation of pendingReservations.value) {
    const existing = map.get(reservation.venueId);
    if (!existing) {
      map.set(reservation.venueId, {
        venueId: reservation.venueId,
        venueName: reservation.venueName,
        pendingCount: 1,
        nextStartTime: reservation.startTime,
      });
      continue;
    }

    existing.pendingCount += 1;
    if (
      venueReservationTimestamp(reservation.startTime) <
      venueReservationTimestamp(existing.nextStartTime)
    ) {
      existing.nextStartTime = reservation.startTime;
    }
  }

  return Array.from(map.values()).sort((a, b) => {
    const byStart =
      venueReservationTimestamp(a.nextStartTime) - venueReservationTimestamp(b.nextStartTime);
    return byStart || a.venueId - b.venueId;
  });
});

const venueGroupRows = computed<VenueGroupRow[]>(() => {
  return venueGroups.value.map((group) => ({
    group,
    searchIndex: createVenueIndex(group.venueId, group.venueName),
  }));
});

const filteredVenueGroups = computed(() => {
  return venueGroupRows.value
    .filter((row) => matchesVenueSearch(row.searchIndex, venueSearch.value))
    .map((row) => row.group);
});

const venueGroupEmptyText = computed(() => {
  return venueSearch.value.trim() ? "未找到匹配场地" : "暂无待审批场地";
});

const venueSearchSummary = computed(() => {
  if (!venueSearch.value.trim()) return `共 ${venueGroups.value.length} 个待审批场地`;
  return `已匹配 ${filteredVenueGroups.value.length} / ${venueGroups.value.length} 个场地`;
});

const selectedPendingReservations = computed(() => {
  const venueId = selectedVenueId.value;
  if (!venueId) return [];

  return pendingReservations.value
    .filter((reservation) => reservation.venueId === venueId)
    .slice()
    .sort(
      (a, b) => venueReservationTimestamp(a.startTime) - venueReservationTimestamp(b.startTime),
    );
});

const selectedPendingReservationRows = computed<VenueReservationRow[]>(() => {
  return selectedPendingReservations.value.map((reservation) => ({
    reservation,
    searchIndex: createReservationIndex(reservation),
  }));
});

const filteredSelectedPendingReservations = computed(() => {
  return selectedPendingReservationRows.value
    .filter((row) => matchesVenueSearch(row.searchIndex, selectedReservationSearch.value))
    .map((row) => row.reservation);
});

const selectedReservationEmptyText = computed(() => {
  return selectedReservationSearch.value.trim() ? "未找到匹配预约" : "该场地暂无待审批申请";
});

const selectedReservationSearchSummary = computed(() => {
  if (!selectedReservationSearch.value.trim()) {
    return `共 ${selectedPendingReservations.value.length} 条待审批申请`;
  }
  return `已匹配 ${filteredSelectedPendingReservations.value.length} / ${selectedPendingReservations.value.length} 条预约`;
});

const historyReservationRows = computed<VenueReservationRow[]>(() => {
  return historyReservations.value.map((reservation) => ({
    reservation,
    searchIndex: createReservationIndex(reservation),
  }));
});

const filteredHistoryReservations = computed(() => {
  return historyReservationRows.value
    .filter((row) => {
      return (
        (historyStatus.value === "all" || row.reservation.status === historyStatus.value) &&
        matchesVenueSearch(row.searchIndex, historySearch.value)
      );
    })
    .map((row) => row.reservation)
    .slice()
    .sort(
      (a, b) => venueReservationTimestamp(b.createdAt) - venueReservationTimestamp(a.createdAt),
    );
});

const historyEmptyText = computed(() => {
  return historySearch.value.trim() ? "未找到匹配历史" : "暂无审批历史";
});

async function readErrorMessage(res: Response) {
  try {
    const payload = (await res.json()) as ApiErrorPayload;
    return payload.message || payload.detail || `请求失败：HTTP ${res.status}`;
  } catch {
    return `请求失败：HTTP ${res.status}`;
  }
}

async function loadVenues() {
  try {
    const res = await fetch("/api/venues");
    if (!res.ok) throw new Error(await readErrorMessage(res));
    venues.value = await res.json();
  } catch {
    venues.value = [];
  }
}

async function loadPendingReservations() {
  if (!canReview.value) return;

  loading.value = true;
  error.value = "";
  try {
    const res = await fetch("/api/venue-reservations?status=pending");
    if (!res.ok) throw new Error(await readErrorMessage(res));
    pendingReservations.value = await res.json();
    syncSelectedVenue();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载待审批预约失败";
  } finally {
    loading.value = false;
  }
}

async function loadHistoryReservations() {
  if (!reviewerUserId.value) return;

  historyLoading.value = true;
  historyError.value = "";
  try {
    const [approved, rejected] = await Promise.all([
      fetchReservationsByStatus("approved"),
      fetchReservationsByStatus("rejected"),
    ]);
    historyReservations.value = [...approved, ...rejected].filter(
      (reservation) => reservation.reviewerUserId === reviewerUserId.value,
    );
  } catch (e) {
    historyError.value = e instanceof Error ? e.message : "加载审批历史失败";
  } finally {
    historyLoading.value = false;
  }
}

async function fetchReservationsByStatus(status: "approved" | "rejected") {
  const res = await fetch(`/api/venue-reservations?status=${status}`);
  if (!res.ok) throw new Error(await readErrorMessage(res));
  return (await res.json()) as VenueReservation[];
}

function syncSelectedVenue() {
  if (filteredVenueGroups.value.some((venue) => venue.venueId === selectedVenueId.value)) {
    return;
  }

  selectedVenueId.value = undefined;
  selectedReservationSearch.value = "";
}

function selectVenue(group: VenueGroup) {
  selectedVenueId.value = group.venueId;
  selectedReservationSearch.value = "";
}

function openReview(reservation: VenueReservation, approved: boolean) {
  reviewTarget.value = reservation;
  reviewForm.approved = approved;
  reviewForm.reviewComment = "";
  reviewDialogVisible.value = true;
}

async function submitReview() {
  const reviewerId = reviewerUserId.value;
  if (!reviewTarget.value || !reviewerId) {
    ElMessage.error("缺少审批人或预约信息，无法提交审批。");
    return;
  }

  if (reviewForm.approved && !canApprove(reviewTarget.value)) {
    ElMessage.error("预约开始时间必须晚于当前时间，不能审批通过。");
    return;
  }

  reviewing.value = true;
  try {
    const res = await fetch(`/api/venue-reservations/${reviewTarget.value.id}/review`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        reviewerUserId: reviewerId,
        approved: reviewForm.approved,
        reviewComment: reviewForm.reviewComment.trim() || null,
      }),
    });

    if (!res.ok) throw new Error(await readErrorMessage(res));
    const reviewedReservation = (await res.json()) as VenueReservation;
    pendingReservations.value = pendingReservations.value.filter(
      (reservation) => reservation.id !== reviewedReservation.id,
    );
    historyReservations.value = [reviewedReservation, ...historyReservations.value];
    reviewDialogVisible.value = false;
    reviewTarget.value = reviewedReservation;
    syncSelectedVenue();
    emit("reviewed", reviewedReservation);
    ElMessage.success(reviewForm.approved ? "预约已审批通过。" : "预约已拒绝。");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "提交审批失败");
  } finally {
    reviewing.value = false;
  }
}

async function openHistory() {
  historyVisible.value = true;
  await loadHistoryReservations();
}

function canApprove(reservation: VenueReservation) {
  return venueReservationTimestamp(reservation.startTime) > Date.now();
}

function approveDisabledReason(reservation: VenueReservation) {
  return canApprove(reservation) ? "" : "开始时间必须晚于当前时间";
}

function createVenueIndex(venueId: number, venueName: string, extraIds: number[] = []) {
  const venue = venueLookup.value.get(venueId);
  const location = venue ? formatVenueLocation(venue.building, venue.roomNo) : "";

  return createVenueSearchIndex({
    ids: [venueId, ...extraIds],
    texts: [venueName, venue?.name, venue?.building, venue?.roomNo, location],
  });
}

function createReservationIndex(reservation: VenueReservation) {
  return createVenueIndex(reservation.venueId, reservation.venueName, [reservation.id]);
}

function venueLocationText(venueId: number) {
  const venue = venueLookup.value.get(venueId);
  if (!venue) return "位置未加载";

  return formatVenueLocation(venue.building, venue.roomNo) || "未填写";
}

function formatDateTime(value: string) {
  return formatVenueReservationDateTime(value);
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    pending: "待审批",
    approved: "已通过",
    rejected: "已拒绝",
    cancelled: "已取消",
  };
  return labels[status] || status;
}

function statusType(status: string) {
  const types: Record<string, "success" | "warning" | "danger" | "info"> = {
    pending: "warning",
    approved: "success",
    rejected: "danger",
    cancelled: "info",
  };
  return types[status] || "info";
}

watch(
  visible,
  (isVisible) => {
    if (!isVisible) return;
    selectedVenueId.value = undefined;
    venueSearch.value = "";
    selectedReservationSearch.value = "";
    loadVenues();
    loadPendingReservations();
  },
  { immediate: true },
);

watch(filteredVenueGroups, syncSelectedVenue);
</script>

<template>
  <el-dialog v-model="visible" title="预约审批" width="min(1180px, 96vw)" top="4vh">
    <div class="toolbar">
      <div>
        <p class="subtitle">先查看被申请的场地，点击场地后再处理该场地的预约记录。</p>
      </div>
      <div class="toolbar-actions">
        <el-button :loading="historyLoading" @click="openHistory">历史记录</el-button>
        <el-button type="primary" :loading="loading" @click="loadPendingReservations">
          刷新申请
        </el-button>
      </div>
    </div>

    <el-alert
      v-if="!canReview"
      title="当前账号没有场地预约审批权限。"
      type="warning"
      show-icon
      class="notice"
    />
    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <section v-if="canReview" class="venue-panel">
      <div class="panel-heading">
        <div>
          <div class="panel-title">被申请场地</div>
          <div class="search-summary">{{ venueSearchSummary }}</div>
        </div>
        <el-input
          v-model="venueSearch"
          clearable
          placeholder="搜索 ID、场地名称或位置"
          class="panel-search"
        />
      </div>
      <el-table
        v-loading="loading"
        :data="filteredVenueGroups"
        stripe
        highlight-current-row
        :empty-text="venueGroupEmptyText"
        @row-click="selectVenue"
      >
        <el-table-column label="场地" min-width="220">
          <template #default="{ row }">
            <div class="primary-text">{{ row.venueName }}</div>
            <div class="muted">ID {{ row.venueId }}</div>
          </template>
        </el-table-column>
        <el-table-column label="位置" min-width="180">
          <template #default="{ row }">
            {{ venueLocationText(row.venueId) }}
          </template>
        </el-table-column>
        <el-table-column prop="pendingCount" label="申请数" width="120" />
        <el-table-column label="最早申请时间" min-width="220">
          <template #default="{ row }">
            {{ formatDateTime(row.nextStartTime) }}
          </template>
        </el-table-column>
      </el-table>
    </section>

    <section v-if="selectedVenueId" class="reservation-panel">
      <div class="panel-heading">
        <div>
          <div class="panel-title">{{ selectedVenueName }} 的预约记录</div>
          <div class="search-summary">{{ selectedReservationSearchSummary }}</div>
        </div>
        <el-input
          v-model="selectedReservationSearch"
          clearable
          placeholder="搜索预约 ID、场地名称或位置"
          class="panel-search"
        />
      </div>
      <el-table
        v-loading="loading"
        :data="filteredSelectedPendingReservations"
        stripe
        :empty-text="selectedReservationEmptyText"
      >
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column label="申请人/社团" min-width="180">
          <template #default="{ row }">
            <div class="primary-text">{{ row.applicantName || `用户 ${row.applicantUserId}` }}</div>
            <div class="muted">{{ row.clubName || `社团 ${row.clubId}` }}</div>
          </template>
        </el-table-column>
        <el-table-column label="预约时间" min-width="220">
          <template #default="{ row }">
            <div>{{ formatDateTime(row.startTime) }}</div>
            <div class="muted">至 {{ formatDateTime(row.endTime) }}</div>
          </template>
        </el-table-column>
        <el-table-column prop="purpose" label="用途" min-width="220" show-overflow-tooltip />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)" size="small">{{
              statusLabel(row.status)
            }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="170" fixed="right">
          <template #default="{ row }">
            <el-tooltip
              :disabled="canApprove(row)"
              :content="approveDisabledReason(row)"
              placement="top"
            >
              <span>
                <el-button
                  type="success"
                  size="small"
                  :disabled="!canApprove(row)"
                  @click="openReview(row, true)"
                >
                  同意
                </el-button>
              </span>
            </el-tooltip>
            <el-button type="danger" plain size="small" @click="openReview(row, false)">
              拒绝
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <el-dialog
      v-model="reviewDialogVisible"
      :title="reviewForm.approved ? '同意预约申请' : '拒绝预约申请'"
      width="560px"
      append-to-body
    >
      <el-descriptions v-if="reviewTarget" :column="1" size="small" border class="review-detail">
        <el-descriptions-item label="场地">{{ reviewTarget.venueName }}</el-descriptions-item>
        <el-descriptions-item label="社团">{{
          reviewTarget.clubName || `社团 ${reviewTarget.clubId}`
        }}</el-descriptions-item>
        <el-descriptions-item label="申请人">{{
          reviewTarget.applicantName || `用户 ${reviewTarget.applicantUserId}`
        }}</el-descriptions-item>
        <el-descriptions-item label="时间">
          {{ formatDateTime(reviewTarget.startTime) }} 至
          {{ formatDateTime(reviewTarget.endTime) }}
        </el-descriptions-item>
        <el-descriptions-item label="用途">{{ reviewTarget.purpose }}</el-descriptions-item>
      </el-descriptions>

      <el-form label-position="top">
        <el-form-item label="审批意见">
          <el-input
            v-model="reviewForm.reviewComment"
            type="textarea"
            maxlength="255"
            show-word-limit
            :placeholder="reviewForm.approved ? '可填写同意说明' : '请填写拒绝原因或补充说明'"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button
          :type="reviewForm.approved ? 'success' : 'danger'"
          :loading="reviewing"
          @click="submitReview"
        >
          {{ reviewForm.approved ? "确认同意" : "确认拒绝" }}
        </el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="historyVisible" title="我的审批历史" size="760px" append-to-body>
      <div class="history-toolbar">
        <div class="history-filters">
          <el-radio-group v-model="historyStatus" size="small">
            <el-radio-button label="all">全部</el-radio-button>
            <el-radio-button label="approved">已同意</el-radio-button>
            <el-radio-button label="rejected">已拒绝</el-radio-button>
          </el-radio-group>
          <el-input
            v-model="historySearch"
            clearable
            placeholder="搜索预约 ID、场地名称或位置"
            class="history-search"
          />
        </div>
        <el-button :loading="historyLoading" size="small" @click="loadHistoryReservations">
          刷新
        </el-button>
      </div>

      <el-alert
        v-if="historyError"
        :title="historyError"
        type="error"
        show-icon
        closable
        class="notice"
        @close="historyError = ''"
      />

      <el-table
        v-loading="historyLoading"
        :data="filteredHistoryReservations"
        stripe
        :empty-text="historyEmptyText"
      >
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column label="场地/社团" min-width="180">
          <template #default="{ row }">
            <div class="primary-text">{{ row.venueName }}</div>
            <div class="muted">{{ venueLocationText(row.venueId) }}</div>
            <div class="muted">{{ row.clubName || `社团 ${row.clubId}` }}</div>
          </template>
        </el-table-column>
        <el-table-column label="预约时间" min-width="220">
          <template #default="{ row }">
            <div>{{ formatDateTime(row.startTime) }}</div>
            <div class="muted">至 {{ formatDateTime(row.endTime) }}</div>
          </template>
        </el-table-column>
        <el-table-column label="结果" width="100">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)" size="small">{{
              statusLabel(row.status)
            }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column
          prop="reviewComment"
          label="审批意见"
          min-width="180"
          show-overflow-tooltip
        />
      </el-table>
    </el-drawer>
  </el-dialog>
</template>

<style scoped>
.toolbar,
.history-toolbar {
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
.venue-panel,
.reservation-panel {
  margin-top: 14px;
  min-width: 0;
}
.panel-heading {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 10px;
}
.panel-title {
  font-weight: 600;
}
.panel-search {
  max-width: 320px;
}
.search-summary {
  margin-top: 4px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.history-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
.history-search {
  width: 280px;
}
.primary-text {
  font-weight: 600;
}
.muted {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.review-detail {
  margin-bottom: 16px;
}

@media (max-width: 900px) {
  .toolbar,
  .history-toolbar,
  .panel-heading {
    flex-direction: column;
  }

  .panel-search,
  .history-search {
    width: 100%;
    max-width: none;
  }
}
</style>

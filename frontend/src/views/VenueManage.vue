<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { readAuth } from "../authSession";
import {
  beijingDateTimeToUtcIso,
  formatVenueReservationDateTime,
  venueReservationTimestamp,
} from "../beijingTime";
import {
  createVenueSearchIndex,
  formatVenueLocation,
  matchesVenueSearch,
  type VenueSearchIndex,
} from "../venueSearch";

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

interface VenueRow {
  venue: Venue;
  searchIndex: VenueSearchIndex;
}

interface VenueReservation {
  id: number;
  venueId: number;
  venueName: string;
  applicantUserId: number;
  applicantName?: string | null;
  startTime: string;
  endTime: string;
  purpose: string;
  status: string;
}

interface ApiErrorPayload {
  message?: string;
  detail?: string | null;
}

interface VenueForm {
  name: string;
  building: string;
  roomNo: string;
  capacity: number;
  managerUserId: string;
  status: "available" | "maintenance" | "disabled";
}

const venues = ref<Venue[]>([]);
const loading = ref(false);
const saving = ref(false);
const statusChangingId = ref<number>();
const deletingId = ref<number>();
const error = ref("");
const venueSearch = ref("");
const dialogVisible = ref(false);
const maintenanceDialogVisible = ref(false);
const statusConflictDialogVisible = ref(false);
const editingVenue = ref<Venue | null>(null);
const venueFormRef = ref<FormInstance>();
const auth = ref(readAuth());

const form = reactive<VenueForm>({
  name: "",
  building: "",
  roomNo: "",
  capacity: 1,
  managerUserId: "",
  status: "available",
});

const maintenanceForm = reactive({
  venueId: undefined as number | undefined,
  venueName: "",
  maintenanceUntil: "",
});
const pendingStatusChange = ref<{
  venue: Venue;
  status: "maintenance" | "disabled";
  maintenanceUntil: string | null;
} | null>(null);
const statusConflictReservations = ref<VenueReservation[]>([]);
const resolvingStatusConflicts = ref(false);

const permissions = computed(() => auth.value?.permissions ?? []);
const operatorUserId = computed(() => auth.value?.user.id);
const canCreate = computed(() => hasPermission("venue:create"));
const canUpdate = computed(() => hasPermission("venue:update"));
const canDisable = computed(() => hasPermission("venue:disable"));
const canManageVenue = computed(() => canCreate.value || canUpdate.value || canDisable.value);

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

const tableEmptyText = computed(() => {
  return venueSearch.value.trim() ? "未找到匹配场地" : "暂无场地数据";
});

const searchSummary = computed(() => {
  if (!venueSearch.value.trim()) return `共 ${venues.value.length} 个场地`;
  return `已匹配 ${filteredVenues.value.length} / ${venues.value.length} 个场地`;
});

const dialogTitle = computed(() => (editingVenue.value ? "编辑场地" : "新增场地"));

const rules: FormRules<VenueForm> = {
  name: [
    { required: true, message: "请输入场地名称", trigger: "blur" },
    { max: 255, message: "场地名称最多 255 个字符", trigger: "blur" },
  ],
  building: [{ max: 255, message: "楼栋最多 255 个字符", trigger: "blur" }],
  roomNo: [{ max: 255, message: "房间号最多 255 个字符", trigger: "blur" }],
  capacity: [{ required: true, message: "请输入容量", trigger: "change" }],
  managerUserId: [{ validator: validateOptionalUserId, trigger: "blur" }],
  status: [{ required: true, message: "请选择状态", trigger: "change" }],
};

const statusLabel: Record<string, string> = {
  available: "可预约",
  maintenance: "维护中",
  disabled: "停用",
};

const statusType: Record<string, "success" | "warning" | "info" | "danger"> = {
  available: "success",
  maintenance: "warning",
  disabled: "info",
};

const reservationStatusLabel: Record<string, string> = {
  pending: "待审批",
  approved: "已通过",
  rejected: "已拒绝",
  cancelled: "已取消",
};

function hasPermission(permission: string) {
  return permissions.value.includes(permission) || permissions.value.includes("*");
}

function validateOptionalUserId(_rule: unknown, value: string, callback: (error?: Error) => void) {
  const normalized = value.trim();
  if (!normalized || /^[1-9]\d*$/.test(normalized)) {
    callback();
    return;
  }

  callback(new Error("负责人 ID 必须为正整数"));
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

function openCreate() {
  editingVenue.value = null;
  resetForm();
  dialogVisible.value = true;
}

function openEdit(venue: Venue) {
  editingVenue.value = venue;
  form.name = venue.name;
  form.building = venue.building ?? "";
  form.roomNo = venue.roomNo ?? "";
  form.capacity = venue.capacity || 1;
  form.managerUserId = venue.managerUserId ? String(venue.managerUserId) : "";
  form.status = normalizeStatus(venue.status);
  dialogVisible.value = true;
}

async function submitVenue() {
  if (!venueFormRef.value || !operatorUserId.value) return;
  const valid = await venueFormRef.value.validate().catch(() => false);
  if (!valid) return;

  const venue = editingVenue.value;
  if (!venue && !canCreate.value) {
    ElMessage.error("当前账号没有创建场地权限。");
    return;
  }

  if (venue && !canUpdate.value) {
    ElMessage.error("当前账号没有维护场地权限。");
    return;
  }

  if (venue && venue.status !== form.status && !canDisable.value) {
    ElMessage.error("当前账号没有停用或恢复场地权限。");
    return;
  }

  saving.value = true;
  try {
    const savedVenue = venue ? await updateVenue(venue.id) : await createVenue();
    if (venue && venue.status !== form.status) {
      if (form.status === "available") {
        await changeVenueStatus(savedVenue.id, "available", false);
      } else {
        await requestVenueStatusChange(savedVenue, form.status);
      }
    }

    dialogVisible.value = false;
    ElMessage.success(venue ? "场地已更新。" : "场地已创建。");
    await loadVenues();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "保存场地失败");
  } finally {
    saving.value = false;
  }
}

async function createVenue() {
  const res = await fetch("/api/venues", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      ...venuePayload(),
      status: form.status,
    }),
  });
  if (!res.ok) throw new Error(await readErrorMessage(res));
  return (await res.json()) as Venue;
}

async function updateVenue(venueId: number) {
  const res = await fetch(`/api/venues/${venueId}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(venuePayload()),
  });
  if (!res.ok) throw new Error(await readErrorMessage(res));
  return (await res.json()) as Venue;
}

function openMaintenanceDialog(venue: Venue) {
  maintenanceForm.venueId = venue.id;
  maintenanceForm.venueName = venue.name;
  maintenanceForm.maintenanceUntil = "";
  maintenanceDialogVisible.value = true;
}

async function submitMaintenanceStatus() {
  const venue = venues.value.find((item) => item.id === maintenanceForm.venueId);
  if (!venue) return;

  await requestVenueStatusChange(
    venue,
    "maintenance",
    maintenanceForm.maintenanceUntil
      ? beijingDateTimeToUtcIso(maintenanceForm.maintenanceUntil)
      : null,
  );
  maintenanceDialogVisible.value = false;
}

async function requestVenueStatusChange(
  venue: Venue,
  status: "maintenance" | "disabled",
  maintenanceUntil: string | null = null,
) {
  if (!operatorUserId.value) return;
  if (!canDisable.value) {
    ElMessage.error("当前账号没有停用或恢复场地权限。");
    return;
  }

  statusChangingId.value = venue.id;
  try {
    const reservations = await fetchVenueReservations(venue.id);
    const conflicts = findStatusConflictReservations(reservations, status, maintenanceUntil);
    if (conflicts.length > 0) {
      pendingStatusChange.value = { venue, status, maintenanceUntil };
      statusConflictReservations.value = conflicts;
      statusConflictDialogVisible.value = true;
      return;
    }

    await changeVenueStatus(venue.id, status, true, maintenanceUntil);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "检查场地预约冲突失败");
  } finally {
    statusChangingId.value = undefined;
  }
}

async function continueStatusChangeWithCancellation() {
  const pending = pendingStatusChange.value;
  if (!pending) return;

  resolvingStatusConflicts.value = true;
  try {
    const changed = await changeVenueStatus(
      pending.venue.id,
      pending.status,
      true,
      pending.maintenanceUntil,
      true,
    );
    if (!changed) return;

    statusConflictDialogVisible.value = false;
    pendingStatusChange.value = null;
    statusConflictReservations.value = [];
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "取消冲突预约失败");
  } finally {
    resolvingStatusConflicts.value = false;
  }
}

function cancelStatusChange() {
  statusConflictDialogVisible.value = false;
  pendingStatusChange.value = null;
  statusConflictReservations.value = [];
}

async function changeVenueStatus(
  venueId: number,
  status: "available" | "maintenance" | "disabled",
  showMessage = true,
  maintenanceUntil: string | null = null,
  cancelConflictingReservations = false,
) {
  if (!operatorUserId.value) return;
  if (!canDisable.value) {
    ElMessage.error("当前账号没有停用或恢复场地权限。");
    return;
  }

  statusChangingId.value = venueId;
  try {
    await updateVenueStatus(venueId, status, maintenanceUntil, cancelConflictingReservations);
    if (showMessage) ElMessage.success("场地状态已更新。");
    await loadVenues();
    return true;
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "更新场地状态失败");
    return false;
  } finally {
    statusChangingId.value = undefined;
  }
}

async function updateVenueStatus(
  venueId: number,
  status: "available" | "maintenance" | "disabled",
  maintenanceUntil: string | null = null,
  cancelConflictingReservations = false,
) {
  const res = await fetch(`/api/venues/${venueId}/status`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      operatorUserId: operatorUserId.value,
      status,
      maintenanceUntil,
      cancelConflictingReservations,
    }),
  });
  if (!res.ok) throw new Error(await readErrorMessage(res));
  return (await res.json()) as Venue;
}

async function fetchVenueReservations(venueId: number) {
  const res = await fetch(`/api/venue-reservations?venueId=${venueId}`);
  if (!res.ok) throw new Error(await readErrorMessage(res));
  return (await res.json()) as VenueReservation[];
}

function findStatusConflictReservations(
  reservations: VenueReservation[],
  status: "maintenance" | "disabled",
  maintenanceUntil: string | null,
) {
  const now = Date.now();
  const maintenanceUntilTime = maintenanceUntil
    ? venueReservationTimestamp(maintenanceUntil)
    : Number.NaN;

  return reservations
    .filter((reservation) => {
      const isActiveStatus = reservation.status === "pending" || reservation.status === "approved";
      const isFuture = venueReservationTimestamp(reservation.endTime) > now;
      if (!isActiveStatus || !isFuture) return false;
      if (status === "disabled") return true;
      if (!Number.isFinite(maintenanceUntilTime)) return true;

      return venueReservationTimestamp(reservation.startTime) < maintenanceUntilTime;
    })
    .sort(
      (a, b) => venueReservationTimestamp(a.startTime) - venueReservationTimestamp(b.startTime),
    );
}

async function deleteVenue(venue: Venue) {
  if (!operatorUserId.value) return;
  if (!canDisable.value) {
    ElMessage.error("当前账号没有删除场地权限。");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定删除场地「${venue.name}」吗？删除后不可恢复。`,
      "确认删除场地",
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

  deletingId.value = venue.id;
  try {
    const res = await fetch(`/api/venues/${venue.id}`, {
      method: "DELETE",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operatorUserId: operatorUserId.value }),
    });
    if (!res.ok) throw new Error(await readErrorMessage(res));
    ElMessage.success("场地已删除。");
    await loadVenues();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "删除场地失败");
  } finally {
    deletingId.value = undefined;
  }
}

function venuePayload() {
  return {
    operatorUserId: operatorUserId.value,
    name: form.name.trim(),
    building: optionalText(form.building),
    roomNo: optionalText(form.roomNo),
    capacity: form.capacity,
    managerUserId: optionalNumber(form.managerUserId),
  };
}

function resetForm() {
  form.name = "";
  form.building = "";
  form.roomNo = "";
  form.capacity = 1;
  form.managerUserId = "";
  form.status = "available";
}

function createVenueIndex(venue: Venue) {
  const location = formatVenueLocation(venue.building, venue.roomNo);
  return createVenueSearchIndex({
    ids: [venue.id],
    texts: [venue.name, venue.building, venue.roomNo, location],
  });
}

function formatLocation(venue: Venue) {
  return formatVenueLocation(venue.building, venue.roomNo) || "未填写";
}

function normalizeStatus(status: string): VenueForm["status"] {
  return status === "maintenance" || status === "disabled" ? status : "available";
}

function statusName(status: string) {
  return statusLabel[status] || status;
}

function statusTagType(status: string) {
  return statusType[status] || "info";
}

function maintenanceUntilText(venue: Venue) {
  if (venue.status !== "maintenance") return "";
  return venue.maintenanceUntil
    ? `维护至 ${formatVenueReservationDateTime(venue.maintenanceUntil)}`
    : "维护至 未知";
}

function optionalText(value: string) {
  const normalized = value.trim();
  return normalized ? normalized : null;
}

function optionalNumber(value: string) {
  const normalized = value.trim();
  return normalized ? Number(normalized) : null;
}

onMounted(loadVenues);
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <div>
        <h2>场地管理</h2>
        <p class="subtitle">维护可预约场地基础信息，并控制场地可预约、维护或停用状态。</p>
      </div>
      <div class="toolbar-actions">
        <el-button :loading="loading" @click="loadVenues">刷新</el-button>
        <el-button v-if="canCreate" type="primary" @click="openCreate">新增场地</el-button>
      </div>
    </div>

    <el-alert
      v-if="!canManageVenue"
      title="当前账号没有场地管理权限，请联系系统管理员分配场地管理员角色。"
      type="warning"
      show-icon
      class="notice"
    />
    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <div class="search-row">
      <el-input
        v-model="venueSearch"
        clearable
        placeholder="搜索 ID、场地名称或位置"
        class="search-input"
      />
      <span class="search-summary">{{ searchSummary }}</span>
    </div>

    <el-table v-loading="loading" :data="filteredVenues" stripe :empty-text="tableEmptyText">
      <el-table-column prop="id" label="ID" width="70" />
      <el-table-column prop="name" label="场地名称" min-width="160" />
      <el-table-column label="位置" min-width="180">
        <template #default="{ row }">{{ formatLocation(row) }}</template>
      </el-table-column>
      <el-table-column prop="capacity" label="容量" width="90" />
      <el-table-column label="状态" width="150">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.status)" size="small">{{
            statusName(row.status)
          }}</el-tag>
          <div v-if="maintenanceUntilText(row)" class="muted status-note">
            {{ maintenanceUntilText(row) }}
          </div>
        </template>
      </el-table-column>
      <el-table-column prop="managerUserId" label="负责人 ID" width="110" />
      <el-table-column label="操作" width="320" fixed="right">
        <template #default="{ row }">
          <div class="row-actions">
            <el-button v-if="canUpdate" size="small" @click="openEdit(row)">编辑</el-button>
            <el-button
              v-if="canDisable"
              :type="row.status === 'maintenance' ? 'success' : undefined"
              size="small"
              plain
              :loading="statusChangingId === row.id"
              @click="
                row.status === 'maintenance'
                  ? changeVenueStatus(row.id, 'available')
                  : openMaintenanceDialog(row)
              "
            >
              {{ row.status === "maintenance" ? "恢复" : "维护中" }}
            </el-button>
            <el-button
              v-if="canDisable"
              :type="row.status === 'disabled' ? 'success' : 'danger'"
              size="small"
              plain
              :loading="statusChangingId === row.id"
              @click="
                row.status === 'disabled'
                  ? changeVenueStatus(row.id, 'available')
                  : requestVenueStatusChange(row, 'disabled')
              "
            >
              {{ row.status === "disabled" ? "恢复" : "停用" }}
            </el-button>
            <el-button
              v-if="canDisable"
              type="danger"
              size="small"
              text
              :loading="deletingId === row.id"
              @click="deleteVenue(row)"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="560px">
      <el-form ref="venueFormRef" :model="form" :rules="rules" label-position="top" status-icon>
        <el-form-item label="场地名称" prop="name">
          <el-input v-model="form.name" maxlength="255" placeholder="例如：大学生活动中心 101" />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="楼栋" prop="building">
            <el-input v-model="form.building" maxlength="255" placeholder="例如：大学生活动中心" />
          </el-form-item>
          <el-form-item label="房间号" prop="roomNo">
            <el-input v-model="form.roomNo" maxlength="255" placeholder="例如：101" />
          </el-form-item>
        </div>
        <div class="form-grid">
          <el-form-item label="容量" prop="capacity">
            <el-input-number
              v-model="form.capacity"
              :min="1"
              :controls="false"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="负责人用户 ID" prop="managerUserId">
            <el-input v-model="form.managerUserId" placeholder="可选" />
          </el-form-item>
        </div>
        <el-form-item label="状态" prop="status">
          <el-select
            v-model="form.status"
            :disabled="Boolean(editingVenue) && !canDisable"
            class="full-width"
          >
            <el-option label="可预约" value="available" />
            <el-option label="维护中" value="maintenance" />
            <el-option label="停用" value="disabled" />
          </el-select>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitVenue">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="maintenanceDialogVisible" title="设置维护时间" width="460px">
      <p class="dialog-tip">
        将场地「{{ maintenanceForm.venueName }}」设为维护中。维护结束时间可以不填。
      </p>
      <el-form label-position="top">
        <el-form-item label="维护结束时间（可选）">
          <el-date-picker
            v-model="maintenanceForm.maintenanceUntil"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            format="YYYY-MM-DD HH:mm"
            placeholder="不填写则显示为未知"
            class="full-width"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="maintenanceDialogVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="maintenanceForm.venueId === statusChangingId"
          @click="submitMaintenanceStatus"
        >
          确认维护
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="statusConflictDialogVisible"
      title="存在冲突预约"
      width="760px"
      @close="cancelStatusChange"
    >
      <el-alert
        :title="`场地「${pendingStatusChange?.venue.name ?? ''}」后续存在与${pendingStatusChange?.status === 'maintenance' ? '维护' : '停用'}冲突的预约。`"
        type="warning"
        show-icon
        class="notice"
      />
      <el-table :data="statusConflictReservations" stripe>
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column label="预约时间" min-width="220">
          <template #default="{ row }">
            <div>{{ formatVenueReservationDateTime(row.startTime) }}</div>
            <div class="muted">至 {{ formatVenueReservationDateTime(row.endTime) }}</div>
          </template>
        </el-table-column>
        <el-table-column label="申请人" min-width="130">
          <template #default="{ row }">
            {{ row.applicantName || `用户 ${row.applicantUserId}` }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            {{ reservationStatusLabel[row.status] || row.status }}
          </template>
        </el-table-column>
        <el-table-column prop="purpose" label="用途" min-width="180" show-overflow-tooltip />
      </el-table>
      <template #footer>
        <el-button @click="cancelStatusChange">
          取消{{ pendingStatusChange?.status === "maintenance" ? "维护" : "停用" }}
        </el-button>
        <el-button
          type="danger"
          :loading="resolvingStatusConflicts"
          @click="continueStatusChangeWithCancellation"
        >
          取消所有冲突预约并继续
        </el-button>
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
.toolbar-actions,
.row-actions {
  display: flex;
  flex-wrap: wrap;
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
.muted {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.status-note {
  margin-top: 4px;
  line-height: 1.35;
}
.dialog-tip {
  margin: 0 0 14px;
  color: var(--el-text-color-regular);
}
.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}
.full-width {
  width: 100%;
}
@media (max-width: 760px) {
  .toolbar,
  .search-row {
    align-items: stretch;
    flex-direction: column;
  }

  .search-input {
    max-width: none;
  }

  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>

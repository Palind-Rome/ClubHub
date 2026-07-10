<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { Box, Check, Plus, Refresh, Warning } from "@element-plus/icons-vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { readAuth } from "../authSession";
import { requestJson } from "../composables/useApiRequest";
import {
  beijingDateTimeTimestamp,
  beijingDateTimeToUtcIso,
  formatVenueReservationDateTime,
  venueReservationTimestamp,
} from "../beijingTime";

interface ClubOption {
  id: number;
  name: string;
}

interface Material {
  id: number;
  clubId: number;
  clubName: string;
  name: string;
  specification?: string | null;
  totalQuantity: number;
  availableQuantity: number;
  borrowedQuantity: number;
  storageLocation?: string | null;
  status: string;
  createdAt: string;
}

interface MaterialBorrow {
  id: number;
  materialId: number;
  materialName: string;
  specification?: string | null;
  clubId: number;
  clubName: string;
  borrowerUserId: number;
  borrowerName?: string | null;
  quantity: number;
  borrowAt: string;
  expectedReturnAt?: string | null;
  returnAt?: string | null;
  status: string;
  damageDescription?: string | null;
  compensationAmount: number;
  overdue: boolean;
}

interface MaterialForm {
  clubId?: number;
  name: string;
  specification: string;
  totalQuantity: number;
  availableQuantity?: number;
  storageLocation: string;
}

interface BorrowForm {
  materialId?: number;
  clubId?: number;
  quantity: number;
  expectedReturnAt: string;
}

interface DamageForm {
  damageDescription: string;
  compensationAmount: number;
}

const auth = ref(readAuth());
const clubs = ref<ClubOption[]>([]);
const materials = ref<Material[]>([]);
const borrows = ref<MaterialBorrow[]>([]);
const loading = ref(false);
const borrowLoading = ref(false);
const submitting = ref(false);
const activeClubId = ref<number>();
const materialSearch = ref("");
const borrowStatus = ref("");
const materialDialogVisible = ref(false);
const borrowDialogVisible = ref(false);
const damageDialogVisible = ref(false);
const currentMaterial = ref<Material | null>(null);
const currentBorrow = ref<MaterialBorrow | null>(null);
const materialFormRef = ref<FormInstance>();
const borrowFormRef = ref<FormInstance>();
const damageFormRef = ref<FormInstance>();

const materialForm = reactive<MaterialForm>({
  clubId: undefined,
  name: "",
  specification: "",
  totalQuantity: 1,
  availableQuantity: undefined,
  storageLocation: "",
});

const borrowForm = reactive<BorrowForm>({
  materialId: undefined,
  clubId: undefined,
  quantity: 1,
  expectedReturnAt: "",
});

const damageForm = reactive<DamageForm>({
  damageDescription: "",
  compensationAmount: 0,
});

const hasGlobalAccess = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("*");
});

const manageableClubIds = computed(() => {
  const roles = auth.value?.roles ?? [];
  return [
    ...new Set(
      roles
        .filter(
          (role) =>
            role.permissions.includes("*") || role.permissions.includes("material:borrow:manage"),
        )
        .flatMap((role) => role.clubIds ?? [])
        .filter((clubId) => Number.isFinite(clubId)),
    ),
  ].sort((a, b) => a - b);
});

const canManageMaterials = computed(
  () => hasGlobalAccess.value || manageableClubIds.value.length > 0,
);

const visibleClubs = computed(() => {
  if (hasGlobalAccess.value) return clubs.value;
  return clubs.value.filter((club) => manageableClubIds.value.includes(club.id));
});

const activeClubOptions = computed(() => [
  ...(hasGlobalAccess.value ? [{ id: 0, name: "全部社团" }] : []),
  ...visibleClubs.value,
]);

const activeMaterials = computed(() => {
  const keyword = materialSearch.value.trim().toLowerCase();
  return materials.value
    .filter((material) => activeClubId.value === 0 || material.clubId === activeClubId.value)
    .filter((material) => {
      if (!keyword) return true;
      return [material.name, material.specification, material.storageLocation, material.clubName]
        .filter(Boolean)
        .some((text) => String(text).toLowerCase().includes(keyword));
    });
});

const activeBorrows = computed(() =>
  borrows.value.filter(
    (borrow) => activeClubId.value === 0 || borrow.clubId === activeClubId.value,
  ),
);

const materialSummary = computed(() => {
  const total = activeMaterials.value.reduce((sum, item) => sum + item.totalQuantity, 0);
  const available = activeMaterials.value.reduce((sum, item) => sum + item.availableQuantity, 0);
  return `共 ${activeMaterials.value.length} 类物资，可用 ${available} / 总量 ${total}`;
});

const borrowedCount = computed(
  () => activeBorrows.value.filter((borrow) => borrow.status === "borrowed").length,
);

const overdueCount = computed(() => activeBorrows.value.filter((borrow) => borrow.overdue).length);

const materialRules: FormRules<MaterialForm> = {
  clubId: [{ required: true, message: "请选择所属社团", trigger: "change" }],
  name: [
    { required: true, message: "请输入物资名称", trigger: "blur" },
    { min: 1, max: 255, message: "物资名称不能超过 255 个字符", trigger: "blur" },
  ],
  totalQuantity: [
    { required: true, message: "请输入总数量", trigger: "change" },
    { type: "number", min: 1, message: "总数量必须大于 0", trigger: "change" },
  ],
  availableQuantity: [{ type: "number", min: 0, message: "可用数量不能小于 0", trigger: "change" }],
};

const borrowRules: FormRules<BorrowForm> = {
  materialId: [{ required: true, message: "请选择物资", trigger: "change" }],
  quantity: [
    { required: true, message: "请输入借用数量", trigger: "change" },
    { type: "number", min: 1, message: "借用数量必须大于 0", trigger: "change" },
  ],
  expectedReturnAt: [
    {
      validator: (_rule, value, callback) => {
        if (value && beijingDateTimeTimestamp(value) <= Date.now()) {
          callback(new Error("预计归还时间必须晚于当前时间"));
          return;
        }
        callback();
      },
      trigger: "change",
    },
  ],
};

const damageRules: FormRules<DamageForm> = {
  damageDescription: [
    { required: true, message: "请输入损坏说明", trigger: "blur" },
    { min: 2, max: 255, message: "损坏说明需要 2-255 个字符", trigger: "blur" },
  ],
  compensationAmount: [
    { required: true, message: "请输入赔偿金额", trigger: "change" },
    { type: "number", min: 0, message: "赔偿金额不能为负数", trigger: "change" },
  ],
};

const materialStatusLabel: Record<string, string> = {
  active: "可借用",
  disabled: "停用",
};

const borrowStatusLabel: Record<string, string> = {
  borrowed: "借用中",
  returned: "已归还",
  damaged: "已损坏",
};

const borrowStatusType: Record<string, "success" | "warning" | "danger" | "info"> = {
  borrowed: "warning",
  returned: "success",
  damaged: "danger",
};

function ensureActiveClub() {
  if (activeClubId.value !== undefined) return;
  activeClubId.value = hasGlobalAccess.value ? 0 : visibleClubs.value[0]?.id;
}

async function loadClubs() {
  const userId = auth.value?.user.id;
  if (!userId) {
    clubs.value = [];
    return;
  }

  clubs.value = await requestJson<ClubOption[]>(`/api/clubs?viewerUserId=${userId}`);
  ensureActiveClub();
}

async function loadMaterials() {
  if (!canManageMaterials.value) return;
  loading.value = true;
  try {
    const query =
      activeClubId.value && activeClubId.value !== 0 ? `?clubId=${activeClubId.value}` : "";
    materials.value = await requestJson<Material[]>(`/api/materials${query}`);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "加载物资失败");
    materials.value = [];
  } finally {
    loading.value = false;
  }
}

async function loadBorrows() {
  if (!canManageMaterials.value) return;
  borrowLoading.value = true;
  try {
    const params = new URLSearchParams();
    if (activeClubId.value && activeClubId.value !== 0)
      params.set("clubId", String(activeClubId.value));
    if (borrowStatus.value) params.set("status", borrowStatus.value);
    const query = params.toString() ? `?${params.toString()}` : "";
    borrows.value = await requestJson<MaterialBorrow[]>(`/api/material-borrows${query}`);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "加载借用记录失败");
    borrows.value = [];
  } finally {
    borrowLoading.value = false;
  }
}

async function refreshData() {
  await Promise.all([loadMaterials(), loadBorrows()]);
}

function openCreateMaterial() {
  materialForm.clubId =
    activeClubId.value && activeClubId.value !== 0 ? activeClubId.value : visibleClubs.value[0]?.id;
  materialForm.name = "";
  materialForm.specification = "";
  materialForm.totalQuantity = 1;
  materialForm.availableQuantity = undefined;
  materialForm.storageLocation = "";
  materialDialogVisible.value = true;
}

function openBorrow(material: Material) {
  currentMaterial.value = material;
  borrowForm.materialId = material.id;
  borrowForm.clubId = material.clubId;
  borrowForm.quantity = 1;
  borrowForm.expectedReturnAt = "";
  borrowDialogVisible.value = true;
}

function openDamage(borrow: MaterialBorrow) {
  currentBorrow.value = borrow;
  damageForm.damageDescription = "";
  damageForm.compensationAmount = 0;
  damageDialogVisible.value = true;
}

async function submitMaterial() {
  if (!materialFormRef.value) return;
  const valid = await materialFormRef.value.validate().catch(() => false);
  if (!valid || !materialForm.clubId) return;

  if (
    materialForm.availableQuantity !== undefined &&
    materialForm.availableQuantity > materialForm.totalQuantity
  ) {
    ElMessage.error("可用数量不能大于总数量");
    return;
  }

  submitting.value = true;
  try {
    await requestJson<Material>("/api/materials", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        clubId: materialForm.clubId,
        name: materialForm.name.trim(),
        specification: materialForm.specification.trim() || null,
        totalQuantity: materialForm.totalQuantity,
        availableQuantity: materialForm.availableQuantity ?? null,
        storageLocation: materialForm.storageLocation.trim() || null,
        status: "active",
      }),
    });
    materialDialogVisible.value = false;
    ElMessage.success("物资已新增");
    await refreshData();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "新增物资失败");
  } finally {
    submitting.value = false;
  }
}

async function submitBorrow() {
  if (!borrowFormRef.value || !currentMaterial.value) return;
  const valid = await borrowFormRef.value.validate().catch(() => false);
  if (!valid || !borrowForm.materialId || !borrowForm.clubId) return;

  if (borrowForm.quantity > currentMaterial.value.availableQuantity) {
    ElMessage.error("借用数量不能大于可用数量");
    return;
  }

  submitting.value = true;
  try {
    await requestJson<MaterialBorrow>("/api/material-borrows", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        materialId: borrowForm.materialId,
        clubId: borrowForm.clubId,
        quantity: borrowForm.quantity,
        expectedReturnAt: borrowForm.expectedReturnAt
          ? beijingDateTimeToUtcIso(borrowForm.expectedReturnAt)
          : null,
      }),
    });
    borrowDialogVisible.value = false;
    ElMessage.success("借用已登记");
    await refreshData();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "登记借用失败");
  } finally {
    submitting.value = false;
  }
}

async function returnBorrow(borrow: MaterialBorrow) {
  try {
    await ElMessageBox.confirm(
      `确认登记「${borrow.materialName}」已归还？归还后库存会回补 ${borrow.quantity} 件。`,
      "登记归还",
      {
        confirmButtonText: "确认归还",
        cancelButtonText: "取消",
        type: "success",
      },
    );
  } catch {
    return;
  }

  submitting.value = true;
  try {
    await requestJson<MaterialBorrow>(`/api/material-borrows/${borrow.id}/return`, {
      method: "POST",
    });
    ElMessage.success("归还已登记");
    await refreshData();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "登记归还失败");
  } finally {
    submitting.value = false;
  }
}

async function submitDamage() {
  if (!damageFormRef.value || !currentBorrow.value) return;
  const valid = await damageFormRef.value.validate().catch(() => false);
  if (!valid) return;

  submitting.value = true;
  try {
    await requestJson<MaterialBorrow>(`/api/material-borrows/${currentBorrow.value.id}/damage`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        damageDescription: damageForm.damageDescription.trim(),
        compensationAmount: damageForm.compensationAmount,
      }),
    });
    damageDialogVisible.value = false;
    ElMessage.success("损坏情况已登记");
    await refreshData();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "登记损坏失败");
  } finally {
    submitting.value = false;
  }
}

function formatDateTime(value?: string | null) {
  return value ? formatVenueReservationDateTime(value) : "-";
}

function materialStatusName(status: string) {
  return materialStatusLabel[status] || status;
}

function borrowStatusName(status: string) {
  return borrowStatusLabel[status] || status;
}

function borrowStatusTagType(status: string) {
  return borrowStatusType[status] || "info";
}

function borrowTimeText(borrow: MaterialBorrow) {
  const expected = borrow.expectedReturnAt
    ? `预计 ${formatDateTime(borrow.expectedReturnAt)}`
    : "未填写预计归还";
  return `${formatDateTime(borrow.borrowAt)} · ${expected}`;
}

function isBorrowInProgress(borrow: MaterialBorrow) {
  return borrow.status === "borrowed";
}

function isExpectedReturnInPast(value?: string) {
  if (!value) return false;
  return venueReservationTimestamp(value) < Date.now();
}

function rowClassName({ row }: { row: MaterialBorrow }) {
  return row.overdue ? "overdue-row" : "";
}

async function changeClub() {
  await refreshData();
}

async function changeBorrowStatus() {
  await loadBorrows();
}

onMounted(async () => {
  if (!canManageMaterials.value) return;
  await loadClubs();
  ensureActiveClub();
  await refreshData();
});
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <div>
        <h2>物资借还</h2>
        <p class="subtitle">登记活动物资借用、归还、损坏和赔偿情况。</p>
      </div>
      <div class="toolbar-actions">
        <el-select
          v-model="activeClubId"
          class="club-select"
          placeholder="选择社团"
          @change="changeClub"
        >
          <el-option
            v-for="club in activeClubOptions"
            :key="club.id"
            :label="club.name"
            :value="club.id"
          />
        </el-select>
        <el-button
          :icon="Plus"
          type="primary"
          :disabled="!canManageMaterials"
          @click="openCreateMaterial"
        >
          新增物资
        </el-button>
        <el-button :icon="Refresh" :loading="loading || borrowLoading" @click="refreshData"
          >刷新</el-button
        >
      </div>
    </div>

    <el-alert
      v-if="!canManageMaterials"
      title="当前账号没有物资借还管理权限。"
      type="warning"
      show-icon
      class="notice"
    />

    <div v-else class="stats">
      <div class="stat-item">
        <span class="stat-label">库存</span>
        <strong>{{ materialSummary }}</strong>
      </div>
      <div class="stat-item">
        <span class="stat-label">借用中</span>
        <strong>{{ borrowedCount }}</strong>
      </div>
      <div class="stat-item danger">
        <span class="stat-label">逾期</span>
        <strong>{{ overdueCount }}</strong>
      </div>
    </div>

    <section v-if="canManageMaterials" class="section">
      <div class="section-head">
        <div>
          <h3>物资库存</h3>
          <p class="subtitle">可用数量会随借用和归还自动更新。</p>
        </div>
        <el-input
          v-model="materialSearch"
          clearable
          placeholder="搜索物资、规格、位置或社团"
          class="search-input"
        />
      </div>

      <el-table v-loading="loading" :data="activeMaterials" stripe empty-text="暂无物资">
        <el-table-column label="物资" min-width="210">
          <template #default="{ row }">
            <div class="primary-text">{{ row.name }}</div>
            <div class="muted">{{ row.specification || "无规格" }}</div>
          </template>
        </el-table-column>
        <el-table-column prop="clubName" label="社团" min-width="150" />
        <el-table-column label="库存" width="170">
          <template #default="{ row }">
            <div>{{ row.availableQuantity }} / {{ row.totalQuantity }}</div>
            <el-progress
              :percentage="
                Math.round((row.availableQuantity / Math.max(1, row.totalQuantity)) * 100)
              "
              :show-text="false"
              :stroke-width="6"
            />
          </template>
        </el-table-column>
        <el-table-column prop="storageLocation" label="存放位置" min-width="160" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.status === 'active' ? 'success' : 'info'" size="small">
              {{ materialStatusName(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-tooltip
              :disabled="row.status === 'active' && row.availableQuantity > 0"
              content="物资停用或库存不足"
              placement="top"
            >
              <span>
                <el-button
                  :icon="Box"
                  type="primary"
                  size="small"
                  :disabled="row.status !== 'active' || row.availableQuantity <= 0"
                  @click="openBorrow(row)"
                >
                  借用
                </el-button>
              </span>
            </el-tooltip>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <section v-if="canManageMaterials" class="section">
      <div class="section-head">
        <div>
          <h3>借用记录</h3>
          <p class="subtitle">借用中的记录可以登记归还或损坏。</p>
        </div>
        <el-select
          v-model="borrowStatus"
          class="status-select"
          placeholder="全部状态"
          clearable
          @change="changeBorrowStatus"
        >
          <el-option label="借用中" value="borrowed" />
          <el-option label="已归还" value="returned" />
          <el-option label="已损坏" value="damaged" />
        </el-select>
      </div>

      <el-table
        v-loading="borrowLoading"
        :data="activeBorrows"
        stripe
        empty-text="暂无借用记录"
        :row-class-name="rowClassName"
      >
        <el-table-column label="物资/借用人" min-width="210">
          <template #default="{ row }">
            <div class="primary-text">{{ row.materialName }}</div>
            <div class="muted">{{ row.borrowerName || "未知用户" }} · {{ row.clubName }}</div>
          </template>
        </el-table-column>
        <el-table-column label="数量" width="80">
          <template #default="{ row }">{{ row.quantity }}</template>
        </el-table-column>
        <el-table-column label="时间" min-width="240">
          <template #default="{ row }">
            <div>{{ borrowTimeText(row) }}</div>
            <div v-if="row.returnAt" class="muted">完成 {{ formatDateTime(row.returnAt) }}</div>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-tag :type="borrowStatusTagType(row.status)" size="small">
              {{ borrowStatusName(row.status) }}
            </el-tag>
            <div v-if="row.overdue" class="danger-text">已逾期</div>
            <div
              v-else-if="
                isExpectedReturnInPast(row.expectedReturnAt || undefined) &&
                row.status === 'borrowed'
              "
              class="danger-text"
            >
              已逾期
            </div>
          </template>
        </el-table-column>
        <el-table-column label="损坏/赔偿" min-width="180">
          <template #default="{ row }">
            <span v-if="row.status !== 'damaged'" class="muted">-</span>
            <div v-else>
              <div>{{ row.damageDescription }}</div>
              <div class="muted">赔偿 {{ row.compensationAmount.toFixed(2) }} 元</div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="170" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button
                :icon="Check"
                type="success"
                size="small"
                :disabled="!isBorrowInProgress(row)"
                @click="returnBorrow(row)"
              >
                归还
              </el-button>
              <el-button
                :icon="Warning"
                type="danger"
                size="small"
                plain
                :disabled="!isBorrowInProgress(row)"
                @click="openDamage(row)"
              >
                损坏
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <el-dialog v-model="materialDialogVisible" title="新增物资" width="520px">
      <el-form
        ref="materialFormRef"
        :model="materialForm"
        :rules="materialRules"
        label-position="top"
      >
        <el-form-item label="所属社团" prop="clubId">
          <el-select
            v-model="materialForm.clubId"
            filterable
            class="full-width"
            placeholder="选择社团"
          >
            <el-option
              v-for="club in visibleClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="物资名称" prop="name">
          <el-input v-model="materialForm.name" maxlength="255" />
        </el-form-item>
        <el-form-item label="规格">
          <el-input
            v-model="materialForm.specification"
            maxlength="255"
            placeholder="如：2m 展架、无线麦克风"
          />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="总数量" prop="totalQuantity">
            <el-input-number
              v-model="materialForm.totalQuantity"
              :min="1"
              :step="1"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="可用数量" prop="availableQuantity">
            <el-input-number
              v-model="materialForm.availableQuantity"
              :min="0"
              :max="materialForm.totalQuantity"
              :step="1"
              class="full-width"
            />
          </el-form-item>
        </div>
        <el-form-item label="存放位置">
          <el-input
            v-model="materialForm.storageLocation"
            maxlength="255"
            placeholder="如：学生活动中心 A102"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="materialDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitMaterial">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="borrowDialogVisible" title="登记借用" width="500px">
      <el-form ref="borrowFormRef" :model="borrowForm" :rules="borrowRules" label-position="top">
        <el-form-item label="物资">
          <el-input :model-value="currentMaterial?.name ?? ''" disabled />
        </el-form-item>
        <el-form-item label="可用库存">
          <el-input :model-value="String(currentMaterial?.availableQuantity ?? 0)" disabled />
        </el-form-item>
        <el-form-item label="借用数量" prop="quantity">
          <el-input-number
            v-model="borrowForm.quantity"
            :min="1"
            :max="currentMaterial?.availableQuantity ?? 1"
            :step="1"
            class="full-width"
          />
        </el-form-item>
        <el-form-item label="预计归还时间" prop="expectedReturnAt">
          <el-date-picker
            v-model="borrowForm.expectedReturnAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            format="YYYY-MM-DD HH:mm"
            placeholder="可选"
            class="full-width"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="borrowDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitBorrow">登记借用</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="damageDialogVisible" title="登记损坏" width="520px">
      <el-form ref="damageFormRef" :model="damageForm" :rules="damageRules" label-position="top">
        <el-alert
          :title="`损坏登记后「${currentBorrow?.materialName ?? '物资'}」本次借用不会回补库存。`"
          type="warning"
          show-icon
          class="notice"
        />
        <el-form-item label="损坏说明" prop="damageDescription">
          <el-input
            v-model="damageForm.damageDescription"
            type="textarea"
            maxlength="255"
            show-word-limit
            placeholder="描述损坏情况、责任确认或处理方式"
          />
        </el-form-item>
        <el-form-item label="赔偿金额" prop="compensationAmount">
          <el-input-number
            v-model="damageForm.compensationAmount"
            :min="0"
            :precision="2"
            :step="10"
            class="full-width"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="damageDialogVisible = false">取消</el-button>
        <el-button type="danger" :loading="submitting" @click="submitDamage">确认损坏</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  max-width: 1120px;
  margin: 0 auto;
}
.toolbar,
.section-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 12px;
}
.toolbar h2,
.section-head h3 {
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
.stats {
  display: grid;
  grid-template-columns: 1fr 160px 160px;
  gap: 12px;
  margin-bottom: 18px;
}
.stat-item {
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 12px;
  background: #fff;
}
.stat-label {
  display: block;
  margin-bottom: 6px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.section {
  margin-top: 20px;
}
.club-select {
  width: 180px;
}
.status-select {
  width: 140px;
}
.search-input {
  max-width: 340px;
}
.primary-text {
  font-weight: 600;
}
.muted {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.danger,
.danger-text {
  color: var(--el-color-danger);
}
.danger-text {
  margin-top: 4px;
  font-size: 12px;
}
.full-width {
  width: 100%;
}
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
:deep(.overdue-row) {
  --el-table-tr-bg-color: var(--el-color-danger-light-9);
}
@media (max-width: 760px) {
  .toolbar,
  .section-head,
  .toolbar-actions {
    align-items: stretch;
    flex-direction: column;
  }

  .stats,
  .form-grid {
    grid-template-columns: 1fr;
  }

  .club-select,
  .status-select,
  .search-input {
    width: 100%;
    max-width: none;
  }
}
</style>

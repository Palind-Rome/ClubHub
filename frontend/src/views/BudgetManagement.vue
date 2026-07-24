<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { Money, Plus, Refresh, Select, Warning } from "@element-plus/icons-vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { readAuth } from "../authSession";
import {
  BUDGET_ACCESS_PERMISSIONS,
  BUDGET_ACCOUNT_MANAGE_PERMISSION,
  BUDGET_APPLY_PERMISSION,
  BUDGET_REVIEW_PERMISSION,
} from "../budgetPermissions";
import type { BudgetAccount, BudgetApplication, BudgetTransaction } from "../api/models";
import { requestJson } from "../composables/useApiRequest";
import { formatVenueReservationDateTime } from "../beijingTime";

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
}

interface AccountForm {
  clubId?: number;
  fiscalYear: string;
  accountName: string;
  initialAmount: number;
  status: "active" | "closed";
}

interface ApplicationForm {
  accountId?: number;
  activityId?: number;
  type: "activity_budget" | "purchase" | "reimbursement";
  title: string;
  amount: number;
  purpose: string;
  detail: string;
}

interface ReviewForm {
  approved: boolean;
  comment: string;
}

const auth = ref(readAuth());
const clubs = ref<ClubOption[]>([]);
const accounts = ref<BudgetAccount[]>([]);
const applications = ref<BudgetApplication[]>([]);
const transactions = ref<BudgetTransaction[]>([]);
const activities = ref<ActivityOption[]>([]);
const activeClubId = ref<number>();
const applicationStatus = ref("");
const loading = ref(false);
const accountDialogVisible = ref(false);
const applicationDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const editingAccount = ref<BudgetAccount | null>(null);
const reviewTarget = ref<BudgetApplication | null>(null);
const accountFormRef = ref<FormInstance>();
const applicationFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();

const accountForm = reactive<AccountForm>({
  clubId: undefined,
  fiscalYear: String(new Date().getFullYear()),
  accountName: "",
  initialAmount: 0,
  status: "active",
});

const applicationForm = reactive<ApplicationForm>({
  accountId: undefined,
  activityId: undefined,
  type: "activity_budget",
  title: "",
  amount: 0,
  purpose: "",
  detail: "",
});

const reviewForm = reactive<ReviewForm>({
  approved: true,
  comment: "",
});

const hasGlobalAccess = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("*");
});

function roleHasPermission(role: { permissions: string[] }, permission: string) {
  return role.permissions.includes("*") || role.permissions.includes(permission);
}

function hasSystemPermission(permission: string) {
  const roles = auth.value?.roles ?? [];
  return (
    hasGlobalAccess.value ||
    roles.some((role) => role.scope === "system" && roleHasPermission(role, permission))
  );
}

function clubIdsForPermission(permission: string) {
  const roles = auth.value?.roles ?? [];
  return [
    ...new Set(
      roles
        .filter((role) => role.scope !== "system" && roleHasPermission(role, permission))
        .flatMap((role) => role.clubIds ?? [])
        .filter((clubId) => Number.isFinite(clubId)),
    ),
  ].sort((a, b) => a - b);
}

function canApplyForClub(clubId: number) {
  return (
    hasSystemPermission(BUDGET_APPLY_PERMISSION) ||
    clubIdsForPermission(BUDGET_APPLY_PERMISSION).includes(clubId)
  );
}

function canReviewForClub(clubId: number) {
  return (
    hasSystemPermission(BUDGET_REVIEW_PERMISSION) ||
    clubIdsForPermission(BUDGET_REVIEW_PERMISSION).includes(clubId)
  );
}

function canManageAccountForClub(clubId: number) {
  return (
    hasSystemPermission(BUDGET_ACCOUNT_MANAGE_PERMISSION) ||
    clubIdsForPermission(BUDGET_ACCOUNT_MANAGE_PERMISSION).includes(clubId)
  );
}

const canViewAllBudgets = computed(() =>
  BUDGET_ACCESS_PERMISSIONS.some((permission) => hasSystemPermission(permission)),
);

const visibleBudgetClubIds = computed(() => [
  ...new Set(BUDGET_ACCESS_PERMISSIONS.flatMap((permission) => clubIdsForPermission(permission))),
]);

const visibleClubs = computed(() => {
  if (canViewAllBudgets.value) return clubs.value;
  return clubs.value.filter((club) => visibleBudgetClubIds.value.includes(club.id));
});

const activeClubOptions = computed(() => [
  ...(canViewAllBudgets.value ? [{ id: 0, name: "全部社团" }] : []),
  ...visibleClubs.value,
]);

const accountManageClubs = computed(() =>
  clubs.value.filter((club) => canManageAccountForClub(club.id)),
);

const applicantAccounts = computed(() =>
  accounts.value.filter(
    (account) => account.status === "active" && canApplyForClub(account.clubId),
  ),
);

const activeApplications = computed(() =>
  applications.value.filter(
    (item) => !applicationStatus.value || item.status === applicationStatus.value,
  ),
);

const activeTransactions = computed(() =>
  transactions.value.filter(
    (item) => activeClubId.value === 0 || item.clubId === activeClubId.value,
  ),
);

const pendingReviewCount = computed(
  () =>
    applications.value.filter((item) => item.status === "pending" && canReviewForClub(item.clubId))
      .length,
);

const totalInitialAmount = computed(() =>
  accounts.value.reduce((sum, account) => sum + account.initialAmount, 0),
);

const totalRemainingAmount = computed(() =>
  accounts.value.reduce((sum, account) => sum + account.remainingAmount, 0),
);

const accountRules: FormRules<AccountForm> = {
  clubId: [{ required: true, message: "请选择社团", trigger: "change" }],
  fiscalYear: [
    { required: true, message: "请输入经费年度", trigger: "blur" },
    { min: 4, max: 20, message: "经费年度长度应在 4 到 20 个字符之间", trigger: "blur" },
  ],
  accountName: [
    { required: true, message: "请输入账户名称", trigger: "blur" },
    { max: 255, message: "账户名称不能超过 255 个字符", trigger: "blur" },
  ],
  initialAmount: [
    { required: true, message: "请输入年度额度", trigger: "change" },
    { type: "number", min: 0, message: "年度额度不能小于 0", trigger: "change" },
  ],
};

const applicationRules: FormRules<ApplicationForm> = {
  accountId: [{ required: true, message: "请选择经费账户", trigger: "change" }],
  type: [{ required: true, message: "请选择申请类型", trigger: "change" }],
  title: [
    { required: true, message: "请输入申请标题", trigger: "blur" },
    { max: 255, message: "申请标题不能超过 255 个字符", trigger: "blur" },
  ],
  amount: [
    { required: true, message: "请输入申请金额", trigger: "change" },
    { type: "number", min: 0.01, message: "申请金额必须大于 0", trigger: "change" },
  ],
  purpose: [
    { required: true, message: "请输入经费用途", trigger: "blur" },
    { max: 255, message: "经费用途不能超过 255 个字符", trigger: "blur" },
  ],
};

const reviewRules: FormRules<ReviewForm> = {
  comment: [{ max: 255, message: "审核意见不能超过 255 个字符", trigger: "blur" }],
};

function ensureActiveClub() {
  if (activeClubId.value !== undefined) return;
  activeClubId.value = canViewAllBudgets.value ? 0 : visibleClubs.value[0]?.id;
}

async function loadClubs() {
  const userId = auth.value?.user.id;
  if (!userId) {
    clubs.value = [];
    return false;
  }

  clubs.value = await requestJson<ClubOption[]>(`/api/clubs?viewerUserId=${userId}`);
  ensureActiveClub();
  return true;
}

async function loadAccounts() {
  const params = new URLSearchParams();
  if (activeClubId.value && activeClubId.value !== 0)
    params.set("clubId", String(activeClubId.value));
  const query = params.toString() ? `?${params.toString()}` : "";
  accounts.value = await requestJson<BudgetAccount[]>(`/api/budget/accounts${query}`);
}

async function loadApplications() {
  const params = new URLSearchParams();
  if (activeClubId.value && activeClubId.value !== 0)
    params.set("clubId", String(activeClubId.value));
  const query = params.toString() ? `?${params.toString()}` : "";
  applications.value = await requestJson<BudgetApplication[]>(`/api/budget/applications${query}`);
}

async function loadTransactions() {
  const params = new URLSearchParams();
  if (activeClubId.value && activeClubId.value !== 0)
    params.set("clubId", String(activeClubId.value));
  const query = params.toString() ? `?${params.toString()}` : "";
  transactions.value = await requestJson<BudgetTransaction[]>(`/api/budget/transactions${query}`);
}

async function loadActivities() {
  const userId = auth.value?.user.id;
  const query = userId ? `?currentUserId=${userId}` : "";
  activities.value = await requestJson<ActivityOption[]>(`/api/activities${query}`);
}

async function refreshData() {
  loading.value = true;
  try {
    await Promise.all([loadAccounts(), loadApplications(), loadTransactions(), loadActivities()]);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "加载经费数据失败");
  } finally {
    loading.value = false;
  }
}

async function refreshAllData() {
  loading.value = true;
  try {
    if (!(await loadClubs())) return;
    await Promise.all([loadAccounts(), loadApplications(), loadTransactions(), loadActivities()]);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "加载经费数据失败");
  } finally {
    loading.value = false;
  }
}

function accountLabel(account: BudgetAccount) {
  return `${account.clubName} / ${account.fiscalYear} / ${account.accountName}`;
}

function formatMoney(value?: number | null) {
  return new Intl.NumberFormat("zh-CN", {
    style: "currency",
    currency: "CNY",
    maximumFractionDigits: 2,
  }).format(value ?? 0);
}

function formatDateTime(value?: string | Date | null) {
  if (!value) return "暂无";
  return formatVenueReservationDateTime(value instanceof Date ? value.toISOString() : value);
}

function statusType(status: string) {
  if (status === "approved" || status === "active") return "success";
  if (status === "pending") return "warning";
  if (status === "closed" || status === "cancelled") return "info";
  return "danger";
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    active: "启用",
    closed: "关闭",
    pending: "待审核",
    approved: "已通过",
    rejected: "已驳回",
    cancelled: "已撤销",
  };
  return labels[status] ?? status;
}

function typeLabel(type: string) {
  const labels: Record<string, string> = {
    activity_budget: "活动预算",
    purchase: "采购申请",
    reimbursement: "报销申请",
    commitment: "审批占用",
    expense: "实际支出",
    refund: "退款",
    adjustment: "调整",
  };
  return labels[type] ?? type;
}

function openCreateAccount() {
  const defaultClub =
    activeClubId.value && activeClubId.value !== 0
      ? activeClubId.value
      : accountManageClubs.value[0]?.id;
  if (!defaultClub) {
    ElMessage.warning("当前账号没有维护经费账户的权限。");
    return;
  }

  editingAccount.value = null;
  accountForm.clubId = defaultClub;
  accountForm.fiscalYear = String(new Date().getFullYear());
  accountForm.accountName = `${accountForm.fiscalYear} 年社团活动经费`;
  accountForm.initialAmount = 0;
  accountForm.status = "active";
  accountDialogVisible.value = true;
}

function openEditAccount(account: BudgetAccount) {
  if (!canManageAccountForClub(account.clubId)) {
    ElMessage.warning("当前账号没有维护该社团经费账户的权限。");
    return;
  }

  editingAccount.value = account;
  accountForm.clubId = account.clubId;
  accountForm.fiscalYear = account.fiscalYear;
  accountForm.accountName = account.accountName;
  accountForm.initialAmount = account.initialAmount;
  accountForm.status = account.status;
  accountDialogVisible.value = true;
}

async function submitAccount() {
  if (!(await accountFormRef.value?.validate())) return;

  try {
    if (editingAccount.value) {
      await requestJson<BudgetAccount>(`/api/budget/accounts/${editingAccount.value.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          accountName: accountForm.accountName,
          initialAmount: accountForm.initialAmount,
          status: accountForm.status,
        }),
      });
      ElMessage.success("经费账户已更新");
    } else {
      await requestJson<BudgetAccount>("/api/budget/accounts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clubId: accountForm.clubId,
          fiscalYear: accountForm.fiscalYear,
          accountName: accountForm.accountName,
          initialAmount: accountForm.initialAmount,
        }),
      });
      ElMessage.success("经费账户已创建");
    }

    accountDialogVisible.value = false;
    await refreshData();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "保存经费账户失败");
  }
}

function openCreateApplication() {
  if (applicantAccounts.value.length === 0) {
    ElMessage.warning("当前账号没有可提交申请的启用经费账户。");
    return;
  }

  const preferredAccount =
    activeClubId.value && activeClubId.value !== 0
      ? applicantAccounts.value.find((account) => account.clubId === activeClubId.value)
      : applicantAccounts.value[0];
  applicationForm.accountId = preferredAccount?.id;
  applicationForm.activityId = undefined;
  applicationForm.type = "activity_budget";
  applicationForm.title = "";
  applicationForm.amount = 0;
  applicationForm.purpose = "";
  applicationForm.detail = "";
  applicationDialogVisible.value = true;
}

function activityOptionsForSelectedAccount() {
  const account = accounts.value.find((item) => item.id === applicationForm.accountId);
  if (!account) return [];
  return activities.value.filter((activity) => activity.clubId === account.clubId);
}

async function submitApplication() {
  if (!(await applicationFormRef.value?.validate())) return;

  try {
    await requestJson<BudgetApplication>("/api/budget/applications", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        accountId: applicationForm.accountId,
        activityId: applicationForm.activityId ?? null,
        type: applicationForm.type,
        title: applicationForm.title,
        amount: applicationForm.amount,
        purpose: applicationForm.purpose,
        detail: applicationForm.detail.trim() || null,
      }),
    });

    applicationDialogVisible.value = false;
    ElMessage.success("经费申请已提交");
    await refreshData();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "提交经费申请失败");
  }
}

function openReviewApplication(application: BudgetApplication, approved: boolean) {
  if (!canReviewForClub(application.clubId)) {
    ElMessage.warning("当前账号没有该社团的经费审核权限。");
    return;
  }

  reviewTarget.value = application;
  reviewForm.approved = approved;
  reviewForm.comment = "";
  reviewDialogVisible.value = true;
}

async function submitReview() {
  if (!reviewTarget.value || !(await reviewFormRef.value?.validate())) return;

  try {
    await requestJson<BudgetApplication>(
      `/api/budget/applications/${reviewTarget.value.id}/review`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          approved: reviewForm.approved,
          comment: reviewForm.comment.trim() || null,
        }),
      },
    );

    reviewDialogVisible.value = false;
    ElMessage.success("经费审核结果已保存");
    await refreshData();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "审核经费申请失败");
  }
}

async function cancelApplication(application: BudgetApplication) {
  const currentUserId = auth.value?.user.id;
  if (application.applicantUserId !== currentUserId && !canApplyForClub(application.clubId)) {
    ElMessage.warning("当前账号没有撤销该经费申请的权限。");
    return;
  }

  try {
    await ElMessageBox.confirm("撤销后该申请将不再进入审核，是否继续？", "撤销经费申请", {
      confirmButtonText: "撤销申请",
      cancelButtonText: "取消",
      type: "warning",
    });
    await requestJson<BudgetApplication>(`/api/budget/applications/${application.id}/cancel`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ comment: "申请人撤销" }),
    });
    ElMessage.success("经费申请已撤销");
    await refreshData();
  } catch (error) {
    if (error === "cancel") return;
    ElMessage.error(error instanceof Error ? error.message : "撤销经费申请失败");
  }
}

onMounted(() => {
  refreshAllData();
});
</script>

<template>
  <section class="budget-page" v-loading="loading">
    <div class="page-header">
      <div>
        <p class="eyebrow">社团经费闭环</p>
        <h1>经费管理</h1>
        <p class="summary">账户额度、经费申请、审核记录和流水统一管理，余额由流水汇总计算。</p>
      </div>
      <div class="toolbar">
        <el-select v-model="activeClubId" class="club-select" @change="refreshData">
          <el-option
            v-for="club in activeClubOptions"
            :key="club.id"
            :label="club.name"
            :value="club.id"
          />
        </el-select>
        <el-button :icon="Refresh" @click="refreshAllData">刷新</el-button>
        <el-button
          v-if="accountManageClubs.length > 0"
          type="primary"
          :icon="Plus"
          @click="openCreateAccount"
        >
          新增账户
        </el-button>
        <el-button
          v-if="applicantAccounts.length > 0"
          type="success"
          :icon="Plus"
          @click="openCreateApplication"
        >
          提交申请
        </el-button>
      </div>
    </div>

    <el-row :gutter="16" class="summary-row">
      <el-col :xs="24" :sm="8">
        <el-card shadow="never">
          <div class="metric">
            <Money class="metric-icon" />
            <div>
              <span>年度总额度</span>
              <strong>{{ formatMoney(totalInitialAmount) }}</strong>
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never">
          <div class="metric">
            <Select class="metric-icon success" />
            <div>
              <span>当前剩余额度</span>
              <strong>{{ formatMoney(totalRemainingAmount) }}</strong>
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never">
          <div class="metric">
            <Warning class="metric-icon warning" />
            <div>
              <span>待审核申请</span>
              <strong>{{ pendingReviewCount }}</strong>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="section-card">
      <template #header>
        <div class="section-header">
          <span>年度经费账户</span>
          <span class="section-hint">额度只在账户中维护，已用金额由流水自动汇总</span>
        </div>
      </template>
      <el-table :data="accounts" empty-text="暂无经费账户">
        <el-table-column prop="clubName" label="社团" min-width="140" />
        <el-table-column prop="fiscalYear" label="年度" width="100" />
        <el-table-column prop="accountName" label="账户名称" min-width="180" />
        <el-table-column label="年度额度" width="140">
          <template #default="{ row }">{{ formatMoney(row.initialAmount) }}</template>
        </el-table-column>
        <el-table-column label="已占用" width="140">
          <template #default="{ row }">{{ formatMoney(row.committedAmount) }}</template>
        </el-table-column>
        <el-table-column label="剩余" width="140">
          <template #default="{ row }">{{ formatMoney(row.remainingAmount) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="canManageAccountForClub(row.clubId)"
              size="small"
              text
              type="primary"
              @click="openEditAccount(row)"
            >
              维护
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" class="section-card">
      <template #header>
        <div class="section-header">
          <span>经费申请与审核</span>
          <el-select
            v-model="applicationStatus"
            class="status-select"
            clearable
            placeholder="全部状态"
          >
            <el-option label="待审核" value="pending" />
            <el-option label="已通过" value="approved" />
            <el-option label="已驳回" value="rejected" />
            <el-option label="已撤销" value="cancelled" />
          </el-select>
        </div>
      </template>
      <el-table :data="activeApplications" empty-text="暂无经费申请">
        <el-table-column prop="clubName" label="社团" min-width="120" />
        <el-table-column label="类型" width="110">
          <template #default="{ row }">{{ typeLabel(row.type) }}</template>
        </el-table-column>
        <el-table-column prop="title" label="申请标题" min-width="180" />
        <el-table-column label="金额" width="130">
          <template #default="{ row }">{{ formatMoney(row.amount) }}</template>
        </el-table-column>
        <el-table-column prop="purpose" label="用途" min-width="160" />
        <el-table-column prop="applicantName" label="申请人" width="110" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="提交时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.submittedAt) }}</template>
        </el-table-column>
        <el-table-column label="审核意见" min-width="160">
          <template #default="{ row }">{{ row.reviewComment || "暂无" }}</template>
        </el-table-column>
        <el-table-column label="操作" width="210" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="row.status === 'pending' && canReviewForClub(row.clubId)"
              size="small"
              type="success"
              text
              @click="openReviewApplication(row, true)"
            >
              通过
            </el-button>
            <el-button
              v-if="row.status === 'pending' && canReviewForClub(row.clubId)"
              size="small"
              type="danger"
              text
              @click="openReviewApplication(row, false)"
            >
              驳回
            </el-button>
            <el-button
              v-if="
                row.status === 'pending' &&
                (row.applicantUserId === auth?.user.id || canApplyForClub(row.clubId))
              "
              size="small"
              type="warning"
              text
              @click="cancelApplication(row)"
            >
              撤销
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" class="section-card">
      <template #header>
        <div class="section-header">
          <span>经费流水</span>
          <span class="section-hint">审批通过会自动生成一条占用流水</span>
        </div>
      </template>
      <el-table :data="activeTransactions" empty-text="暂无经费流水">
        <el-table-column prop="clubName" label="社团" min-width="130" />
        <el-table-column label="类型" width="110">
          <template #default="{ row }">{{ typeLabel(row.type) }}</template>
        </el-table-column>
        <el-table-column label="金额" width="130">
          <template #default="{ row }">{{ formatMoney(row.amount) }}</template>
        </el-table-column>
        <el-table-column prop="description" label="说明" min-width="220" />
        <el-table-column label="发生时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.occurredAt) }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog
      v-model="accountDialogVisible"
      :title="editingAccount ? '维护经费账户' : '新增经费账户'"
      width="560px"
    >
      <el-form ref="accountFormRef" :model="accountForm" :rules="accountRules" label-width="110px">
        <el-form-item label="所属社团" prop="clubId">
          <el-select
            v-model="accountForm.clubId"
            :disabled="Boolean(editingAccount)"
            placeholder="请选择社团"
          >
            <el-option
              v-for="club in accountManageClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="经费年度" prop="fiscalYear">
          <el-input v-model="accountForm.fiscalYear" :disabled="Boolean(editingAccount)" />
        </el-form-item>
        <el-form-item label="账户名称" prop="accountName">
          <el-input v-model="accountForm.accountName" placeholder="如：2026 年社团活动经费" />
        </el-form-item>
        <el-form-item label="年度额度" prop="initialAmount">
          <el-input-number
            v-model="accountForm.initialAmount"
            :min="0"
            :precision="2"
            :step="500"
          />
        </el-form-item>
        <el-form-item v-if="editingAccount" label="账户状态" prop="status">
          <el-radio-group v-model="accountForm.status">
            <el-radio-button value="active">启用</el-radio-button>
            <el-radio-button value="closed">关闭</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="accountDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitAccount">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="applicationDialogVisible" title="提交经费申请" width="640px">
      <el-form
        ref="applicationFormRef"
        :model="applicationForm"
        :rules="applicationRules"
        label-width="110px"
      >
        <el-form-item label="经费账户" prop="accountId">
          <el-select v-model="applicationForm.accountId" filterable placeholder="请选择经费账户">
            <el-option
              v-for="account in applicantAccounts"
              :key="account.id"
              :label="accountLabel(account)"
              :value="account.id"
            >
              <span>{{ accountLabel(account) }}</span>
              <span class="option-hint">剩余 {{ formatMoney(account.remainingAmount) }}</span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="关联活动">
          <el-select
            v-model="applicationForm.activityId"
            clearable
            filterable
            placeholder="可选，按活动标题选择"
          >
            <el-option
              v-for="activity in activityOptionsForSelectedAccount()"
              :key="activity.id"
              :label="`${activity.title} / ${formatDateTime(activity.startTime)}`"
              :value="activity.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="申请类型" prop="type">
          <el-radio-group v-model="applicationForm.type">
            <el-radio-button value="activity_budget">活动预算</el-radio-button>
            <el-radio-button value="purchase">采购申请</el-radio-button>
            <el-radio-button value="reimbursement">报销申请</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="申请标题" prop="title">
          <el-input v-model="applicationForm.title" placeholder="如：迎新活动物料采购" />
        </el-form-item>
        <el-form-item label="申请金额" prop="amount">
          <el-input-number v-model="applicationForm.amount" :min="0" :precision="2" :step="100" />
        </el-form-item>
        <el-form-item label="经费用途" prop="purpose">
          <el-input
            v-model="applicationForm.purpose"
            placeholder="如：活动布置、设备耗材、报名物料"
          />
        </el-form-item>
        <el-form-item label="明细说明">
          <el-input
            v-model="applicationForm.detail"
            type="textarea"
            :rows="4"
            maxlength="4000"
            show-word-limit
            placeholder="填写预算条目、数量、单价或报销说明"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="applicationDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitApplication">提交申请</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="审核经费申请" width="560px">
      <el-descriptions v-if="reviewTarget" :column="1" border class="review-summary">
        <el-descriptions-item label="申请标题">{{ reviewTarget.title }}</el-descriptions-item>
        <el-descriptions-item label="申请金额">{{
          formatMoney(reviewTarget.amount)
        }}</el-descriptions-item>
        <el-descriptions-item label="经费用途">{{ reviewTarget.purpose }}</el-descriptions-item>
      </el-descriptions>
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="110px">
        <el-form-item label="审核结果">
          <el-radio-group v-model="reviewForm.approved">
            <el-radio :value="true">通过</el-radio>
            <el-radio :value="false">驳回</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="审核意见" prop="comment">
          <el-input
            v-model="reviewForm.comment"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit
            placeholder="请输入审核意见"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitReview">保存审核</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.budget-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: flex-start;
  flex-wrap: wrap;
}

.eyebrow {
  margin: 0 0 4px;
  color: var(--el-color-primary);
  font-size: 13px;
  font-weight: 600;
}

h1 {
  margin: 0;
  font-size: 24px;
}

.summary {
  margin: 8px 0 0;
  color: var(--el-text-color-secondary);
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}

.club-select {
  width: 220px;
}

.summary-row {
  row-gap: 16px;
}

.metric {
  display: flex;
  align-items: center;
  gap: 12px;
}

.metric span {
  display: block;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.metric strong {
  display: block;
  margin-top: 4px;
  font-size: 20px;
}

.metric-icon {
  width: 28px;
  height: 28px;
  color: var(--el-color-primary);
}

.metric-icon.success {
  color: var(--el-color-success);
}

.metric-icon.warning {
  color: var(--el-color-warning);
}

.section-card {
  border-radius: 6px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  font-weight: 600;
}

.section-hint {
  color: var(--el-text-color-secondary);
  font-size: 13px;
  font-weight: 400;
}

.status-select {
  width: 140px;
}

.option-hint {
  float: right;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.review-summary {
  margin-bottom: 16px;
}

@media (max-width: 720px) {
  .page-header {
    display: block;
  }

  .toolbar {
    justify-content: flex-start;
    margin-top: 12px;
  }

  .club-select {
    width: 100%;
  }
}
</style>

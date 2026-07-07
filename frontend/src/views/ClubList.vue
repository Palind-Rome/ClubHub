<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { Check, Close, Plus, Refresh, Search, User } from "@element-plus/icons-vue";

type AuditStatus = "pending" | "approved" | "rejected";
type ReviewDecision = "approved" | "rejected";

interface UserRoleSummary {
  roleCode: string;
  roleName: string;
  roleScope: string | null;
  clubId: number | null;
  clubName: string | null;
}

interface UserMembershipSummary {
  clubId: number;
  clubName: string;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  termName: string | null;
  memberStatus: string | null;
}

interface UserSummary {
  id: number;
  username: string | null;
  realName: string | null;
  studentNo: string | null;
  displayName: string;
  accountStatus: string | null;
  roles: UserRoleSummary[];
  memberships: UserMembershipSummary[];
  canSubmitClubApplication: boolean;
  canReviewClubApplication: boolean;
}

interface Club {
  id: number;
  name: string;
  description: string | null;
  category: string | null;
  status: string | null;
  statusText: string;
  logoUrl: string | null;
  presidentUserId: number | null;
  presidentName: string | null;
  advisorName: string | null;
  contactPhone: string | null;
  auditStatus: string | null;
  auditStatusText: string;
  applicantUserId: number | null;
  applicantName: string | null;
  reviewerUserId: number | null;
  reviewerName: string | null;
  reviewComment: string | null;
  foundedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

interface ClubApplication {
  id: number;
  name: string;
  category: string | null;
  description: string | null;
  applicantUserId: number | null;
  applicantName: string | null;
  applyReason: string;
  materialUrl: string;
  auditStatus: AuditStatus;
  auditStatusText: string;
  reviewerUserId: number | null;
  reviewerName: string | null;
  reviewComment: string | null;
  clubStatus: string | null;
  clubStatusText: string;
  foundedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

interface ApiError {
  message?: string;
  title?: string;
}

const users = ref<UserSummary[]>([]);
const currentUserId = ref<number>();
const clubs = ref<Club[]>([]);
const applications = ref<ClubApplication[]>([]);
const loading = ref(true);
const usersLoading = ref(true);
const saving = ref(false);
const reviewing = ref(false);
const error = ref("");
const activeTab = ref("workspace");

const filters = reactive({
  auditStatus: "",
});

const applicationDialogVisible = ref(false);
const applicationFormRef = ref<FormInstance>();
const applicationForm = reactive({
  name: "",
  category: "",
  description: "",
  applyReason: "",
  materialUrl: "",
  advisorName: "",
  contactPhone: "",
});

const reviewDialogVisible = ref(false);
const reviewFormRef = ref<FormInstance>();
const reviewTarget = ref<ClubApplication | null>(null);
const reviewForm = reactive({
  decision: "approved" as ReviewDecision,
  reviewComment: "",
});

const applicationRules: FormRules = {
  name: [{ required: true, message: "请填写社团名称", trigger: "blur" }],
  category: [{ required: true, message: "请填写社团类别", trigger: "blur" }],
  applyReason: [{ required: true, message: "请填写申请理由", trigger: "blur" }],
  materialUrl: [{ required: true, message: "请填写材料地址", trigger: "blur" }],
};

const reviewRules: FormRules = {
  decision: [{ required: true, message: "请选择审核结果", trigger: "change" }],
};

const currentUser = computed(
  () => users.value.find((user) => user.id === currentUserId.value) ?? null,
);
const isReviewer = computed(() => currentUser.value?.canReviewClubApplication ?? false);
const canSubmitApplication = computed(() => currentUser.value?.canSubmitClubApplication ?? false);
const myApplications = computed(() =>
  applications.value.filter((item) => item.applicantUserId === currentUserId.value),
);
const reviewApplications = computed(() =>
  isReviewer.value ? applications.value : myApplications.value,
);
const pendingCount = computed(
  () => applications.value.filter((item) => item.auditStatus === "pending").length,
);
const approvedCount = computed(
  () => applications.value.filter((item) => item.auditStatus === "approved").length,
);
const rejectedCount = computed(
  () => applications.value.filter((item) => item.auditStatus === "rejected").length,
);

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
  return (await res.json()) as T;
}

async function loadUsers() {
  usersLoading.value = true;
  try {
    users.value = await requestJson<UserSummary[]>("/api/users");
    if (!currentUserId.value && users.value.length > 0) {
      currentUserId.value = users.value[0].id;
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : "用户加载失败";
  } finally {
    usersLoading.value = false;
  }
}

async function loadData() {
  if (!currentUserId.value) return;

  loading.value = true;
  error.value = "";
  try {
    const query = new URLSearchParams({ viewerUserId: String(currentUserId.value) });
    if (filters.auditStatus) query.set("auditStatus", filters.auditStatus);

    const [applicationData, clubData] = await Promise.all([
      requestJson<ClubApplication[]>(`/api/clubs/applications?${query.toString()}`),
      requestJson<Club[]>(`/api/clubs?viewerUserId=${currentUserId.value}`),
    ]);
    applications.value = applicationData;
    clubs.value = clubData;
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
}

function resetApplicationForm() {
  applicationForm.name = "";
  applicationForm.category = "";
  applicationForm.description = "";
  applicationForm.applyReason = "";
  applicationForm.materialUrl = "";
  applicationForm.advisorName = "";
  applicationForm.contactPhone = "";
  applicationFormRef.value?.clearValidate();
}

function openApplicationDialog() {
  if (!canSubmitApplication.value) {
    ElMessage.warning("当前用户不能提交社团注册申请，请切换到学生账号。");
    return;
  }

  resetApplicationForm();
  applicationDialogVisible.value = true;
}

async function submitApplication() {
  if (!applicationFormRef.value || !currentUserId.value) return;
  await applicationFormRef.value.validate();

  saving.value = true;
  try {
    await requestJson<ClubApplication>("/api/clubs/applications", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentUserId: currentUserId.value,
        ...applicationForm,
      }),
    });
    ElMessage.success("社团注册申请已提交");
    applicationDialogVisible.value = false;
    await loadData();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "提交失败");
  } finally {
    saving.value = false;
  }
}

function openReviewDialog(row: ClubApplication) {
  if (!isReviewer.value) {
    ElMessage.warning("只有平台管理员可以审核社团注册申请。");
    return;
  }

  reviewTarget.value = row;
  reviewForm.decision = "approved";
  reviewForm.reviewComment = "";
  reviewFormRef.value?.clearValidate();
  reviewDialogVisible.value = true;
}

async function submitReview() {
  if (!reviewFormRef.value || !reviewTarget.value || !currentUserId.value) return;
  await reviewFormRef.value.validate();

  if (reviewForm.decision === "rejected" && !reviewForm.reviewComment.trim()) {
    ElMessage.warning("退回申请时必须填写审核意见");
    return;
  }

  const actionText = reviewForm.decision === "approved" ? "通过" : "退回";
  await ElMessageBox.confirm(
    `确认${actionText}「${reviewTarget.value.name}」的注册申请？`,
    "审核确认",
    {
      type: reviewForm.decision === "approved" ? "success" : "warning",
    },
  );

  reviewing.value = true;
  try {
    await requestJson<ClubApplication>(`/api/clubs/applications/${reviewTarget.value.id}/review`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentUserId: currentUserId.value,
        ...reviewForm,
      }),
    });
    ElMessage.success(`申请已${actionText}`);
    reviewDialogVisible.value = false;
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "审核失败");
  } finally {
    reviewing.value = false;
  }
}

function resetFilters() {
  filters.auditStatus = "";
  void loadData();
}

function formatDate(value: string | null | undefined) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function auditTagType(status: string | null | undefined) {
  if (status === "approved") return "success";
  if (status === "rejected") return "danger";
  if (status === "pending") return "warning";
  return "info";
}

function clubTagType(status: string | null | undefined) {
  if (status === "active") return "success";
  if (status === "rejected") return "danger";
  if (status === "pending") return "warning";
  return "info";
}

function statusStep(row: ClubApplication) {
  return row.auditStatus === "pending" ? 1 : 3;
}

function statusProcess(row: ClubApplication) {
  return row.auditStatus === "rejected" ? "error" : "process";
}

function roleLabel(user: UserSummary | null) {
  if (!user) return "未选择用户";
  if (user.canReviewClubApplication) return "平台管理员";
  if (user.memberships.length > 0) return "社团负责人/成员";
  return "学生申请人";
}

function userOptionLabel(user: UserSummary) {
  return `${user.displayName} - ${roleLabel(user)}`;
}

watch(currentUserId, () => {
  void loadData();
});

onMounted(async () => {
  await loadUsers();
  await loadData();
});
</script>

<template>
  <div class="page">
    <section class="toolbar">
      <div>
        <h2>社团组织管理</h2>
        <div class="subtitle">基于 USERS、ROLES、USER_ROLES、CLUBS、CLUB_MEMBERS 的角色化流程</div>
      </div>
      <div class="toolbar-actions">
        <el-select
          v-model="currentUserId"
          :loading="usersLoading"
          class="user-switcher"
          filterable
          placeholder="选择当前用户"
        >
          <el-option
            v-for="user in users"
            :key="user.id"
            :label="userOptionLabel(user)"
            :value="user.id"
          />
        </el-select>
        <el-button :icon="Refresh" @click="loadData">刷新</el-button>
      </div>
    </section>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <section v-if="currentUser" class="identity-band">
      <div class="identity-main">
        <el-icon><User /></el-icon>
        <div>
          <strong>{{ currentUser.displayName }}</strong>
          <span>{{ roleLabel(currentUser) }}</span>
        </div>
      </div>
      <div class="identity-tags">
        <el-tag v-if="currentUser.canSubmitClubApplication" type="success" effect="plain">
          可提交注册申请
        </el-tag>
        <el-tag v-if="currentUser.canReviewClubApplication" type="warning" effect="plain">
          可审核注册申请
        </el-tag>
        <el-tag
          v-for="role in currentUser.roles"
          :key="`${role.roleCode}-${role.clubId}`"
          effect="plain"
        >
          {{ role.roleName }}{{ role.clubName ? ` / ${role.clubName}` : "" }}
        </el-tag>
      </div>
    </section>

    <section class="metrics">
      <div class="metric">
        <span>待审核</span>
        <strong>{{ pendingCount }}</strong>
      </div>
      <div class="metric">
        <span>已通过</span>
        <strong>{{ approvedCount }}</strong>
      </div>
      <div class="metric">
        <span>已退回</span>
        <strong>{{ rejectedCount }}</strong>
      </div>
      <div class="metric">
        <span>当前可见社团</span>
        <strong>{{ clubs.length }}</strong>
      </div>
    </section>

    <el-tabs v-model="activeTab" class="workspace-tabs">
      <el-tab-pane label="当前工作台" name="workspace">
        <div class="workspace-head">
          <div>
            <h3>{{ isReviewer ? "申请审核池" : "我的注册申请" }}</h3>
            <p>
              {{
                isReviewer
                  ? "平台管理员可以查看全部注册申请，并决定通过或退回。"
                  : "学生只能提交申请，并查看自己的审核状态和审核意见。"
              }}
            </p>
          </div>
          <el-button
            v-if="canSubmitApplication"
            type="primary"
            :icon="Plus"
            @click="openApplicationDialog"
          >
            提交社团注册申请
          </el-button>
        </div>

        <div v-if="isReviewer" class="filter-bar">
          <el-select
            v-model="filters.auditStatus"
            clearable
            placeholder="审核状态"
            class="filter-item"
          >
            <el-option label="待审核" value="pending" />
            <el-option label="已通过" value="approved" />
            <el-option label="已退回" value="rejected" />
          </el-select>
          <el-button type="primary" plain :icon="Search" @click="loadData">查询</el-button>
          <el-button @click="resetFilters">重置</el-button>
        </div>

        <el-table
          v-loading="loading"
          :data="reviewApplications"
          border
          stripe
          empty-text="暂无社团注册申请"
          row-key="id"
        >
          <el-table-column type="expand">
            <template #default="{ row }">
              <div class="application-detail">
                <el-steps
                  :active="statusStep(row)"
                  :process-status="statusProcess(row)"
                  finish-status="success"
                  align-center
                >
                  <el-step title="提交申请" :description="formatDate(row.createdAt)" />
                  <el-step title="平台审核" :description="row.reviewerName || '等待处理'" />
                  <el-step
                    :title="row.auditStatus === 'rejected' ? '申请退回' : '社团生效'"
                    :description="row.reviewComment || row.clubStatusText"
                  />
                </el-steps>
                <div class="detail-grid">
                  <div>
                    <span>申请理由</span>
                    <p>{{ row.applyReason }}</p>
                  </div>
                  <div>
                    <span>材料地址</span>
                    <p>
                      <el-link :href="row.materialUrl" target="_blank" type="primary">
                        {{ row.materialUrl }}
                      </el-link>
                    </p>
                  </div>
                  <div>
                    <span>审核意见</span>
                    <p>{{ row.reviewComment || "暂无" }}</p>
                  </div>
                </div>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="name" label="社团名称" min-width="150" />
          <el-table-column prop="category" label="类别" width="110" />
          <el-table-column label="申请人" min-width="150">
            <template #default="{ row }">
              {{ row.applicantName || `用户 ${row.applicantUserId}` }}
            </template>
          </el-table-column>
          <el-table-column label="审核状态" width="110">
            <template #default="{ row }">
              <el-tag :type="auditTagType(row.auditStatus)">{{ row.auditStatusText }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="社团状态" width="110">
            <template #default="{ row }">
              <el-tag :type="clubTagType(row.clubStatus)" effect="plain">
                {{ row.clubStatusText }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="更新时间" width="170">
            <template #default="{ row }">{{ formatDate(row.updatedAt || row.createdAt) }}</template>
          </el-table-column>
          <el-table-column v-if="isReviewer" label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button
                type="primary"
                size="small"
                text
                :disabled="row.auditStatus !== 'pending'"
                @click="openReviewDialog(row)"
              >
                审核
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="社团档案" name="clubs">
        <el-table v-loading="loading" :data="clubs" border stripe empty-text="暂无可见社团">
          <el-table-column prop="id" label="ID" width="70" />
          <el-table-column prop="name" label="社团名称" min-width="150" />
          <el-table-column prop="category" label="类别" width="110" />
          <el-table-column prop="description" label="简介" min-width="220" show-overflow-tooltip />
          <el-table-column label="负责人" min-width="140">
            <template #default="{ row }">{{
              row.presidentName || row.applicantName || "-"
            }}</template>
          </el-table-column>
          <el-table-column prop="advisorName" label="指导老师" width="120" />
          <el-table-column prop="contactPhone" label="联系电话" width="140" />
          <el-table-column label="审核状态" width="110">
            <template #default="{ row }">
              <el-tag :type="auditTagType(row.auditStatus)">{{ row.auditStatusText }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="社团状态" width="110">
            <template #default="{ row }">
              <el-tag :type="clubTagType(row.status)" effect="plain">{{ row.statusText }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="成立时间" width="170">
            <template #default="{ row }">{{ formatDate(row.foundedAt) }}</template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="我的社团身份" name="membership">
        <el-table
          :data="currentUser?.memberships ?? []"
          border
          stripe
          empty-text="当前用户暂无社团成员或干部任期记录"
        >
          <el-table-column prop="clubName" label="社团" min-width="150" />
          <el-table-column prop="departmentName" label="部门" width="130" />
          <el-table-column prop="groupName" label="小组" width="130" />
          <el-table-column prop="positionName" label="职位" width="130" />
          <el-table-column prop="termName" label="任期" width="150" />
          <el-table-column prop="memberStatus" label="成员状态" width="120" />
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="applicationDialogVisible" title="提交社团注册申请" width="580px">
      <el-form
        ref="applicationFormRef"
        :model="applicationForm"
        :rules="applicationRules"
        label-position="top"
      >
        <div class="form-grid">
          <el-form-item label="申请人">
            <el-input :model-value="currentUser?.displayName" disabled />
          </el-form-item>
          <el-form-item label="社团类别" prop="category">
            <el-input v-model="applicationForm.category" placeholder="如：学术科技" />
          </el-form-item>
        </div>
        <el-form-item label="社团名称" prop="name">
          <el-input
            v-model="applicationForm.name"
            maxlength="60"
            show-word-limit
            placeholder="请输入社团名称"
          />
        </el-form-item>
        <el-form-item label="社团简介">
          <el-input
            v-model="applicationForm.description"
            type="textarea"
            :rows="2"
            maxlength="200"
            show-word-limit
            placeholder="介绍社团定位和主要活动"
          />
        </el-form-item>
        <el-form-item label="申请理由" prop="applyReason">
          <el-input
            v-model="applicationForm.applyReason"
            type="textarea"
            :rows="3"
            maxlength="300"
            show-word-limit
            placeholder="说明成立社团的必要性、服务对象和预期价值"
          />
        </el-form-item>
        <el-form-item label="材料地址" prop="materialUrl">
          <el-input
            v-model="applicationForm.materialUrl"
            placeholder="填写申请材料链接或文件路径"
          />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="指导老师">
            <el-input v-model="applicationForm.advisorName" placeholder="可选" />
          </el-form-item>
          <el-form-item label="联系电话">
            <el-input v-model="applicationForm.contactPhone" placeholder="可选" />
          </el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="applicationDialogVisible = false">取消</el-button>
        <el-button type="primary" :icon="Plus" :loading="saving" @click="submitApplication">
          提交申请
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="审核社团注册申请" width="520px">
      <div v-if="reviewTarget" class="review-target">
        <strong>{{ reviewTarget.name }}</strong>
        <span>{{ reviewTarget.category || "未分类" }}</span>
      </div>
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-position="top">
        <el-form-item label="审核人">
          <el-input :model-value="currentUser?.displayName" disabled />
        </el-form-item>
        <el-form-item label="审核结果" prop="decision">
          <el-radio-group v-model="reviewForm.decision">
            <el-radio-button label="approved">
              <el-icon><Check /></el-icon>
              通过
            </el-radio-button>
            <el-radio-button label="rejected">
              <el-icon><Close /></el-icon>
              退回
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="审核意见">
          <el-input
            v-model="reviewForm.reviewComment"
            type="textarea"
            :rows="3"
            maxlength="200"
            show-word-limit
            placeholder="通过时可填写备注；退回时必须说明原因"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="reviewing" @click="submitReview">
          保存审核结果
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  max-width: 1200px;
  margin: 0 auto;
}

.toolbar,
.identity-band,
.workspace-head,
.filter-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.toolbar {
  margin-bottom: 14px;
}

.toolbar h2,
.workspace-head h3 {
  margin: 0;
  line-height: 1.4;
}

.subtitle,
.workspace-head p,
.identity-main span {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.toolbar-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.user-switcher {
  width: 360px;
}

.identity-band,
.filter-bar,
.workspace-tabs {
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  background: var(--el-bg-color);
}

.identity-band {
  padding: 12px 14px;
  margin-bottom: 14px;
}

.identity-main {
  display: flex;
  align-items: center;
  gap: 10px;
}

.identity-main strong,
.identity-main span {
  display: block;
}

.identity-tags {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  flex-wrap: wrap;
}

.metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin: 14px 0;
}

.metric {
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  padding: 14px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  background: var(--el-bg-color);
}

.metric span {
  color: var(--el-text-color-secondary);
}

.metric strong {
  font-size: 24px;
  line-height: 1;
}

.workspace-tabs {
  padding: 0 12px 12px;
}

.workspace-head {
  margin: 6px 0 14px;
}

.filter-bar {
  justify-content: flex-start;
  padding: 12px;
  margin-bottom: 12px;
}

.filter-item {
  width: 160px;
}

.application-detail {
  padding: 8px 12px 12px;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.detail-grid span {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.detail-grid p {
  margin: 6px 0 0;
  line-height: 1.6;
  word-break: break-word;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.review-target {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  padding: 10px 12px;
  margin-bottom: 14px;
  background: var(--el-fill-color-lighter);
}

.review-target span {
  color: var(--el-text-color-secondary);
}

@media (max-width: 820px) {
  .toolbar,
  .identity-band,
  .workspace-head {
    align-items: flex-start;
    flex-direction: column;
  }

  .toolbar-actions,
  .user-switcher {
    width: 100%;
  }

  .metrics,
  .detail-grid,
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>

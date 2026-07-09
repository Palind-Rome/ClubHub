<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { Check, Edit, Plus, Refresh, Upload } from "@element-plus/icons-vue";
import { onSessionChange, readAuth, type AuthRole } from "../authSession";

type CampaignStatus = "open" | "closed" | "published";
type ApplicationStatus =
  "submitted" | "leader_approved" | "advisor_approved" | "rejected" | "published";
type ReviewDecision = "approved" | "rejected";

interface Club {
  id: number;
  name: string;
  status: string | null;
  presidentUserId: number | null;
  advisorUserId: number | null;
  advisorName: string | null;
}

interface AwardCampaignRecord {
  campaignId: number;
  clubId: number;
  clubName: string;
  title: string;
  awardType: string | null;
  termName: string;
  description: string | null;
  publisherUserId: number;
  publisherName: string;
  campaignStatus: CampaignStatus;
  campaignStatusText: string;
  createdAt: string | null;
  applicationCount: number;
  submittedCount: number;
  approvedCount: number;
  publishedCount: number;
}

interface AwardApplicationRecord {
  applicationId: number;
  campaignId: number;
  clubId: number;
  clubName: string;
  title: string;
  awardLevel: string | null;
  termName: string;
  applicantUserId: number;
  applicantName: string;
  studentNo: string | null;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  applyReason: string;
  applicationStatus: ApplicationStatus;
  applicationStatusText: string;
  reviewerUserId: number | null;
  reviewerName: string | null;
  reviewComment: string | null;
  createdAt: string | null;
}

interface ApiError {
  message?: string;
  title?: string;
}

const principalRoleCodes = new Set(["club_president", "club_leader", "club_manager", "president"]);
const officerRoleCodes = new Set(["club_officer", "officer", "club_manager"]);
const advisorRoleCodes = new Set(["advisor", "club_advisor", "teacher_advisor"]);
const platformRoleCodes = new Set(["platform_admin", "club_admin", "admin", "club_reviewer"]);
const systemRoleCodes = new Set(["system_admin", "sysadmin"]);

const auth = ref(readAuth());
const clubs = ref<Club[]>([]);
const campaigns = ref<AwardCampaignRecord[]>([]);
const applications = ref<AwardApplicationRecord[]>([]);
const selectedCampaignId = ref<number | null>(null);
const loading = ref(false);
const applicationLoading = ref(false);
const saving = ref(false);
const reviewSavingId = ref<number | null>(null);
const publishSavingId = ref<number | null>(null);
const campaignDialogVisible = ref(false);
const applyDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const campaignFormRef = ref<FormInstance>();
const applyFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();
const applyTarget = ref<AwardCampaignRecord | null>(null);
const reviewTarget = ref<AwardApplicationRecord | null>(null);
let stopSessionListener: (() => void) | null = null;

const filters = reactive({
  clubId: undefined as number | undefined,
  status: "" as "" | CampaignStatus,
});

const campaignForm = reactive({
  clubId: undefined as number | undefined,
  title: "",
  awardType: "",
  termName: `${new Date().getFullYear()} 学年春季学期`,
  description: "",
});

const applyForm = reactive({
  awardLevel: "",
  applyReason: "",
});

const reviewForm = reactive({
  decision: "approved" as ReviewDecision,
  reviewComment: "",
});

const currentUserId = computed(() => auth.value?.user.id ?? null);
const currentRoles = computed(() => auth.value?.roles ?? []);
const selectedCampaign = computed(
  () => campaigns.value.find((item) => item.campaignId === selectedCampaignId.value) ?? null,
);
const creatableClubs = computed(() =>
  clubs.value.filter((club) => club.status === "active" && canMaintainCampaign(club.id)),
);
const summary = computed(() => ({
  total: campaigns.value.length,
  open: campaigns.value.filter((item) => item.campaignStatus === "open").length,
  published: campaigns.value.filter((item) => item.campaignStatus === "published").length,
  applications: campaigns.value.reduce((sum, item) => sum + item.applicationCount, 0),
}));

const campaignRules: FormRules = {
  clubId: [{ required: true, message: "请选择发布社团", trigger: "change" }],
  title: [{ required: true, message: "请填写评奖标题", trigger: "blur" }],
  termName: [{ required: true, message: "请填写学期", trigger: "blur" }],
};

const applyRules: FormRules = {
  applyReason: [{ required: true, message: "请填写申报理由", trigger: "blur" }],
};

const reviewRules: FormRules = {
  decision: [{ required: true, message: "请选择审核结果", trigger: "change" }],
};

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
    ...init,
  });

  if (!res.ok) {
    let message = `请求失败：${res.status}`;
    try {
      const body = (await res.json()) as ApiError;
      message = body.message || body.title || message;
    } catch {
      const text = await res.text();
      if (text) message = text;
    }
    throw new Error(message);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

async function validateForm(form?: FormInstance) {
  if (!form) return false;
  return form.validate().catch(() => false);
}

async function loadClubs() {
  if (!currentUserId.value) {
    clubs.value = [];
    return;
  }

  try {
    clubs.value = await requestJson<Club[]>(`/api/clubs?viewerUserId=${currentUserId.value}`);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "社团列表加载失败");
  }
}

async function loadCampaigns() {
  if (!currentUserId.value) {
    campaigns.value = [];
    applications.value = [];
    return;
  }

  loading.value = true;
  try {
    const query = new URLSearchParams({ viewerUserId: String(currentUserId.value) });
    if (filters.clubId) query.set("clubId", String(filters.clubId));
    if (filters.status) query.set("status", filters.status);
    campaigns.value = await requestJson<AwardCampaignRecord[]>(
      `/api/award-campaigns?${query.toString()}`,
    );
    if (!selectedCampaign.value && campaigns.value.length > 0) {
      selectedCampaignId.value = campaigns.value[0].campaignId;
    }
    if (selectedCampaign.value) await loadApplications(selectedCampaign.value.campaignId);
  } catch (error) {
    campaigns.value = [];
    applications.value = [];
    ElMessage.error(error instanceof Error ? error.message : "评优评奖活动加载失败");
  } finally {
    loading.value = false;
  }
}

async function loadApplications(campaignId = selectedCampaignId.value) {
  if (!campaignId || !currentUserId.value) {
    applications.value = [];
    return;
  }

  applicationLoading.value = true;
  try {
    applications.value = await requestJson<AwardApplicationRecord[]>(
      `/api/award-campaigns/${campaignId}/applications?viewerUserId=${currentUserId.value}`,
    );
  } catch (error) {
    applications.value = [];
    ElMessage.error(error instanceof Error ? error.message : "申报列表加载失败");
  } finally {
    applicationLoading.value = false;
  }
}

function selectCampaign(row: AwardCampaignRecord) {
  selectedCampaignId.value = row.campaignId;
  void loadApplications(row.campaignId);
}

function openCampaignDialog() {
  if (creatableClubs.value.length === 0) {
    ElMessage.warning("当前账号没有可发布评优评奖活动的社团");
    return;
  }
  campaignForm.clubId =
    filters.clubId && canMaintainCampaign(filters.clubId)
      ? filters.clubId
      : creatableClubs.value[0]?.id;
  campaignForm.title = "";
  campaignForm.awardType = "";
  campaignForm.termName = `${new Date().getFullYear()} 学年春季学期`;
  campaignForm.description = "";
  campaignFormRef.value?.clearValidate();
  campaignDialogVisible.value = true;
}

async function createCampaign() {
  if (
    !(await validateForm(campaignFormRef.value)) ||
    !campaignForm.clubId ||
    !currentUserId.value
  ) {
    return;
  }

  saving.value = true;
  try {
    const created = await requestJson<AwardCampaignRecord>("/api/award-campaigns", {
      method: "POST",
      body: JSON.stringify({
        currentUserId: currentUserId.value,
        clubId: campaignForm.clubId,
        title: campaignForm.title.trim(),
        awardType: emptyToNull(campaignForm.awardType),
        termName: campaignForm.termName.trim(),
        description: emptyToNull(campaignForm.description),
        campaignStatus: "open",
      }),
    });
    ElMessage.success("评优评奖活动已发布");
    campaignDialogVisible.value = false;
    await loadCampaigns();
    selectedCampaignId.value = created.campaignId;
    await loadApplications(created.campaignId);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "评优评奖活动发布失败");
  } finally {
    saving.value = false;
  }
}

function openApplyDialog(campaign: AwardCampaignRecord) {
  if (campaign.campaignStatus !== "open") {
    ElMessage.warning("该活动当前不在申报期");
    return;
  }
  applyTarget.value = campaign;
  applyForm.awardLevel = "";
  applyForm.applyReason = "";
  applyFormRef.value?.clearValidate();
  applyDialogVisible.value = true;
}

async function submitApplication() {
  if (!(await validateForm(applyFormRef.value)) || !applyTarget.value || !currentUserId.value)
    return;

  saving.value = true;
  try {
    await requestJson<AwardApplicationRecord>(
      `/api/award-campaigns/${applyTarget.value.campaignId}/apply`,
      {
        method: "POST",
        body: JSON.stringify({
          currentUserId: currentUserId.value,
          awardLevel: emptyToNull(applyForm.awardLevel),
          applyReason: applyForm.applyReason.trim(),
        }),
      },
    );
    ElMessage.success("申报已提交");
    applyDialogVisible.value = false;
    await Promise.all([loadCampaigns(), loadApplications(applyTarget.value.campaignId)]);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "申报提交失败");
  } finally {
    saving.value = false;
  }
}

function openReviewDialog(row: AwardApplicationRecord) {
  if (!canReviewApplication(row)) {
    ElMessage.warning("当前账号不能处理该阶段申报");
    return;
  }
  reviewTarget.value = row;
  reviewForm.decision = "approved";
  reviewForm.reviewComment = "";
  reviewFormRef.value?.clearValidate();
  reviewDialogVisible.value = true;
}

async function reviewApplication() {
  if (!(await validateForm(reviewFormRef.value)) || !reviewTarget.value || !currentUserId.value)
    return;
  if (reviewForm.decision === "rejected" && !reviewForm.reviewComment.trim()) {
    ElMessage.warning("退回申报时必须填写审核意见");
    return;
  }

  reviewSavingId.value = reviewTarget.value.applicationId;
  try {
    await requestJson<AwardApplicationRecord>(
      `/api/award-applications/${reviewTarget.value.applicationId}/review`,
      {
        method: "POST",
        body: JSON.stringify({
          currentUserId: currentUserId.value,
          decision: reviewForm.decision,
          reviewComment: emptyToNull(reviewForm.reviewComment),
        }),
      },
    );
    ElMessage.success("审核结果已保存");
    reviewDialogVisible.value = false;
    await Promise.all([loadCampaigns(), loadApplications(reviewTarget.value.campaignId)]);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "审核失败");
  } finally {
    reviewSavingId.value = null;
  }
}

async function publishCampaign(campaign: AwardCampaignRecord) {
  if (!currentUserId.value || !canPublishCampaign(campaign)) {
    ElMessage.warning("当前账号不能公示该评优评奖活动");
    return;
  }

  try {
    await ElMessageBox.confirm(`确认公示“${campaign.title}”的终审通过结果？`, "公示结果", {
      confirmButtonText: "公示",
      cancelButtonText: "取消",
      type: "warning",
    });
  } catch {
    return;
  }

  publishSavingId.value = campaign.campaignId;
  try {
    await requestJson<AwardCampaignRecord>(`/api/award-campaigns/${campaign.campaignId}/publish`, {
      method: "POST",
      body: JSON.stringify({ currentUserId: currentUserId.value }),
    });
    ElMessage.success("评优评奖结果已公示");
    await Promise.all([loadCampaigns(), loadApplications(campaign.campaignId)]);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "公示失败");
  } finally {
    publishSavingId.value = null;
  }
}

function canMaintainCampaign(clubId: number) {
  return hasSystemRole() || hasPlatformRole() || hasScopedRole(clubId, principalRoleCodes);
}

function canReviewApplication(row: AwardApplicationRecord) {
  if (row.applicationStatus === "submitted") {
    return canMaintainCampaign(row.clubId) || hasScopedRole(row.clubId, officerRoleCodes);
  }
  if (row.applicationStatus === "leader_approved") {
    return hasSystemRole() || hasPlatformRole() || hasScopedRole(row.clubId, advisorRoleCodes);
  }
  return false;
}

function canPublishCampaign(campaign: AwardCampaignRecord) {
  return (
    campaign.campaignStatus !== "published" &&
    campaign.approvedCount > 0 &&
    (canMaintainCampaign(campaign.clubId) || hasScopedRole(campaign.clubId, advisorRoleCodes))
  );
}

function hasScopedRole(clubId: number, roleCodes: ReadonlySet<string>) {
  return currentRoles.value.some(
    (role) => roleCodes.has(normalizeRoleText(role.code)) && roleCoversClub(role, clubId),
  );
}

function hasPlatformRole() {
  return currentRoles.value.some((role) => platformRoleCodes.has(normalizeRoleText(role.code)));
}

function hasSystemRole() {
  return currentRoles.value.some((role) => systemRoleCodes.has(normalizeRoleText(role.code)));
}

function roleCoversClub(role: AuthRole, clubId: number) {
  const clubIds = role.clubIds ?? [];
  return role.clubId === clubId || clubIds.includes(clubId);
}

function normalizeRoleText(value?: string | null) {
  return String(value ?? "")
    .trim()
    .toLowerCase();
}

function campaignStatusTagType(status: CampaignStatus) {
  if (status === "open") return "success";
  if (status === "published") return "primary";
  return "info";
}

function applicationStatusTagType(status: ApplicationStatus) {
  if (status === "submitted") return "warning";
  if (status === "leader_approved") return "primary";
  if (status === "advisor_approved") return "success";
  if (status === "published") return "success";
  return "danger";
}

function formatDate(value?: string | null) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", { hour12: false });
}

function emptyToNull(value: string) {
  const text = value.trim();
  return text.length === 0 ? null : text;
}

watch([() => filters.clubId, () => filters.status], () => {
  void loadCampaigns();
});

onMounted(async () => {
  stopSessionListener = onSessionChange(() => {
    auth.value = readAuth();
    void Promise.all([loadClubs(), loadCampaigns()]);
  });
  await loadClubs();
  await loadCampaigns();
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <div class="page">
    <section class="toolbar">
      <div>
        <h2>评优评奖</h2>
        <div class="subtitle">负责人发布活动，成员自主申报，审核通过后统一公示</div>
      </div>
      <div class="toolbar-actions">
        <el-button :icon="Refresh" @click="loadCampaigns">刷新</el-button>
        <el-button
          v-if="creatableClubs.length > 0"
          type="primary"
          :icon="Plus"
          @click="openCampaignDialog"
        >
          发布活动
        </el-button>
      </div>
    </section>

    <section class="summary-bar">
      <span>活动 {{ summary.total }} 个</span>
      <span>申报中 {{ summary.open }} 个</span>
      <span>已公示 {{ summary.published }} 个</span>
      <span>申报 {{ summary.applications }} 条</span>
    </section>

    <section class="workspace">
      <div class="filters">
        <el-select v-model="filters.clubId" class="filter-item" clearable placeholder="社团">
          <el-option v-for="club in clubs" :key="club.id" :label="club.name" :value="club.id" />
        </el-select>
        <el-select v-model="filters.status" class="filter-item" clearable placeholder="活动状态">
          <el-option label="申报中" value="open" />
          <el-option label="已关闭" value="closed" />
          <el-option label="已公示" value="published" />
        </el-select>
      </div>

      <el-table
        v-loading="loading"
        :data="campaigns"
        border
        stripe
        highlight-current-row
        empty-text="暂无评优评奖活动"
        row-key="campaignId"
        @row-click="selectCampaign"
      >
        <el-table-column prop="title" label="活动标题" min-width="180" />
        <el-table-column prop="clubName" label="社团" min-width="150" />
        <el-table-column prop="termName" label="学期" min-width="140" />
        <el-table-column prop="awardType" label="类型" width="130" />
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="campaignStatusTagType(row.campaignStatus)" effect="plain">
              {{ row.campaignStatusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="申报" width="160">
          <template #default="{ row }">
            {{ row.applicationCount }} / 通过 {{ row.approvedCount }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button type="success" plain :icon="Edit" @click.stop="openApplyDialog(row)">
              申报
            </el-button>
            <el-button
              v-if="canPublishCampaign(row)"
              type="primary"
              plain
              :icon="Upload"
              :loading="publishSavingId === row.campaignId"
              @click.stop="publishCampaign(row)"
            >
              公示
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <section class="workspace">
      <div class="section-head">
        <div>
          <h3>{{ selectedCampaign?.title || "申报记录" }}</h3>
          <p>
            {{ selectedCampaign?.description || "选择一个活动查看成员申报、审核状态和公示结果。" }}
          </p>
        </div>
      </div>

      <el-table
        v-loading="applicationLoading"
        :data="applications"
        border
        stripe
        empty-text="暂无申报记录"
        row-key="applicationId"
      >
        <el-table-column type="expand">
          <template #default="{ row }">
            <el-descriptions :column="2" border>
              <el-descriptions-item label="申报人">
                {{ row.applicantName }} / {{ row.studentNo || "-" }}
              </el-descriptions-item>
              <el-descriptions-item label="部门小组">
                {{ row.departmentName || "-" }} / {{ row.groupName || "-" }}
              </el-descriptions-item>
              <el-descriptions-item label="申报奖项">
                {{ row.awardLevel || "-" }}
              </el-descriptions-item>
              <el-descriptions-item label="审核人">
                {{ row.reviewerName || "-" }}
              </el-descriptions-item>
              <el-descriptions-item label="申报理由" :span="2">
                {{ row.applyReason }}
              </el-descriptions-item>
              <el-descriptions-item label="审核意见" :span="2">
                {{ row.reviewComment || "-" }}
              </el-descriptions-item>
            </el-descriptions>
          </template>
        </el-table-column>
        <el-table-column prop="applicantName" label="申报人" min-width="150" />
        <el-table-column prop="awardLevel" label="申报奖项" min-width="150" />
        <el-table-column label="状态" width="150">
          <template #default="{ row }">
            <el-tag :type="applicationStatusTagType(row.applicationStatus)" effect="plain">
              {{ row.applicationStatusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="提交时间" width="180">
          <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="130" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="canReviewApplication(row)"
              type="primary"
              plain
              :icon="Check"
              :loading="reviewSavingId === row.applicationId"
              @click="openReviewDialog(row)"
            >
              审核
            </el-button>
            <span v-else class="muted">查看</span>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <el-dialog v-model="campaignDialogVisible" title="发布评优评奖活动" width="620px">
      <el-form
        ref="campaignFormRef"
        :model="campaignForm"
        :rules="campaignRules"
        label-width="90px"
      >
        <el-form-item label="社团" prop="clubId">
          <el-select v-model="campaignForm.clubId" filterable placeholder="选择社团">
            <el-option
              v-for="club in creatableClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="标题" prop="title">
          <el-input v-model="campaignForm.title" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="类型">
          <el-input v-model="campaignForm.awardType" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="学期" prop="termName">
          <el-input v-model="campaignForm.termName" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="说明">
          <el-input
            v-model="campaignForm.description"
            type="textarea"
            :rows="4"
            maxlength="255"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="campaignDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="createCampaign">发布</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="applyDialogVisible" title="提交评优评奖申报" width="620px">
      <el-form ref="applyFormRef" :model="applyForm" :rules="applyRules" label-width="90px">
        <el-form-item label="活动">
          <el-input :model-value="applyTarget?.title" disabled />
        </el-form-item>
        <el-form-item label="申报奖项">
          <el-input v-model="applyForm.awardLevel" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="申报理由" prop="applyReason">
          <el-input
            v-model="applyForm.applyReason"
            type="textarea"
            :rows="5"
            maxlength="255"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="applyDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitApplication">提交申报</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="审核评优评奖申报" width="560px">
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="90px">
        <el-form-item label="申报人">
          <el-input :model-value="reviewTarget?.applicantName" disabled />
        </el-form-item>
        <el-form-item label="结果" prop="decision">
          <el-radio-group v-model="reviewForm.decision">
            <el-radio-button label="approved">通过</el-radio-button>
            <el-radio-button label="rejected">退回</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="审核意见">
          <el-input
            v-model="reviewForm.reviewComment"
            type="textarea"
            :rows="4"
            maxlength="255"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="reviewSavingId !== null" @click="reviewApplication">
          保存审核
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: 18px;
  padding: 24px;
  color: #20262e;
}

.toolbar,
.summary-bar,
.workspace {
  border: 1px solid #d9e1ea;
  background: #fff;
}

.toolbar,
.summary-bar,
.section-head,
.filters {
  display: flex;
  align-items: center;
  gap: 14px;
}

.toolbar {
  justify-content: space-between;
  padding: 18px 20px;
}

.toolbar h2,
.section-head h3 {
  margin: 0;
  font-weight: 650;
}

.subtitle,
.section-head p,
.muted {
  color: #66727f;
}

.subtitle {
  margin-top: 6px;
  font-size: 14px;
}

.toolbar-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.summary-bar {
  flex-wrap: wrap;
  padding: 12px 18px;
}

.summary-bar span {
  padding-right: 18px;
  border-right: 1px solid #d9e1ea;
  font-size: 14px;
}

.summary-bar span:last-child {
  border-right: none;
}

.workspace {
  padding: 16px;
}

.filters {
  margin-bottom: 14px;
}

.filter-item {
  width: 220px;
}

.section-head {
  justify-content: space-between;
  margin-bottom: 14px;
}

.section-head p {
  margin: 6px 0 0;
}
</style>

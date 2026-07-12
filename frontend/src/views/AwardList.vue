<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { Edit, Plus, Refresh, Search, View } from "@element-plus/icons-vue";
import { type AuthResponse, onSessionChange, readAuth } from "../authSession";
import { requestJson } from "../composables/useApiRequest";
import {
  canMaintainScopedMember,
  collectCadreScopesFromMembers,
  hasGlobalClubViewRole,
  hasWholeClubMaintainRole,
  type MemberGroupingScope,
} from "../composables/useClubEvaluationScope";
import { collectManageableClubIds, collectScopedClubIds } from "../composables/useManageableClubs";

type PublicStatus = "draft" | "published";
type AwardFormMode = "create" | "edit";

interface Club {
  id: number;
  name: string;
  status: string | null;
  statusText: string;
}

interface ClubMemberRecord {
  memberId: number;
  clubId: number;
  clubName: string;
  userId: number;
  userName: string;
  studentNo: string | null;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  isCurrent: boolean;
}

interface ClubEvaluationRecord {
  evaluationId: number;
  evaluationType: "semester" | "award";
  clubId: number;
  clubName: string;
  userId: number;
  userName: string;
  studentNo: string | null;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  evaluatorName: string | null;
  termName: string;
  awardTitle: string | null;
  awardLevel: string | null;
  awardReason: string | null;
  awardScore: number;
  totalScore: number;
  grade: string;
  publicStatus: PublicStatus;
  publicStatusText: string;
  commentText: string | null;
  createdAt: string | null;
}

const evaluationDraftPermission = "evaluation:draft";

const auth = ref<AuthResponse | null>(readAuth());
const clubs = ref<Club[]>([]);
const members = ref<ClubMemberRecord[]>([]);
const awards = ref<ClubEvaluationRecord[]>([]);
const selectedClubId = ref<number>();
const activeTab = ref<"public" | "manage">("public");
const loading = ref(false);
const memberLoading = ref(false);
const saving = ref(false);
const detailVisible = ref(false);
const awardDialogVisible = ref(false);
const detailTarget = ref<ClubEvaluationRecord | null>(null);
const awardTarget = ref<ClubEvaluationRecord | null>(null);
const awardFormRef = ref<FormInstance>();
const awardFormMode = ref<AwardFormMode>("create");
let stopSessionListener: (() => void) | null = null;
let awardRequestId = 0;
let memberRequestId = 0;

const filters = reactive({
  keyword: "",
  publicStatus: "" as "" | PublicStatus,
});

const awardForm = reactive({
  userId: undefined as number | undefined,
  termName: `${new Date().getFullYear()} 学年春季学期`,
  awardTitle: "",
  awardLevel: "",
  awardReason: "",
  awardScore: 100,
  publicStatus: "draft" as PublicStatus,
  commentText: "",
});

const awardRules: FormRules = {
  userId: [{ required: true, message: "请选择获奖成员", trigger: "change" }],
  termName: [{ required: true, message: "请填写评定学期", trigger: "blur" }],
  awardTitle: [{ required: true, message: "请填写奖项名称", trigger: "blur" }],
  awardLevel: [{ required: true, message: "请填写奖项等级", trigger: "blur" }],
  awardReason: [{ required: true, message: "请填写获奖原因", trigger: "blur" }],
  awardScore: [{ required: true, message: "请填写奖项分", trigger: "blur" }],
};

const currentUserId = computed(() => auth.value?.user.id);
const permissions = computed(() => auth.value?.permissions ?? []);
const hasAllPermissions = computed(
  () =>
    permissions.value.includes("*") ||
    (auth.value?.roles ?? []).some((role) => role.permissions?.includes("*")),
);
const manageableClubIds = computed(() =>
  collectManageableClubIds(auth.value?.roles ?? [], evaluationDraftPermission),
);
const scopedClubIds = computed(() => collectScopedClubIds(auth.value?.roles ?? []));
const canViewAllClubs = computed(
  () => hasAllPermissions.value || hasGlobalClubViewRole(auth.value?.roles ?? []),
);
const accessibleClubs = computed(() =>
  canViewAllClubs.value
    ? clubs.value
    : clubs.value.filter(
        (club) => scopedClubIds.value.has(club.id) || manageableClubIds.value.has(club.id),
      ),
);
const selectedClub = computed(() => clubs.value.find((club) => club.id === selectedClubId.value));
const canMaintainSelectedClub = computed(
  () =>
    Boolean(selectedClub.value && selectedClub.value.status === "active") &&
    (hasAllPermissions.value ||
      (selectedClubId.value !== undefined && manageableClubIds.value.has(selectedClubId.value))),
);
const canMaintainWholeClub = computed(() => {
  const clubId = selectedClubId.value;
  if (!clubId || !canMaintainSelectedClub.value) return false;
  if (hasAllPermissions.value) return true;

  return hasWholeClubMaintainRole(auth.value?.roles ?? [], clubId);
});
const selectedCadreGroupingScopes = computed<MemberGroupingScope[]>(() => {
  const clubId = selectedClubId.value;
  const userId = currentUserId.value;
  if (!clubId || !userId || !canMaintainSelectedClub.value || canMaintainWholeClub.value) return [];

  return collectCadreScopesFromMembers(members.value, auth.value?.roles ?? [], clubId, userId);
});
const publicAwards = computed(() =>
  filteredAwards.value.filter((award) => award.publicStatus === "published"),
);
const manageAwards = computed(() => filteredAwards.value);
const displayedAwards = computed(() =>
  activeTab.value === "public" ? publicAwards.value : manageAwards.value,
);
const filteredAwards = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return awards.value.filter((award) => {
    if (filters.publicStatus && award.publicStatus !== filters.publicStatus) return false;
    if (!keyword) return true;
    return [
      award.awardTitle,
      award.awardLevel,
      award.userName,
      award.studentNo,
      award.termName,
      award.awardReason,
    ]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(keyword));
  });
});
const summary = computed(() => ({
  total: awards.value.length,
  published: awards.value.filter((award) => award.publicStatus === "published").length,
  draft: awards.value.filter((award) => award.publicStatus !== "published").length,
}));
const memberOptions = computed(() =>
  members.value.filter((member) => member.isCurrent && canMaintainMember(member)),
);

async function validateForm(form?: FormInstance) {
  if (!form) return false;
  try {
    await form.validate();
    return true;
  } catch {
    return false;
  }
}

function refreshSession() {
  auth.value = readAuth();
}

async function loadClubs() {
  if (!currentUserId.value) {
    clubs.value = [];
    return;
  }

  try {
    clubs.value = await requestJson<Club[]>(`/api/clubs`);
    if (
      !selectedClubId.value ||
      !accessibleClubs.value.some((club) => club.id === selectedClubId.value)
    ) {
      selectedClubId.value = accessibleClubs.value[0]?.id;
    }
  } catch (error) {
    clubs.value = [];
    selectedClubId.value = undefined;
    ElMessage.error(error instanceof Error ? error.message : "社团列表加载失败");
  }
}

async function loadAwards() {
  const requestId = ++awardRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId) {
    if (requestId === awardRequestId) awards.value = [];
    return;
  }

  loading.value = true;
  try {
    const query = new URLSearchParams({
      evaluationType: "award",
    });
    const data = await requestJson<ClubEvaluationRecord[]>(
      `/api/clubs/${clubId}/evaluations?${query.toString()}`,
    );
    if (requestId === awardRequestId) awards.value = data;
  } catch (error) {
    if (requestId === awardRequestId) {
      awards.value = [];
      ElMessage.error(error instanceof Error ? error.message : "评奖评优结果加载失败");
    }
  } finally {
    if (requestId === awardRequestId) loading.value = false;
  }
}

async function loadMembers() {
  const requestId = ++memberRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId || !canMaintainSelectedClub.value) {
    if (requestId === memberRequestId) members.value = [];
    return;
  }

  memberLoading.value = true;
  try {
    const query = new URLSearchParams({
      includeHistory: "false",
    });
    const data = await requestJson<ClubMemberRecord[]>(
      `/api/clubs/${clubId}/members?${query.toString()}`,
    );
    if (requestId === memberRequestId) members.value = data;
  } catch {
    if (requestId === memberRequestId) members.value = [];
  } finally {
    if (requestId === memberRequestId) memberLoading.value = false;
  }
}

async function reloadAll() {
  await loadClubs();
  await Promise.all([loadAwards(), loadMembers()]);
}

function resetAwardForm() {
  awardForm.userId = memberOptions.value[0]?.userId;
  awardForm.termName = `${new Date().getFullYear()} 学年春季学期`;
  awardForm.awardTitle = "";
  awardForm.awardLevel = "";
  awardForm.awardReason = "";
  awardForm.awardScore = 100;
  awardForm.publicStatus = "draft";
  awardForm.commentText = "";
  awardFormRef.value?.clearValidate();
}

function openCreateDialog() {
  if (!canMaintainSelectedClub.value) {
    ElMessage.warning("当前账号没有维护该社团评奖评优结果的权限。");
    return;
  }
  if (memberOptions.value.length === 0) {
    ElMessage.warning("当前社团暂无可选择的有效成员。");
    return;
  }

  awardFormMode.value = "create";
  awardTarget.value = null;
  resetAwardForm();
  awardDialogVisible.value = true;
}

function openEditDialog(row: ClubEvaluationRecord) {
  if (!canMaintainAwardRecord(row)) {
    ElMessage.warning("当前账号没有维护该社团评奖评优结果的权限。");
    return;
  }

  awardFormMode.value = "edit";
  awardTarget.value = row;
  awardForm.userId = row.userId;
  awardForm.termName = row.termName;
  awardForm.awardTitle = row.awardTitle ?? "";
  awardForm.awardLevel = row.awardLevel ?? "";
  awardForm.awardReason = row.awardReason ?? "";
  awardForm.awardScore = row.awardScore ?? 100;
  awardForm.publicStatus = row.publicStatus;
  awardForm.commentText = row.commentText ?? "";
  awardFormRef.value?.clearValidate();
  awardDialogVisible.value = true;
}

function openDetail(row: ClubEvaluationRecord) {
  detailTarget.value = row;
  detailVisible.value = true;
}

async function submitAward() {
  if (!selectedClubId.value || !currentUserId.value || !(await validateForm(awardFormRef.value))) {
    return;
  }

  saving.value = true;
  try {
    const payload = {
      evaluationType: "award",
      userId: awardForm.userId,
      termName: awardForm.termName,
      awardTitle: awardForm.awardTitle,
      awardLevel: awardForm.awardLevel,
      awardReason: awardForm.awardReason,
      activityScore: 0,
      taskScore: 0,
      learningScore: 0,
      awardScore: awardForm.awardScore,
      publicStatus: awardForm.publicStatus,
      commentText: emptyToNull(awardForm.commentText),
    };

    if (awardFormMode.value === "create") {
      await requestJson<ClubEvaluationRecord>(`/api/clubs/${selectedClubId.value}/evaluations`, {
        method: "POST",
        body: JSON.stringify(payload),
      });
    } else if (awardTarget.value) {
      await requestJson<ClubEvaluationRecord>(
        `/api/clubs/${selectedClubId.value}/evaluations/${awardTarget.value.evaluationId}`,
        {
          method: "PATCH",
          body: JSON.stringify(payload),
        },
      );
    }

    ElMessage.success(
      awardForm.publicStatus === "published" ? "评奖评优结果已公示" : "评奖评优结果已保存",
    );
    awardDialogVisible.value = false;
    await loadAwards();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "评奖评优结果保存失败");
  } finally {
    saving.value = false;
  }
}

function clearFilters() {
  filters.keyword = "";
  filters.publicStatus = "";
}

function emptyToNull(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function canMaintainAwardRecord(row: ClubEvaluationRecord) {
  const member = members.value.find((item) => item.userId === row.userId);
  return Boolean(member && canMaintainMember(member));
}

function canMaintainMember(member: ClubMemberRecord) {
  return canMaintainScopedMember(member, {
    canMaintainSelectedClub: canMaintainSelectedClub.value,
    canMaintainWholeClub: canMaintainWholeClub.value,
    scopes: selectedCadreGroupingScopes.value,
  });
}

function statusTagType(status: PublicStatus) {
  return status === "published" ? "success" : "info";
}

function formatDate(value: string | null) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", { hour12: false });
}

function memberLabel(member: ClubMemberRecord) {
  const scope = [member.departmentName, member.groupName, member.positionName]
    .filter(Boolean)
    .join(" / ");
  return `${member.userName}${member.studentNo ? `（${member.studentNo}）` : ""}${scope ? ` - ${scope}` : ""}`;
}

watch(selectedClubId, () => {
  void Promise.all([loadAwards(), loadMembers()]);
});

watch(
  [activeTab, canMaintainSelectedClub],
  ([tab, canMaintain]) => {
    if (tab === "manage" && !canMaintain) {
      activeTab.value = "public";
    }
  },
  { immediate: true },
);

onMounted(() => {
  stopSessionListener = onSessionChange(() => {
    refreshSession();
    void reloadAll();
  });
  void reloadAll();
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <section class="award-page">
    <div class="page-head">
      <div>
        <h2>评奖评优</h2>
        <div class="subtitle">录入评奖评优结果，维护奖项信息和公示状态。</div>
      </div>
      <div class="head-actions">
        <el-button :icon="Refresh" @click="reloadAll">刷新</el-button>
        <el-button
          v-if="canMaintainSelectedClub"
          type="primary"
          :icon="Plus"
          @click="openCreateDialog"
        >
          录入结果
        </el-button>
      </div>
    </div>

    <div class="summary-strip">
      <div>
        <span>结果记录</span>
        <strong>{{ summary.total }}</strong>
      </div>
      <div>
        <span>已公示</span>
        <strong>{{ summary.published }}</strong>
      </div>
      <div>
        <span>未公示</span>
        <strong>{{ summary.draft }}</strong>
      </div>
      <div>
        <span>当前社团</span>
        <strong>{{ selectedClub?.name || "-" }}</strong>
      </div>
    </div>

    <div class="toolbar">
      <el-select v-model="selectedClubId" class="club-select" placeholder="选择社团" filterable>
        <el-option
          v-for="club in accessibleClubs"
          :key="club.id"
          :label="club.name"
          :value="club.id"
        />
      </el-select>
      <el-input
        v-model="filters.keyword"
        class="keyword"
        :prefix-icon="Search"
        clearable
        placeholder="请输入奖项名称、成员姓名或学期"
      />
      <el-radio-group v-model="filters.publicStatus">
        <el-radio-button label="">全部</el-radio-button>
        <el-radio-button label="published">已公示</el-radio-button>
        <el-radio-button label="draft">未公示</el-radio-button>
      </el-radio-group>
      <el-button @click="clearFilters">清除</el-button>
    </div>

    <el-tabs v-model="activeTab" class="award-tabs">
      <el-tab-pane label="公示结果" name="public" />
      <el-tab-pane v-if="canMaintainSelectedClub" label="结果维护" name="manage" />
    </el-tabs>

    <div v-loading="loading" class="award-list">
      <el-empty
        v-if="displayedAwards.length === 0"
        :description="activeTab === 'public' ? '暂无已公示评奖评优结果' : '暂无评奖评优结果记录'"
      />
      <article v-for="award in displayedAwards" v-else :key="award.evaluationId" class="award-card">
        <div class="award-main">
          <div class="award-title-row">
            <h3>{{ award.awardTitle || "未命名奖项" }}</h3>
            <el-tag type="primary" effect="plain">{{ award.awardLevel || "未分级" }}</el-tag>
            <el-tag :type="statusTagType(award.publicStatus)" effect="plain">
              {{ award.publicStatusText }}
            </el-tag>
          </div>
          <div class="award-meta">
            <span>{{ award.userName }}</span>
            <span>{{ award.studentNo || "无学号" }}</span>
            <span>{{ award.termName }}</span>
            <span>{{ award.clubName }}</span>
          </div>
          <p>{{ award.awardReason || "暂无获奖原因" }}</p>
        </div>
        <div class="award-actions">
          <el-button :icon="View" @click="openDetail(award)">查看</el-button>
          <el-button
            v-if="canMaintainAwardRecord(award)"
            type="primary"
            plain
            :icon="Edit"
            @click="openEditDialog(award)"
          >
            编辑
          </el-button>
        </div>
      </article>
    </div>

    <el-dialog v-model="detailVisible" title="评奖评优详情" width="760px">
      <el-descriptions v-if="detailTarget" :column="2" border>
        <el-descriptions-item label="奖项名称">
          {{ detailTarget.awardTitle || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="奖项等级">
          {{ detailTarget.awardLevel || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="获奖成员">
          {{ detailTarget.userName }}（{{ detailTarget.studentNo || "-" }}）
        </el-descriptions-item>
        <el-descriptions-item label="评定学期">
          {{ detailTarget.termName }}
        </el-descriptions-item>
        <el-descriptions-item label="所属社团">
          {{ detailTarget.clubName }}
        </el-descriptions-item>
        <el-descriptions-item label="公示状态">
          <el-tag :type="statusTagType(detailTarget.publicStatus)" effect="plain">
            {{ detailTarget.publicStatusText }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="部门/小组">
          {{ detailTarget.departmentName || "-" }} / {{ detailTarget.groupName || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="职位">
          {{ detailTarget.positionName || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="奖项分">
          {{ detailTarget.awardScore }}
        </el-descriptions-item>
        <el-descriptions-item label="记录时间">
          {{ formatDate(detailTarget.createdAt) }}
        </el-descriptions-item>
        <el-descriptions-item label="获奖原因" :span="2">
          {{ detailTarget.awardReason || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="评价说明" :span="2">
          {{ detailTarget.commentText || "-" }}
        </el-descriptions-item>
      </el-descriptions>
    </el-dialog>

    <el-dialog
      v-model="awardDialogVisible"
      :title="awardFormMode === 'create' ? '录入评奖评优结果' : '编辑评奖评优结果'"
      width="720px"
    >
      <el-form ref="awardFormRef" :model="awardForm" :rules="awardRules" label-width="100px">
        <el-form-item label="社团">
          <el-input :model-value="selectedClub?.name" disabled />
        </el-form-item>
        <el-form-item v-if="awardFormMode === 'create'" label="获奖成员" prop="userId">
          <el-select
            v-model="awardForm.userId"
            v-loading="memberLoading"
            filterable
            placeholder="选择获奖成员"
          >
            <el-option
              v-for="member in memberOptions"
              :key="member.memberId"
              :label="memberLabel(member)"
              :value="member.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-else label="获奖成员">
          <el-input :model-value="awardTarget?.userName" disabled />
        </el-form-item>
        <el-form-item label="评定学期" prop="termName">
          <el-input v-model="awardForm.termName" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="奖项名称" prop="awardTitle">
          <el-input v-model="awardForm.awardTitle" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="奖项等级" prop="awardLevel">
          <el-input v-model="awardForm.awardLevel" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="奖项分" prop="awardScore">
          <el-input-number v-model="awardForm.awardScore" :min="0" :max="100" :precision="1" />
        </el-form-item>
        <el-form-item label="公示状态">
          <el-radio-group v-model="awardForm.publicStatus">
            <el-radio-button label="draft">未公示</el-radio-button>
            <el-radio-button label="published">已公示</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="获奖原因" prop="awardReason">
          <el-input
            v-model="awardForm.awardReason"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="评价说明">
          <el-input
            v-model="awardForm.commentText"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="awardDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitAward">保存</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.award-page {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.page-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.page-head h2 {
  margin: 0 0 6px;
  font-size: 24px;
}

.subtitle,
.award-meta {
  color: #66727f;
}

.head-actions,
.toolbar,
.award-title-row,
.award-meta,
.award-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.summary-strip {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  border: 1px solid #dce3ec;
  background: #fff;
}

.summary-strip div {
  min-height: 78px;
  border-right: 1px solid #e6ebf2;
  padding: 16px 22px;
}

.summary-strip div:last-child {
  border-right: none;
}

.summary-strip span {
  display: block;
  color: #66727f;
  font-size: 13px;
}

.summary-strip strong {
  display: block;
  margin-top: 8px;
  color: #20262e;
  font-size: 26px;
}

.toolbar {
  flex-wrap: wrap;
  border: 1px solid #dce3ec;
  background: #fff;
  padding: 14px;
}

.club-select {
  width: 260px;
}

.keyword {
  width: 360px;
}

.award-tabs {
  border-bottom: 1px solid #dce3ec;
}

.award-list {
  min-height: 420px;
}

.award-card {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  border: 1px solid #dce3ec;
  background: #fff;
  padding: 18px 20px;
}

.award-card + .award-card {
  margin-top: 12px;
}

.award-main {
  min-width: 0;
}

.award-title-row {
  flex-wrap: wrap;
}

.award-title-row h3 {
  margin: 0;
  font-size: 18px;
}

.award-meta {
  flex-wrap: wrap;
  margin: 10px 0;
  font-size: 13px;
}

.award-meta span + span::before {
  padding-right: 10px;
  color: #b6c0cc;
  content: "/";
}

.award-main p {
  margin: 0;
  color: #3d4652;
}

.award-actions {
  flex: 0 0 auto;
}

@media (max-width: 900px) {
  .page-head,
  .award-card {
    flex-direction: column;
  }

  .summary-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .keyword,
  .club-select {
    width: 100%;
  }
}
</style>

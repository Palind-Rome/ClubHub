<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { Edit, Plus, Refresh, Search, View } from "@element-plus/icons-vue";
import { type AuthResponse, onSessionChange, readAuth } from "../authSession";
import { requestJson } from "../composables/useApiRequest";
import {
  collectManageableClubIds,
  collectScopedClubIds,
  normalizedRoleCodeOf,
  roleCoversClub,
} from "../composables/useManageableClubs";

type PublicStatus = "draft" | "published";
type EvaluationFormMode = "create" | "edit";

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
  activityScore: number;
  taskScore: number;
  learningScore: number;
  awardScore: number;
  totalScore: number;
  grade: string;
  publicStatus: PublicStatus;
  publicStatusText: string;
  commentText: string | null;
  createdAt: string | null;
}

interface MemberGroupingScope {
  departmentName: string;
  groupName: string;
}

const evaluationDraftPermission = "evaluation:draft";
const evaluationReviewPermission = "evaluation:review";
const wholeClubMaintainRoleCodes = new Set([
  "advisor",
  "club_leader",
  "club_manager",
  "club_president",
  "president",
]);
const globalViewRoleCodes = new Set([
  "admin",
  "club_admin",
  "club_reviewer",
  "platform_admin",
  "system_admin",
  "sysadmin",
]);
const officerRoleCodes = new Set(["club_officer"]);
const cadrePositionNames = new Set([
  "干部",
  "部长",
  "副部长",
  "组长",
  "副组长",
  "干事",
  "社团干部",
  "部门负责人",
  "小组负责人",
  "officer",
  "cadre",
  "minister",
  "group leader",
]);

const auth = ref<AuthResponse | null>(readAuth());
const clubs = ref<Club[]>([]);
const members = ref<ClubMemberRecord[]>([]);
const evaluations = ref<ClubEvaluationRecord[]>([]);
const selectedClubId = ref<number>();
const loading = ref(false);
const memberLoading = ref(false);
const saving = ref(false);
const detailVisible = ref(false);
const evaluationDialogVisible = ref(false);
const detailTarget = ref<ClubEvaluationRecord | null>(null);
const evaluationTarget = ref<ClubEvaluationRecord | null>(null);
const evaluationFormRef = ref<FormInstance>();
const evaluationFormMode = ref<EvaluationFormMode>("create");
let stopSessionListener: (() => void) | null = null;
let evaluationRequestId = 0;
let memberRequestId = 0;

const filters = reactive({
  keyword: "",
  publicStatus: "" as "" | PublicStatus,
  termName: "",
});

const evaluationForm = reactive({
  userId: undefined as number | undefined,
  termName: `${new Date().getFullYear()} 学年春季学期`,
  activityScore: 0,
  taskScore: 0,
  learningScore: 0,
  awardScore: 0,
  publicStatus: "draft" as PublicStatus,
  commentText: "",
});

const evaluationRules: FormRules = {
  userId: [{ required: true, message: "请选择被考核成员", trigger: "change" }],
  termName: [{ required: true, message: "请填写考核学期", trigger: "blur" }],
  activityScore: [{ required: true, message: "请填写参与分", trigger: "blur" }],
  taskScore: [{ required: true, message: "请填写任务分", trigger: "blur" }],
  learningScore: [{ required: true, message: "请填写学习分", trigger: "blur" }],
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
  collectManageableClubIds(auth.value?.roles ?? [], [
    evaluationDraftPermission,
    evaluationReviewPermission,
  ]),
);
const scopedClubIds = computed(() => collectScopedClubIds(auth.value?.roles ?? []));
const canViewAllClubs = computed(
  () =>
    hasAllPermissions.value ||
    (auth.value?.roles ?? []).some((role) => globalViewRoleCodes.has(normalizedRoleCodeOf(role))),
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

  return (auth.value?.roles ?? []).some(
    (role) => roleCoversClub(role, clubId) && wholeClubMaintainRoleCodes.has(roleCodeOf(role)),
  );
});
const selectedCadreGroupingScopes = computed<MemberGroupingScope[]>(() => {
  const clubId = selectedClubId.value;
  const userId = currentUserId.value;
  if (!clubId || !userId || !canMaintainSelectedClub.value || canMaintainWholeClub.value) return [];

  const hasOfficerRole = (auth.value?.roles ?? []).some(
    (role) => roleCoversClub(role, clubId) && officerRoleCodes.has(roleCodeOf(role)),
  );
  const scopeMap = new Map<string, MemberGroupingScope>();

  members.value
    .filter(
      (member) =>
        member.clubId === clubId &&
        member.userId === userId &&
        member.isCurrent &&
        Boolean(member.groupName?.trim()) &&
        (hasOfficerRole || isCadrePosition(member.positionName)),
    )
    .forEach((member) => {
      const departmentName = member.departmentName?.trim() ?? "";
      const groupName = member.groupName?.trim() ?? "";
      scopeMap.set(`${departmentName}\n${groupName}`, { departmentName, groupName });
    });

  return Array.from(scopeMap.values());
});
const filteredEvaluations = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return evaluations.value.filter((evaluation) => {
    if (filters.publicStatus && evaluation.publicStatus !== filters.publicStatus) return false;
    if (!keyword) return true;
    return [
      evaluation.userName,
      evaluation.studentNo,
      evaluation.termName,
      evaluation.clubName,
      evaluation.departmentName,
      evaluation.groupName,
      evaluation.positionName,
      evaluation.grade,
      evaluation.commentText,
    ]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(keyword));
  });
});
const summary = computed(() => {
  const published = evaluations.value.filter(
    (evaluation) => evaluation.publicStatus === "published",
  ).length;
  const average =
    evaluations.value.length === 0
      ? 0
      : evaluations.value.reduce((sum, evaluation) => sum + evaluation.totalScore, 0) /
        evaluations.value.length;

  return {
    total: evaluations.value.length,
    published,
    draft: evaluations.value.length - published,
    average: Number(average.toFixed(1)),
  };
});
const memberOptions = computed(() =>
  members.value.filter((member) => member.isCurrent && canMaintainMember(member)),
);
const termOptions = computed(() =>
  Array.from(
    new Set(
      evaluations.value
        .map((evaluation) => evaluation.termName?.trim())
        .filter((value): value is string => Boolean(value)),
    ),
  ).sort((left, right) => left.localeCompare(right, "zh-CN")),
);
const evaluationFormTotal = computed(() =>
  Number(
    (
      Number(evaluationForm.activityScore || 0) +
      Number(evaluationForm.taskScore || 0) +
      Number(evaluationForm.learningScore || 0) +
      Number(evaluationForm.awardScore || 0)
    ).toFixed(1),
  ),
);
const evaluationFormGrade = computed(() => evaluationGrade(evaluationFormTotal.value));

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

async function loadEvaluations() {
  const requestId = ++evaluationRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId) {
    if (requestId === evaluationRequestId) evaluations.value = [];
    return;
  }

  loading.value = true;
  try {
    const query = new URLSearchParams({
      evaluationType: "semester",
    });
    if (filters.termName) query.set("termName", filters.termName);
    const data = await requestJson<ClubEvaluationRecord[]>(
      `/api/clubs/${clubId}/evaluations?${query.toString()}`,
    );
    if (requestId === evaluationRequestId) evaluations.value = data;
  } catch (error) {
    if (requestId === evaluationRequestId) {
      evaluations.value = [];
      ElMessage.error(error instanceof Error ? error.message : "成员考核加载失败");
    }
  } finally {
    if (requestId === evaluationRequestId) loading.value = false;
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
  await Promise.all([loadEvaluations(), loadMembers()]);
}

function resetEvaluationForm() {
  evaluationForm.userId = memberOptions.value[0]?.userId;
  evaluationForm.termName = `${new Date().getFullYear()} 学年春季学期`;
  evaluationForm.activityScore = 0;
  evaluationForm.taskScore = 0;
  evaluationForm.learningScore = 0;
  evaluationForm.awardScore = 0;
  evaluationForm.publicStatus = "draft";
  evaluationForm.commentText = "";
  evaluationFormRef.value?.clearValidate();
}

function openCreateDialog() {
  if (!canMaintainSelectedClub.value) {
    ElMessage.warning("当前账号没有维护该社团成员考核的权限。");
    return;
  }
  if (memberOptions.value.length === 0) {
    ElMessage.warning("当前社团暂无可选择的有效成员。");
    return;
  }

  evaluationFormMode.value = "create";
  evaluationTarget.value = null;
  resetEvaluationForm();
  evaluationDialogVisible.value = true;
}

function openEditDialog(row: ClubEvaluationRecord) {
  if (!canMaintainEvaluationRecord(row)) {
    ElMessage.warning("当前账号没有维护该社团成员考核的权限。");
    return;
  }

  evaluationFormMode.value = "edit";
  evaluationTarget.value = row;
  evaluationForm.userId = row.userId;
  evaluationForm.termName = row.termName;
  evaluationForm.activityScore = row.activityScore;
  evaluationForm.taskScore = row.taskScore;
  evaluationForm.learningScore = row.learningScore;
  evaluationForm.awardScore = row.awardScore;
  evaluationForm.publicStatus = row.publicStatus;
  evaluationForm.commentText = row.commentText ?? "";
  evaluationFormRef.value?.clearValidate();
  evaluationDialogVisible.value = true;
}

function openDetail(row: ClubEvaluationRecord) {
  detailTarget.value = row;
  detailVisible.value = true;
}

async function submitEvaluation() {
  if (
    !selectedClubId.value ||
    !currentUserId.value ||
    !(await validateForm(evaluationFormRef.value))
  ) {
    return;
  }

  saving.value = true;
  try {
    const payload = {
      evaluationType: "semester",
      userId: evaluationForm.userId,
      termName: evaluationForm.termName,
      awardTitle: null,
      awardLevel: null,
      awardReason: null,
      activityScore: evaluationForm.activityScore,
      taskScore: evaluationForm.taskScore,
      learningScore: evaluationForm.learningScore,
      awardScore: evaluationForm.awardScore,
      publicStatus: evaluationForm.publicStatus,
      commentText: emptyToNull(evaluationForm.commentText),
    };

    if (evaluationFormMode.value === "create") {
      await requestJson<ClubEvaluationRecord>(`/api/clubs/${selectedClubId.value}/evaluations`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
    } else if (evaluationTarget.value) {
      await requestJson<ClubEvaluationRecord>(
        `/api/clubs/${selectedClubId.value}/evaluations/${evaluationTarget.value.evaluationId}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        },
      );
    }

    ElMessage.success(evaluationFormMode.value === "create" ? "成员考核已录入" : "成员考核已更新");
    evaluationDialogVisible.value = false;
    await loadEvaluations();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "成员考核保存失败");
  } finally {
    saving.value = false;
  }
}

function clearFilters() {
  filters.keyword = "";
  filters.publicStatus = "";
  filters.termName = "";
}

function emptyToNull(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function canMaintainEvaluationRecord(row: ClubEvaluationRecord) {
  const member = members.value.find((item) => item.userId === row.userId);
  return Boolean(member && canMaintainMember(member));
}

function canMaintainMember(member: ClubMemberRecord) {
  if (!member.isCurrent || !canMaintainSelectedClub.value) return false;
  if (canMaintainWholeClub.value) return true;

  return selectedCadreGroupingScopes.value.some((scope) =>
    groupingMatchesScope(
      member.departmentName,
      member.groupName,
      scope.departmentName,
      scope.groupName,
    ),
  );
}

function groupingMatchesScope(
  targetDepartment: string | null | undefined,
  targetGroup: string | null | undefined,
  scopeDepartment: string | null | undefined,
  scopeGroup: string | null | undefined,
) {
  if (!targetGroup?.trim() || !scopeGroup?.trim()) return false;

  const groupMatches = targetGroup.trim().toLowerCase() === scopeGroup.trim().toLowerCase();
  const departmentMatches =
    !scopeDepartment?.trim() ||
    (targetDepartment ?? "").trim().toLowerCase() === scopeDepartment.trim().toLowerCase();
  return groupMatches && departmentMatches;
}

function isCadrePosition(positionName: string | null | undefined) {
  if (!positionName) return false;
  const normalized = positionName.trim().toLowerCase();
  return cadrePositionNames.has(normalized) || cadrePositionNames.has(positionName.trim());
}

function roleCodeOf(role: { code?: string | null; roleCode?: string | null }) {
  return (role.code ?? role.roleCode ?? "").trim().toLowerCase();
}

function memberLabel(member: ClubMemberRecord) {
  const scope = [member.departmentName, member.groupName, member.positionName]
    .filter(Boolean)
    .join(" / ");
  return `${member.userName}${member.studentNo ? `（${member.studentNo}）` : ""}${scope ? ` - ${scope}` : ""}`;
}

function statusTagType(status: PublicStatus) {
  return status === "published" ? "success" : "info";
}

function gradeTagType(grade: string) {
  const normalized = grade.trim().toUpperCase();
  if (normalized.startsWith("A") || normalized.includes("优秀")) return "success";
  if (normalized.startsWith("B") || normalized.includes("良好")) return "primary";
  if (normalized.startsWith("C") || normalized.includes("合格")) return "warning";
  if (normalized.startsWith("D") || normalized.includes("不合格")) return "danger";
  return "info";
}

function evaluationGrade(totalScore: number) {
  if (totalScore >= 320) return "优秀";
  if (totalScore >= 260) return "良好";
  if (totalScore >= 200) return "合格";
  return "待提升";
}

function formatDate(value: string | null) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", { hour12: false });
}

watch(selectedClubId, () => {
  filters.termName = "";
  void Promise.all([loadEvaluations(), loadMembers()]);
});

watch(
  () => filters.termName,
  () => {
    void loadEvaluations();
  },
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
  <section class="evaluation-page">
    <div class="page-head">
      <div>
        <h2>成员考核</h2>
        <div class="subtitle">查看成员学期考核，维护参与、任务、学习和奖项分。</div>
      </div>
      <div class="head-actions">
        <el-button :icon="Refresh" @click="reloadAll">刷新</el-button>
        <el-button
          v-if="canMaintainSelectedClub"
          type="primary"
          :icon="Plus"
          @click="openCreateDialog"
        >
          录入考核
        </el-button>
      </div>
    </div>

    <div class="summary-strip">
      <div>
        <span>考核记录</span>
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
        <span>平均总分</span>
        <strong>{{ summary.average }}</strong>
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
      <el-select
        v-model="filters.termName"
        class="term-select"
        clearable
        filterable
        allow-create
        default-first-option
        placeholder="考核学期"
      >
        <el-option v-for="term in termOptions" :key="term" :label="term" :value="term" />
      </el-select>
      <el-input
        v-model="filters.keyword"
        class="keyword"
        :prefix-icon="Search"
        clearable
        placeholder="请输入成员姓名、学号、部门或等级"
      />
      <el-radio-group v-model="filters.publicStatus">
        <el-radio-button label="">全部</el-radio-button>
        <el-radio-button label="published">已公示</el-radio-button>
        <el-radio-button label="draft">未公示</el-radio-button>
      </el-radio-group>
      <el-button @click="clearFilters">清除</el-button>
    </div>

    <el-table
      v-loading="loading"
      :data="filteredEvaluations"
      border
      stripe
      empty-text="暂无成员考核记录"
      row-key="evaluationId"
    >
      <el-table-column prop="userName" label="成员" min-width="140" />
      <el-table-column prop="studentNo" label="学号" min-width="120" />
      <el-table-column prop="termName" label="学期" min-width="150" />
      <el-table-column prop="departmentName" label="部门" min-width="120" />
      <el-table-column prop="groupName" label="小组" min-width="110" />
      <el-table-column prop="activityScore" label="参与分" width="90" />
      <el-table-column prop="taskScore" label="任务分" width="90" />
      <el-table-column prop="learningScore" label="学习分" width="90" />
      <el-table-column prop="awardScore" label="奖项分" width="90" />
      <el-table-column prop="totalScore" label="总分" width="90" />
      <el-table-column label="等级" width="95">
        <template #default="{ row }">
          <el-tag :type="gradeTagType(row.grade)" effect="plain">{{ row.grade }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="公示" width="105">
        <template #default="{ row }">
          <el-tag :type="statusTagType(row.publicStatus)" effect="plain">
            {{ row.publicStatusText }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="170" fixed="right">
        <template #default="{ row }">
          <el-button :icon="View" @click="openDetail(row)">查看</el-button>
          <el-button
            v-if="canMaintainEvaluationRecord(row)"
            type="primary"
            plain
            :icon="Edit"
            @click="openEditDialog(row)"
          >
            编辑
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="detailVisible" title="成员考核详情" width="760px">
      <el-descriptions v-if="detailTarget" :column="2" border>
        <el-descriptions-item label="成员">
          {{ detailTarget.userName }}（{{ detailTarget.studentNo || "-" }}）
        </el-descriptions-item>
        <el-descriptions-item label="考核学期">
          {{ detailTarget.termName }}
        </el-descriptions-item>
        <el-descriptions-item label="所属社团">
          {{ detailTarget.clubName }}
        </el-descriptions-item>
        <el-descriptions-item label="评价人">
          {{ detailTarget.evaluatorName || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="部门/小组">
          {{ detailTarget.departmentName || "-" }} / {{ detailTarget.groupName || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="职位">
          {{ detailTarget.positionName || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="总分">
          {{ detailTarget.totalScore }}
        </el-descriptions-item>
        <el-descriptions-item label="等级">
          <el-tag :type="gradeTagType(detailTarget.grade)" effect="plain">
            {{ detailTarget.grade }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="公示状态">
          <el-tag :type="statusTagType(detailTarget.publicStatus)" effect="plain">
            {{ detailTarget.publicStatusText }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="记录时间">
          {{ formatDate(detailTarget.createdAt) }}
        </el-descriptions-item>
        <el-descriptions-item label="评价说明" :span="2">
          {{ detailTarget.commentText || "-" }}
        </el-descriptions-item>
      </el-descriptions>
    </el-dialog>

    <el-dialog
      v-model="evaluationDialogVisible"
      :title="evaluationFormMode === 'create' ? '录入成员考核' : '编辑成员考核'"
      width="720px"
    >
      <el-form
        ref="evaluationFormRef"
        :model="evaluationForm"
        :rules="evaluationRules"
        label-width="100px"
      >
        <el-form-item label="社团">
          <el-input :model-value="selectedClub?.name" disabled />
        </el-form-item>
        <el-form-item v-if="evaluationFormMode === 'create'" label="成员" prop="userId">
          <el-select
            v-model="evaluationForm.userId"
            v-loading="memberLoading"
            filterable
            placeholder="选择被考核成员"
          >
            <el-option
              v-for="member in memberOptions"
              :key="member.memberId"
              :label="memberLabel(member)"
              :value="member.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-else label="成员">
          <el-input :model-value="evaluationTarget?.userName" disabled />
        </el-form-item>
        <el-form-item label="考核学期" prop="termName">
          <el-input v-model="evaluationForm.termName" maxlength="80" show-word-limit />
        </el-form-item>

        <div class="score-grid">
          <el-form-item label="参与分" prop="activityScore">
            <el-input-number
              v-model="evaluationForm.activityScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="任务分" prop="taskScore">
            <el-input-number
              v-model="evaluationForm.taskScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="学习分" prop="learningScore">
            <el-input-number
              v-model="evaluationForm.learningScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="奖项分" prop="awardScore">
            <el-input-number
              v-model="evaluationForm.awardScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
        </div>

        <div class="evaluation-preview">
          <span>总分 {{ evaluationFormTotal }}</span>
          <el-tag :type="gradeTagType(evaluationFormGrade)" effect="plain">
            {{ evaluationFormGrade }}
          </el-tag>
        </div>

        <el-form-item label="公示状态">
          <el-radio-group v-model="evaluationForm.publicStatus">
            <el-radio-button label="draft">未公示</el-radio-button>
            <el-radio-button label="published">已公示</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="评价说明">
          <el-input
            v-model="evaluationForm.commentText"
            type="textarea"
            :rows="3"
            maxlength="255"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="evaluationDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitEvaluation">保存考核</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.evaluation-page {
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
  margin: 0;
  font-size: 22px;
}

.subtitle {
  margin-top: 6px;
  color: var(--el-text-color-secondary);
}

.head-actions,
.toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.summary-strip {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  overflow: hidden;
}

.summary-strip div {
  padding: 14px 16px;
  border-right: 1px solid var(--el-border-color-light);
  background: var(--el-fill-color-lighter);
}

.summary-strip div:last-child {
  border-right: none;
}

.summary-strip span {
  display: block;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.summary-strip strong {
  display: block;
  margin-top: 6px;
  font-size: 20px;
  color: var(--el-text-color-primary);
}

.club-select {
  width: 220px;
}

.term-select {
  width: 180px;
}

.keyword {
  width: 280px;
}

.score-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 12px;
}

.evaluation-preview {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 14px;
  margin: 2px 0 18px 100px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: var(--el-fill-color-light);
}

@media (max-width: 900px) {
  .page-head {
    flex-direction: column;
  }

  .summary-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .summary-strip div {
    border-bottom: 1px solid var(--el-border-color-light);
  }

  .club-select,
  .term-select,
  .keyword {
    width: 100%;
  }

  .score-grid {
    grid-template-columns: 1fr;
  }

  .evaluation-preview {
    margin-left: 0;
  }
}
</style>

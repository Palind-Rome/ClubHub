<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules,
  type UploadUserFile,
} from "element-plus";
import {
  Check,
  Close,
  Download,
  Edit,
  Plus,
  Refresh,
  Search,
  Upload,
  View,
} from "@element-plus/icons-vue";
import { type AuthResponse, onSessionChange, readAuth } from "../authSession";
import { requestJson } from "../composables/useApiRequest";
import {
  canMaintainScopedMember,
  collectCadreScopesFromMembers,
  hasGlobalClubViewRole,
  hasWholeClubMaintainRole,
  type MemberGroupingScope,
} from "../composables/useClubEvaluationScope";
import {
  collectManageableClubIds,
  collectScopedClubIds,
  hasScopedRole,
  normalizedRoleCodeOf,
  type ScopedClubRole,
} from "../composables/useManageableClubs";

type AwardCategory = "honor" | "scholarship" | "competition" | "service" | "other";
type SchemeStatus = "draft" | "open" | "reviewing" | "publicizing" | "archived" | "closed";
type ApplicationType = "self" | "recommendation";
type ApplicationStatus =
  | "draft"
  | "club_review"
  | "advisor_review"
  | "school_review"
  | "returned"
  | "rejected"
  | "approved"
  | "publicizing"
  | "publicized"
  | "archived"
  | "withdrawn";
type ReviewResult = "approve" | "reject" | "return";
type PublicityStatus = "draft" | "publicizing" | "closed" | "archived";
type RuleScope = "global" | "club";
type RuleStatus = "draft" | "published" | "archived";

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

interface AwardLevelRecord {
  awardLevelId: number;
  awardSchemeId: number;
  levelName: string;
  awardScore: number;
  amount: number | null;
  quota: number | null;
  displayOrder: number;
  levelStatus: "active" | "inactive";
}

interface AwardSchemeRecord {
  awardSchemeId: number;
  clubId: number;
  clubName: string;
  awardName: string;
  awardCategory: AwardCategory;
  academicYear: string;
  termName: string | null;
  sponsorUnit: string | null;
  rewardLevel: string | null;
  fundingSource: string | null;
  isRanked: boolean;
  isFixedAmount: boolean;
  description: string | null;
  materialDescription: string | null;
  applicationStartAt: string | null;
  applicationEndAt: string | null;
  publicityStartAt: string | null;
  publicityEndAt: string | null;
  schemeStatus: SchemeStatus;
  schemeStatusText: string;
  createdByUserId: number | null;
  createdByName: string | null;
  createdAt: string;
  updatedAt: string;
  levels: AwardLevelRecord[];
}

interface AwardRuleDocumentRecord {
  ruleDocumentId: number;
  clubId: number | null;
  clubName: string | null;
  ruleTitle: string;
  ruleScope: RuleScope;
  ruleScopeText: string;
  academicYear: string;
  termName: string | null;
  issuerName: string | null;
  summary: string | null;
  contentText: string | null;
  materialUrl: string | null;
  materialName: string | null;
  versionNo: string;
  ruleStatus: RuleStatus;
  ruleStatusText: string;
  effectiveStartAt: string | null;
  effectiveEndAt: string | null;
  publishedByUserId: number | null;
  publishedByName: string | null;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

interface AwardReviewRecord {
  reviewId: number;
  awardApplicationId: number;
  reviewRound: number;
  reviewStep: string;
  reviewResult: string;
  reviewerUserId: number | null;
  reviewerName: string | null;
  reviewComment: string | null;
  fromStatus: string | null;
  toStatus: string | null;
  reviewedAt: string;
}

interface AwardAttachmentRecord {
  attachmentId: number;
  awardApplicationId: number;
  attachmentName: string;
  attachmentUrl: string;
  attachmentType: string | null;
  uploadedByUserId: number;
  uploadedByName: string | null;
  uploadedAt: string;
}

interface AwardApplicationRecord {
  awardApplicationId: number;
  clubId: number;
  clubName: string;
  awardSchemeId: number;
  awardName: string;
  awardCategory: AwardCategory;
  academicYear: string;
  termName: string | null;
  awardLevelId: number;
  levelName: string;
  applicantUserId: number;
  applicantName: string;
  applicantStudentNo: string | null;
  recommenderUserId: number | null;
  recommenderName: string | null;
  submitterUserId: number;
  submitterName: string | null;
  applicationType: ApplicationType;
  applicationReason: string | null;
  materialUrl: string | null;
  currentStep: string;
  currentStepText: string;
  applicationStatus: ApplicationStatus;
  applicationStatusText: string;
  publicStatus: "none" | "publicizing" | "publicized" | "withdrawn";
  reviewRound: number;
  finalAwardScore: number | null;
  finalAmount: number | null;
  submittedAt: string | null;
  approvedAt: string | null;
  publicizedAt: string | null;
  archivedAt: string | null;
  createdAt: string;
  updatedAt: string;
  reviewRecords: AwardReviewRecord[];
  attachments: AwardAttachmentRecord[];
}

interface AwardPublicityItemRecord {
  publicityItemId: number;
  publicityBatchId: number;
  awardApplicationId: number;
  applicantUserId: number;
  applicantName: string;
  awardName: string;
  levelName: string;
  finalAwardScore: number | null;
  finalAmount: number | null;
  displayOrder: number;
  publicityResult: string;
  createdAt: string;
}

interface AwardPublicityBatchRecord {
  publicityBatchId: number;
  clubId: number;
  clubName: string;
  title: string;
  description: string | null;
  publicityStartAt: string | null;
  publicityEndAt: string | null;
  publicityStatus: PublicityStatus;
  publicityStatusText: string;
  publisherUserId: number | null;
  publisherName: string | null;
  createdAt: string;
  updatedAt: string;
  items: AwardPublicityItemRecord[];
}

interface EditableAwardLevel {
  awardLevelId?: number;
  levelName: string;
  awardScore: number;
  amount: number | null;
  quota: number | null;
  displayOrder: number;
  levelStatus: "active" | "inactive";
}

const awardWorkflowPermission = "evaluation:draft";
const systemAdminRoleCodes = new Set(["system_admin", "sysadmin"]);
const platformAdminRoleCodes = new Set([
  "platform_admin",
  "club_admin",
  "system_admin",
  "admin",
  "club_reviewer",
]);
const clubPrincipalRoleCodes = new Set([
  "club_president",
  "club_leader",
  "club_manager",
  "president",
]);
const clubAdvisorRoleCodes = new Set(["advisor", "club_advisor", "teacher_advisor"]);
const principalPositionNames = new Set([
  "负责人",
  "会长",
  "社长",
  "社团负责人",
  "president",
  "leader",
  "club president",
  "club leader",
]);
const auth = ref<AuthResponse | null>(readAuth());
const clubs = ref<Club[]>([]);
const members = ref<ClubMemberRecord[]>([]);
const schemes = ref<AwardSchemeRecord[]>([]);
const ruleDocuments = ref<AwardRuleDocumentRecord[]>([]);
const applications = ref<AwardApplicationRecord[]>([]);
const publicityBatches = ref<AwardPublicityBatchRecord[]>([]);
const ruleFiles = ref<UploadUserFile[]>([]);
const applicationFiles = ref<UploadUserFile[]>([]);
const selectedClubId = ref<number>();
const activeTab = ref<"rules" | "schemes" | "applications" | "publicity">("applications");
const loading = ref(false);
const memberLoading = ref(false);
const saving = ref(false);
const schemeDialogVisible = ref(false);
const ruleDialogVisible = ref(false);
const ruleDetailVisible = ref(false);
const applicationDialogVisible = ref(false);
const reviewDialogVisible = ref(false);
const publicityDialogVisible = ref(false);
const detailVisible = ref(false);
const schemeFormRef = ref<FormInstance>();
const ruleFormRef = ref<FormInstance>();
const applicationFormRef = ref<FormInstance>();
const reviewFormRef = ref<FormInstance>();
const publicityFormRef = ref<FormInstance>();
const schemeTarget = ref<AwardSchemeRecord | null>(null);
const ruleTarget = ref<AwardRuleDocumentRecord | null>(null);
const ruleDetailTarget = ref<AwardRuleDocumentRecord | null>(null);
const applicationTarget = ref<AwardApplicationRecord | null>(null);
const detailTarget = ref<AwardApplicationRecord | null>(null);
let stopSessionListener: (() => void) | null = null;
let loadRequestId = 0;
let memberRequestId = 0;

const filters = reactive({
  keyword: "",
  ruleStatus: "" as "" | RuleStatus,
  schemeStatus: "" as "" | SchemeStatus,
  applicationStatus: "" as "" | ApplicationStatus,
  publicityStatus: "" as "" | PublicityStatus,
});

const schemeForm = reactive({
  awardName: "",
  awardCategory: "honor" as AwardCategory,
  academicYear: defaultAcademicYear(),
  termName: defaultTermName(),
  sponsorUnit: "",
  rewardLevel: "",
  fundingSource: "",
  isRanked: true,
  isFixedAmount: true,
  description: "",
  materialDescription: "",
  applicationStartAt: "",
  applicationEndAt: "",
  publicityStartAt: "",
  publicityEndAt: "",
  schemeStatus: "draft" as SchemeStatus,
  levels: [
    {
      levelName: "一等奖",
      awardScore: 20,
      amount: null,
      quota: null,
      displayOrder: 1,
      levelStatus: "active",
    },
  ] as EditableAwardLevel[],
});

const ruleForm = reactive({
  ruleTitle: "",
  ruleScope: "club" as RuleScope,
  academicYear: defaultAcademicYear(),
  termName: defaultTermName(),
  issuerName: "",
  summary: "",
  contentText: "",
  materialUrl: "",
  materialName: "",
  versionNo: "1.0",
  ruleStatus: "draft" as RuleStatus,
  effectiveStartAt: "",
  effectiveEndAt: "",
});

const applicationForm = reactive({
  awardSchemeId: undefined as number | undefined,
  awardLevelId: undefined as number | undefined,
  applicantUserId: undefined as number | undefined,
  applicationType: "self" as ApplicationType,
  applicationReason: "",
  materialUrl: "",
  submitNow: true,
});

const reviewForm = reactive({
  reviewResult: "approve" as ReviewResult,
  finalAwardScore: 0,
  finalAmount: null as number | null,
  reviewComment: "",
});

const publicityForm = reactive({
  title: "",
  description: "",
  publicityStartAt: "",
  publicityEndAt: "",
  awardApplicationIds: [] as number[],
});

const schemeRules: FormRules = {
  awardName: [{ required: true, message: "请填写奖项名称", trigger: "blur" }],
  academicYear: [{ required: true, message: "请填写评定学年", trigger: "blur" }],
};
const ruleRules: FormRules = {
  ruleTitle: [{ required: true, message: "请填写细则标题", trigger: "blur" }],
  academicYear: [{ required: true, message: "请填写适用学年", trigger: "blur" }],
  ruleScope: [{ required: true, message: "请选择细则范围", trigger: "change" }],
  ruleStatus: [{ required: true, message: "请选择细则状态", trigger: "change" }],
};
const applicationRules: FormRules = {
  awardSchemeId: [{ required: true, message: "请选择奖项项目", trigger: "change" }],
  awardLevelId: [{ required: true, message: "请选择奖项等级", trigger: "change" }],
  applicantUserId: [{ required: true, message: "请选择申请成员", trigger: "change" }],
  applicationReason: [{ required: true, message: "请填写申请理由", trigger: "blur" }],
};
const reviewRules: FormRules = {
  reviewResult: [{ required: true, message: "请选择审核结果", trigger: "change" }],
};
const publicityRules: FormRules = {
  title: [{ required: true, message: "请填写公示标题", trigger: "blur" }],
  awardApplicationIds: [{ required: true, message: "请选择公示名单", trigger: "change" }],
};

const currentUserId = computed(() => auth.value?.user.id);
const currentRoles = computed(() => auth.value?.roles ?? []);
const permissions = computed(() => auth.value?.permissions ?? []);
const hasAllPermissions = computed(
  () =>
    permissions.value.includes("*") ||
    currentRoles.value.some((role) => role.permissions?.includes("*")),
);
const hasSystemAdminRole = computed(
  () => hasAllPermissions.value || hasAnyRoleCode(currentRoles.value, systemAdminRoleCodes),
);
const hasPlatformAwardReviewRole = computed(
  () => hasSystemAdminRole.value || hasAnyRoleCode(currentRoles.value, platformAdminRoleCodes),
);
const canManageGlobalRules = computed(() => hasPlatformAwardReviewRole.value);
const manageableClubIds = computed(() =>
  collectManageableClubIds(currentRoles.value, awardWorkflowPermission),
);
const scopedClubIds = computed(() => collectScopedClubIds(currentRoles.value));
const canViewAllClubs = computed(
  () => hasAllPermissions.value || hasGlobalClubViewRole(currentRoles.value),
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
const canUploadRuleDocument = computed(
  () => canMaintainSelectedClub.value || canManageGlobalRules.value,
);
const canMaintainWholeClub = computed(() => {
  const clubId = selectedClubId.value;
  if (!clubId || !canMaintainSelectedClub.value) return false;
  if (hasAllPermissions.value) return true;
  return hasWholeClubMaintainRole(currentRoles.value, clubId);
});
const selectedCadreGroupingScopes = computed<MemberGroupingScope[]>(() => {
  const clubId = selectedClubId.value;
  const userId = currentUserId.value;
  if (!clubId || !userId || !canMaintainSelectedClub.value || canMaintainWholeClub.value) return [];
  return collectCadreScopesFromMembers(members.value, currentRoles.value, clubId, userId);
});
const currentMembers = computed(() => members.value.filter((member) => member.isCurrent));
const applicantOptions = computed(() =>
  canMaintainSelectedClub.value
    ? currentMembers.value.filter((member) => canMaintainMember(member))
    : currentMembers.value.filter((member) => member.userId === currentUserId.value),
);
const openSchemes = computed(() =>
  schemes.value.filter((scheme) => scheme.schemeStatus === "open" || canMaintainSelectedClub.value),
);
const selectedApplicationScheme = computed(() =>
  schemes.value.find((scheme) => scheme.awardSchemeId === applicationForm.awardSchemeId),
);
const selectedApplicationLevels = computed(() =>
  (selectedApplicationScheme.value?.levels ?? []).filter((level) => level.levelStatus === "active"),
);
const filteredRuleDocuments = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return ruleDocuments.value.filter((document) => {
    if (filters.ruleStatus && document.ruleStatus !== filters.ruleStatus) return false;
    if (!keyword) return true;
    return [
      document.ruleTitle,
      document.ruleScopeText,
      document.academicYear,
      document.termName,
      document.issuerName,
      document.summary,
      document.clubName,
      document.materialName,
    ]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(keyword));
  });
});
const filteredSchemes = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return schemes.value.filter((scheme) => {
    if (filters.schemeStatus && scheme.schemeStatus !== filters.schemeStatus) return false;
    if (!keyword) return true;
    return [
      scheme.awardName,
      scheme.academicYear,
      scheme.termName,
      scheme.sponsorUnit,
      scheme.rewardLevel,
    ]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(keyword));
  });
});
const filteredApplications = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return applications.value.filter((application) => {
    if (filters.applicationStatus && application.applicationStatus !== filters.applicationStatus) {
      return false;
    }
    if (!keyword) return true;
    return [
      application.awardName,
      application.levelName,
      application.applicantName,
      application.applicantStudentNo,
      application.applicationReason,
      application.currentStepText,
      application.applicationStatusText,
    ]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(keyword));
  });
});
const filteredPublicityBatches = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return publicityBatches.value.filter((batch) => {
    if (filters.publicityStatus && batch.publicityStatus !== filters.publicityStatus) return false;
    if (!keyword) return true;
    return [batch.title, batch.description, batch.publicityStatusText]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(keyword));
  });
});
const approvedApplications = computed(() =>
  applications.value.filter((application) => application.applicationStatus === "approved"),
);
const summary = computed(() => ({
  schemes: schemes.value.length,
  rules: ruleDocuments.value.filter((document) => document.ruleStatus === "published").length,
  reviewing: applications.value.filter((application) =>
    ["club_review", "advisor_review", "school_review"].includes(application.applicationStatus),
  ).length,
  approved: approvedApplications.value.length,
  archived: applications.value.filter((application) => application.applicationStatus === "archived")
    .length,
}));

function defaultAcademicYear(date = new Date()) {
  const year = date.getFullYear();
  const month = date.getMonth() + 1;
  const startYear = month >= 9 ? year : year - 1;
  return `${startYear}-${startYear + 1}`;
}

function defaultTermName(date = new Date()) {
  const month = date.getMonth() + 1;
  return month >= 2 && month <= 7 ? "春季" : "秋季";
}

function refreshSession() {
  auth.value = readAuth();
}

async function validateForm(form?: FormInstance) {
  if (!form) return false;
  try {
    await form.validate();
    return true;
  } catch {
    return false;
  }
}

async function loadClubs() {
  if (!currentUserId.value) {
    clubs.value = [];
    selectedClubId.value = undefined;
    return;
  }

  try {
    clubs.value = await requestJson<Club[]>("/api/clubs");
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

async function loadMembers() {
  const requestId = ++memberRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId) {
    if (requestId === memberRequestId) members.value = [];
    return;
  }

  memberLoading.value = true;
  try {
    const query = new URLSearchParams({ includeHistory: "false" });
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

async function loadAwardWorkspace() {
  const requestId = ++loadRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId) {
    schemes.value = [];
    ruleDocuments.value = [];
    applications.value = [];
    publicityBatches.value = [];
    return;
  }

  loading.value = true;
  try {
    const [schemeData, ruleDocumentData, applicationData, publicityData] = await Promise.all([
      requestJson<AwardSchemeRecord[]>(`/api/clubs/${clubId}/award-schemes`),
      requestJson<AwardRuleDocumentRecord[]>(`/api/clubs/${clubId}/award-rule-documents`),
      requestJson<AwardApplicationRecord[]>(`/api/clubs/${clubId}/award-applications`),
      requestJson<AwardPublicityBatchRecord[]>(`/api/clubs/${clubId}/award-publicity`),
    ]);
    if (requestId !== loadRequestId) return;
    schemes.value = schemeData;
    ruleDocuments.value = ruleDocumentData;
    applications.value = applicationData;
    publicityBatches.value = publicityData;
  } catch (error) {
    if (requestId === loadRequestId) {
      schemes.value = [];
      ruleDocuments.value = [];
      applications.value = [];
      publicityBatches.value = [];
      ElMessage.error(error instanceof Error ? error.message : "评奖评优流程加载失败");
    }
  } finally {
    if (requestId === loadRequestId) loading.value = false;
  }
}

async function reloadAll() {
  await loadClubs();
  await Promise.all([loadMembers(), loadAwardWorkspace()]);
}

function resetSchemeForm() {
  schemeForm.awardName = "";
  schemeForm.awardCategory = "honor";
  schemeForm.academicYear = defaultAcademicYear();
  schemeForm.termName = defaultTermName();
  schemeForm.sponsorUnit = "";
  schemeForm.rewardLevel = "";
  schemeForm.fundingSource = "";
  schemeForm.isRanked = true;
  schemeForm.isFixedAmount = true;
  schemeForm.description = "";
  schemeForm.materialDescription = "";
  schemeForm.applicationStartAt = "";
  schemeForm.applicationEndAt = "";
  schemeForm.publicityStartAt = "";
  schemeForm.publicityEndAt = "";
  schemeForm.schemeStatus = "draft";
  schemeForm.levels = [
    {
      levelName: "一等奖",
      awardScore: 20,
      amount: null,
      quota: null,
      displayOrder: 1,
      levelStatus: "active",
    },
  ];
  schemeFormRef.value?.clearValidate();
}

function openCreateSchemeDialog() {
  if (!canMaintainSelectedClub.value) {
    ElMessage.warning("当前账号没有维护奖项项目的权限。");
    return;
  }
  schemeTarget.value = null;
  resetSchemeForm();
  schemeDialogVisible.value = true;
}

function openEditSchemeDialog(row: AwardSchemeRecord) {
  if (!canMaintainSelectedClub.value) {
    ElMessage.warning("当前账号没有维护奖项项目的权限。");
    return;
  }
  schemeTarget.value = row;
  schemeForm.awardName = row.awardName;
  schemeForm.awardCategory = row.awardCategory;
  schemeForm.academicYear = row.academicYear;
  schemeForm.termName = row.termName ?? "";
  schemeForm.sponsorUnit = row.sponsorUnit ?? "";
  schemeForm.rewardLevel = row.rewardLevel ?? "";
  schemeForm.fundingSource = row.fundingSource ?? "";
  schemeForm.isRanked = row.isRanked;
  schemeForm.isFixedAmount = row.isFixedAmount;
  schemeForm.description = row.description ?? "";
  schemeForm.materialDescription = row.materialDescription ?? "";
  schemeForm.applicationStartAt = toDateTimeLocal(row.applicationStartAt);
  schemeForm.applicationEndAt = toDateTimeLocal(row.applicationEndAt);
  schemeForm.publicityStartAt = toDateTimeLocal(row.publicityStartAt);
  schemeForm.publicityEndAt = toDateTimeLocal(row.publicityEndAt);
  schemeForm.schemeStatus = row.schemeStatus;
  schemeForm.levels = row.levels.map((level, index) => ({
    awardLevelId: level.awardLevelId,
    levelName: level.levelName,
    awardScore: level.awardScore,
    amount: level.amount,
    quota: level.quota,
    displayOrder: index + 1,
    levelStatus: level.levelStatus,
  }));
  schemeFormRef.value?.clearValidate();
  schemeDialogVisible.value = true;
}

function addAwardLevel() {
  schemeForm.levels.push({
    levelName: "",
    awardScore: 0,
    amount: null,
    quota: null,
    displayOrder: schemeForm.levels.length + 1,
    levelStatus: "active",
  });
}

function removeAwardLevel(index: number) {
  if (schemeForm.levels.length <= 1) {
    ElMessage.warning("至少保留一个奖项等级。");
    return;
  }
  schemeForm.levels.splice(index, 1);
  schemeForm.levels.forEach((level, levelIndex) => {
    level.displayOrder = levelIndex + 1;
  });
}

async function submitScheme() {
  if (!selectedClubId.value || !(await validateForm(schemeFormRef.value))) return;
  const levelError = validateAwardLevels();
  if (levelError) {
    ElMessage.warning(levelError);
    return;
  }

  saving.value = true;
  try {
    const payload = {
      awardName: schemeForm.awardName.trim(),
      awardCategory: schemeForm.awardCategory,
      academicYear: schemeForm.academicYear.trim(),
      termName: emptyToNull(schemeForm.termName),
      sponsorUnit: emptyToNull(schemeForm.sponsorUnit),
      rewardLevel: emptyToNull(schemeForm.rewardLevel),
      fundingSource: emptyToNull(schemeForm.fundingSource),
      isRanked: schemeForm.isRanked,
      isFixedAmount: schemeForm.isFixedAmount,
      description: emptyToNull(schemeForm.description),
      materialDescription: emptyToNull(schemeForm.materialDescription),
      applicationStartAt: fromDateTimeLocal(schemeForm.applicationStartAt),
      applicationEndAt: fromDateTimeLocal(schemeForm.applicationEndAt),
      publicityStartAt: fromDateTimeLocal(schemeForm.publicityStartAt),
      publicityEndAt: fromDateTimeLocal(schemeForm.publicityEndAt),
      schemeStatus: schemeForm.schemeStatus,
      levels: schemeForm.levels.map((level, index) => ({
        awardLevelId: level.awardLevelId,
        levelName: level.levelName.trim(),
        awardScore: level.awardScore,
        amount: level.amount,
        quota: level.quota,
        displayOrder: index + 1,
        levelStatus: level.levelStatus,
      })),
    };
    const url = schemeTarget.value
      ? `/api/clubs/${selectedClubId.value}/award-schemes/${schemeTarget.value.awardSchemeId}`
      : `/api/clubs/${selectedClubId.value}/award-schemes`;
    await requestJson<AwardSchemeRecord>(url, {
      method: schemeTarget.value ? "PATCH" : "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    ElMessage.success(schemeTarget.value ? "奖项项目已更新" : "奖项项目已创建");
    schemeDialogVisible.value = false;
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "奖项项目保存失败");
  } finally {
    saving.value = false;
  }
}

function resetRuleForm() {
  ruleForm.ruleTitle = "";
  ruleForm.ruleScope = canMaintainSelectedClub.value ? "club" : "global";
  ruleForm.academicYear = defaultAcademicYear();
  ruleForm.termName = defaultTermName();
  ruleForm.issuerName =
    ruleForm.ruleScope === "club"
      ? `${selectedClub.value?.name ?? "社团"}评奖评优工作组`
      : "学生社团管理部门";
  ruleForm.summary = "";
  ruleForm.contentText = "";
  ruleForm.materialUrl = "";
  ruleForm.materialName = "";
  ruleForm.versionNo = "1.0";
  ruleForm.ruleStatus = "draft";
  ruleForm.effectiveStartAt = "";
  ruleForm.effectiveEndAt = "";
  ruleFiles.value = [];
  ruleFormRef.value?.clearValidate();
}

function openCreateRuleDialog() {
  if (!canUploadRuleDocument.value) {
    ElMessage.warning("当前账号没有上传评定细则的权限。");
    return;
  }
  ruleTarget.value = null;
  resetRuleForm();
  ruleDialogVisible.value = true;
}

function openEditRuleDialog(row: AwardRuleDocumentRecord) {
  if (!canEditRuleDocument(row)) {
    ElMessage.warning("当前账号没有维护该评定细则的权限。");
    return;
  }
  ruleTarget.value = row;
  ruleForm.ruleTitle = row.ruleTitle;
  ruleForm.ruleScope = row.ruleScope;
  ruleForm.academicYear = row.academicYear;
  ruleForm.termName = row.termName ?? "";
  ruleForm.issuerName = row.issuerName ?? "";
  ruleForm.summary = row.summary ?? "";
  ruleForm.contentText = row.contentText ?? "";
  ruleForm.materialUrl = row.materialUrl ?? "";
  ruleForm.materialName = row.materialName ?? "";
  ruleFiles.value = [];
  ruleForm.versionNo = row.versionNo;
  ruleForm.ruleStatus = row.ruleStatus;
  ruleForm.effectiveStartAt = toDateTimeLocal(row.effectiveStartAt);
  ruleForm.effectiveEndAt = toDateTimeLocal(row.effectiveEndAt);
  ruleFormRef.value?.clearValidate();
  ruleDialogVisible.value = true;
}

async function submitRuleDocument() {
  if (!selectedClubId.value || !(await validateForm(ruleFormRef.value))) return;
  if (ruleForm.ruleScope === "global" && !canManageGlobalRules.value) {
    ElMessage.warning("只有平台或系统管理员可以上传学校级细则。");
    return;
  }
  if (ruleForm.ruleScope === "club" && !canUploadRuleDocument.value) {
    ElMessage.warning("当前账号没有上传社团细则的权限。");
    return;
  }

  saving.value = true;
  try {
    const payload = {
      ruleTitle: ruleForm.ruleTitle.trim(),
      ruleScope: ruleForm.ruleScope,
      academicYear: ruleForm.academicYear.trim(),
      termName: emptyToNull(ruleForm.termName),
      issuerName: emptyToNull(ruleForm.issuerName),
      summary: emptyToNull(ruleForm.summary),
      contentText: emptyToNull(ruleForm.contentText),
      materialUrl: emptyToNull(ruleForm.materialUrl),
      materialName: emptyToNull(ruleForm.materialName),
      versionNo: emptyToNull(ruleForm.versionNo) ?? "1.0",
      ruleStatus: ruleForm.ruleStatus,
      effectiveStartAt: fromDateTimeLocal(ruleForm.effectiveStartAt),
      effectiveEndAt: fromDateTimeLocal(ruleForm.effectiveEndAt),
    };
    const url = ruleTarget.value
      ? `/api/clubs/${selectedClubId.value}/award-rule-documents/${ruleTarget.value.ruleDocumentId}`
      : `/api/clubs/${selectedClubId.value}/award-rule-documents`;
    const savedDocument = await requestJson<AwardRuleDocumentRecord>(url, {
      method: ruleTarget.value ? "PATCH" : "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (ruleFiles.value.some((entry) => entry.raw)) {
      await uploadRuleDocumentFile(savedDocument.ruleDocumentId);
    }
    ElMessage.success(ruleTarget.value ? "评定细则已更新" : "评定细则已创建");
    ruleDialogVisible.value = false;
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "评定细则保存失败");
  } finally {
    saving.value = false;
  }
}

async function uploadRuleDocumentFile(ruleDocumentId: number) {
  const file = ruleFiles.value.find((entry) => entry.raw)?.raw;
  if (!selectedClubId.value || !file) return;
  if (file.size > 50 * 1024 * 1024) {
    throw new Error(`“${file.name}”超过 50 MB，无法上传`);
  }

  const formData = new FormData();
  formData.append("file", file, file.name);
  const response = await fetch(
    `/api/clubs/${selectedClubId.value}/award-rule-documents/${ruleDocumentId}/file`,
    {
      method: "POST",
      headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
      body: formData,
    },
  );
  if (!response.ok) throw new Error(await readResponseMessage(response, "评定细则附件上传失败"));
}

async function publishRuleDocument(row: AwardRuleDocumentRecord) {
  if (!selectedClubId.value || !canEditRuleDocument(row)) return;
  await confirmAction(`发布评定细则“${row.ruleTitle}”？`);
  saving.value = true;
  try {
    await requestJson<AwardRuleDocumentRecord>(
      `/api/clubs/${selectedClubId.value}/award-rule-documents/${row.ruleDocumentId}/publish`,
      { method: "POST" },
    );
    ElMessage.success("评定细则已发布");
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "评定细则发布失败");
  } finally {
    saving.value = false;
  }
}

function resetApplicationForm() {
  const firstScheme = openSchemes.value[0];
  const firstLevel = firstScheme?.levels.find((level) => level.levelStatus === "active");
  applicationForm.awardSchemeId = firstScheme?.awardSchemeId;
  applicationForm.awardLevelId = firstLevel?.awardLevelId;
  applicationForm.applicantUserId = canMaintainSelectedClub.value
    ? applicantOptions.value[0]?.userId
    : currentUserId.value;
  applicationForm.applicationType = canMaintainSelectedClub.value ? "recommendation" : "self";
  applicationForm.applicationReason = "";
  applicationForm.materialUrl = "";
  applicationForm.submitNow = true;
  applicationFiles.value = [];
  applicationFormRef.value?.clearValidate();
}

function openCreateApplicationDialog() {
  if (!selectedClubId.value) return;
  if (openSchemes.value.length === 0) {
    ElMessage.warning("当前社团暂无可申请的奖项项目。");
    return;
  }
  applicationTarget.value = null;
  resetApplicationForm();
  applicationDialogVisible.value = true;
}

function openEditApplicationDialog(row: AwardApplicationRecord) {
  if (!canEditApplication(row)) {
    ElMessage.warning("当前申请不可修改。");
    return;
  }
  applicationTarget.value = row;
  applicationForm.awardSchemeId = row.awardSchemeId;
  applicationForm.awardLevelId = row.awardLevelId;
  applicationForm.applicantUserId = row.applicantUserId;
  applicationForm.applicationType = row.applicationType;
  applicationForm.applicationReason = row.applicationReason ?? "";
  applicationForm.materialUrl = row.materialUrl ?? "";
  applicationForm.submitNow = false;
  applicationFiles.value = [];
  applicationFormRef.value?.clearValidate();
  applicationDialogVisible.value = true;
}

async function submitApplication() {
  if (!selectedClubId.value || !(await validateForm(applicationFormRef.value))) return;
  saving.value = true;
  try {
    const payload = {
      awardSchemeId: applicationForm.awardSchemeId,
      awardLevelId: applicationForm.awardLevelId,
      applicantUserId: applicationForm.applicantUserId,
      applicationType: applicationForm.applicationType,
      applicationReason: applicationForm.applicationReason.trim(),
      materialUrl: emptyToNull(applicationForm.materialUrl),
      submitNow: applicationForm.submitNow,
    };
    let savedApplication: AwardApplicationRecord;
    if (applicationTarget.value) {
      savedApplication = await requestJson<AwardApplicationRecord>(
        `/api/clubs/${selectedClubId.value}/award-applications/${applicationTarget.value.awardApplicationId}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            awardLevelId: payload.awardLevelId,
            applicationReason: payload.applicationReason,
            materialUrl: payload.materialUrl,
          }),
        },
      );
    } else {
      savedApplication = await requestJson<AwardApplicationRecord>(
        `/api/clubs/${selectedClubId.value}/award-applications`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            ...payload,
            submitNow: applicationFiles.value.some((entry) => entry.raw)
              ? false
              : payload.submitNow,
          }),
        },
      );
    }
    if (applicationFiles.value.some((entry) => entry.raw)) {
      savedApplication = await uploadApplicationFiles(savedApplication.awardApplicationId);
      if (!applicationTarget.value && applicationForm.submitNow) {
        savedApplication = await requestJson<AwardApplicationRecord>(
          `/api/clubs/${selectedClubId.value}/award-applications/${savedApplication.awardApplicationId}/submit`,
          { method: "POST" },
        );
      }
    }
    ElMessage.success(applicationTarget.value ? "申请已更新" : "申请已提交");
    applicationDialogVisible.value = false;
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "申请保存失败");
  } finally {
    saving.value = false;
  }
}

async function uploadApplicationFiles(awardApplicationId: number) {
  if (!selectedClubId.value) throw new Error("请选择社团。");
  let latest: AwardApplicationRecord | null = null;
  const files = applicationFiles.value.flatMap((entry) => (entry.raw ? [entry.raw] : []));
  for (const file of files) {
    if (file.size > 50 * 1024 * 1024) {
      throw new Error(`“${file.name}”超过 50 MB，无法上传`);
    }
    const formData = new FormData();
    formData.append("file", file, file.name);
    formData.append("attachmentType", "申请材料");
    const response = await fetch(
      `/api/clubs/${selectedClubId.value}/award-applications/${awardApplicationId}/attachments`,
      {
        method: "POST",
        headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
        body: formData,
      },
    );
    if (!response.ok) throw new Error(await readResponseMessage(response, "申请材料上传失败"));
    latest = (await response.json()) as AwardApplicationRecord;
  }
  if (!latest) throw new Error("请先选择申请材料文件。");
  return latest;
}

async function submitExistingApplication(row: AwardApplicationRecord) {
  if (!selectedClubId.value) return;
  try {
    await requestJson<AwardApplicationRecord>(
      `/api/clubs/${selectedClubId.value}/award-applications/${row.awardApplicationId}/submit`,
      { method: "POST" },
    );
    ElMessage.success("申请已提交审核");
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "提交申请失败");
  }
}

async function downloadRuleDocument(row: AwardRuleDocumentRecord) {
  if (!selectedClubId.value || !row.materialUrl) return;
  await downloadManagedAwardFile(
    `/api/clubs/${selectedClubId.value}/award-rule-documents/${row.ruleDocumentId}/file`,
    row.materialName || row.ruleTitle,
  );
}

async function downloadApplicationAttachment(
  application: AwardApplicationRecord,
  attachment: AwardAttachmentRecord,
) {
  if (!selectedClubId.value) return;
  await downloadManagedAwardFile(
    `/api/clubs/${selectedClubId.value}/award-applications/${application.awardApplicationId}/attachments/${attachment.attachmentId}/file`,
    attachment.attachmentName,
  );
}

async function downloadManagedAwardFile(url: string, fileName: string) {
  try {
    const response = await fetch(url, {
      headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
    });
    if (response.redirected) {
      window.open(response.url, "_blank", "noopener");
      return;
    }
    if (!response.ok) throw new Error(await readResponseMessage(response, "文件下载失败"));
    const objectUrl = URL.createObjectURL(await response.blob());
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = fileName;
    link.click();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1_000);
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "文件下载失败");
  }
}

async function readResponseMessage(response: Response, fallback: string) {
  const text = await response.text();
  if (!text) return fallback;
  try {
    const body = JSON.parse(text) as { message?: string; detail?: string; title?: string };
    return body.message || body.detail || body.title || fallback;
  } catch {
    return text;
  }
}

function openReviewDialog(row: AwardApplicationRecord, result: ReviewResult) {
  applicationTarget.value = row;
  reviewForm.reviewResult = result;
  reviewForm.finalAwardScore = row.finalAwardScore ?? inferredApplicationScore(row);
  reviewForm.finalAmount = row.finalAmount;
  reviewForm.reviewComment = "";
  reviewFormRef.value?.clearValidate();
  reviewDialogVisible.value = true;
}

async function submitReview() {
  if (
    !selectedClubId.value ||
    !applicationTarget.value ||
    !(await validateForm(reviewFormRef.value))
  ) {
    return;
  }
  saving.value = true;
  try {
    await requestJson<AwardApplicationRecord>(
      `/api/clubs/${selectedClubId.value}/award-applications/${applicationTarget.value.awardApplicationId}/review`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          reviewResult: reviewForm.reviewResult,
          finalAwardScore:
            reviewForm.reviewResult === "approve" ? reviewForm.finalAwardScore : undefined,
          finalAmount: reviewForm.reviewResult === "approve" ? reviewForm.finalAmount : undefined,
          reviewComment: emptyToNull(reviewForm.reviewComment),
        }),
      },
    );
    ElMessage.success("审核结果已提交");
    reviewDialogVisible.value = false;
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "审核提交失败");
  } finally {
    saving.value = false;
  }
}

function resetPublicityForm() {
  publicityForm.title = `${selectedClub.value?.name ?? "社团"}评奖评优公示`;
  publicityForm.description = "";
  publicityForm.publicityStartAt = "";
  publicityForm.publicityEndAt = "";
  publicityForm.awardApplicationIds = approvedApplications.value.map(
    (application) => application.awardApplicationId,
  );
  publicityFormRef.value?.clearValidate();
}

function openCreatePublicityDialog() {
  if (!canMaintainSelectedClub.value) {
    ElMessage.warning("当前账号没有维护公示批次的权限。");
    return;
  }
  if (approvedApplications.value.length === 0) {
    ElMessage.warning("暂无已通过终审的申请可公示。");
    return;
  }
  resetPublicityForm();
  publicityDialogVisible.value = true;
}

async function submitPublicity() {
  if (!selectedClubId.value || !(await validateForm(publicityFormRef.value))) return;
  saving.value = true;
  try {
    await requestJson<AwardPublicityBatchRecord>(
      `/api/clubs/${selectedClubId.value}/award-publicity`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          title: publicityForm.title.trim(),
          description: emptyToNull(publicityForm.description),
          publicityStartAt: fromDateTimeLocal(publicityForm.publicityStartAt),
          publicityEndAt: fromDateTimeLocal(publicityForm.publicityEndAt),
          awardApplicationIds: publicityForm.awardApplicationIds,
        }),
      },
    );
    ElMessage.success("公示批次已创建");
    publicityDialogVisible.value = false;
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "公示批次创建失败");
  } finally {
    saving.value = false;
  }
}

async function publishPublicity(row: AwardPublicityBatchRecord) {
  if (!selectedClubId.value) return;
  await confirmAction(`发布公示批次“${row.title}”？`);
  saving.value = true;
  try {
    await requestJson<AwardPublicityBatchRecord>(
      `/api/clubs/${selectedClubId.value}/award-publicity/${row.publicityBatchId}/publish`,
      { method: "POST" },
    );
    ElMessage.success("公示已发布");
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "发布公示失败");
  } finally {
    saving.value = false;
  }
}

async function archivePublicity(row: AwardPublicityBatchRecord) {
  if (!selectedClubId.value) return;
  if (!canArchivePublicity(row)) {
    ElMessage.warning("公示期结束后才能归档");
    return;
  }

  await confirmAction(`归档公示批次“${row.title}”？`);
  saving.value = true;
  try {
    await requestJson<AwardPublicityBatchRecord>(
      `/api/clubs/${selectedClubId.value}/award-publicity/${row.publicityBatchId}/archive`,
      { method: "POST" },
    );
    ElMessage.success("公示已归档，奖项分可进入成员考核");
    await loadAwardWorkspace();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "归档公示失败");
  } finally {
    saving.value = false;
  }
}

function openDetail(row: AwardApplicationRecord) {
  detailTarget.value = row;
  detailVisible.value = true;
}

function openRuleDetail(row: AwardRuleDocumentRecord) {
  ruleDetailTarget.value = row;
  ruleDetailVisible.value = true;
}

function canEditRuleDocument(row: AwardRuleDocumentRecord) {
  if (row.ruleScope === "global") return canManageGlobalRules.value;
  return (
    row.clubId === selectedClubId.value &&
    (canMaintainSelectedClub.value || canManageGlobalRules.value)
  );
}

function canMaintainMember(member: ClubMemberRecord) {
  return canMaintainScopedMember(member, {
    canMaintainSelectedClub: canMaintainSelectedClub.value,
    canMaintainWholeClub: canMaintainWholeClub.value,
    scopes: selectedCadreGroupingScopes.value,
  });
}

function canEditApplication(row: AwardApplicationRecord) {
  if (!["draft", "returned"].includes(row.applicationStatus)) return false;
  if (canMaintainSelectedClub.value) return true;
  const userId = currentUserId.value;
  return (
    row.applicantUserId === userId ||
    row.submitterUserId === userId ||
    row.recommenderUserId === userId
  );
}

function hasAnyRoleCode(roles: readonly ScopedClubRole[], roleCodes: ReadonlySet<string>) {
  return roles.some((role) => roleCodes.has(normalizedRoleCodeOf(role)));
}

function isCurrentPrincipalMember(clubId: number) {
  const userId = currentUserId.value;
  if (!userId) return false;

  return currentMembers.value.some((member) => {
    const position = (member.positionName ?? "").trim();
    return (
      member.clubId === clubId &&
      member.userId === userId &&
      position.length > 0 &&
      !position.startsWith("副") &&
      principalPositionNames.has(position.toLowerCase())
    );
  });
}

function canReviewClubStep(clubId: number) {
  return (
    hasSystemAdminRole.value ||
    hasScopedRole(currentRoles.value, clubId, clubPrincipalRoleCodes) ||
    isCurrentPrincipalMember(clubId)
  );
}

function canReviewAdvisorStep(clubId: number) {
  return (
    hasSystemAdminRole.value || hasScopedRole(currentRoles.value, clubId, clubAdvisorRoleCodes)
  );
}

function canReviewApplication(row: AwardApplicationRecord) {
  if (!["club_review", "advisor_review", "school_review"].includes(row.applicationStatus)) {
    return false;
  }

  if (row.currentStep === "club_review") return canReviewClubStep(row.clubId);
  if (row.currentStep === "advisor_review") return canReviewAdvisorStep(row.clubId);
  if (row.currentStep === "school_review") return hasPlatformAwardReviewRole.value;
  return false;
}

function canSubmitApplication(row: AwardApplicationRecord) {
  return ["draft", "returned"].includes(row.applicationStatus) && canEditApplication(row);
}

function canArchivePublicity(row: AwardPublicityBatchRecord) {
  if (!canMaintainSelectedClub.value) return false;
  if (!["publicizing", "closed"].includes(row.publicityStatus)) return false;
  if (!row.publicityEndAt) return false;

  const endAt = new Date(row.publicityEndAt);
  return !Number.isNaN(endAt.getTime()) && endAt.getTime() <= Date.now();
}

function validateAwardLevels() {
  const names = new Set<string>();
  for (const level of schemeForm.levels) {
    const name = level.levelName.trim();
    if (!name) return "请填写所有奖项等级名称。";
    if (names.has(name)) return "同一奖项下不能有重复等级。";
    names.add(name);
    if (level.awardScore < 0 || level.awardScore > 100) return "奖项分必须在 0 到 100 之间。";
    if (level.amount !== null && level.amount < 0) return "奖励金额不能为负数。";
    if (level.quota !== null && level.quota < 0) return "名额不能为负数。";
  }
  return null;
}

function clearFilters() {
  filters.keyword = "";
  filters.ruleStatus = "";
  filters.schemeStatus = "";
  filters.applicationStatus = "";
  filters.publicityStatus = "";
}

function ruleStatusType(status: RuleStatus) {
  if (status === "published") return "success";
  if (status === "archived") return "info";
  return "warning";
}

function ruleScopeType(scope: RuleScope) {
  return scope === "global" ? "primary" : "info";
}

function schemeStatusType(status: SchemeStatus) {
  if (status === "open") return "success";
  if (status === "reviewing" || status === "publicizing") return "warning";
  if (status === "archived") return "info";
  if (status === "closed") return "danger";
  return "info";
}

function applicationStatusType(status: ApplicationStatus) {
  if (status === "approved" || status === "archived" || status === "publicized") return "success";
  if (["club_review", "advisor_review", "school_review", "publicizing"].includes(status)) {
    return "warning";
  }
  if (status === "rejected" || status === "withdrawn") return "danger";
  return "info";
}

function publicityStatusType(status: PublicityStatus) {
  if (status === "publicizing") return "warning";
  if (status === "archived") return "success";
  if (status === "closed") return "info";
  return "info";
}

function categoryText(category: AwardCategory) {
  return (
    {
      honor: "荣誉评优",
      scholarship: "奖学金",
      competition: "竞赛成果",
      service: "服务贡献",
      other: "其他",
    } satisfies Record<AwardCategory, string>
  )[category];
}

function applicationTypeText(type: ApplicationType) {
  return type === "recommendation" ? "负责人推荐" : "本人申请";
}

function inferredApplicationScore(row: AwardApplicationRecord) {
  const scheme = schemes.value.find((item) => item.awardSchemeId === row.awardSchemeId);
  return scheme?.levels.find((level) => level.awardLevelId === row.awardLevelId)?.awardScore ?? 0;
}

function memberLabel(member: ClubMemberRecord) {
  const scope = [member.departmentName, member.groupName, member.positionName]
    .filter(Boolean)
    .join(" / ");
  return `${member.userName}${member.studentNo ? `（${member.studentNo}）` : ""}${scope ? ` - ${scope}` : ""}`;
}

function applicationLabel(application: AwardApplicationRecord) {
  return `${application.applicantName} - ${application.awardName} / ${application.levelName}`;
}

function formatDate(value: string | null) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", { hour12: false });
}

function toDateTimeLocal(value: string | null) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const offset = date.getTimezoneOffset();
  const local = new Date(date.getTime() - offset * 60 * 1000);
  return local.toISOString().slice(0, 16);
}

function fromDateTimeLocal(value: string) {
  return value ? new Date(value).toISOString() : null;
}

function emptyToNull(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

async function confirmAction(message: string) {
  await ElMessageBox.confirm(message, "确认操作", {
    type: "warning",
    confirmButtonText: "确认",
    cancelButtonText: "取消",
  });
}

watch(selectedClubId, () => {
  void Promise.all([loadMembers(), loadAwardWorkspace()]);
});

watch(
  () => applicationForm.awardSchemeId,
  () => {
    if (
      !selectedApplicationLevels.value.some(
        (level) => level.awardLevelId === applicationForm.awardLevelId,
      )
    ) {
      applicationForm.awardLevelId = selectedApplicationLevels.value[0]?.awardLevelId;
    }
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
  <section class="award-page">
    <div class="page-head">
      <div>
        <h2>评奖评优</h2>
        <div class="subtitle">申请、审核、公示与成员考核奖项分联动</div>
      </div>
      <div class="head-actions">
        <el-button :icon="Refresh" @click="reloadAll">刷新</el-button>
        <el-button
          v-if="activeTab === 'rules' && canUploadRuleDocument"
          type="primary"
          :icon="Plus"
          @click="openCreateRuleDialog"
        >
          上传细则
        </el-button>
        <el-button
          v-if="canMaintainSelectedClub"
          type="primary"
          :icon="Plus"
          @click="openCreateSchemeDialog"
        >
          新增奖项
        </el-button>
        <el-button type="success" :icon="Plus" @click="openCreateApplicationDialog">
          发起申请
        </el-button>
      </div>
    </div>

    <div class="summary-strip">
      <div>
        <span>奖项项目</span>
        <strong>{{ summary.schemes }}</strong>
      </div>
      <div>
        <span>已发布细则</span>
        <strong>{{ summary.rules }}</strong>
      </div>
      <div>
        <span>审核中</span>
        <strong>{{ summary.reviewing }}</strong>
      </div>
      <div>
        <span>待公示</span>
        <strong>{{ summary.approved }}</strong>
      </div>
      <div>
        <span>已归档</span>
        <strong>{{ summary.archived }}</strong>
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
      <el-input v-model="filters.keyword" class="keyword-input" clearable placeholder="搜索">
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-select
        v-if="activeTab === 'rules'"
        v-model="filters.ruleStatus"
        class="status-select"
        clearable
        placeholder="细则状态"
      >
        <el-option label="草稿" value="draft" />
        <el-option label="已发布" value="published" />
        <el-option label="已归档" value="archived" />
      </el-select>
      <el-select
        v-if="activeTab === 'schemes'"
        v-model="filters.schemeStatus"
        class="status-select"
        clearable
        placeholder="项目状态"
      >
        <el-option label="草稿" value="draft" />
        <el-option label="开放申请" value="open" />
        <el-option label="审核中" value="reviewing" />
        <el-option label="公示中" value="publicizing" />
        <el-option label="已归档" value="archived" />
        <el-option label="已关闭" value="closed" />
      </el-select>
      <el-select
        v-if="activeTab === 'applications'"
        v-model="filters.applicationStatus"
        class="status-select"
        clearable
        placeholder="申请状态"
      >
        <el-option label="草稿" value="draft" />
        <el-option label="负责人初审" value="club_review" />
        <el-option label="指导老师审核" value="advisor_review" />
        <el-option label="校级终审" value="school_review" />
        <el-option label="退回" value="returned" />
        <el-option label="驳回" value="rejected" />
        <el-option label="已通过" value="approved" />
        <el-option label="公示中" value="publicizing" />
        <el-option label="已归档" value="archived" />
      </el-select>
      <el-select
        v-if="activeTab === 'publicity'"
        v-model="filters.publicityStatus"
        class="status-select"
        clearable
        placeholder="公示状态"
      >
        <el-option label="草稿" value="draft" />
        <el-option label="公示中" value="publicizing" />
        <el-option label="已结束" value="closed" />
        <el-option label="已归档" value="archived" />
      </el-select>
      <el-button @click="clearFilters">重置</el-button>
    </div>

    <el-tabs v-model="activeTab" class="award-tabs">
      <el-tab-pane label="评定细则" name="rules" />
      <el-tab-pane label="申请审批" name="applications" />
      <el-tab-pane label="奖项项目" name="schemes" />
      <el-tab-pane label="公示归档" name="publicity" />
    </el-tabs>

    <div v-loading="loading || memberLoading" class="workspace-panel">
      <el-table
        v-if="activeTab === 'rules'"
        :data="filteredRuleDocuments"
        empty-text="暂无评定细则"
        row-key="ruleDocumentId"
      >
        <el-table-column type="expand">
          <template #default="{ row }">
            <div class="rule-expand">
              <div>
                <strong>摘要</strong>
                <p>{{ row.summary || "暂无摘要" }}</p>
              </div>
              <div>
                <strong>正文</strong>
                <p class="rule-content">
                  {{ row.contentText || "暂无正文，请查看附件或联系发布人。" }}
                </p>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="细则标题" min-width="260">
          <template #default="{ row }">
            <strong>{{ row.ruleTitle }}</strong>
            <div class="muted">
              {{ row.academicYear }}{{ row.termName || "" }} · {{ row.versionNo }}
            </div>
          </template>
        </el-table-column>
        <el-table-column label="范围" width="120">
          <template #default="{ row }">
            <el-tag :type="ruleScopeType(row.ruleScope)" effect="plain">
              {{ row.ruleScopeText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="制定单位" min-width="180">
          <template #default="{ row }">
            {{ row.issuerName || row.clubName || "学生社团管理部门" }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="ruleStatusType(row.ruleStatus)" effect="plain">
              {{ row.ruleStatusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="附件" min-width="180">
          <template #default="{ row }">
            <el-button
              v-if="row.materialUrl"
              link
              type="primary"
              :icon="Download"
              @click="downloadRuleDocument(row)"
            >
              {{ row.materialName || "查看附件" }}
            </el-button>
            <span v-else class="muted">未上传</span>
          </template>
        </el-table-column>
        <el-table-column label="发布时间" width="170">
          <template #default="{ row }">{{ formatDate(row.publishedAt || row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="300" fixed="right">
          <template #default="{ row }">
            <el-button :icon="View" @click="openRuleDetail(row)">查看</el-button>
            <el-button
              v-if="canEditRuleDocument(row)"
              type="primary"
              :icon="Edit"
              @click="openEditRuleDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              v-if="canEditRuleDocument(row) && row.ruleStatus !== 'published'"
              type="success"
              @click="publishRuleDocument(row)"
            >
              发布
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-table
        v-else-if="activeTab === 'applications'"
        :data="filteredApplications"
        empty-text="暂无评奖评优申请"
        row-key="awardApplicationId"
      >
        <el-table-column label="申请人" min-width="150">
          <template #default="{ row }">
            <strong>{{ row.applicantName }}</strong>
            <div class="muted">{{ row.applicantStudentNo || "无学号" }}</div>
          </template>
        </el-table-column>
        <el-table-column label="奖项" min-width="220">
          <template #default="{ row }">
            <strong>{{ row.awardName }}</strong>
            <div class="muted">
              {{ row.levelName }} · {{ row.academicYear }}{{ row.termName || "" }}
            </div>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="120">
          <template #default="{ row }">{{ applicationTypeText(row.applicationType) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="140">
          <template #default="{ row }">
            <el-tag :type="applicationStatusType(row.applicationStatus)" effect="plain">
              {{ row.applicationStatusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="当前节点" min-width="150" prop="currentStepText" />
        <el-table-column label="奖项分" width="100">
          <template #default="{ row }">{{
            row.finalAwardScore ?? inferredApplicationScore(row)
          }}</template>
        </el-table-column>
        <el-table-column label="更新时间" width="170">
          <template #default="{ row }">{{ formatDate(row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="300" fixed="right">
          <template #default="{ row }">
            <el-button :icon="View" @click="openDetail(row)">查看</el-button>
            <el-button
              v-if="canEditApplication(row)"
              type="primary"
              :icon="Edit"
              @click="openEditApplicationDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              v-if="canSubmitApplication(row)"
              type="success"
              :icon="Check"
              @click="submitExistingApplication(row)"
            >
              提交
            </el-button>
            <el-dropdown v-if="canReviewApplication(row)" trigger="click">
              <el-button type="warning">审核</el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item @click="openReviewDialog(row, 'approve')"
                    >通过</el-dropdown-item
                  >
                  <el-dropdown-item @click="openReviewDialog(row, 'return')">退回</el-dropdown-item>
                  <el-dropdown-item @click="openReviewDialog(row, 'reject')">驳回</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </template>
        </el-table-column>
      </el-table>

      <el-table
        v-else-if="activeTab === 'schemes'"
        :data="filteredSchemes"
        empty-text="暂无奖项项目"
        row-key="awardSchemeId"
      >
        <el-table-column label="奖项项目" min-width="220">
          <template #default="{ row }">
            <strong>{{ row.awardName }}</strong>
            <div class="muted">
              {{ categoryText(row.awardCategory) }} · {{ row.rewardLevel || "未分级" }}
            </div>
          </template>
        </el-table-column>
        <el-table-column label="学年学期" width="180">
          <template #default="{ row }">{{ row.academicYear }}{{ row.termName || "" }}</template>
        </el-table-column>
        <el-table-column label="等级" min-width="180">
          <template #default="{ row }">
            <el-tag
              v-for="level in row.levels"
              :key="level.awardLevelId"
              class="level-tag"
              effect="plain"
            >
              {{ level.levelName }} {{ level.awardScore }}分
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="申请窗口" width="250">
          <template #default="{ row }">
            {{ formatDate(row.applicationStartAt) }} 至 {{ formatDate(row.applicationEndAt) }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-tag :type="schemeStatusType(row.schemeStatus)" effect="plain">
              {{ row.schemeStatusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column v-if="canMaintainSelectedClub" label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button :icon="Edit" @click="openEditSchemeDialog(row)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div v-else class="publicity-pane">
        <div class="table-actions">
          <el-button
            v-if="canMaintainSelectedClub"
            type="primary"
            :icon="Plus"
            @click="openCreatePublicityDialog"
          >
            新建公示
          </el-button>
        </div>
        <el-table
          :data="filteredPublicityBatches"
          empty-text="暂无公示批次"
          row-key="publicityBatchId"
        >
          <el-table-column label="公示标题" min-width="220">
            <template #default="{ row }">
              <strong>{{ row.title }}</strong>
              <div class="muted">{{ row.description || "无备注" }}</div>
            </template>
          </el-table-column>
          <el-table-column label="名单" width="120">
            <template #default="{ row }">{{ row.items.length }} 人</template>
          </el-table-column>
          <el-table-column label="公示时间" width="250">
            <template #default="{ row }">
              {{ formatDate(row.publicityStartAt) }} 至 {{ formatDate(row.publicityEndAt) }}
            </template>
          </el-table-column>
          <el-table-column label="状态" width="120">
            <template #default="{ row }">
              <el-tag :type="publicityStatusType(row.publicityStatus)" effect="plain">
                {{ row.publicityStatusText }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="220" fixed="right">
            <template #default="{ row }">
              <el-button
                v-if="canMaintainSelectedClub && row.publicityStatus === 'draft'"
                type="warning"
                @click="publishPublicity(row)"
              >
                发布
              </el-button>
              <el-button
                v-if="canArchivePublicity(row)"
                type="success"
                @click="archivePublicity(row)"
              >
                归档
              </el-button>
            </template>
          </el-table-column>
          <el-table-column type="expand">
            <template #default="{ row }">
              <div class="publicity-items">
                <div v-for="item in row.items" :key="item.publicityItemId" class="publicity-item">
                  <strong>{{ item.applicantName }}</strong>
                  <span>{{ item.awardName }} / {{ item.levelName }}</span>
                  <el-tag effect="plain">{{ item.finalAwardScore ?? 0 }} 分</el-tag>
                </div>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </div>

    <el-dialog
      v-model="ruleDialogVisible"
      :title="ruleTarget ? '编辑评定细则' : '上传评定细则'"
      width="820px"
    >
      <el-form ref="ruleFormRef" :model="ruleForm" :rules="ruleRules" label-width="110px">
        <div class="form-grid">
          <el-form-item label="细则标题" prop="ruleTitle">
            <el-input v-model="ruleForm.ruleTitle" maxlength="255" show-word-limit />
          </el-form-item>
          <el-form-item label="适用范围" prop="ruleScope">
            <el-select v-model="ruleForm.ruleScope">
              <el-option label="学校通用" value="global" :disabled="!canManageGlobalRules" />
              <el-option
                label="当前社团"
                value="club"
                :disabled="!canMaintainSelectedClub && !canManageGlobalRules"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="适用学年" prop="academicYear">
            <el-input v-model="ruleForm.academicYear" maxlength="50" />
          </el-form-item>
          <el-form-item label="适用学期">
            <el-select v-model="ruleForm.termName" clearable>
              <el-option label="春季" value="春季" />
              <el-option label="秋季" value="秋季" />
              <el-option label="学年" value="学年" />
            </el-select>
          </el-form-item>
          <el-form-item label="制定单位">
            <el-input v-model="ruleForm.issuerName" maxlength="255" />
          </el-form-item>
          <el-form-item label="状态" prop="ruleStatus">
            <el-select v-model="ruleForm.ruleStatus">
              <el-option label="草稿" value="draft" />
              <el-option label="已发布" value="published" />
              <el-option label="已归档" value="archived" />
            </el-select>
          </el-form-item>
          <el-form-item label="版本号">
            <el-input v-model="ruleForm.versionNo" maxlength="50" />
          </el-form-item>
          <el-form-item label="生效开始">
            <el-date-picker
              v-model="ruleForm.effectiveStartAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
          <el-form-item label="生效结束">
            <el-date-picker
              v-model="ruleForm.effectiveEndAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
        </div>
        <el-form-item label="附件文件">
          <div class="upload-field">
            <el-upload
              v-model:file-list="ruleFiles"
              :auto-upload="false"
              :limit="1"
              :on-exceed="() => ElMessage.warning('评定细则一次只能选择一个附件')"
            >
              <el-button :icon="Upload">选择文件</el-button>
            </el-upload>
            <span v-if="ruleForm.materialName && ruleFiles.length === 0" class="field-tip">
              当前文件：{{ ruleForm.materialName }}
            </span>
          </div>
        </el-form-item>
        <el-form-item label="摘要">
          <el-input
            v-model="ruleForm.summary"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="正文">
          <el-input
            v-model="ruleForm.contentText"
            type="textarea"
            :rows="8"
            maxlength="5000"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="ruleDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitRuleDocument">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="schemeDialogVisible"
      :title="schemeTarget ? '编辑奖项项目' : '新增奖项项目'"
      width="860px"
    >
      <el-form ref="schemeFormRef" :model="schemeForm" :rules="schemeRules" label-width="110px">
        <div class="form-grid">
          <el-form-item label="奖项名称" prop="awardName">
            <el-input v-model="schemeForm.awardName" maxlength="80" show-word-limit />
          </el-form-item>
          <el-form-item label="奖项类别" prop="awardCategory">
            <el-select v-model="schemeForm.awardCategory">
              <el-option label="荣誉评优" value="honor" />
              <el-option label="奖学金" value="scholarship" />
              <el-option label="竞赛成果" value="competition" />
              <el-option label="服务贡献" value="service" />
              <el-option label="其他" value="other" />
            </el-select>
          </el-form-item>
          <el-form-item label="评定学年" prop="academicYear">
            <el-input v-model="schemeForm.academicYear" maxlength="50" />
          </el-form-item>
          <el-form-item label="学期">
            <el-select v-model="schemeForm.termName" clearable>
              <el-option label="春季" value="春季" />
              <el-option label="秋季" value="秋季" />
              <el-option label="学年" value="学年" />
            </el-select>
          </el-form-item>
          <el-form-item label="主办单位">
            <el-input v-model="schemeForm.sponsorUnit" maxlength="80" />
          </el-form-item>
          <el-form-item label="奖励层级">
            <el-input v-model="schemeForm.rewardLevel" maxlength="80" />
          </el-form-item>
          <el-form-item label="资金来源">
            <el-input v-model="schemeForm.fundingSource" maxlength="80" />
          </el-form-item>
          <el-form-item label="项目状态">
            <el-select v-model="schemeForm.schemeStatus">
              <el-option label="草稿" value="draft" />
              <el-option label="开放申请" value="open" />
              <el-option label="审核中" value="reviewing" />
              <el-option label="公示中" value="publicizing" />
              <el-option label="已归档" value="archived" />
              <el-option label="已关闭" value="closed" />
            </el-select>
          </el-form-item>
        </div>

        <div class="form-grid">
          <el-form-item label="申请开始">
            <el-date-picker
              v-model="schemeForm.applicationStartAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
          <el-form-item label="申请结束">
            <el-date-picker
              v-model="schemeForm.applicationEndAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
          <el-form-item label="公示开始">
            <el-date-picker
              v-model="schemeForm.publicityStartAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
          <el-form-item label="公示结束">
            <el-date-picker
              v-model="schemeForm.publicityEndAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
        </div>

        <div class="switch-row">
          <el-checkbox v-model="schemeForm.isRanked">区分名次/等级</el-checkbox>
          <el-checkbox v-model="schemeForm.isFixedAmount">金额固定</el-checkbox>
        </div>

        <div class="level-editor">
          <div class="section-title">
            <strong>奖项等级</strong>
            <el-button size="small" :icon="Plus" @click="addAwardLevel">新增等级</el-button>
          </div>
          <div class="level-list">
            <div v-for="(level, index) in schemeForm.levels" :key="index" class="level-card">
              <div class="level-card-head">
                <span class="level-card-title">等级 {{ index + 1 }}</span>
                <el-button
                  text
                  type="danger"
                  :icon="Close"
                  :disabled="schemeForm.levels.length <= 1"
                  @click="removeAwardLevel(index)"
                />
              </div>
              <div class="level-fields">
                <div class="field-block level-name-field">
                  <span class="field-label">等级名称</span>
                  <el-input v-model="level.levelName" placeholder="如：一等奖" />
                </div>
                <div class="field-block">
                  <span class="field-label">奖项分</span>
                  <el-input-number
                    v-model="level.awardScore"
                    controls-position="right"
                    :min="0"
                    :max="100"
                    :precision="1"
                  />
                </div>
                <div class="field-block">
                  <span class="field-label">奖励金额</span>
                  <el-input-number
                    v-model="level.amount"
                    controls-position="right"
                    :min="0"
                    :precision="2"
                    placeholder="金额"
                  />
                </div>
                <div class="field-block">
                  <span class="field-label">名额</span>
                  <el-input-number
                    v-model="level.quota"
                    controls-position="right"
                    :min="0"
                    :precision="0"
                    placeholder="名额"
                  />
                </div>
                <div class="field-block">
                  <span class="field-label">状态</span>
                  <el-select v-model="level.levelStatus">
                    <el-option label="启用" value="active" />
                    <el-option label="停用" value="inactive" />
                  </el-select>
                </div>
              </div>
            </div>
          </div>
        </div>

        <el-form-item label="项目说明">
          <el-input
            v-model="schemeForm.description"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="材料要求">
          <el-input
            v-model="schemeForm.materialDescription"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="schemeDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitScheme">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="applicationDialogVisible"
      :title="applicationTarget ? '编辑评优申请' : '发起评优申请'"
      width="720px"
    >
      <el-form
        ref="applicationFormRef"
        :model="applicationForm"
        :rules="applicationRules"
        label-width="110px"
      >
        <el-form-item label="奖项项目" prop="awardSchemeId">
          <el-select v-model="applicationForm.awardSchemeId" filterable>
            <el-option
              v-for="scheme in openSchemes"
              :key="scheme.awardSchemeId"
              :label="`${scheme.awardName}（${scheme.academicYear}${scheme.termName || ''}）`"
              :value="scheme.awardSchemeId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="奖项等级" prop="awardLevelId">
          <el-select v-model="applicationForm.awardLevelId">
            <el-option
              v-for="level in selectedApplicationLevels"
              :key="level.awardLevelId"
              :label="`${level.levelName}（${level.awardScore}分）`"
              :value="level.awardLevelId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="申请成员" prop="applicantUserId">
          <el-select
            v-model="applicationForm.applicantUserId"
            :disabled="Boolean(applicationTarget)"
            filterable
          >
            <el-option
              v-for="member in applicantOptions"
              :key="member.memberId"
              :label="memberLabel(member)"
              :value="member.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="申请类型">
          <el-radio-group
            v-model="applicationForm.applicationType"
            :disabled="Boolean(applicationTarget)"
          >
            <el-radio-button label="self">本人申请</el-radio-button>
            <el-radio-button :disabled="!canMaintainSelectedClub" label="recommendation">
              负责人推荐
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="申请材料">
          <div class="upload-field">
            <el-upload v-model:file-list="applicationFiles" :auto-upload="false" multiple>
              <el-button :icon="Upload">选择文件</el-button>
            </el-upload>
            <span
              v-if="applicationTarget?.attachments.length && applicationFiles.length === 0"
              class="field-tip"
            >
              已有 {{ applicationTarget.attachments.length }} 个材料文件，可继续补充上传。
            </span>
          </div>
        </el-form-item>
        <el-form-item label="申请理由" prop="applicationReason">
          <el-input
            v-model="applicationForm.applicationReason"
            type="textarea"
            :rows="5"
            maxlength="2000"
            show-word-limit
          />
        </el-form-item>
        <el-form-item v-if="!applicationTarget" label="提交状态">
          <el-checkbox v-model="applicationForm.submitNow">保存后提交审核</el-checkbox>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="applicationDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitApplication">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reviewDialogVisible" title="审核评优申请" width="560px">
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="100px">
        <el-form-item label="审核结果" prop="reviewResult">
          <el-radio-group v-model="reviewForm.reviewResult">
            <el-radio-button label="approve">通过</el-radio-button>
            <el-radio-button label="return">退回</el-radio-button>
            <el-radio-button label="reject">驳回</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <template v-if="reviewForm.reviewResult === 'approve'">
          <el-form-item label="最终奖项分">
            <el-input-number
              v-model="reviewForm.finalAwardScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="最终金额">
            <el-input-number v-model="reviewForm.finalAmount" :min="0" :precision="2" />
          </el-form-item>
        </template>
        <el-form-item label="审核意见">
          <el-input v-model="reviewForm.reviewComment" type="textarea" :rows="4" maxlength="1000" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitReview">提交</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="publicityDialogVisible" title="新建公示批次" width="700px">
      <el-form
        ref="publicityFormRef"
        :model="publicityForm"
        :rules="publicityRules"
        label-width="100px"
      >
        <el-form-item label="公示标题" prop="title">
          <el-input v-model="publicityForm.title" maxlength="120" />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="公示开始">
            <el-date-picker
              v-model="publicityForm.publicityStartAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
          <el-form-item label="公示结束">
            <el-date-picker
              v-model="publicityForm.publicityEndAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm"
            />
          </el-form-item>
        </div>
        <el-form-item label="公示名单" prop="awardApplicationIds">
          <el-select v-model="publicityForm.awardApplicationIds" multiple filterable collapse-tags>
            <el-option
              v-for="application in approvedApplications"
              :key="application.awardApplicationId"
              :label="applicationLabel(application)"
              :value="application.awardApplicationId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="publicityForm.description"
            type="textarea"
            :rows="3"
            maxlength="1000"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="publicityDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitPublicity">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="ruleDetailVisible" title="评定细则" width="760px">
      <template v-if="ruleDetailTarget">
        <div class="rule-reader">
          <div class="rule-reader-head">
            <div>
              <h3>{{ ruleDetailTarget.ruleTitle }}</h3>
              <div class="muted">
                {{ ruleDetailTarget.academicYear }}{{ ruleDetailTarget.termName || "" }} ·
                {{ ruleDetailTarget.versionNo }}
              </div>
            </div>
            <div class="rule-reader-tags">
              <el-tag :type="ruleScopeType(ruleDetailTarget.ruleScope)" effect="plain">
                {{ ruleDetailTarget.ruleScopeText }}
              </el-tag>
              <el-tag :type="ruleStatusType(ruleDetailTarget.ruleStatus)" effect="plain">
                {{ ruleDetailTarget.ruleStatusText }}
              </el-tag>
            </div>
          </div>
          <el-descriptions :column="2" border>
            <el-descriptions-item label="制定单位">
              {{ ruleDetailTarget.issuerName || ruleDetailTarget.clubName || "学生社团管理部门" }}
            </el-descriptions-item>
            <el-descriptions-item label="发布人">
              {{ ruleDetailTarget.publishedByName || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="生效时间">
              {{ formatDate(ruleDetailTarget.effectiveStartAt) }}
              至 {{ formatDate(ruleDetailTarget.effectiveEndAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="发布时间">
              {{ formatDate(ruleDetailTarget.publishedAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="附件" :span="2">
              <el-button
                v-if="ruleDetailTarget.materialUrl"
                link
                type="primary"
                :icon="Download"
                @click="downloadRuleDocument(ruleDetailTarget)"
              >
                {{ ruleDetailTarget.materialName || "下载附件" }}
              </el-button>
              <span v-else>-</span>
            </el-descriptions-item>
            <el-descriptions-item label="摘要" :span="2">
              {{ ruleDetailTarget.summary || "-" }}
            </el-descriptions-item>
          </el-descriptions>
          <div class="rule-reader-content">
            {{ ruleDetailTarget.contentText || "暂无正文，请查看附件或联系发布人。" }}
          </div>
        </div>
      </template>
    </el-dialog>

    <el-dialog v-model="detailVisible" title="评优申请详情" width="760px">
      <template v-if="detailTarget">
        <el-descriptions :column="2" border>
          <el-descriptions-item label="申请人">{{
            detailTarget.applicantName
          }}</el-descriptions-item>
          <el-descriptions-item label="奖项">{{ detailTarget.awardName }}</el-descriptions-item>
          <el-descriptions-item label="等级">{{ detailTarget.levelName }}</el-descriptions-item>
          <el-descriptions-item label="状态">{{
            detailTarget.applicationStatusText
          }}</el-descriptions-item>
          <el-descriptions-item label="当前节点">{{
            detailTarget.currentStepText
          }}</el-descriptions-item>
          <el-descriptions-item label="最终奖项分">
            {{ detailTarget.finalAwardScore ?? inferredApplicationScore(detailTarget) }}
          </el-descriptions-item>
          <el-descriptions-item label="申请材料" :span="2">
            <div v-if="detailTarget.attachments.length" class="attachment-list">
              <el-button
                v-for="attachment in detailTarget.attachments"
                :key="attachment.attachmentId"
                link
                type="primary"
                :icon="Download"
                @click="downloadApplicationAttachment(detailTarget, attachment)"
              >
                {{ attachment.attachmentName }}
              </el-button>
            </div>
            <span v-else>-</span>
          </el-descriptions-item>
          <el-descriptions-item label="申请理由" :span="2">
            {{ detailTarget.applicationReason || "-" }}
          </el-descriptions-item>
        </el-descriptions>
        <el-timeline class="review-timeline">
          <el-timeline-item
            v-for="record in detailTarget.reviewRecords"
            :key="record.reviewId"
            :timestamp="formatDate(record.reviewedAt)"
          >
            <strong>{{ record.reviewerName || "系统" }}</strong>
            <span> {{ record.reviewResult }} </span>
            <div class="muted">{{ record.reviewComment || "无审核意见" }}</div>
          </el-timeline-item>
        </el-timeline>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.award-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-head,
.toolbar,
.head-actions,
.table-actions,
.section-title,
.switch-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.page-head {
  justify-content: space-between;
}

.page-head h2 {
  margin: 0;
  font-size: 24px;
}

.subtitle,
.muted {
  color: #6b7280;
  font-size: 13px;
}

.summary-strip {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  overflow: hidden;
}

.summary-strip > div {
  padding: 14px 18px;
  border-right: 1px solid #e5e7eb;
  background: #fff;
}

.summary-strip > div:last-child {
  border-right: 0;
}

.summary-strip span {
  display: block;
  color: #6b7280;
  font-size: 13px;
}

.summary-strip strong {
  display: block;
  margin-top: 4px;
  font-size: 22px;
}

.toolbar {
  flex-wrap: wrap;
}

.club-select {
  width: 240px;
}

.keyword-input {
  width: 260px;
}

.status-select {
  width: 160px;
}

.award-tabs {
  margin-bottom: -16px;
}

.workspace-panel {
  min-height: 380px;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: #fff;
  padding: 8px;
}

.level-tag {
  margin: 2px 6px 2px 0;
}

.rule-expand {
  display: grid;
  gap: 12px;
  padding: 12px 24px;
}

.rule-expand p,
.rule-reader-content {
  margin: 8px 0 0;
  color: #374151;
  line-height: 1.7;
  white-space: pre-wrap;
}

.rule-reader {
  display: grid;
  gap: 16px;
}

.rule-reader-head,
.rule-reader-tags {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  justify-content: space-between;
}

.rule-reader-head h3 {
  margin: 0 0 4px;
  font-size: 20px;
}

.rule-reader-tags {
  align-items: center;
  justify-content: flex-end;
}

.rule-reader-content {
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: #f9fafb;
  padding: 14px 16px;
}

.publicity-pane {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.table-actions {
  justify-content: flex-end;
  padding: 4px;
}

.publicity-items {
  display: grid;
  gap: 8px;
  padding: 10px 24px;
}

.publicity-item {
  display: grid;
  grid-template-columns: 160px 1fr 80px;
  align-items: center;
  gap: 12px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 16px;
}

.upload-field,
.attachment-list {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
}

.field-tip {
  color: #6b7280;
  font-size: 12px;
}

.switch-row {
  padding: 0 0 12px 110px;
}

.level-editor {
  margin: 2px 0 16px;
  padding-left: 110px;
}

.section-title {
  justify-content: space-between;
  margin-bottom: 8px;
}

.level-list {
  display: grid;
  gap: 10px;
}

.level-card {
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: #f9fafb;
  padding: 12px;
}

.level-card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 10px;
}

.level-card-title {
  color: #374151;
  font-size: 13px;
  font-weight: 600;
}

.level-fields {
  display: grid;
  grid-template-columns: minmax(180px, 1.35fr) repeat(3, minmax(118px, 0.8fr)) minmax(
      110px,
      0.65fr
    );
  gap: 10px 12px;
}

.field-block {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 6px;
}

.field-label {
  color: #6b7280;
  font-size: 12px;
  line-height: 1;
}

.field-block :deep(.el-input-number),
.field-block :deep(.el-select) {
  width: 100%;
}

.review-timeline {
  margin-top: 18px;
}

@media (max-width: 900px) {
  .page-head,
  .toolbar,
  .head-actions {
    align-items: stretch;
    flex-direction: column;
  }

  .summary-strip,
  .form-grid,
  .level-fields {
    grid-template-columns: 1fr;
  }

  .summary-strip > div {
    border-right: 0;
    border-bottom: 1px solid #e5e7eb;
  }

  .summary-strip > div:last-child {
    border-bottom: 0;
  }

  .club-select,
  .keyword-input,
  .status-select {
    width: 100%;
  }

  .switch-row,
  .level-editor {
    padding-left: 0;
  }

  .rule-reader-head {
    flex-direction: column;
  }

  .rule-reader-tags {
    justify-content: flex-start;
  }
}
</style>

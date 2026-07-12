<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import {
  Check,
  Close,
  Delete as DeleteIcon,
  Edit,
  Plus,
  Refresh,
  Search,
  User,
} from "@element-plus/icons-vue";
import { type LearningTeacherCandidate } from "../api";
import {
  type AuthResponse,
  type AuthRole,
  onSessionChange,
  readAuth,
  saveAuth,
} from "../authSession";
import { requestJson } from "../composables/useApiRequest";
import {
  collectCadreScopesFromMemberships,
  groupingMatchesScope,
  type MemberGroupingScope,
} from "../composables/useClubEvaluationScope";
import { hasScopedRole, roleCoversClub } from "../composables/useManageableClubs";

type AuditStatus = "pending" | "approved" | "rejected";
type ReviewDecision = "approved" | "rejected";
type MemberStatus = "active" | "ended" | "suspended";
type TermMode = "create" | "edit";
type GroupingMode = "free" | "own" | "department";
type GroupingField = "departmentName" | "groupName";
type EvaluationType = "semester" | "award";
type EvaluationPublicStatus = "draft" | "published";
type EvaluationMode = "create" | "edit";

interface UserRoleSummary {
  roleCode: string;
  roleName: string;
  roleScope: string | null;
  clubId: number | null;
  clubIds?: number[];
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
  advisorUserId: number | null;
  advisorName: string | null;
  contactPhone: string | null;
  auditStatus: string | null;
  auditStatusText: string;
  applicantUserId: number | null;
  applicantName: string | null;
  applyReason: string | null;
  materialUrl: string | null;
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
  advisorUserId: number | null;
  advisorName: string | null;
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
  termName: string | null;
  termStart: string | null;
  termEnd: string | null;
  memberStatus: string | null;
  joinAt: string | null;
  contributionScore: number | null;
  isCurrent: boolean;
}

interface IdentityRow {
  clubId: number;
  clubName: string;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  termName: string | null;
  memberStatus: string | null;
}

interface ClubEvaluationRecord {
  evaluationId: number;
  evaluationType: EvaluationType;
  evaluationTypeText: string;
  clubId: number;
  clubName: string;
  userId: number;
  userName: string;
  studentNo: string | null;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  evaluatorUserId: number | null;
  evaluatorName: string | null;
  termName: string;
  awardTitle: string | null;
  awardLevel: string | null;
  awardReason: string | null;
  activityScore: number;
  taskScore: number;
  learningScore: number;
  awardScore: number;
  totalScore: number;
  grade: string;
  publicStatus: EvaluationPublicStatus;
  publicStatusText: string;
  commentText: string | null;
  createdAt: string | null;
}

interface ClubContextOption {
  clubId: number;
  clubName: string;
  roleText: string;
  statusText: string;
  optionLabel: string;
  canManage: boolean;
}

interface IdentityRow {
  clubId: number;
  clubName: string;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  termName: string | null;
  memberStatus: string | null;
}

interface MemberGroupOption {
  departmentName: string;
  groupName: string;
}

type ClubWorkspace = "club" | "members" | "registration";
type MemberWorkspaceMode = "current" | "history" | "transition";

interface AcademicTermOption {
  label: string;
  termStart: string;
  termEnd: string;
}

const props = withDefaults(
  defineProps<{
    workspace?: ClubWorkspace;
  }>(),
  {
    workspace: "club",
  },
);

const principalRoleCodes = new Set(["club_president", "club_leader", "club_manager", "president"]);
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
const clubParticipantRoleCodes = new Set([
  "club_member",
  "club_officer",
  "club_leader",
  "club_president",
]);
const advisorRoleCodes = new Set(["advisor"]);
const clubApplyPermission = "club:apply";
const clubReviewPermission = "club:review";
const clubInfoManagePermission = "club:info:manage";
const clubMemberManagePermission = "club:member:manage";
const clubInternalViewPermission = "club:internal:view";
const clubOperationViewPermission = "club:operation:view";

const route = useRoute();
const router = useRouter();
const auth = ref<AuthResponse | null>(readAuth());
const users = ref<UserSummary[]>([]);
const dialogUsers = ref<UserSummary[]>([]);
const applicationAdvisorCandidates = ref<LearningTeacherCandidate[]>([]);
const currentUserId = computed(() => auth.value?.user.id);
const clubs = ref<Club[]>([]);
const applications = ref<ClubApplication[]>([]);
const clubMembers = ref<ClubMemberRecord[]>([]);
const evaluationMembers = ref<ClubMemberRecord[]>([]);
const evaluations = ref<ClubEvaluationRecord[]>([]);
const manualDepartmentOptions = ref<Record<number, string[]>>({});
const manualGroupOptions = ref<Record<number, MemberGroupOption[]>>({});
const manualAcademicTermOptions = ref<AcademicTermOption[]>([]);
const loading = ref(true);
const usersLoading = ref(true);
const dialogUsersLoading = ref(false);
const applicationAdvisorLoading = ref(false);
const memberLoading = ref(false);
const evaluationLoading = ref(false);
const saving = ref(false);
const reviewing = ref(false);
const profileSaving = ref(false);
const termSaving = ref(false);
const groupingSaving = ref(false);
const evaluationSaving = ref(false);
const dissolvingClubId = ref<number | null>(null);
const exitingMemberId = ref<number | null>(null);
const exitingClubId = ref<number | null>(null);
const error = ref("");
const isClubWorkspace = computed(() => props.workspace === "club");
const isMemberWorkspace = computed(() => props.workspace === "members");
const isRegistrationWorkspace = computed(() => props.workspace === "registration");
const activeTab = ref(defaultActiveTab(props.workspace));
const routeClubId = Number(route.query.clubId);
const selectedClubId = ref<number | undefined>(
  Number.isFinite(routeClubId) && routeClubId > 0 ? routeClubId : undefined,
);
const memberWorkspaceMode = ref<MemberWorkspaceMode>("current");
const transitionTermForm = reactive(currentAcademicTermOption());

const filters = reactive({
  auditStatus: "",
});

const memberFilters = reactive({
  termName: "",
  departmentName: "",
  groupName: "",
  unassignedOnly: false,
});
const newDepartmentName = ref("");
const newGroupDepartmentName = ref("");
const newGroupName = ref("");
const newAcademicTermStartYear = ref(academicYearStart(new Date()) + 3);

const evaluationFilters = reactive({
  termName: "",
});

const applicationDialogVisible = ref(false);
const applicationFormRef = ref<FormInstance>();
const applicationForm = reactive({
  name: "",
  category: "",
  description: "",
  applyReason: "",
  materialUrl: "",
  contactPhone: "",
  advisorUserId: null as number | null,
});

const reviewDialogVisible = ref(false);
const reviewFormRef = ref<FormInstance>();
const reviewTarget = ref<ClubApplication | null>(null);
const reviewForm = reactive({
  decision: "approved" as ReviewDecision,
  reviewComment: "",
});

const profileDialogVisible = ref(false);
const profileFormRef = ref<FormInstance>();
const profileTarget = ref<Club | null>(null);
const profileForm = reactive({
  name: "",
  category: "",
  description: "",
  logoUrl: "",
  presidentUserId: null as number | null,
  advisorUserId: null as number | null,
  contactPhone: "",
});

const memberTermDialogVisible = ref(false);
const memberTermFormRef = ref<FormInstance>();
const memberTermMode = ref<TermMode>("create");
const memberTermTarget = ref<ClubMemberRecord | null>(null);
const memberTermForm = reactive({
  userId: undefined as number | undefined,
  departmentName: "",
  groupName: "",
  positionName: "",
  termName: currentAcademicTermOption().label,
  termStart: currentAcademicTermOption().termStart,
  termEnd: currentAcademicTermOption().termEnd,
  memberStatus: "active" as MemberStatus,
  contributionScore: 0,
  closeCurrentTerm: true,
});

const memberGroupingDialogVisible = ref(false);
const memberGroupingTarget = ref<ClubMemberRecord | null>(null);
const memberGroupingMode = ref<GroupingMode>("free");
const memberGroupingField = ref<GroupingField>("departmentName");
const memberGroupingForm = reactive({
  departmentName: "",
  groupName: "",
});

const evaluationDialogVisible = ref(false);
const evaluationFormRef = ref<FormInstance>();
const evaluationMode = ref<EvaluationMode>("create");
const evaluationTarget = ref<ClubEvaluationRecord | null>(null);
const evaluationForm = reactive({
  evaluationType: "semester" as EvaluationType,
  userId: undefined as number | undefined,
  termName: `${new Date().getFullYear()} 学年春季学期`,
  awardTitle: "",
  awardLevel: "",
  awardReason: "",
  activityScore: 0,
  taskScore: 0,
  learningScore: 0,
  awardScore: 0,
  publicStatus: "draft" as EvaluationPublicStatus,
  commentText: "",
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

const profileRules: FormRules = {
  name: [{ required: true, message: "请填写社团名称", trigger: "blur" }],
  category: [{ required: true, message: "请填写社团类别", trigger: "blur" }],
};

const memberTermRules: FormRules = {
  userId: [{ required: true, message: "请选择成员", trigger: "change" }],
  positionName: [{ required: true, message: "请填写职位", trigger: "blur" }],
  termName: [{ required: true, message: "请填写任期名称", trigger: "blur" }],
  termStart: [{ required: true, message: "请选择任期开始时间", trigger: "change" }],
};

const evaluationRules: FormRules = {
  evaluationType: [{ required: true, message: "请选择评价类型", trigger: "change" }],
  userId: [{ required: true, message: "请选择被评价成员", trigger: "change" }],
  termName: [{ required: true, message: "请填写考核学期", trigger: "blur" }],
  awardTitle: [
    {
      validator: (_rule, value, callback) => {
        if (evaluationForm.evaluationType === "award" && !String(value ?? "").trim()) {
          callback(new Error("请填写评优评奖标题"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
  ],
  awardLevel: [
    {
      validator: (_rule, value, callback) => {
        if (evaluationForm.evaluationType === "award" && !String(value ?? "").trim()) {
          callback(new Error("请填写奖项等级"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
  ],
  awardReason: [
    {
      validator: (_rule, value, callback) => {
        if (evaluationForm.evaluationType === "award" && !String(value ?? "").trim()) {
          callback(new Error("请填写获奖原因"));
          return;
        }
        callback();
      },
      trigger: "blur",
    },
  ],
};

function defaultActiveTab(workspace: ClubWorkspace) {
  if (workspace === "members") return "members";
  if (workspace === "registration") return "workspace";
  return "profile";
}

let stopSessionListener: (() => void) | null = null;
let usersRequestId = 0;
let dialogUsersRequestId = 0;
let dataRequestId = 0;
let membersRequestId = 0;
let evaluationMembersRequestId = 0;
let evaluationsRequestId = 0;

const currentUser = computed<UserSummary | null>(() => {
  const session = auth.value;
  if (!session) return null;

  const summary = users.value.find((user) => user.id === session.user.id);
  const roles = session.roles ?? [];

  return {
    id: session.user.id,
    username: session.user.username,
    realName: session.user.realName,
    studentNo: session.user.studentNo ?? null,
    displayName: buildSessionDisplayName(session),
    accountStatus: session.user.accountStatus,
    roles: roles.map(toUserRoleSummary),
    memberships: summary?.memberships ?? [],
    canSubmitClubApplication:
      isStudentSession(roles) &&
      !hasPermission(clubReviewPermission) &&
      hasPermission(clubApplyPermission),
    canReviewClubApplication: hasPermission(clubReviewPermission),
  };
});
const hasAllPermissions = computed(() => auth.value?.permissions?.includes("*") ?? false);
const isReviewer = computed(() => currentUser.value?.canReviewClubApplication ?? false);
const canSubmitApplication = computed(() => currentUser.value?.canSubmitClubApplication ?? false);
const canManageClubProfiles = computed(() => hasPermission(clubInfoManagePermission));
// Member evaluations now live in EvaluationList.vue; keep the legacy club tab disabled explicitly.
const showLegacyEvaluationTab = false;
const hasAnyMemberTermManagePermission = computed(() => hasPermission(clubMemberManagePermission));
const canManageMemberTerms = computed(() => {
  const clubId = selectedClubId.value;
  return Boolean(clubId && hasScopedClubPermission(clubId, clubMemberManagePermission));
});
const canViewAllClubWorkspaces = computed(() => hasAllPermissions.value || isReviewer.value);
const canAdministerMemberTerms = computed(
  () => hasAllPermissions.value || canManageMemberTerms.value,
);
const canViewMemberDirectory = computed(
  () =>
    canViewAllClubWorkspaces.value ||
    hasAnyMemberTermManagePermission.value ||
    hasPermission(clubInternalViewPermission) ||
    hasPermission(clubOperationViewPermission),
);
const canViewClubProfiles = computed(
  () => canViewAllClubWorkspaces.value || canViewMemberDirectory.value,
);
const myApplications = computed(() =>
  applications.value.filter((item) => item.applicantUserId === currentUserId.value),
);
const reviewApplications = computed(() =>
  isReviewer.value ? applications.value : myApplications.value,
);
const applicationRows = computed(() => reviewApplications.value);
const selectedClub = computed(
  () => clubs.value.find((club) => club.id === selectedClubId.value) ?? null,
);
const clubInfoRows = computed(() =>
  canViewClubProfiles.value ? clubs.value.filter((club) => canViewClubInfo(club)) : [],
);
const isGlobalClubGovernance = computed(() => canViewAllClubWorkspaces.value);
const focusedClubRows = computed(() => {
  const club = selectedClub.value;
  return club && canViewClubInfo(club) ? [club] : [];
});
const visibleClubInfoRows = computed(() => {
  if (selectedClubId.value) return focusedClubRows.value;
  return isGlobalClubGovernance.value ? clubInfoRows.value : focusedClubRows.value;
});
const manageableClubs = computed(() => clubs.value.filter((club) => canManageClub(club)));
const profileRows = computed(() => (canManageClubProfiles.value ? manageableClubs.value : []));
const memberViewClubs = computed(() =>
  canViewMemberDirectory.value ? clubs.value.filter((club) => canViewClubDirectory(club)) : [],
);
const evaluationViewClubs = computed(() =>
  clubs.value.filter((club) => canViewClubEvaluations(club)),
);
const clubContextOptions = computed<ClubContextOption[]>(() =>
  memberViewClubs.value.map((club) => buildClubContextOption(club)),
);
const selectedClubContext = computed(
  () => clubContextOptions.value.find((option) => option.clubId === selectedClubId.value) ?? null,
);
const canManageSelectedClub = computed(
  () => selectedClub.value !== null && canManageClub(selectedClub.value),
);
const canRemoveSelectedClubMember = computed(
  () => selectedClub.value !== null && canRemoveClubMember(selectedClub.value),
);
const canExitSelectedClub = computed(() => {
  const clubId = selectedClubId.value;
  if (!clubId) return false;

  return myMemberships.value.some(
    (membership) => membership.clubId === clubId && isActiveStatus(membership.memberStatus),
  );
});
const myMemberships = computed(() => currentUser.value?.memberships ?? []);
const identityRows = computed<IdentityRow[]>(() => {
  const user = currentUser.value;
  if (!user) return [];

  const rows = myMemberships.value.map((membership) => ({
    clubId: membership.clubId,
    clubName: membership.clubName,
    departmentName: membership.departmentName,
    groupName: membership.groupName,
    positionName: membership.positionName,
    termName: membership.termName,
    memberStatus: membership.memberStatus,
  }));
  const existingClubIds = new Set(rows.map((row) => row.clubId));

  user.roles.forEach((role) => {
    const clubIds = role.clubIds?.length ? role.clubIds : role.clubId !== null ? [role.clubId] : [];

    clubIds
      .filter((clubId) => !existingClubIds.has(clubId))
      .forEach((clubId) => {
        existingClubIds.add(clubId);

        rows.push({
          clubId,
          clubName: role.clubName ?? `社团 ${clubId}`,
          departmentName: null,
          groupName: null,
          positionName: role.roleName,
          termName: "未登记任期",
          memberStatus: "role_only",
        });
      });
  });

  return rows;
});
const activePresidentOptions = computed(() =>
  clubMembers.value.filter((member) => member.isCurrent && isActiveStatus(member.memberStatus)),
);
const advisorOptions = computed(() => dialogUsers.value.filter((user) => isAdvisorCandidate(user)));
const memberTermUserOptions = computed(() => dialogUsers.value);
const memberTermUserLocked = computed(
  () =>
    memberWorkspaceMode.value === "transition" &&
    memberTermMode.value === "create" &&
    memberTermTarget.value !== null,
);
const selectedManualDepartmentOptions = computed(() =>
  selectedClubId.value ? (manualDepartmentOptions.value[selectedClubId.value] ?? []) : [],
);
const selectedManualGroupOptions = computed(() =>
  selectedClubId.value ? (manualGroupOptions.value[selectedClubId.value] ?? []) : [],
);
const memberGroupEntries = computed<MemberGroupOption[]>(() => [
  ...clubMembers.value
    .map((member) => ({
      departmentName: member.departmentName?.trim() ?? "",
      groupName: member.groupName?.trim() ?? "",
    }))
    .filter((entry) => entry.groupName),
  ...selectedManualGroupOptions.value,
]);
const memberDepartmentOptions = computed(() =>
  uniqueTextOptions([
    ...clubMembers.value.map((member) => member.departmentName),
    ...selectedManualDepartmentOptions.value,
    ...selectedManualGroupOptions.value.map((group) => group.departmentName),
  ]),
);
const memberGroupOptions = computed(() => groupOptionsForDepartment(memberFilters.departmentName));
const selectedCadreGroupingScopes = computed<MemberGroupingScope[]>(() => {
  const user = currentUser.value;
  const clubId = selectedClubId.value;
  if (!user || !clubId) return [];

  return collectCadreScopesFromMemberships(user.memberships, user.roles, clubId);
});
const selectedDepartmentManagerScopes = computed(() =>
  selectedCadreGroupingScopes.value.filter(
    (scope) => scope.departmentName.trim() && !scope.groupName.trim(),
  ),
);
const canCreateMemberDepartment = computed(
  () => memberWorkspaceMode.value === "current" && canManageSelectedClub.value,
);
const canCreateAcademicTerm = computed(() => canManageSelectedClub.value);
const canCreateMemberGroup = computed(
  () =>
    memberWorkspaceMode.value === "current" &&
    (canManageSelectedClub.value || selectedDepartmentManagerScopes.value.length > 0),
);
const canShowMemberOperationColumn = computed(
  () =>
    memberWorkspaceMode.value === "current" &&
    (canAdministerMemberTerms.value ||
      canManageSelectedClub.value ||
      canRemoveSelectedClubMember.value ||
      canExitSelectedClub.value ||
      selectedCadreGroupingScopes.value.length > 0),
);
const groupCreateDepartmentOptions = computed(() =>
  canManageSelectedClub.value
    ? memberDepartmentOptions.value
    : uniqueTextOptions(selectedDepartmentManagerScopes.value.map((scope) => scope.departmentName)),
);
const memberGroupingGroupOptions = computed(() =>
  groupOptionsForDepartment(memberGroupingForm.departmentName),
);
const academicTermOptions = computed<AcademicTermOption[]>(() => {
  const currentYear = academicYearStart(new Date());
  const baseTerms = [currentYear - 1, currentYear, currentYear + 1, currentYear + 2].map((year) =>
    academicTermOption(year),
  );
  const memberTerms = clubMembers.value
    .filter((member) => member.termName && member.termStart && member.termEnd)
    .map((member) => ({
      label: member.termName as string,
      termStart: dateOnly(member.termStart),
      termEnd: dateOnly(member.termEnd),
    }));

  return uniqueAcademicTermOptions([
    ...baseTerms,
    ...manualAcademicTermOptions.value,
    ...memberTerms,
  ]);
});
const memberTermSelectOptions = computed<AcademicTermOption[]>(() => {
  const options = [...academicTermOptions.value];
  const currentLabel = memberTermForm.termName.trim();
  if (currentLabel && !options.some((option) => option.label === currentLabel)) {
    options.unshift({
      label: currentLabel,
      termStart: memberTermForm.termStart,
      termEnd: memberTermForm.termEnd,
    });
  }
  return options;
});
const memberTermFilterOptions = computed(() =>
  uniqueTextOptions([
    ...clubMembers.value.map((member) => member.termName),
    ...academicTermOptions.value.map((term) => term.label),
  ]),
);
const currentClubMembers = computed(() => clubMembers.value.filter((member) => member.isCurrent));
const memberTableRows = computed(() =>
  (memberWorkspaceMode.value === "history" ? clubMembers.value : currentClubMembers.value).filter(
    (member) =>
      (memberWorkspaceMode.value !== "history" ||
        !memberFilters.termName ||
        member.termName === memberFilters.termName) &&
      (!memberFilters.unassignedOnly || isMemberUnassigned(member)),
  ),
);
const transitionSourceRows = computed(() => {
  const rows = new Map<number, ClubMemberRecord>();

  clubMembers.value
    .filter((member) => shouldEnterTransitionQueue(member))
    .forEach((member) => {
      const existing = rows.get(member.userId);
      if (!existing || transitionRecordSortKey(member) > transitionRecordSortKey(existing)) {
        rows.set(member.userId, member);
      }
    });

  return Array.from(rows.values()).sort((left, right) =>
    transitionRecordSortKey(right).localeCompare(transitionRecordSortKey(left)),
  );
});
const memberGroupSummary = computed(() => {
  const rows = memberTableRows.value;
  const currentRows = rows.filter((member) => member.isCurrent);
  return {
    total: rows.length,
    current: currentRows.length,
    departments: uniqueTextOptions(rows.map((member) => member.departmentName)).length,
    groups: uniqueTextOptions(rows.map((member) => member.groupName)).length,
    unassigned: rows.filter(isMemberUnassigned).length,
  };
});
const evaluationTermOptions = computed(() =>
  uniqueTextOptions(evaluations.value.map((evaluation) => evaluation.termName)),
);
const evaluationSummary = computed(() => {
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
    average: Number(average.toFixed(1)),
  };
});
const evaluationTargetOptions = computed(() =>
  evaluationMembers.value.filter(
    (member) => member.isCurrent && canMaintainEvaluationForMember(member),
  ),
);
const canMaintainSelectedEvaluations = computed(
  () => selectedClub.value?.status === "active" && evaluationTargetOptions.value.length > 0,
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
const workspaceTitle = computed(() => {
  if (isMemberWorkspace.value) return "成员管理";
  if (isRegistrationWorkspace.value) return "社团注册";
  return "我的社团";
});
const workspaceSubtitle = computed(() => {
  if (isMemberWorkspace.value) return "成员名册、任期维护、干部换届与成员退出";
  if (isRegistrationWorkspace.value) return "社团注册申请、我的申请进度与负责人审核";
  return "社团基本信息与我的社团身份";
});
const workspaceEmptyDescription = computed(() => {
  if (isMemberWorkspace.value) return "当前账号暂无成员管理相关任务";
  if (isRegistrationWorkspace.value) return "当前账号暂无社团注册相关任务";
  return "当前账号暂无社团身份或可查看的社团信息";
});
const visibleIdentityRows = computed(() => {
  if (!selectedClubId.value) return identityRows.value;
  return identityRows.value.filter((row) => row.clubId === selectedClubId.value);
});
const visibleTabs = computed(() => {
  const tabs: string[] = [];

  if (isRegistrationWorkspace.value) {
    if (canSubmitApplication.value || isReviewer.value) tabs.push("workspace");
    return tabs;
  }

  if (isMemberWorkspace.value) {
    if (memberViewClubs.value.length > 0) tabs.push("members");
    return tabs;
  }

  if (visibleClubInfoRows.value.length > 0) tabs.push("profile");
  if (visibleIdentityRows.value.length > 0) tabs.push("identity");
  return tabs;
});
const hasClubWorkspace = computed(() => visibleTabs.value.length > 0);

async function refreshAuthSession() {
  if (!currentUserId.value) return;

  const session = await requestJson<AuthResponse>("/api/auth/session");
  saveAuth(session);
  auth.value = session;
}

async function refreshAuthSessionQuietly() {
  try {
    await refreshAuthSession();
  } catch {
    /* 会话刷新失败不应覆盖已成功完成的主操作。 */
  }
}

async function validateForm(form: FormInstance | undefined) {
  if (!form) return false;

  try {
    await form.validate();
    return true;
  } catch {
    return false;
  }
}

async function loadUsers() {
  const requestId = ++usersRequestId;
  if (!currentUserId.value) {
    if (requestId === usersRequestId) users.value = [];
    usersLoading.value = false;
    return;
  }

  usersLoading.value = true;
  try {
    const data = await requestJson<UserSummary[]>(`/api/users`);
    if (requestId === usersRequestId) users.value = data;
  } catch (e) {
    if (requestId === usersRequestId) {
      error.value = e instanceof Error ? e.message : "用户加载失败";
      users.value = [];
    }
  } finally {
    if (requestId === usersRequestId) usersLoading.value = false;
  }
}

async function loadDialogUsers(clubId?: number) {
  const requestId = ++dialogUsersRequestId;
  if (!currentUserId.value) {
    if (requestId === dialogUsersRequestId) dialogUsers.value = [];
    dialogUsersLoading.value = false;
    return;
  }

  dialogUsersLoading.value = true;
  try {
    const query = clubId ? `?clubId=${clubId}` : "";
    const data = await requestJson<UserSummary[]>(`/api/users${query}`);
    if (requestId === dialogUsersRequestId) dialogUsers.value = data;
  } catch (e) {
    if (requestId === dialogUsersRequestId) {
      error.value = e instanceof Error ? e.message : "用户加载失败";
      dialogUsers.value = [];
    }
  } finally {
    if (requestId === dialogUsersRequestId) dialogUsersLoading.value = false;
  }
}

async function loadApplicationAdvisorCandidates() {
  if (!currentUserId.value) {
    applicationAdvisorCandidates.value = [];
    return;
  }

  applicationAdvisorLoading.value = true;
  try {
    applicationAdvisorCandidates.value = await requestJson<LearningTeacherCandidate[]>(
      "/api/clubs/advisor-candidates",
    );
  } catch (e) {
    applicationAdvisorCandidates.value = [];
    ElMessage.error(e instanceof Error ? e.message : "指导老师候选加载失败");
  } finally {
    applicationAdvisorLoading.value = false;
  }
}

async function loadData() {
  const requestId = ++dataRequestId;
  if (!currentUserId.value) {
    if (requestId === dataRequestId) {
      loading.value = false;
      applications.value = [];
      clubs.value = [];
      clubMembers.value = [];
      evaluationMembers.value = [];
      evaluations.value = [];
    }
    return;
  }

  loading.value = true;
  error.value = "";
  try {
    const query = new URLSearchParams();
    if (filters.auditStatus) query.set("auditStatus", filters.auditStatus);
    const shouldLoadApplications =
      isRegistrationWorkspace.value && (canSubmitApplication.value || isReviewer.value);
    const shouldLoadClubs =
      !isRegistrationWorkspace.value && (canViewClubProfiles.value || canManageClubProfiles.value);

    const [applicationData, clubData] = await Promise.all([
      shouldLoadApplications
        ? requestJson<ClubApplication[]>(`/api/clubs/applications?${query.toString()}`)
        : Promise.resolve([]),
      shouldLoadClubs ? requestJson<Club[]>(`/api/clubs`) : Promise.resolve([]),
    ]);
    if (requestId !== dataRequestId) return;
    applications.value = applicationData;
    clubs.value = clubData;
    syncSelectedClub();
    await Promise.all([loadMembers(), loadEvaluationMembers(), loadEvaluations()]);
  } catch (e) {
    if (requestId === dataRequestId) {
      error.value = e instanceof Error ? e.message : "加载失败";
      applications.value = [];
      clubs.value = [];
      clubMembers.value = [];
      evaluationMembers.value = [];
      evaluations.value = [];
    }
  } finally {
    if (requestId === dataRequestId) loading.value = false;
  }
}

async function loadMembers() {
  const requestId = ++membersRequestId;
  const clubId = selectedClubId.value;
  const userId = currentUserId.value;
  const include = memberWorkspaceMode.value !== "current";
  if (!userId || !clubId || !canViewSelectedClub()) {
    if (requestId === membersRequestId) {
      clubMembers.value = [];
    }
    return;
  }

  memberLoading.value = true;
  try {
    const query = new URLSearchParams({
      includeHistory: String(include),
    });
    if (memberWorkspaceMode.value === "history" && memberFilters.termName) {
      query.set("termName", memberFilters.termName);
    }
    if (memberFilters.departmentName) query.set("departmentName", memberFilters.departmentName);
    if (memberFilters.groupName) query.set("groupName", memberFilters.groupName);
    const data = await requestJson<ClubMemberRecord[]>(
      `/api/clubs/${clubId}/members?${query.toString()}`,
    );
    if (requestId === membersRequestId) clubMembers.value = data;
  } catch (e) {
    if (requestId === membersRequestId) {
      clubMembers.value = [];
      ElMessage.error(e instanceof Error ? e.message : "成员任期加载失败");
    }
  } finally {
    if (requestId === membersRequestId) memberLoading.value = false;
  }
}

async function loadEvaluationMembers() {
  const requestId = ++evaluationMembersRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId || !canViewSelectedClub()) {
    if (requestId === evaluationMembersRequestId) evaluationMembers.value = [];
    return;
  }

  try {
    const query = new URLSearchParams({
      includeHistory: "false",
    });
    const data = await requestJson<ClubMemberRecord[]>(
      `/api/clubs/${clubId}/members?${query.toString()}`,
    );
    if (requestId === evaluationMembersRequestId) evaluationMembers.value = data;
  } catch {
    if (requestId === evaluationMembersRequestId) evaluationMembers.value = [];
  }
}

async function loadEvaluations() {
  const requestId = ++evaluationsRequestId;
  const clubId = selectedClubId.value;
  if (!currentUserId.value || !clubId || evaluationViewClubs.value.length === 0) {
    if (requestId === evaluationsRequestId) evaluations.value = [];
    return;
  }

  evaluationLoading.value = true;
  try {
    const query = new URLSearchParams();
    if (evaluationFilters.termName) query.set("termName", evaluationFilters.termName);
    query.set("evaluationType", "semester");
    const data = await requestJson<ClubEvaluationRecord[]>(
      `/api/clubs/${clubId}/evaluations?${query.toString()}`,
    );
    if (requestId === evaluationsRequestId) evaluations.value = data;
  } catch (e) {
    if (requestId === evaluationsRequestId) {
      evaluations.value = [];
      ElMessage.error(e instanceof Error ? e.message : "成员考核加载失败");
    }
  } finally {
    if (requestId === evaluationsRequestId) evaluationLoading.value = false;
  }
}

function syncSelectedClub() {
  const options = memberViewClubs.value;
  if (options.some((club) => club.id === selectedClubId.value)) return;
  selectedClubId.value = options[0]?.id;
}

function canManageClub(club: Club) {
  const user = currentUser.value;
  if (!user || club.status !== "active") return false;
  if (hasAllPermissions.value) return true;
  if (club.presidentUserId === user.id) return true;
  if (club.advisorUserId === user.id) return true;

  const hasRole = user.roles.some(
    (role) =>
      roleCoversClub(role, club.id) &&
      (principalRoleCodes.has((role.roleCode ?? "").toLowerCase()) ||
        advisorRoleCodes.has((role.roleCode ?? "").toLowerCase())),
  );
  const hasPrincipalMembership = user.memberships.some(
    (membership) =>
      membership.clubId === club.id &&
      isActiveStatus(membership.memberStatus) &&
      isPrincipalPosition(membership.positionName),
  );

  return hasRole || hasPrincipalMembership;
}

function canRemoveClubMember(club: Club) {
  const user = currentUser.value;
  if (!user || club.status !== "active") return false;
  if (canManageClub(club)) return true;

  return hasScopedRole(user.roles, club.id, ["club_officer"]);
}

function canDissolveClub(club: Club) {
  return hasAllPermissions.value && club.status === "active";
}

function canViewClubInfo(club: Club) {
  if (canViewAllClubWorkspaces.value) return true;
  return canManageClub(club) || canViewClubDirectory(club);
}

function canViewClubDirectory(club: Club) {
  if (canViewAllClubWorkspaces.value) return true;
  if (canManageClub(club)) return true;

  const user = currentUser.value;
  if (!user) return false;

  const hasParticipantRole = user.roles.some(
    (role) =>
      roleCoversClub(role, club.id) &&
      clubParticipantRoleCodes.has((role.roleCode ?? "").toLowerCase()),
  );
  const hasAdvisorRole = user.roles.some(
    (role) =>
      roleCoversClub(role, club.id) && advisorRoleCodes.has((role.roleCode ?? "").toLowerCase()),
  );
  const hasMembership = user.memberships.some(
    (membership) => membership.clubId === club.id && isActiveStatus(membership.memberStatus),
  );

  return hasParticipantRole || hasAdvisorRole || hasMembership;
}

function canViewClubEvaluations(club: Club) {
  if (hasAllPermissions.value || canManageClub(club)) return true;

  const user = currentUser.value;
  if (!user) return false;

  return user.memberships.some(
    (membership) => membership.clubId === club.id && isActiveStatus(membership.memberStatus),
  );
}

function buildClubContextOption(club: Club): ClubContextOption {
  const user = currentUser.value;
  const memberships = user?.memberships.filter((membership) => membership.clubId === club.id) ?? [];
  const activeMembership =
    memberships.find((membership) => isActiveStatus(membership.memberStatus)) ?? memberships[0];
  const scopedRoles =
    user?.roles
      .filter((role) => role.roleScope === "club" && roleCoversClub(role, club.id))
      .map((role) => roleNameInClub(role, club.name)) ?? [];
  const labels = new Set<string>();

  if (activeMembership?.departmentName) labels.add(activeMembership.departmentName);
  if (activeMembership?.positionName) labels.add(activeMembership.positionName);
  scopedRoles.forEach((label) => {
    if (label) labels.add(label);
  });

  if (hasAllPermissions.value) labels.add("系统管理员");

  const canManage = canManageClub(club);
  const roleText = Array.from(labels).join(" / ") || (canManage ? "可维护" : "可查看");
  const statusText = canManage ? "可维护档案与任期" : "查看成员任期";

  return {
    clubId: club.id,
    clubName: club.name,
    roleText,
    statusText,
    optionLabel: `${club.name} / ${roleText}`,
    canManage,
  };
}

function roleNameInClub(role: UserRoleSummary, clubName: string) {
  const name = role.roleName || "";
  if (name.startsWith(clubName)) return name.slice(clubName.length) || name;
  return name;
}

function canViewSelectedClub() {
  return selectedClub.value !== null && canViewClubDirectory(selectedClub.value);
}

function resetApplicationForm() {
  applicationForm.name = "";
  applicationForm.category = "";
  applicationForm.description = "";
  applicationForm.applyReason = "";
  applicationForm.materialUrl = "";
  applicationForm.contactPhone = "";
  applicationForm.advisorUserId = null;
  applicationFormRef.value?.clearValidate();
}

function openApplicationDialog() {
  if (!canSubmitApplication.value) {
    ElMessage.warning("当前用户不能提交社团注册申请，请切换到学生账号。");
    return;
  }

  resetApplicationForm();
  applicationDialogVisible.value = true;
  void loadApplicationAdvisorCandidates();
}

async function submitApplication() {
  if (!applicationFormRef.value || !currentUserId.value) return;
  if (!(await validateForm(applicationFormRef.value))) return;

  saving.value = true;
  try {
    await requestJson<ClubApplication>("/api/clubs/applications", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
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
  if (!(await validateForm(reviewFormRef.value))) return;

  if (reviewForm.decision === "rejected" && !reviewForm.reviewComment.trim()) {
    ElMessage.warning("退回申请时必须填写审核意见");
    return;
  }

  const actionText = reviewForm.decision === "approved" ? "通过" : "退回";
  try {
    await ElMessageBox.confirm(
      `确认${actionText}“${reviewTarget.value.name}”的注册申请？`,
      "审核确认",
      {
        type: reviewForm.decision === "approved" ? "success" : "warning",
      },
    );
  } catch {
    return;
  }

  reviewing.value = true;
  try {
    await requestJson<ClubApplication>(`/api/clubs/applications/${reviewTarget.value.id}/review`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
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

async function openProfileDialog(row: Club) {
  if (!canManageClub(row)) {
    ElMessage.warning("当前身份不能维护该社团档案。");
    return;
  }

  selectedClubId.value = row.id;
  await Promise.all([loadMembers(), loadDialogUsers()]);
  profileTarget.value = row;
  profileForm.name = row.name;
  profileForm.category = row.category ?? "";
  profileForm.description = row.description ?? "";
  profileForm.logoUrl = row.logoUrl ?? "";
  profileForm.presidentUserId = row.presidentUserId;
  profileForm.advisorUserId = row.advisorUserId;
  profileForm.contactPhone = row.contactPhone ?? "";
  profileFormRef.value?.clearValidate();
  profileDialogVisible.value = true;
}

async function submitProfile() {
  if (!profileFormRef.value || !profileTarget.value || !currentUserId.value) return;
  if (!(await validateForm(profileFormRef.value))) return;

  profileSaving.value = true;
  try {
    await requestJson<Club>(`/api/clubs/${profileTarget.value.id}/profile`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name: profileForm.name,
        category: profileForm.category,
        description: emptyToNull(profileForm.description),
        logoUrl: emptyToNull(profileForm.logoUrl),
        presidentUserId: profileForm.presidentUserId,
        advisorUserId: profileForm.advisorUserId,
        contactPhone: emptyToNull(profileForm.contactPhone),
      }),
    });
    ElMessage.success("社团档案已保存");
    profileDialogVisible.value = false;
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "保存失败");
  } finally {
    profileSaving.value = false;
  }
}

async function dissolveClub(row: Club) {
  if (!currentUserId.value || !canDissolveClub(row)) {
    ElMessage.warning("当前身份不能解散该社团。");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确认解散“${row.name}”？解散后社团档案和成员任期会保留，但社团不再处于运营状态。`,
      "解散社团",
      {
        type: "warning",
        confirmButtonText: "确认解散",
        cancelButtonText: "取消",
      },
    );
  } catch {
    return;
  }

  dissolvingClubId.value = row.id;
  try {
    await requestJson<void>(`/api/clubs/${row.id}/dissolve`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({}),
    });
    ElMessage.success("社团已解散");
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "解散失败");
  } finally {
    dissolvingClubId.value = null;
  }
}

async function exitCurrentClub(row: IdentityRow) {
  if (!currentUserId.value || !canExitIdentity(row)) {
    ElMessage.warning(memberExitDisabledReason(row));
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确认退出“${row.clubName}”？退出后将保留历史任期，但不再拥有该社团成员身份。`,
      "退出社团",
      {
        type: "warning",
        confirmButtonText: "确认退出",
        cancelButtonText: "取消",
      },
    );
  } catch {
    return;
  }

  exitingClubId.value = row.clubId;
  try {
    await requestJson<void>(`/api/clubs/${row.clubId}/members/self/exit`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({}),
    });
    ElMessage.success("已退出社团");
    await refreshAuthSessionQuietly();
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "退出失败");
  } finally {
    exitingClubId.value = null;
  }
}

async function removeClubMember(row: ClubMemberRecord) {
  if (!currentUserId.value || !canRemoveMemberRow(row)) {
    ElMessage.warning(memberExitDisabledReason(row));
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确认将“${row.userName}”移出“${row.clubName}”？成员任期会转为历史记录。`,
      "移出成员",
      {
        type: "warning",
        confirmButtonText: "确认移出",
        cancelButtonText: "取消",
      },
    );
  } catch {
    return;
  }

  exitingMemberId.value = row.memberId;
  try {
    await requestJson<void>(`/api/clubs/${row.clubId}/members/${row.memberId}/exit`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({}),
    });
    ElMessage.success("成员已移出");
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "移出失败");
  } finally {
    exitingMemberId.value = null;
  }
}

function resetMemberTermForm() {
  const term = currentAcademicTermOption();
  memberTermForm.userId = undefined;
  memberTermForm.departmentName = "";
  memberTermForm.groupName = "";
  memberTermForm.positionName = "";
  memberTermForm.termName = term.label;
  memberTermForm.termStart = term.termStart;
  memberTermForm.termEnd = term.termEnd;
  memberTermForm.memberStatus = "active";
  memberTermForm.contributionScore = 0;
  memberTermForm.closeCurrentTerm = true;
  memberTermFormRef.value?.clearValidate();
}

async function openCreateMemberTermDialog() {
  if (!selectedClub.value || !canManageSelectedClub.value) {
    ElMessage.warning("当前身份不能维护该社团成员任期。");
    return;
  }

  memberTermMode.value = "create";
  memberTermTarget.value = null;
  resetMemberTermForm();
  await loadDialogUsers(selectedClub.value.id);
  memberTermDialogVisible.value = true;
}

async function openTransitionMemberTermDialog(row?: ClubMemberRecord) {
  if (!selectedClub.value || !canManageSelectedClub.value) {
    ElMessage.warning("当前身份不能发起该社团换届。");
    return;
  }

  if (!row || !shouldEnterTransitionQueue(row)) {
    ElMessage.warning("只能处理已进入换届暂存区的成员。");
    return;
  }

  memberTermMode.value = "create";
  memberTermTarget.value = row;
  resetMemberTermForm();
  memberTermForm.termName = transitionTermForm.label;
  memberTermForm.termStart = transitionTermForm.termStart;
  memberTermForm.termEnd = transitionTermForm.termEnd;
  memberTermForm.closeCurrentTerm = true;
  memberTermForm.userId = row.userId;
  memberTermForm.departmentName = row.departmentName ?? "";
  memberTermForm.groupName = row.groupName ?? "";
  memberTermForm.positionName = row.positionName ?? "";
  memberTermForm.contributionScore = row.contributionScore ?? 0;
  await loadDialogUsers(selectedClub.value.id);
  memberTermDialogVisible.value = true;
}

function canEditMemberTerm(row: ClubMemberRecord) {
  if (canAdministerMemberTerms.value) return true;
  return canManageSelectedClub.value && row.userId !== currentUserId.value;
}

function canEditMemberRow(row: ClubMemberRecord) {
  return canEditMemberTerm(row) || canUpdateMemberDepartment(row) || canUpdateMemberGroup(row);
}

function memberTermEditDeniedMessage(row: ClubMemberRecord) {
  if (canManageSelectedClub.value && row.userId === currentUserId.value) {
    return "负责人不能修改自己的任期，请由指导老师或系统管理员处理。";
  }

  return "当前身份不能维护该社团成员任期。";
}

function openEditMemberTermDialog(row: ClubMemberRecord) {
  if (!canEditMemberTerm(row)) {
    ElMessage.warning(memberTermEditDeniedMessage(row));
    return;
  }

  memberTermMode.value = "edit";
  memberTermTarget.value = row;
  memberTermForm.userId = row.userId;
  memberTermForm.departmentName = row.departmentName ?? "";
  memberTermForm.groupName = row.groupName ?? "";
  memberTermForm.positionName = row.positionName ?? "";
  memberTermForm.termName = row.termName ?? "";
  memberTermForm.termStart = dateOnly(row.termStart);
  memberTermForm.termEnd = dateOnly(row.termEnd);
  memberTermForm.memberStatus = normalizeMemberStatus(row.memberStatus);
  memberTermForm.contributionScore = row.contributionScore ?? 0;
  memberTermForm.closeCurrentTerm = false;
  memberTermFormRef.value?.clearValidate();
  memberTermDialogVisible.value = true;
}

function openMemberEditDialog(row: ClubMemberRecord) {
  if (canEditMemberTerm(row)) {
    openEditMemberTermDialog(row);
    return;
  }

  if (canManageSelectedClub.value && row.userId === currentUserId.value) {
    ElMessage.warning(memberTermEditDeniedMessage(row));
    return;
  }

  if (canUpdateMemberDepartment(row)) {
    openMemberGroupingDialog(row, "departmentName");
    return;
  }

  if (canUpdateMemberGroup(row)) {
    openMemberGroupingDialog(row, "groupName");
    return;
  }

  ElMessage.warning("当前身份不能维护该成员。");
}

function canFreelyUpdateMemberGrouping(row: ClubMemberRecord) {
  if (!row.isCurrent) return false;
  return canAdministerMemberTerms.value || canManageSelectedClub.value;
}

function canUpdateMemberDepartment(row: ClubMemberRecord) {
  return canFreelyUpdateMemberGrouping(row);
}

function canUpdateMemberGroup(row: ClubMemberRecord) {
  if (canFreelyUpdateMemberGrouping(row)) return true;
  if (!row.isCurrent) return false;
  return selectedCadreGroupingScopes.value.length > 0;
}

function groupingDialogTitle() {
  if (memberGroupingMode.value === "own") return "纳入我的小组";
  return memberGroupingField.value === "departmentName" ? "修改部门" : "修改小组";
}

function openMemberGroupingDialog(row: ClubMemberRecord, field: GroupingField) {
  if (field === "departmentName" && !canUpdateMemberDepartment(row)) {
    ElMessage.warning("当前身份不能维护该成员部门。");
    return;
  }

  if (field === "groupName" && !canUpdateMemberGroup(row)) {
    ElMessage.warning("当前身份不能维护该成员分组。");
    return;
  }

  memberGroupingTarget.value = row;
  memberGroupingField.value = field;
  if (canFreelyUpdateMemberGrouping(row)) {
    memberGroupingMode.value = "free";
    memberGroupingForm.departmentName = row.departmentName ?? "";
    memberGroupingForm.groupName = row.groupName ?? "";
  } else {
    const scope =
      selectedCadreGroupingScopes.value.find(
        (item) => !item.groupName && item.departmentName === (row.departmentName ?? ""),
      ) ??
      selectedCadreGroupingScopes.value.find((item) => !item.groupName) ??
      selectedCadreGroupingScopes.value[0];
    memberGroupingMode.value = scope.groupName ? "own" : "department";
    memberGroupingForm.departmentName = scope.departmentName;
    memberGroupingForm.groupName = scope.groupName || row.groupName || "";
  }
  handleMemberGroupingDepartmentChange();
  memberGroupingDialogVisible.value = true;
}

async function submitMemberGrouping() {
  if (!memberGroupingTarget.value || !selectedClubId.value || !currentUserId.value) return;

  if (memberGroupingField.value === "groupName" && !memberGroupingForm.groupName.trim()) {
    ElMessage.warning("请选择小组。");
    return;
  }

  if (memberGroupingMode.value !== "free" && !memberGroupingForm.groupName.trim()) {
    ElMessage.warning(
      memberGroupingMode.value === "own"
        ? "干部账号需要先登记自己的小组，才能把成员纳入本组。"
        : "请选择本部门下的小组。",
    );
    return;
  }

  const nextDepartment = memberGroupingForm.departmentName;
  const nextGroup = memberGroupingForm.groupName;

  groupingSaving.value = true;
  try {
    await requestJson<ClubMemberRecord>(
      `/api/clubs/${selectedClubId.value}/members/${memberGroupingTarget.value.memberId}/grouping`,
      {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          departmentName: emptyToNull(nextDepartment),
          groupName: emptyToNull(nextGroup),
        }),
      },
    );
    ElMessage.success(
      memberGroupingMode.value === "own"
        ? "成员已纳入我的小组"
        : memberGroupingMode.value === "department"
          ? "成员已纳入本部门小组"
          : memberGroupingField.value === "departmentName"
            ? "成员部门已更新"
            : "成员小组已更新",
    );
    memberGroupingDialogVisible.value = false;
    await Promise.all([loadUsers(), loadMembers()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "分组保存失败");
  } finally {
    groupingSaving.value = false;
  }
}

async function submitMemberTerm() {
  if (!memberTermFormRef.value || !selectedClubId.value || !currentUserId.value) return;
  if (!(await validateForm(memberTermFormRef.value))) return;

  termSaving.value = true;
  try {
    const payload = {
      userId: memberTermForm.userId,
      departmentName: emptyToNull(memberTermForm.departmentName),
      groupName: emptyToNull(memberTermForm.groupName),
      positionName: memberTermForm.positionName,
      termName: memberTermForm.termName,
      termStart: datePayload(memberTermForm.termStart),
      termEnd: datePayload(memberTermForm.termEnd),
      memberStatus: memberTermForm.memberStatus,
      contributionScore: memberTermForm.contributionScore,
      closeCurrentTerm: memberTermForm.closeCurrentTerm,
    };

    if (memberTermMode.value === "create") {
      await requestJson<ClubMemberRecord>(`/api/clubs/${selectedClubId.value}/members/terms`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
    } else if (memberTermTarget.value) {
      await requestJson<ClubMemberRecord>(
        `/api/clubs/${selectedClubId.value}/members/${memberTermTarget.value.memberId}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        },
      );
    }

    ElMessage.success(memberTermMode.value === "create" ? "成员任期已新增" : "成员任期已更新");
    memberTermDialogVisible.value = false;
    if (memberTermForm.userId === currentUserId.value) {
      await refreshAuthSessionQuietly();
    }
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "任期保存失败");
  } finally {
    termSaving.value = false;
  }
}

function canMaintainEvaluationForMember(member: ClubMemberRecord) {
  if (hasAllPermissions.value || canManageSelectedClub.value) return true;

  return selectedCadreGroupingScopes.value.some((scope) =>
    groupingMatchesScope(
      member.departmentName,
      member.groupName,
      scope.departmentName,
      scope.groupName,
    ),
  );
}

function canMaintainEvaluationRecord(row: ClubEvaluationRecord) {
  const member = evaluationMembers.value.find((item) => item.userId === row.userId);
  return Boolean(member && canMaintainEvaluationForMember(member));
}

function resetEvaluationForm() {
  evaluationForm.evaluationType = "semester";
  evaluationForm.userId = evaluationTargetOptions.value[0]?.userId;
  evaluationForm.termName = `${new Date().getFullYear()} 学年春季学期`;
  evaluationForm.awardTitle = "";
  evaluationForm.awardLevel = "";
  evaluationForm.awardReason = "";
  evaluationForm.activityScore = 0;
  evaluationForm.taskScore = 0;
  evaluationForm.learningScore = 0;
  evaluationForm.awardScore = 0;
  evaluationForm.publicStatus = "draft";
  evaluationForm.commentText = "";
  evaluationFormRef.value?.clearValidate();
}

function addAcademicTermOption() {
  if (!canCreateAcademicTerm.value) return;

  const startYear = Number(newAcademicTermStartYear.value);
  if (!Number.isInteger(startYear) || startYear < 2000 || startYear > 2100) {
    ElMessage.warning("请输入 2000-2100 之间的学年起始年份。");
    return;
  }

  const term = academicTermOption(startYear);
  if (academicTermOptions.value.some((option) => option.label === term.label)) {
    ElMessage.info("该任期已存在。");
    return;
  }

  manualAcademicTermOptions.value = uniqueAcademicTermOptions([
    ...manualAcademicTermOptions.value,
    term,
  ]);
  newAcademicTermStartYear.value = startYear + 1;
  ElMessage.success("任期已加入可选项");
}

function openCreateEvaluationDialog() {
  if (!canMaintainSelectedEvaluations.value) {
    ElMessage.warning("当前身份没有可录入评价的成员。");
    return;
  }

  evaluationMode.value = "create";
  evaluationTarget.value = null;
  resetEvaluationForm();
  evaluationDialogVisible.value = true;
}

function openEditEvaluationDialog(row: ClubEvaluationRecord) {
  const target = evaluationMembers.value.find((member) => member.userId === row.userId);
  if (!target || !canMaintainEvaluationForMember(target)) {
    ElMessage.warning("当前身份不能维护该成员评价。");
    return;
  }

  evaluationMode.value = "edit";
  evaluationTarget.value = row;
  evaluationForm.evaluationType = row.evaluationType;
  evaluationForm.userId = row.userId;
  evaluationForm.termName = row.termName;
  evaluationForm.awardTitle = row.awardTitle ?? "";
  evaluationForm.awardLevel = row.awardLevel ?? "";
  evaluationForm.awardReason = row.awardReason ?? "";
  evaluationForm.activityScore = row.activityScore;
  evaluationForm.taskScore = row.taskScore;
  evaluationForm.learningScore = row.learningScore;
  evaluationForm.awardScore = row.awardScore;
  evaluationForm.publicStatus = row.publicStatus;
  evaluationForm.commentText = row.commentText ?? "";
  evaluationFormRef.value?.clearValidate();
  evaluationDialogVisible.value = true;
}

async function submitEvaluation() {
  if (!evaluationFormRef.value || !selectedClubId.value || !currentUserId.value) return;
  if (!(await validateForm(evaluationFormRef.value))) return;

  evaluationSaving.value = true;
  try {
    const payload = {
      evaluationType: evaluationForm.evaluationType,
      userId: evaluationForm.userId,
      termName: evaluationForm.termName,
      awardTitle: emptyToNull(evaluationForm.awardTitle),
      awardLevel: emptyToNull(evaluationForm.awardLevel),
      awardReason: emptyToNull(evaluationForm.awardReason),
      activityScore: evaluationForm.activityScore,
      taskScore: evaluationForm.taskScore,
      learningScore: evaluationForm.learningScore,
      awardScore: evaluationForm.awardScore,
      publicStatus: evaluationForm.publicStatus,
      commentText: emptyToNull(evaluationForm.commentText),
    };

    if (evaluationMode.value === "create") {
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

    ElMessage.success(evaluationMode.value === "create" ? "评价考核已录入" : "评价考核已更新");
    evaluationDialogVisible.value = false;
    await loadEvaluations();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "评价考核保存失败");
  } finally {
    evaluationSaving.value = false;
  }
}

function clearEvaluationFilters() {
  evaluationFilters.termName = "";
  void loadEvaluations();
}

function resetFilters() {
  filters.auditStatus = "";
  void loadData();
}

function clearMemberFilters() {
  memberFilters.termName = "";
  memberFilters.departmentName = "";
  memberFilters.groupName = "";
  memberFilters.unassignedOnly = false;
  void loadMembers();
}

function toggleUnassignedMemberFilter(value: string | number | boolean) {
  if (!value) return;

  memberFilters.departmentName = "";
  memberFilters.groupName = "";
  void loadMembers();
}

function addMemberDepartmentOption() {
  const clubId = selectedClubId.value;
  const departmentName = newDepartmentName.value.trim();
  if (!clubId || !canCreateMemberDepartment.value) return;
  if (!departmentName) {
    ElMessage.warning("请填写部门名称。");
    return;
  }
  if (memberDepartmentOptions.value.includes(departmentName)) {
    ElMessage.info("该部门已存在。");
    return;
  }

  manualDepartmentOptions.value = {
    ...manualDepartmentOptions.value,
    [clubId]: [...(manualDepartmentOptions.value[clubId] ?? []), departmentName],
  };
  if (!newGroupDepartmentName.value) newGroupDepartmentName.value = departmentName;
  newDepartmentName.value = "";
  ElMessage.success("部门已加入可选项");
}

function addMemberGroupOption() {
  const clubId = selectedClubId.value;
  const groupName = newGroupName.value.trim();
  if (!clubId || !canCreateMemberGroup.value) return;

  const departmentOptions = groupCreateDepartmentOptions.value;
  const departmentName =
    newGroupDepartmentName.value.trim() ||
    (departmentOptions.length === 1 ? departmentOptions[0] : "");
  if (!departmentName) {
    ElMessage.warning("请选择小组所属部门。");
    return;
  }
  if (!groupName) {
    ElMessage.warning("请填写小组名称。");
    return;
  }
  if (!canManageSelectedClub.value && !departmentOptions.includes(departmentName)) {
    ElMessage.warning("部长只能在自己所在部门新增小组。");
    return;
  }
  if (groupOptionExists(departmentName, groupName)) {
    ElMessage.info("该部门下已存在同名小组。");
    return;
  }

  const nextGroups = [...(manualGroupOptions.value[clubId] ?? []), { departmentName, groupName }];
  manualGroupOptions.value = {
    ...manualGroupOptions.value,
    [clubId]: nextGroups,
  };
  if (!memberDepartmentOptions.value.includes(departmentName)) {
    manualDepartmentOptions.value = {
      ...manualDepartmentOptions.value,
      [clubId]: [...(manualDepartmentOptions.value[clubId] ?? []), departmentName],
    };
  }
  newGroupName.value = "";
  newGroupDepartmentName.value = departmentName;
  ElMessage.success("小组已加入可选项");
}

function groupOptionExists(departmentName: string, groupName: string) {
  return memberGroupEntries.value.some(
    (entry) =>
      entry.departmentName.trim() === departmentName.trim() &&
      entry.groupName.trim() === groupName.trim(),
  );
}

function handleMemberGroupingDepartmentChange() {
  if (
    memberGroupingForm.groupName &&
    !memberGroupingGroupOptions.value.includes(memberGroupingForm.groupName)
  ) {
    memberGroupingForm.groupName = "";
  }
}

function handleMemberTermDepartmentChange() {
  if (
    memberTermForm.groupName &&
    !groupOptionsForDepartment(memberTermForm.departmentName).includes(memberTermForm.groupName)
  ) {
    memberTermForm.groupName = "";
  }
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

function formatDateOnly(value: string | null | undefined) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(value));
}

function dateOnly(value: string | null | undefined) {
  if (!value) return "";
  return value.slice(0, 10);
}

function todayDateOnly() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

function academicYearStart(date: Date) {
  const year = date.getFullYear();
  return date.getMonth() >= 6 ? year : year - 1;
}

function academicTermOption(startYear: number): AcademicTermOption {
  return {
    label: `${startYear}-${startYear + 1}学年`,
    termStart: `${startYear}-07-01`,
    termEnd: `${startYear + 1}-06-30`,
  };
}

function uniqueAcademicTermOptions(options: AcademicTermOption[]) {
  const terms = new Map<string, AcademicTermOption>();
  options.forEach((option) => {
    const label = option.label.trim();
    if (!label || terms.has(label)) return;
    terms.set(label, {
      label,
      termStart: dateOnly(option.termStart),
      termEnd: dateOnly(option.termEnd),
    });
  });

  return Array.from(terms.values()).sort((left, right) => {
    const startCompare = left.termStart.localeCompare(right.termStart);
    return startCompare !== 0 ? startCompare : left.label.localeCompare(right.label, "zh-CN");
  });
}

function currentAcademicTermOption(offset = 0) {
  return academicTermOption(academicYearStart(new Date()) + offset);
}

function applyAcademicTermToMemberForm(termName: string) {
  const option = memberTermSelectOptions.value.find((item) => item.label === termName);
  if (!option) return;
  memberTermForm.termStart = option.termStart;
  memberTermForm.termEnd = option.termEnd;
}

function applyAcademicTermToTransition(termName: string) {
  const option = academicTermOptions.value.find((item) => item.label === termName);
  if (!option) return;
  transitionTermForm.label = option.label;
  transitionTermForm.termStart = option.termStart;
  transitionTermForm.termEnd = option.termEnd;
}

function shouldEnterTransitionQueue(member: ClubMemberRecord) {
  if (!isActiveStatus(member.memberStatus)) return false;

  const termEnd = dateOnly(member.termEnd);
  if (!termEnd || termEnd >= transitionTermForm.termStart) return false;

  return !clubMembers.value.some(
    (candidate) =>
      candidate.memberId !== member.memberId &&
      candidate.userId === member.userId &&
      dateOnly(candidate.termStart) >= transitionTermForm.termStart &&
      isActiveStatus(candidate.memberStatus),
  );
}

function transitionRecordSortKey(member: ClubMemberRecord) {
  return dateOnly(member.termEnd) || dateOnly(member.termStart) || "";
}

function datePayload(value: string) {
  return value ? `${value}T00:00:00` : null;
}

function emptyToNull(value: string) {
  return value.trim() ? value.trim() : null;
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

function memberStatusTagType(status: string | null | undefined) {
  if (status === "role_only") return "info";
  if (normalizeMemberStatus(status) === "active") return "success";
  if (normalizeMemberStatus(status) === "ended") return "info";
  return "warning";
}

function memberStatusText(status: string | null | undefined) {
  if (status === "role_only") return "未登记任期";
  const normalized = normalizeMemberStatus(status);
  if (normalized === "active") return "在任";
  if (normalized === "ended") return "已结束";
  return "暂停";
}

function isFutureMemberTerm(row: ClubMemberRecord) {
  const termStart = dateOnly(row.termStart);
  return isActiveStatus(row.memberStatus) && Boolean(termStart) && termStart > todayDateOnly();
}

function memberRecordStatusTagType(row: ClubMemberRecord) {
  return isFutureMemberTerm(row) ? "warning" : memberStatusTagType(row.memberStatus);
}

function memberRecordStatusText(row: ClubMemberRecord) {
  return isFutureMemberTerm(row) ? "未开始" : memberStatusText(row.memberStatus);
}

function memberTermPhase(row: ClubMemberRecord) {
  if (row.isCurrent) return "current";
  if (isFutureMemberTerm(row)) return "future";
  return "history";
}

function memberTermPhaseTagType(row: ClubMemberRecord) {
  return memberTermPhase(row) === "current"
    ? "success"
    : memberTermPhase(row) === "future"
      ? "warning"
      : "info";
}

function memberTermPhaseText(row: ClubMemberRecord) {
  return memberTermPhase(row) === "current"
    ? "当前"
    : memberTermPhase(row) === "future"
      ? "未来"
      : "历史";
}

function evaluationPublicTagType(status: EvaluationPublicStatus) {
  return status === "published" ? "success" : "info";
}

function evaluationGradeTagType(grade: string) {
  if (grade === "优秀") return "success";
  if (grade === "良好") return "primary";
  if (grade === "合格") return "warning";
  return "danger";
}

function evaluationGrade(totalScore: number) {
  if (totalScore >= 320) return "优秀";
  if (totalScore >= 260) return "良好";
  if (totalScore >= 200) return "合格";
  return "待提升";
}

function statusStep(row: ClubApplication) {
  if (row.auditStatus === "pending") return 1;
  if (row.auditStatus === "rejected") return 2;
  return 3;
}

function statusProcess(row: ClubApplication) {
  return row.auditStatus === "rejected" ? "error" : "process";
}

function roleLabel(user: UserSummary | null) {
  if (!user) return "未选择用户";
  const labels = user.roles.map(roleDisplayName).filter(Boolean);
  return labels.length > 0 ? labels.join(" / ") : "未分配角色";
}

function roleDisplayName(role: UserRoleSummary) {
  return role.clubName ? `${role.roleName} / ${role.clubName}` : role.roleName;
}

function isAdvisorCandidate(user: UserSummary) {
  const studentNo = user.studentNo?.trim() ?? "";
  if (/^\d{5}$/.test(studentNo)) return true;

  return user.roles.some((role) => {
    const code = (role.roleCode ?? "").toLowerCase();
    const name = role.roleName ?? "";
    return (
      code === "teacher" || code === "advisor" || name.includes("教师") || name.includes("老师")
    );
  });
}

function advisorOptionLabel(user: UserSummary) {
  return `${user.displayName} - ${roleLabel(user)}`;
}

function applicationAdvisorLabel(candidate: LearningTeacherCandidate) {
  return candidate.displayName.trim() || candidate.realName?.trim() || `用户 ${candidate.id}`;
}

function hasPermission(permission: string) {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("*") || permissions.includes(permission);
}

function hasScopedClubPermission(clubId: number, permission: string) {
  return (auth.value?.roles ?? []).some(
    (role) =>
      roleCoversClub(role, clubId) &&
      (role.permissions?.includes("*") || role.permissions?.includes(permission)),
  );
}

function buildSessionDisplayName(session: AuthResponse) {
  const name = session.user.realName || session.user.username;
  return session.user.studentNo ? `${name}（${session.user.studentNo}）` : name;
}

function toUserRoleSummary(role: AuthRole): UserRoleSummary {
  const clubIds = role.clubIds ?? [];
  return {
    roleCode: role.code,
    roleName: role.displayName || role.name,
    roleScope: role.scope,
    clubId: role.clubId ?? (clubIds.length === 1 ? clubIds[0] : null),
    clubIds,
    clubName: null,
  };
}

function isStudentSession(roles: AuthRole[]) {
  return roles.some((role) => (role.code ?? "").toLowerCase() === "student");
}

function goProfile() {
  activeTab.value = "profile";
}

function goMembers() {
  if (!isMemberWorkspace.value) {
    const query = selectedClubId.value ? { clubId: String(selectedClubId.value) } : undefined;
    void router.push({ path: "/club-members", query });
    return;
  }

  activeTab.value = "members";
  syncSelectedClub();
}

function openClubMembers(clubId: number) {
  if (!isMemberWorkspace.value) {
    void router.push({ path: "/club-members", query: { clubId: String(clubId) } });
    return;
  }

  selectedClubId.value = clubId;
  activeTab.value = "members";
}

function canExitIdentity(row: IdentityRow) {
  return isActiveStatus(row.memberStatus) && !isPrincipalIdentity(row);
}

function canExitMemberRow(row: ClubMemberRecord) {
  return row.isCurrent && row.userId === currentUserId.value && !isPrincipalMember(row);
}

function canRemoveMemberRow(row: ClubMemberRecord) {
  return (
    row.isCurrent &&
    row.userId !== currentUserId.value &&
    canRemoveSelectedClubMember.value &&
    !isPrincipalMember(row)
  );
}

function hasMemberRowActions(row: ClubMemberRecord) {
  return canEditMemberRow(row) || canExitMemberRow(row) || canRemoveMemberRow(row);
}

function memberExitDisabledReason(row: IdentityRow | ClubMemberRecord) {
  const status = "isCurrent" in row ? (row.isCurrent ? "active" : "ended") : row.memberStatus;
  if (!isActiveStatus(status)) return "只能处理当前有效成员身份。";
  if (isPrincipalIdentity(row)) return "负责人请先转交社团负责人后再退出或移出。";
  return "当前身份不能办理退出或移出。";
}

function isPrincipalIdentity(row: IdentityRow | ClubMemberRecord) {
  return isPrincipalPosition(row.positionName);
}

function isPrincipalMember(row: ClubMemberRecord) {
  return selectedClub.value?.presidentUserId === row.userId || isPrincipalIdentity(row);
}

function memberOptionLabel(member: ClubMemberRecord) {
  const position = member.positionName ? ` / ${member.positionName}` : "";
  return `${member.userName}${position}`;
}

function isPrincipalPosition(positionName: string | null | undefined) {
  if (!positionName) return false;
  const normalized = positionName.trim().toLowerCase();
  if (positionName.trim().startsWith("副")) return false;
  return principalPositionNames.has(normalized) || principalPositionNames.has(positionName.trim());
}

function isActiveStatus(status: string | null | undefined) {
  if (!status) return true;
  const normalized = status.trim().toLowerCase();
  return ["active", "normal", "enabled", "在任", "正常"].includes(normalized);
}

function normalizeMemberStatus(status: string | null | undefined): MemberStatus {
  if (!status) return "active";
  const normalized = status.trim().toLowerCase();
  if (["ended", "left", "finished", "离任", "已结束"].includes(normalized)) return "ended";
  if (["suspended", "paused", "disabled", "暂停", "停用"].includes(normalized)) {
    return "suspended";
  }
  return "active";
}

function uniqueTextOptions(values: Array<string | null | undefined>) {
  return Array.from(
    new Set(
      values.map((value) => value?.trim()).filter((value): value is string => Boolean(value)),
    ),
  ).sort((left, right) => left.localeCompare(right, "zh-CN"));
}

function groupOptionsForDepartment(departmentName: string | null | undefined) {
  const normalizedDepartment = departmentName?.trim() ?? "";
  return uniqueTextOptions(
    memberGroupEntries.value
      .filter((entry) => !normalizedDepartment || entry.departmentName === normalizedDepartment)
      .map((entry) => entry.groupName),
  );
}

function isMemberUnassigned(member: ClubMemberRecord) {
  return !member.departmentName?.trim() || !member.groupName?.trim();
}

watch(currentUserId, () => {
  void loadData();
});

watch(selectedClubId, () => {
  memberFilters.departmentName = "";
  memberFilters.groupName = "";
  memberFilters.termName = "";
  memberFilters.unassignedOnly = false;
  newDepartmentName.value = "";
  newGroupDepartmentName.value = "";
  newGroupName.value = "";
  evaluationFilters.termName = "";
});

watch(
  groupCreateDepartmentOptions,
  (options) => {
    if (!newGroupDepartmentName.value && options.length === 1) {
      newGroupDepartmentName.value = options[0];
      return;
    }

    if (newGroupDepartmentName.value && !options.includes(newGroupDepartmentName.value)) {
      newGroupDepartmentName.value = options[0] ?? "";
    }
  },
  { immediate: true },
);

watch(
  [
    selectedClubId,
    memberWorkspaceMode,
    () => memberFilters.termName,
    () => memberFilters.departmentName,
    () => memberFilters.groupName,
  ],
  () => {
    void loadMembers();
  },
);

watch(
  () => props.workspace,
  () => {
    activeTab.value = defaultActiveTab(props.workspace);
    applications.value = [];
    clubs.value = [];
    clubMembers.value = [];
    void loadData();
  },
);

watch(selectedClubId, () => {
  void Promise.all([loadEvaluationMembers(), loadEvaluations()]);
});

watch(
  () => evaluationFilters.termName,
  () => {
    void loadEvaluations();
  },
);

watch(
  visibleTabs,
  (tabs) => {
    if (tabs.length === 0) {
      activeTab.value = "";
      return;
    }

    if (!tabs.includes(activeTab.value)) {
      activeTab.value = tabs[0];
    }
  },
  { immediate: true },
);

onMounted(async () => {
  stopSessionListener = onSessionChange(() => {
    auth.value = readAuth();
    void Promise.all([loadUsers(), loadData()]);
  });
  await loadUsers();
  await loadData();
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <div class="page">
    <section class="toolbar">
      <div>
        <h2>{{ workspaceTitle }}</h2>
        <div class="subtitle">{{ workspaceSubtitle }}</div>
      </div>
      <div class="toolbar-actions">
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
      <div class="identity-side">
        <div
          v-if="!isRegistrationWorkspace && clubContextOptions.length > 0"
          class="identity-switch"
        >
          <span>{{ isGlobalClubGovernance ? "快速定位社团" : "当前社团" }}</span>
          <el-select
            v-model="selectedClubId"
            class="context-select"
            placeholder="选择社团身份"
            filterable
          >
            <el-option
              v-for="option in clubContextOptions"
              :key="option.clubId"
              :label="option.optionLabel"
              :value="option.clubId"
            >
              <div class="context-option">
                <strong>{{ option.clubName }}</strong>
                <span>{{ option.roleText }} · {{ option.statusText }}</span>
              </div>
            </el-option>
          </el-select>
          <small v-if="selectedClubContext">{{ selectedClubContext.statusText }}</small>
        </div>
        <div class="identity-tags">
          <el-tag
            v-if="isRegistrationWorkspace && currentUser.canSubmitClubApplication"
            type="success"
            effect="plain"
          >
            可提交注册申请
          </el-tag>
          <el-tag
            v-if="isRegistrationWorkspace && currentUser.canReviewClubApplication"
            type="warning"
            effect="plain"
          >
            可审核注册申请
          </el-tag>
          <el-tag v-if="isClubWorkspace && clubInfoRows.length > 0" effect="plain">
            可查看社团信息
          </el-tag>
          <el-tag v-if="isClubWorkspace && profileRows.length > 0" type="primary" effect="plain">
            可维护社团档案
          </el-tag>
          <el-tag v-if="isMemberWorkspace && memberViewClubs.length > 0" effect="plain">
            可查看成员任期
          </el-tag>
          <el-tag v-if="isClubWorkspace && identityRows.length > 0" effect="plain">
            我的社团身份
          </el-tag>
        </div>
        <div class="identity-actions">
          <el-button
            v-if="isClubWorkspace && clubInfoRows.length > 0"
            type="primary"
            plain
            @click="goProfile"
          >
            查看社团
          </el-button>
          <el-button
            v-if="isMemberWorkspace && memberViewClubs.length > 0"
            plain
            @click="goMembers"
          >
            查看任期
          </el-button>
        </div>
      </div>
    </section>

    <el-empty
      v-if="!hasClubWorkspace"
      :description="workspaceEmptyDescription"
      class="empty-workspace"
    />

    <el-tabs v-else v-model="activeTab" class="workspace-tabs">
      <el-tab-pane
        v-if="isRegistrationWorkspace && (canSubmitApplication || isReviewer)"
        label="当前工作台"
        name="workspace"
      >
        <div class="workspace-head">
          <div>
            <h3>{{ isReviewer ? "申请审核池" : "我的注册申请" }}</h3>
            <p>
              {{
                isReviewer
                  ? "平台管理员处理社团注册申请，审核通过后社团进入运营状态。"
                  : "学生提交注册申请后，可以在这里查看审核状态和意见。"
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
          :data="applicationRows"
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
                <el-descriptions :column="2" border>
                  <el-descriptions-item label="申请理由">
                    {{ row.applyReason }}
                  </el-descriptions-item>
                  <el-descriptions-item label="拟选指导老师">
                    {{ row.advisorName || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="材料地址">
                    {{ row.materialUrl }}
                  </el-descriptions-item>
                  <el-descriptions-item label="社团简介">
                    {{ row.description || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="成立时间">
                    {{ formatDate(row.foundedAt) }}
                  </el-descriptions-item>
                </el-descriptions>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="name" label="社团名称" min-width="160" />
          <el-table-column prop="category" label="类别" width="120" />
          <el-table-column prop="applicantName" label="申请人" width="130" />
          <el-table-column prop="advisorName" label="指导老师" width="130">
            <template #default="{ row }">{{ row.advisorName || "-" }}</template>
          </el-table-column>
          <el-table-column label="审核状态" width="120">
            <template #default="{ row }">
              <el-tag :type="auditTagType(row.auditStatus)" effect="plain">
                {{ row.auditStatusText }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="社团状态" width="120">
            <template #default="{ row }">
              <el-tag :type="clubTagType(row.clubStatus)" effect="plain">
                {{ row.clubStatusText }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="更新时间" width="170">
            <template #default="{ row }">{{ formatDate(row.updatedAt || row.createdAt) }}</template>
          </el-table-column>
          <el-table-column v-if="isReviewer" label="操作" width="150" fixed="right">
            <template #default="{ row }">
              <el-button
                v-if="row.auditStatus === 'pending'"
                type="primary"
                plain
                :icon="Edit"
                @click="openReviewDialog(row)"
              >
                审核
              </el-button>
              <span v-else class="muted">已处理</span>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane
        v-if="isClubWorkspace && visibleClubInfoRows.length > 0"
        label="社团信息"
        name="profile"
      >
        <div class="workspace-head">
          <div>
            <h3>{{ selectedClub?.name || "社团治理" }}</h3>
            <p>
              {{
                selectedClub
                  ? "当前社团的基础档案、负责人、指导老师和备案信息。"
                  : isGlobalClubGovernance
                    ? "查看全校社团档案，处理社团运营状态。"
                    : "当前社团的基础档案、负责人、指导老师和备案信息。"
              }}
            </p>
          </div>
          <el-button :icon="Refresh" @click="loadData">刷新</el-button>
        </div>

        <el-table
          v-if="isGlobalClubGovernance"
          v-loading="loading"
          :data="visibleClubInfoRows"
          border
          stripe
          empty-text="暂无可见社团"
          row-key="id"
        >
          <el-table-column type="expand">
            <template #default="{ row }">
              <div class="club-detail compact">
                <el-descriptions :column="2" border>
                  <el-descriptions-item label="社团简介" :span="2">
                    {{ row.description || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="Logo 地址">
                    {{ row.logoUrl || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="申请材料">
                    {{ row.materialUrl || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="申请人">
                    {{ row.applicantName || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="审核人">
                    {{ row.reviewerName || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="审核意见" :span="2">
                    {{ row.reviewComment || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="成立时间">
                    {{ formatDate(row.foundedAt) }}
                  </el-descriptions-item>
                  <el-descriptions-item label="创建时间">
                    {{ formatDate(row.createdAt) }}
                  </el-descriptions-item>
                </el-descriptions>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="name" label="社团名称" min-width="160" />
          <el-table-column prop="category" label="类别" width="120" />
          <el-table-column label="社团状态" width="120">
            <template #default="{ row }">
              <el-tag :type="clubTagType(row.status)" effect="plain">{{ row.statusText }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="presidentName" label="负责人" width="130" />
          <el-table-column prop="advisorName" label="指导老师" width="130" />
          <el-table-column prop="contactPhone" label="联系电话" width="150" />
          <el-table-column label="更新时间" width="170">
            <template #default="{ row }">{{ formatDate(row.updatedAt || row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="220" fixed="right">
            <template #default="{ row }">
              <div class="row-actions">
                <el-button
                  v-if="canManageClub(row)"
                  type="primary"
                  plain
                  :icon="Edit"
                  @click="openProfileDialog(row)"
                >
                  维护
                </el-button>
                <el-button
                  v-if="canDissolveClub(row)"
                  type="danger"
                  plain
                  :icon="DeleteIcon"
                  :loading="dissolvingClubId === row.id"
                  @click="dissolveClub(row)"
                >
                  解散
                </el-button>
                <span v-if="!canManageClub(row) && !canDissolveClub(row)" class="muted">
                  仅查看
                </span>
              </div>
            </template>
          </el-table-column>
        </el-table>

        <div v-else-if="selectedClub" class="club-detail">
          <div class="club-detail-header">
            <div>
              <h4>{{ selectedClub.name }}</h4>
              <div class="club-tags">
                <el-tag :type="clubTagType(selectedClub.status)" effect="plain">
                  {{ selectedClub.statusText }}
                </el-tag>
                <el-tag effect="plain">{{ selectedClub.category || "未设置类别" }}</el-tag>
                <el-tag :type="auditTagType(selectedClub.auditStatus)" effect="plain">
                  {{ selectedClub.auditStatusText }}
                </el-tag>
              </div>
            </div>
            <el-button
              v-if="canManageSelectedClub"
              type="primary"
              plain
              :icon="Edit"
              @click="openProfileDialog(selectedClub)"
            >
              维护档案
            </el-button>
          </div>

          <el-descriptions :column="2" border>
            <el-descriptions-item label="社团名称">
              {{ selectedClub.name }}
            </el-descriptions-item>
            <el-descriptions-item label="类别">
              {{ selectedClub.category || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="负责人">
              {{ selectedClub.presidentName || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="指导老师">
              {{ selectedClub.advisorName || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="联系电话">
              {{ selectedClub.contactPhone || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="成立时间">
              {{ formatDate(selectedClub.foundedAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="社团简介" :span="2">
              {{ selectedClub.description || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="Logo 地址">
              {{ selectedClub.logoUrl || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="申请材料">
              {{ selectedClub.materialUrl || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="申请理由" :span="2">
              {{ selectedClub.applyReason || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="审核意见" :span="2">
              {{ selectedClub.reviewComment || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="创建时间">
              {{ formatDate(selectedClub.createdAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="更新时间">
              {{ formatDate(selectedClub.updatedAt || selectedClub.createdAt) }}
            </el-descriptions-item>
          </el-descriptions>
        </div>
      </el-tab-pane>

      <el-tab-pane
        v-if="isMemberWorkspace && memberViewClubs.length > 0"
        label="成员分组与任期"
        name="members"
      >
        <el-empty v-if="memberViewClubs.length === 0" description="当前账号暂无可查看的社团任期" />

        <div v-else>
          <div class="member-head">
            <div class="member-controls">
              <el-select
                v-model="selectedClubId"
                class="club-selector"
                placeholder="选择社团"
                filterable
              >
                <el-option
                  v-for="club in memberViewClubs"
                  :key="club.id"
                  :label="club.name"
                  :value="club.id"
                />
              </el-select>
              <el-segmented
                v-model="memberWorkspaceMode"
                :options="[
                  { label: '当前名册', value: 'current' },
                  { label: '任期历史', value: 'history' },
                  { label: '换届管理', value: 'transition' },
                ]"
              />
            </div>
            <el-button
              v-if="canManageSelectedClub && memberWorkspaceMode === 'current'"
              type="primary"
              :icon="Plus"
              @click="openCreateMemberTermDialog"
            >
              新增任期
            </el-button>
          </div>

          <div class="member-filter-row">
            <el-select
              v-if="memberWorkspaceMode === 'history'"
              v-model="memberFilters.termName"
              class="filter-item"
              clearable
              placeholder="按届筛选"
            >
              <el-option
                v-for="term in memberTermFilterOptions"
                :key="term"
                :label="term"
                :value="term"
              />
            </el-select>
            <el-select
              v-model="memberFilters.departmentName"
              class="filter-item"
              clearable
              :disabled="memberFilters.unassignedOnly"
              placeholder="按部门筛选"
            >
              <el-option
                v-for="department in memberDepartmentOptions"
                :key="department"
                :label="department"
                :value="department"
              />
            </el-select>
            <el-select
              v-model="memberFilters.groupName"
              class="filter-item"
              clearable
              :disabled="memberFilters.unassignedOnly"
              placeholder="按小组筛选"
            >
              <el-option
                v-for="group in memberGroupOptions"
                :key="group"
                :label="group"
                :value="group"
              />
            </el-select>
            <el-checkbox
              v-model="memberFilters.unassignedOnly"
              @change="toggleUnassignedMemberFilter"
            >
              待分组
            </el-checkbox>
            <el-button :icon="Refresh" @click="clearMemberFilters">清除筛选</el-button>
          </div>

          <div class="member-summary">
            <span>当前列表 {{ memberGroupSummary.total }} 条</span>
            <span>有效任期 {{ memberGroupSummary.current }} 条</span>
            <span>部门 {{ memberGroupSummary.departments }} 个</span>
            <span>小组 {{ memberGroupSummary.groups }} 个</span>
            <span>待分组 {{ memberGroupSummary.unassigned }} 条</span>
            <div v-if="canCreateAcademicTerm" class="taxonomy-add term-add">
              <el-input-number
                v-model="newAcademicTermStartYear"
                size="small"
                :min="2000"
                :max="2100"
                :step="1"
                :precision="0"
                controls-position="right"
              />
              <el-button
                size="small"
                type="primary"
                plain
                :icon="Plus"
                @click="addAcademicTermOption"
              >
                新增学年
              </el-button>
            </div>
            <div v-if="canCreateMemberDepartment" class="taxonomy-add">
              <el-input
                v-model="newDepartmentName"
                size="small"
                maxlength="60"
                placeholder="新增部门"
                @keyup.enter="addMemberDepartmentOption"
              />
              <el-button
                size="small"
                type="primary"
                plain
                :icon="Plus"
                @click="addMemberDepartmentOption"
              >
                新增部门
              </el-button>
            </div>
            <div v-if="canCreateMemberGroup" class="taxonomy-add group-add">
              <el-select
                v-model="newGroupDepartmentName"
                size="small"
                placeholder="所属部门"
                :disabled="!canManageSelectedClub && groupCreateDepartmentOptions.length <= 1"
              >
                <el-option
                  v-for="department in groupCreateDepartmentOptions"
                  :key="department"
                  :label="department"
                  :value="department"
                />
              </el-select>
              <el-input
                v-model="newGroupName"
                size="small"
                maxlength="60"
                placeholder="新增小组"
                @keyup.enter="addMemberGroupOption"
              />
              <el-button
                size="small"
                type="primary"
                plain
                :icon="Plus"
                @click="addMemberGroupOption"
              >
                新增小组
              </el-button>
            </div>
          </div>
        </div>

        <el-table
          v-if="memberViewClubs.length > 0 && memberWorkspaceMode !== 'transition'"
          v-loading="memberLoading"
          :data="memberTableRows"
          border
          stripe
          :empty-text="
            memberWorkspaceMode === 'current'
              ? canManageSelectedClub
                ? '暂无当前名册，可新增成员任期'
                : '暂无当前有效成员任期'
              : '暂无历史任期记录'
          "
          row-key="memberId"
        >
          <el-table-column prop="userName" label="成员" min-width="150" />
          <el-table-column prop="studentNo" label="学号/工号" width="130" />
          <el-table-column label="部门" width="150">
            <template #default="{ row }">
              <span v-if="row.departmentName">{{ row.departmentName }}</span>
              <el-tag v-else type="warning" effect="plain">待分配</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="小组" width="140">
            <template #default="{ row }">
              <span v-if="row.groupName">{{ row.groupName }}</span>
              <el-tag v-else type="warning" effect="plain">待分配</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="职位" width="150">
            <template #default="{ row }">
              <span>{{ row.positionName || "-" }}</span>
            </template>
          </el-table-column>
          <el-table-column label="任期" min-width="170">
            <template #default="{ row }">
              <span>{{ row.termName || "-" }}</span>
            </template>
          </el-table-column>
          <el-table-column label="任期开始" width="130">
            <template #default="{ row }">{{ formatDateOnly(row.termStart) }}</template>
          </el-table-column>
          <el-table-column label="任期结束" width="130">
            <template #default="{ row }">{{ formatDateOnly(row.termEnd) }}</template>
          </el-table-column>
          <el-table-column label="状态" width="130">
            <template #default="{ row }">
              <el-tag :type="memberRecordStatusTagType(row)" effect="plain">
                {{ memberRecordStatusText(row) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="当前" width="100">
            <template #default="{ row }">
              <el-tag :type="memberTermPhaseTagType(row)" effect="plain">
                {{ memberTermPhaseText(row) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="contributionScore" label="贡献分" width="100" />
          <el-table-column
            v-if="canShowMemberOperationColumn"
            label="操作"
            width="220"
            fixed="right"
          >
            <template #default="{ row }">
              <div class="row-actions member-row-actions">
                <el-button
                  v-if="canEditMemberRow(row)"
                  type="primary"
                  plain
                  :icon="Edit"
                  @click="openMemberEditDialog(row)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="canExitMemberRow(row)"
                  type="danger"
                  plain
                  :icon="DeleteIcon"
                  :loading="exitingClubId === row.clubId"
                  @click="exitCurrentClub(row)"
                >
                  退出
                </el-button>
                <el-button
                  v-if="canRemoveMemberRow(row)"
                  type="danger"
                  plain
                  :icon="DeleteIcon"
                  :loading="exitingMemberId === row.memberId"
                  @click="removeClubMember(row)"
                >
                  移出
                </el-button>
                <span v-if="!hasMemberRowActions(row)" class="muted">仅查看</span>
              </div>
            </template>
          </el-table-column>
        </el-table>

        <div v-else class="transition-panel">
          <div class="transition-toolbar">
            <div>
              <h3>换届管理</h3>
              <p>
                选择目标学年后，系统只把已到期且尚未生成新届任期的成员放入暂存区，再由负责人或指导老师处理续任与职务调整。
              </p>
            </div>
            <div class="transition-actions">
              <el-select
                v-model="transitionTermForm.label"
                class="filter-item"
                @change="applyAcademicTermToTransition"
              >
                <el-option
                  v-for="term in academicTermOptions"
                  :key="term.label"
                  :label="term.label"
                  :value="term.label"
                />
              </el-select>
              <el-tag effect="plain">
                {{ transitionTermForm.termStart }} 至 {{ transitionTermForm.termEnd }}
              </el-tag>
            </div>
          </div>

          <div class="transition-steps">
            <span>1. 选择换届学年</span>
            <span>2. 查看待换届暂存区</span>
            <span>3. 逐人确认续任或职务调整</span>
          </div>

          <el-table
            v-loading="memberLoading"
            :data="transitionSourceRows"
            border
            stripe
            empty-text="暂无到期待换届成员"
            row-key="memberId"
          >
            <el-table-column prop="userName" label="成员" min-width="150" />
            <el-table-column prop="departmentName" label="当前部门" width="150" />
            <el-table-column prop="groupName" label="当前小组" width="140" />
            <el-table-column prop="positionName" label="当前职务" width="150" />
            <el-table-column prop="termName" label="当前任期" min-width="160" />
            <el-table-column label="当前任期时间" width="230">
              <template #default="{ row }">
                {{ formatDateOnly(row.termStart) }} 至 {{ formatDateOnly(row.termEnd) }}
              </template>
            </el-table-column>
            <el-table-column
              v-if="canManageSelectedClub"
              label="换届操作"
              width="150"
              fixed="right"
            >
              <template #default="{ row }">
                <el-button
                  type="primary"
                  plain
                  :icon="Edit"
                  @click="openTransitionMemberTermDialog(row)"
                >
                  处理换届
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-tab-pane>

      <el-tab-pane
        v-if="showLegacyEvaluationTab && evaluationViewClubs.length > 0"
        label="成员考核"
        name="evaluations"
      >
        <div class="member-head">
          <div class="member-controls">
            <el-select
              v-model="selectedClubId"
              class="club-selector"
              placeholder="选择社团"
              filterable
            >
              <el-option
                v-for="club in evaluationViewClubs"
                :key="club.id"
                :label="club.name"
                :value="club.id"
              />
            </el-select>
            <el-select
              v-model="evaluationFilters.termName"
              class="filter-item"
              clearable
              filterable
              allow-create
              default-first-option
              placeholder="考核学期"
            >
              <el-option
                v-for="term in evaluationTermOptions"
                :key="term"
                :label="term"
                :value="term"
              />
            </el-select>
            <el-button :icon="Refresh" @click="clearEvaluationFilters">清除筛选</el-button>
          </div>
          <el-button
            v-if="canMaintainSelectedEvaluations"
            type="primary"
            :icon="Plus"
            @click="openCreateEvaluationDialog"
          >
            录入评价
          </el-button>
        </div>

        <div class="member-summary">
          <span>考核记录 {{ evaluationSummary.total }} 条</span>
          <span>已公示 {{ evaluationSummary.published }} 条</span>
          <span>平均总分 {{ evaluationSummary.average }}</span>
          <span>可评价成员 {{ evaluationTargetOptions.length }} 人</span>
        </div>

        <el-table
          v-loading="evaluationLoading"
          :data="evaluations"
          border
          stripe
          empty-text="暂无成员考核记录"
          row-key="evaluationId"
        >
          <el-table-column type="expand">
            <template #default="{ row }">
              <div class="application-detail">
                <el-descriptions :column="2" border>
                  <el-descriptions-item label="成员">
                    {{ row.userName }}（{{ row.studentNo || "-" }}）
                  </el-descriptions-item>
                  <el-descriptions-item label="评价人">
                    {{ row.evaluatorName || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="部门/小组">
                    {{ row.departmentName || "-" }} / {{ row.groupName || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="职位">
                    {{ row.positionName || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="评价说明" :span="2">
                    {{ row.commentText || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item label="创建时间">
                    {{ formatDate(row.createdAt) }}
                  </el-descriptions-item>
                </el-descriptions>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="userName" label="成员" min-width="150" />
          <el-table-column prop="termName" label="学期" min-width="150" />
          <el-table-column prop="activityScore" label="参与分" width="100" />
          <el-table-column prop="taskScore" label="任务分" width="100" />
          <el-table-column prop="learningScore" label="学习分" width="100" />
          <el-table-column prop="awardScore" label="奖项分" width="100" />
          <el-table-column prop="totalScore" label="总分" width="100" />
          <el-table-column label="等级" width="110">
            <template #default="{ row }">
              <el-tag :type="evaluationGradeTagType(row.grade)" effect="plain">
                {{ row.grade }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="公示" width="110">
            <template #default="{ row }">
              <el-tag :type="evaluationPublicTagType(row.publicStatus)" effect="plain">
                {{ row.publicStatusText }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button
                v-if="canMaintainEvaluationRecord(row)"
                type="primary"
                plain
                :icon="Edit"
                @click="openEditEvaluationDialog(row)"
              >
                编辑
              </el-button>
              <span v-else class="muted">仅查看</span>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane
        v-if="isClubWorkspace && visibleIdentityRows.length > 0"
        label="我的社团身份"
        name="identity"
      >
        <el-table :data="visibleIdentityRows" border stripe empty-text="暂无社团成员身份">
          <el-table-column prop="clubName" label="社团" min-width="160" />
          <el-table-column prop="departmentName" label="部门" width="130" />
          <el-table-column prop="groupName" label="小组" width="120" />
          <el-table-column prop="positionName" label="职位" width="130" />
          <el-table-column prop="termName" label="任期" min-width="150" />
          <el-table-column label="状态" width="120">
            <template #default="{ row }">
              <el-tag :type="memberStatusTagType(row.memberStatus)" effect="plain">
                {{ memberStatusText(row.memberStatus) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="210" fixed="right">
            <template #default="{ row }">
              <el-button type="primary" plain :icon="Search" @click="openClubMembers(row.clubId)">
                查看
              </el-button>
              <el-button
                v-if="canExitIdentity(row)"
                type="danger"
                plain
                :icon="DeleteIcon"
                :loading="exitingClubId === row.clubId"
                @click="exitCurrentClub(row)"
              >
                退出社团
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog
      v-if="isRegistrationWorkspace"
      v-model="applicationDialogVisible"
      title="提交社团注册申请"
      width="620px"
    >
      <el-form
        ref="applicationFormRef"
        :model="applicationForm"
        :rules="applicationRules"
        label-width="100px"
      >
        <el-form-item label="社团名称" prop="name">
          <el-input v-model="applicationForm.name" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="社团类别" prop="category">
          <el-input v-model="applicationForm.category" maxlength="40" show-word-limit />
        </el-form-item>
        <el-form-item label="指导老师">
          <el-select
            v-model="applicationForm.advisorUserId"
            :loading="applicationAdvisorLoading"
            clearable
            filterable
            placeholder="选择拟邀请指导老师（可选）"
            no-data-text="暂无可选指导老师"
          >
            <el-option
              v-for="candidate in applicationAdvisorCandidates"
              :key="candidate.id"
              :label="applicationAdvisorLabel(candidate)"
              :value="candidate.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="社团简介">
          <el-input
            v-model="applicationForm.description"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="申请理由" prop="applyReason">
          <el-input
            v-model="applicationForm.applyReason"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="材料地址" prop="materialUrl">
          <el-input
            v-model="applicationForm.materialUrl"
            placeholder="线上材料链接或校内存档地址"
          />
        </el-form-item>
        <el-form-item label="联系电话">
          <el-input v-model="applicationForm.contactPhone" maxlength="30" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="applicationDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitApplication">提交申请</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-if="isRegistrationWorkspace"
      v-model="reviewDialogVisible"
      title="审核社团注册申请"
      width="560px"
    >
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="90px">
        <el-form-item label="社团">
          <el-input :model-value="reviewTarget?.name" disabled />
        </el-form-item>
        <el-form-item label="指导老师">
          <el-input :model-value="reviewTarget?.advisorName || '-'" disabled />
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
            :rows="4"
            maxlength="500"
            show-word-limit
            placeholder="退回时必须填写，通过时可填写补充意见"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reviewDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="reviewing" @click="submitReview"
          >保存审核结果</el-button
        >
      </template>
    </el-dialog>

    <el-dialog v-model="profileDialogVisible" title="维护社团档案" width="660px">
      <el-form ref="profileFormRef" :model="profileForm" :rules="profileRules" label-width="100px">
        <el-form-item label="社团名称" prop="name">
          <el-input v-model="profileForm.name" maxlength="80" show-word-limit />
        </el-form-item>
        <el-form-item label="社团类别" prop="category">
          <el-input v-model="profileForm.category" maxlength="40" show-word-limit />
        </el-form-item>
        <el-form-item label="社团简介">
          <el-input
            v-model="profileForm.description"
            type="textarea"
            :rows="4"
            maxlength="800"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="Logo 地址">
          <el-input v-model="profileForm.logoUrl" />
        </el-form-item>
        <el-form-item label="负责人">
          <el-select
            v-model="profileForm.presidentUserId"
            clearable
            filterable
            placeholder="从当前有效成员中选择"
          >
            <el-option
              v-for="member in activePresidentOptions"
              :key="member.memberId"
              :label="memberOptionLabel(member)"
              :value="member.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="指导老师">
          <el-select
            v-model="profileForm.advisorUserId"
            :loading="dialogUsersLoading"
            clearable
            filterable
            placeholder="选择教师账号"
          >
            <el-option
              v-for="user in advisorOptions"
              :key="user.id"
              :label="advisorOptionLabel(user)"
              :value="user.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="联系电话">
          <el-input v-model="profileForm.contactPhone" maxlength="30" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="profileDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="profileSaving" @click="submitProfile"
          >保存档案</el-button
        >
      </template>
    </el-dialog>

    <el-dialog v-model="memberGroupingDialogVisible" :title="groupingDialogTitle()" width="520px">
      <el-alert
        v-if="memberGroupingMode === 'own'"
        class="dialog-alert"
        type="info"
        show-icon
        :closable="false"
        title="干部只能将成员纳入自己所在的小组，不能调整到其他部门或小组。"
      />
      <el-alert
        v-else-if="memberGroupingMode === 'department'"
        class="dialog-alert"
        type="info"
        show-icon
        :closable="false"
        title="部长只能在自己所在部门内选择小组。"
      />
      <el-form label-width="90px">
        <el-form-item label="成员">
          <el-input :model-value="memberGroupingTarget?.userName" disabled />
        </el-form-item>
        <el-form-item label="部门">
          <el-select
            v-model="memberGroupingForm.departmentName"
            class="full-width"
            :disabled="memberGroupingMode !== 'free'"
            placeholder="选择部门"
            @change="handleMemberGroupingDepartmentChange"
          >
            <el-option
              v-for="department in memberDepartmentOptions"
              :key="department"
              :label="department"
              :value="department"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="小组">
          <el-select
            v-model="memberGroupingForm.groupName"
            :disabled="memberGroupingMode === 'own'"
            class="full-width"
            clearable
            placeholder="选择小组"
          >
            <el-option
              v-for="group in memberGroupingGroupOptions"
              :key="group"
              :label="group"
              :value="group"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="memberGroupingDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="groupingSaving" @click="submitMemberGrouping">
          保存
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-if="isMemberWorkspace"
      v-model="memberTermDialogVisible"
      :title="
        memberWorkspaceMode === 'transition' && memberTermMode === 'create'
          ? '处理换届任期'
          : memberTermMode === 'create'
            ? '新增成员任期'
            : '编辑成员信息'
      "
      width="660px"
    >
      <el-form
        ref="memberTermFormRef"
        :model="memberTermForm"
        :rules="memberTermRules"
        label-width="100px"
      >
        <el-form-item label="社团">
          <el-input :model-value="selectedClub?.name" disabled />
        </el-form-item>
        <el-form-item v-if="memberTermMode === 'create'" label="成员" prop="userId">
          <el-select
            v-model="memberTermForm.userId"
            :loading="dialogUsersLoading"
            :disabled="memberTermUserLocked"
            filterable
            placeholder="选择用户"
          >
            <el-option
              v-for="user in memberTermUserOptions"
              :key="user.id"
              :label="user.displayName"
              :value="user.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-else label="成员">
          <el-input :model-value="memberTermTarget?.userName" disabled />
        </el-form-item>
        <el-form-item label="部门">
          <el-select
            v-model="memberTermForm.departmentName"
            class="full-width"
            clearable
            placeholder="选择部门"
            @change="handleMemberTermDepartmentChange"
          >
            <el-option
              v-for="department in memberDepartmentOptions"
              :key="department"
              :label="department"
              :value="department"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="小组">
          <el-select
            v-model="memberTermForm.groupName"
            class="full-width"
            clearable
            placeholder="选择小组"
          >
            <el-option
              v-for="group in groupOptionsForDepartment(memberTermForm.departmentName)"
              :key="group"
              :label="group"
              :value="group"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="职位" prop="positionName">
          <el-input v-model="memberTermForm.positionName" maxlength="60" />
        </el-form-item>
        <el-form-item label="任期名称" prop="termName">
          <el-select
            v-model="memberTermForm.termName"
            filterable
            class="full-width"
            @change="applyAcademicTermToMemberForm"
          >
            <el-option
              v-for="term in memberTermSelectOptions"
              :key="term.label"
              :label="term.label"
              :value="term.label"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="任期开始" prop="termStart">
          <el-date-picker
            v-model="memberTermForm.termStart"
            type="date"
            value-format="YYYY-MM-DD"
            disabled
            class="full-width"
          />
        </el-form-item>
        <el-form-item label="任期结束">
          <el-date-picker
            v-model="memberTermForm.termEnd"
            type="date"
            value-format="YYYY-MM-DD"
            disabled
            class="full-width"
          />
        </el-form-item>
        <el-form-item label="成员状态">
          <el-radio-group v-model="memberTermForm.memberStatus">
            <el-radio-button label="active">在任</el-radio-button>
            <el-radio-button label="ended">已结束</el-radio-button>
            <el-radio-button label="suspended">暂停</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="贡献分">
          <el-input-number v-model="memberTermForm.contributionScore" :min="0" :precision="1" />
        </el-form-item>
        <el-form-item
          v-if="memberTermMode === 'create' && memberWorkspaceMode !== 'transition'"
          label="换届处理"
        >
          <el-switch
            v-model="memberTermForm.closeCurrentTerm"
            active-text="关闭原有效任期"
            inactive-text="保留并行任期"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="memberTermDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="termSaving" @click="submitMemberTerm"> 保存 </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="evaluationDialogVisible"
      :title="evaluationMode === 'create' ? '录入评价考核' : '编辑评价考核'"
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
        <el-form-item v-if="evaluationMode === 'create'" label="成员" prop="userId">
          <el-select v-model="evaluationForm.userId" filterable placeholder="选择被评价成员">
            <el-option
              v-for="member in evaluationTargetOptions"
              :key="member.memberId"
              :label="memberOptionLabel(member)"
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
          <el-form-item label="参与分">
            <el-input-number
              v-model="evaluationForm.activityScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="任务分">
            <el-input-number
              v-model="evaluationForm.taskScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="学习分">
            <el-input-number
              v-model="evaluationForm.learningScore"
              :min="0"
              :max="100"
              :precision="1"
            />
          </el-form-item>
          <el-form-item label="奖项分">
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
          <el-tag :type="evaluationGradeTagType(evaluationFormGrade)" effect="plain">
            {{ evaluationFormGrade }}
          </el-tag>
        </div>

        <el-form-item label="公示状态">
          <el-radio-group v-model="evaluationForm.publicStatus">
            <el-radio-button label="draft">草稿</el-radio-button>
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
        <el-button type="primary" :loading="evaluationSaving" @click="submitEvaluation">
          保存评价
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
.identity-band,
.workspace-tabs {
  border: 1px solid #d9e1ea;
  background: #fff;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 18px 20px;
}

.toolbar h2,
.workspace-head h3 {
  margin: 0;
  font-weight: 650;
}

.subtitle,
.workspace-head p,
.muted {
  color: #66727f;
}

.subtitle {
  margin-top: 6px;
  font-size: 14px;
}

.toolbar-actions,
.member-head,
.member-controls,
.member-filter-row,
.filter-bar {
  display: flex;
  align-items: center;
  gap: 12px;
}

.identity-band {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 18px;
}

.identity-main {
  display: flex;
  align-items: center;
  gap: 10px;
}

.identity-main strong {
  display: block;
}

.identity-main span {
  display: block;
  margin-top: 2px;
  color: #66727f;
  font-size: 13px;
}

.identity-side {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
}

.identity-switch {
  display: grid;
  gap: 4px;
  min-width: 260px;
}

.identity-switch span,
.identity-switch small,
.context-option span {
  color: #66727f;
  font-size: 12px;
}

.context-select {
  width: 280px;
}

.context-option {
  display: grid;
  gap: 2px;
  line-height: 1.3;
}

.identity-tags,
.identity-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.workspace-tabs {
  padding: 0 18px 18px;
}

.empty-workspace {
  border: 1px solid #d9e1ea;
  background: #fff;
  padding: 48px 0;
}

.workspace-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 18px 0 14px;
}

.workspace-head p {
  margin: 6px 0 0;
  font-size: 14px;
}

.filter-bar {
  margin-bottom: 14px;
}

.filter-item {
  width: 180px;
}

.application-detail {
  display: grid;
  gap: 16px;
  padding: 12px;
}

.club-detail {
  display: grid;
  gap: 16px;
  padding: 4px 0 10px;
}

.club-detail.compact {
  padding: 12px;
}

.club-detail-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.club-detail-header h4 {
  margin: 0 0 10px;
  font-size: 20px;
  font-weight: 650;
}

.club-tags,
.row-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.row-actions {
  align-items: center;
}

.member-head {
  justify-content: space-between;
  padding: 18px 0 14px;
}

.member-filter-row {
  padding-bottom: 12px;
}

.member-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  padding: 0 0 14px;
  color: #66727f;
  font-size: 13px;
}

.member-summary span {
  border: 1px solid #d9e1ea;
  padding: 4px 8px;
}

.taxonomy-add {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.taxonomy-add .el-input,
.taxonomy-add .el-select,
.taxonomy-add .el-input-number {
  width: 132px;
}

.taxonomy-add.term-add .el-input-number {
  width: 128px;
}

.taxonomy-add.group-add .el-select {
  width: 150px;
}

.transition-panel {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.transition-toolbar {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.transition-toolbar h3 {
  margin: 0 0 6px;
}

.transition-toolbar p {
  margin: 0;
  color: #66727f;
}

.transition-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 10px;
}

.transition-steps {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.transition-steps span {
  border: 1px solid var(--el-color-primary-light-5);
  padding: 5px 9px;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
}

.score-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 12px;
}

.evaluation-preview {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 0 18px 100px;
  color: #20262e;
  font-weight: 600;
}

.club-selector {
  width: 280px;
}

.dialog-alert {
  margin-bottom: 16px;
}

:deep(.el-dialog__body .el-select),
:deep(.el-dialog__body .el-date-editor) {
  width: 100%;
}

@media (max-width: 900px) {
  .page {
    padding: 14px;
  }

  .toolbar,
  .identity-band,
  .workspace-head,
  .member-head,
  .transition-toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .toolbar-actions,
  .identity-side,
  .member-controls,
  .member-filter-row,
  .filter-bar,
  .taxonomy-add,
  .transition-actions {
    align-items: stretch;
    flex-direction: column;
  }

  .taxonomy-add .el-input,
  .taxonomy-add .el-select,
  .taxonomy-add .el-input-number,
  .taxonomy-add.group-add .el-select {
    width: 100%;
  }

  .identity-tags,
  .identity-actions {
    justify-content: flex-start;
  }

  .club-selector,
  .context-select,
  .filter-item {
    width: 100%;
  }

  .club-detail-header {
    align-items: stretch;
    flex-direction: column;
  }

  .score-grid {
    grid-template-columns: 1fr;
  }

  .evaluation-preview {
    padding-left: 0;
  }
}
</style>

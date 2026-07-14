<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import {
  Check,
  Close,
  Delete as DeleteIcon,
  Edit,
  Minus,
  Plus,
  Rank,
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
  type MemberGroupingScope,
} from "../composables/useClubEvaluationScope";
import { hasScopedRole, roleCoversClub } from "../composables/useManageableClubs";

type AuditStatus = "pending" | "approved" | "rejected";
type ReviewDecision = "approved" | "rejected";
type MemberStatus = "active" | "ended" | "suspended";
type TermMode = "create" | "edit";
type GroupingMode = "free" | "own" | "department";
type GroupingField = "departmentName" | "groupName";

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
  contactPhone: string | null;
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
  departmentId: number | null;
  departmentName: string | null;
  groupId: number | null;
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

interface ClubContextOption {
  clubId: number;
  clubName: string;
  roleText: string;
  statusText: string;
  optionLabel: string;
  canManage: boolean;
}

interface ClubGroupRecord {
  groupId: number;
  clubId: number;
  departmentId: number;
  groupName: string;
  groupCode: string | null;
  description: string | null;
  responsibilities: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
  activityLocation: string | null;
  displayOrder: number;
  groupStatus: "active" | "inactive";
  createdAt: string;
  updatedAt: string;
}

interface ClubDepartmentRecord {
  departmentId: number;
  clubId: number;
  departmentName: string;
  departmentCode: string | null;
  description: string | null;
  responsibilities: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
  officeLocation: string | null;
  displayOrder: number;
  departmentStatus: "active" | "inactive";
  createdAt: string;
  updatedAt: string;
  groups: ClubGroupRecord[];
}

interface OrganizationSelectionForm {
  departmentId: number | undefined;
  departmentName: string;
  groupId: number | undefined;
  groupName: string;
}

type ClubWorkspace = "club" | "members" | "registration" | "organization";
type MemberWorkspaceMode = "current" | "history" | "transition";
type MemberSortMode = "organization" | "studentNo" | "term";

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
const defaultMemberPositionOptions = ["社员", "干事", "组长", "副组长", "部长", "副部长"];
const memberSortCollator = new Intl.Collator("zh-CN", {
  numeric: true,
  sensitivity: "base",
});
const memberSortOptions: Array<{ label: string; value: MemberSortMode }> = [
  { label: "按部门小组", value: "organization" },
  { label: "按学号", value: "studentNo" },
  { label: "按任期", value: "term" },
];

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
const clubDepartments = ref<ClubDepartmentRecord[]>([]);
const manualAcademicTermOptions = ref<AcademicTermOption[]>([]);
const loading = ref(true);
const usersLoading = ref(true);
const dialogUsersLoading = ref(false);
const applicationAdvisorLoading = ref(false);
const memberLoading = ref(false);
const saving = ref(false);
const reviewing = ref(false);
const profileSaving = ref(false);
const termSaving = ref(false);
const groupingSaving = ref(false);
const organizationLoading = ref(false);
const organizationSaving = ref(false);
const draggingDepartmentId = ref<number | null>(null);
const draggingGroup = ref<{ departmentId: number; groupId: number } | null>(null);
const dragOverDepartmentId = ref<number | null>(null);
const dragOverGroupId = ref<number | null>(null);
const organizationTreeCollapsed = ref(false);
const collapsedOrganizationDepartmentIds = ref<Set<number>>(new Set());
const expandedOrganizationGroupIds = ref<Set<number>>(new Set());
const departmentDialogVisible = ref(false);
const groupDialogVisible = ref(false);
const dissolvingClubId = ref<number | null>(null);
const exitingMemberId = ref<number | null>(null);
const exitingClubId = ref<number | null>(null);
const error = ref("");
const isClubWorkspace = computed(() => props.workspace === "club");
const isMemberWorkspace = computed(() => props.workspace === "members");
const isRegistrationWorkspace = computed(() => props.workspace === "registration");
const isOrganizationWorkspace = computed(() => props.workspace === "organization");
const activeTab = ref(defaultActiveTab(props.workspace));
const routeClubId = Number(route.query.clubId);
const selectedClubId = ref<number | undefined>(
  Number.isFinite(routeClubId) && routeClubId > 0 ? routeClubId : undefined,
);
const memberWorkspaceMode = ref<MemberWorkspaceMode>("current");
const transitionTermForm = reactive(currentAcademicTermOption());

const filters = reactive({
  auditStatus: "",
  keyword: "",
  submittedRange: [] as string[],
});

const memberFilters = reactive({
  termName: "",
  departmentId: undefined as number | undefined,
  groupId: undefined as number | undefined,
  unassignedOnly: false,
});
const memberSortMode = ref<MemberSortMode>("organization");
const newAcademicTermStartYear = ref(academicYearStart(new Date()) + 3);

const applicationDialogVisible = ref(false);
const applicationFormRef = ref<FormInstance>();
const applicationMode = ref<"create" | "resubmit">("create");
const applicationTarget = ref<ClubApplication | null>(null);
const applicationForm = reactive({
  name: "",
  category: "",
  description: "",
  applyReason: "",
  materialUrl: "",
  contactPhone: "",
  advisorUserId: null as number | null,
});
const applicationDialogTitle = computed(() =>
  applicationMode.value === "resubmit" ? "修改后重交社团注册申请" : "提交社团注册申请",
);
const applicationSubmitLabel = computed(() =>
  applicationMode.value === "resubmit" ? "重新提交" : "提交申请",
);

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
  departmentId: undefined as number | undefined,
  departmentName: "",
  groupId: undefined as number | undefined,
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
  departmentId: undefined as number | undefined,
  departmentName: "",
  groupId: undefined as number | undefined,
  groupName: "",
});
const selectedMemberRows = ref<ClubMemberRecord[]>([]);
const memberBatchGroupingDialogVisible = ref(false);
const memberBatchGroupingForm = reactive({
  departmentId: undefined as number | undefined,
  departmentName: "",
  groupId: undefined as number | undefined,
  groupName: "",
});
const memberBatchPositionDialogVisible = ref(false);
const memberBatchPositionForm = reactive({
  positionName: "",
});

const departmentForm = reactive({
  departmentName: "",
  responsibilities: "",
  contactPhone: "",
  contactEmail: "",
  officeLocation: "",
  displayOrder: 0,
  departmentStatus: "active" as "active" | "inactive",
});
const departmentEditTarget = ref<ClubDepartmentRecord | null>(null);
const groupForm = reactive({
  departmentId: undefined as number | undefined,
  groupName: "",
  responsibilities: "",
  contactPhone: "",
  contactEmail: "",
  activityLocation: "",
  displayOrder: 0,
  groupStatus: "active" as "active" | "inactive",
});
const groupEditTarget = ref<ClubGroupRecord | null>(null);

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

function defaultActiveTab(workspace: ClubWorkspace) {
  if (workspace === "organization") return "organization";
  if (workspace === "members") return "members";
  if (workspace === "registration") return "workspace";
  return "profile";
}

let stopSessionListener: (() => void) | null = null;
let usersRequestId = 0;
let dialogUsersRequestId = 0;
let dataRequestId = 0;
let membersRequestId = 0;
let organizationRequestId = 0;

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
const activeClubDepartments = computed(() =>
  clubDepartments.value.filter((department) => department.departmentStatus === "active"),
);
const activeClubGroups = computed(() =>
  activeClubDepartments.value.flatMap((department) =>
    department.groups.filter((group) => group.groupStatus === "active"),
  ),
);
const memberGroupOptions = computed(() => groupOptionsForDepartmentId(memberFilters.departmentId));
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
  () => isOrganizationWorkspace.value && canManageSelectedClub.value,
);
const canCreateAcademicTerm = computed(() => canManageSelectedClub.value);
const canCreateMemberGroup = computed(
  () => isOrganizationWorkspace.value && groupCreateDepartmentOptions.value.length > 0,
);
const canReorderDepartments = computed(
  () => isOrganizationWorkspace.value && canManageSelectedClub.value,
);
const canShowOrganizationOperationColumn = computed(
  () => canManageSelectedClub.value || canCreateMemberGroup.value,
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
    ? activeClubDepartments.value
    : activeClubDepartments.value.filter((department) =>
        selectedDepartmentManagerScopes.value.some((scope) =>
          organizationNameMatches(scope.departmentName, department.departmentName),
        ),
      ),
);
const memberGroupingGroupOptions = computed(() =>
  groupOptionsForDepartmentId(memberGroupingForm.departmentId),
);
const memberBatchGroupingGroupOptions = computed(() =>
  groupOptionsForDepartmentId(memberBatchGroupingForm.departmentId),
);
const memberTermGroupOptions = computed(() =>
  groupOptionsForDepartmentId(memberTermForm.departmentId),
);
const memberPositionOptions = computed(() =>
  uniqueTextOptions([
    ...defaultMemberPositionOptions,
    ...clubMembers.value.map((member) => member.positionName),
  ]),
);
const canShowMemberBatchSelection = computed(
  () =>
    memberWorkspaceMode.value === "current" &&
    (canAdministerMemberTerms.value || canManageSelectedClub.value),
);
const selectedBatchGroupableRows = computed(() =>
  selectedMemberRows.value.filter((row) => canBatchUpdateMemberGrouping(row)),
);
const selectedBatchPositionRows = computed(() =>
  selectedMemberRows.value.filter((row) => canBatchUpdateMemberTerm(row)),
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
const currentActiveClubMembers = computed(() =>
  currentClubMembers.value.filter((member) => isActiveStatus(member.memberStatus)),
);
const organizationDepartmentMemberCounts = computed(() => {
  const counts = new Map<number, number>();
  currentActiveClubMembers.value.forEach((member) => {
    if (!member.departmentId) return;
    counts.set(member.departmentId, (counts.get(member.departmentId) ?? 0) + 1);
  });
  return counts;
});
const organizationGroupMembers = computed(() => {
  const rows = new Map<number, ClubMemberRecord[]>();
  currentActiveClubMembers.value.forEach((member) => {
    if (!member.groupId) return;
    const members = rows.get(member.groupId) ?? [];
    members.push(member);
    rows.set(member.groupId, members);
  });

  rows.forEach((members) => {
    members.sort(
      (left, right) =>
        compareOptionalText(left.positionName, right.positionName) ||
        compareOptionalText(left.studentNo, right.studentNo) ||
        compareOptionalText(left.userName, right.userName),
    );
  });
  return rows;
});
const memberTableRows = computed(() => {
  const rows = (
    memberWorkspaceMode.value === "history" ? clubMembers.value : currentClubMembers.value
  ).filter(
    (member) =>
      (memberWorkspaceMode.value !== "history" ||
        !memberFilters.termName ||
        member.termName === memberFilters.termName) &&
      (!memberFilters.unassignedOnly || isMemberUnassigned(member)),
  );

  return sortMemberRows(rows, memberSortMode.value);
});
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
const organizationSummary = computed(() => {
  const departments = clubDepartments.value;
  const groups = departments.flatMap((department) => department.groups);

  return {
    departments: departments.length,
    activeDepartments: activeClubDepartments.value.length,
    groups: groups.length,
    activeGroups: activeClubGroups.value.length,
    currentMembers: currentActiveClubMembers.value.length,
  };
});
const workspaceTitle = computed(() => {
  if (isOrganizationWorkspace.value) return "社团架构";
  if (isMemberWorkspace.value) return "成员管理";
  if (isRegistrationWorkspace.value) return "社团注册";
  return "我的社团";
});
const workspaceSubtitle = computed(() => {
  if (isOrganizationWorkspace.value) return "独立维护社团部门、小组和内部组织架构";
  if (isMemberWorkspace.value) return "成员名册、任期维护、干部换届与成员退出";
  if (isRegistrationWorkspace.value) return "社团注册申请、我的申请进度与负责人审核";
  return "社团基本信息与我的社团身份";
});
const workspaceEmptyDescription = computed(() => {
  if (isOrganizationWorkspace.value) return "当前账号暂无可查看的社团架构";
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

  if (isOrganizationWorkspace.value) {
    if (memberViewClubs.value.length > 0) tabs.push("organization");
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
      clubDepartments.value = [];
    }
    return;
  }

  loading.value = true;
  error.value = "";
  try {
    const query = new URLSearchParams();
    if (filters.auditStatus) query.set("auditStatus", filters.auditStatus);
    if (filters.keyword.trim()) query.set("keyword", filters.keyword.trim());
    if (filters.submittedRange.length >= 2) {
      query.set("startDate", filters.submittedRange[0]);
      query.set("endDate", filters.submittedRange[1]);
    }
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
    await Promise.all([loadMembers(), loadDepartments()]);
  } catch (e) {
    if (requestId === dataRequestId) {
      error.value = e instanceof Error ? e.message : "加载失败";
      applications.value = [];
      clubs.value = [];
      clubMembers.value = [];
    }
  } finally {
    if (requestId === dataRequestId) loading.value = false;
  }
}

async function loadMembers() {
  const requestId = ++membersRequestId;
  const clubId = selectedClubId.value;
  const userId = currentUserId.value;
  const include =
    memberWorkspaceMode.value === "history" || memberWorkspaceMode.value === "transition";
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
    const shouldApplyMemberFilters = isMemberWorkspace.value;
    if (
      shouldApplyMemberFilters &&
      memberWorkspaceMode.value === "history" &&
      memberFilters.termName
    ) {
      query.set("termName", memberFilters.termName);
    }
    if (shouldApplyMemberFilters && memberFilters.departmentId) {
      query.set("departmentId", String(memberFilters.departmentId));
    }
    if (shouldApplyMemberFilters && memberFilters.groupId) {
      query.set("groupId", String(memberFilters.groupId));
    }
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

async function loadDepartments() {
  const requestId = ++organizationRequestId;
  const clubId = selectedClubId.value;
  const userId = currentUserId.value;
  if (!userId || !clubId || !canViewSelectedClub()) {
    if (requestId === organizationRequestId) clubDepartments.value = [];
    return;
  }

  organizationLoading.value = true;
  try {
    const data = await requestJson<ClubDepartmentRecord[]>(
      `/api/clubs/${clubId}/departments?includeInactive=true`,
    );
    if (requestId === organizationRequestId) clubDepartments.value = data;
  } catch (e) {
    if (requestId === organizationRequestId) {
      clubDepartments.value = [];
      ElMessage.error(e instanceof Error ? e.message : "部门和小组加载失败");
    }
  } finally {
    if (requestId === organizationRequestId) organizationLoading.value = false;
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

function canMaintainDepartment(_department: ClubDepartmentRecord) {
  return canManageSelectedClub.value;
}

function canMaintainDepartmentGroups(department: ClubDepartmentRecord) {
  if (canManageSelectedClub.value) return true;
  return selectedDepartmentManagerScopes.value.some((scope) =>
    organizationNameMatches(scope.departmentName, department.departmentName),
  );
}

function canMaintainGroup(group: ClubGroupRecord) {
  const department = departmentById(group.departmentId);
  return Boolean(department && canMaintainDepartmentGroups(department));
}

function canShowGroupOperationColumn(department: ClubDepartmentRecord) {
  return department.groups.some((group) => canMaintainGroup(group));
}

function canReorderDepartmentGroups(department: ClubDepartmentRecord) {
  return canMaintainDepartmentGroups(department);
}

function departmentCurrentMemberCount(department: ClubDepartmentRecord) {
  return organizationDepartmentMemberCounts.value.get(department.departmentId) ?? 0;
}

function groupCurrentMembers(group: ClubGroupRecord) {
  return organizationGroupMembers.value.get(group.groupId) ?? [];
}

function groupCurrentMemberCount(group: ClubGroupRecord) {
  return groupCurrentMembers(group).length;
}

function organizationNameMatches(
  left: string | null | undefined,
  right: string | null | undefined,
) {
  const normalizedLeft = left?.trim().toLowerCase();
  const normalizedRight = right?.trim().toLowerCase();
  return Boolean(normalizedLeft && normalizedRight && normalizedLeft === normalizedRight);
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
  applicationMode.value = "create";
  applicationTarget.value = null;
  applicationForm.name = "";
  applicationForm.category = "";
  applicationForm.description = "";
  applicationForm.applyReason = "";
  applicationForm.materialUrl = "";
  applicationForm.contactPhone = "";
  applicationForm.advisorUserId = null;
  applicationFormRef.value?.clearValidate();
}

function fillApplicationForm(row: ClubApplication) {
  applicationForm.name = row.name;
  applicationForm.category = row.category ?? "";
  applicationForm.description = row.description ?? "";
  applicationForm.applyReason = row.applyReason;
  applicationForm.materialUrl = row.materialUrl;
  applicationForm.contactPhone = row.contactPhone ?? "";
  applicationForm.advisorUserId = row.advisorUserId;
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

function canResubmitApplication(row: ClubApplication) {
  return (
    canSubmitApplication.value &&
    row.auditStatus === "rejected" &&
    row.applicantUserId === currentUserId.value
  );
}

function openResubmitApplicationDialog(row: ClubApplication) {
  if (!canResubmitApplication(row)) {
    ElMessage.warning("只有已退回的本人申请可以修改后重交。");
    return;
  }

  applicationMode.value = "resubmit";
  applicationTarget.value = row;
  fillApplicationForm(row);
  applicationDialogVisible.value = true;
  void loadApplicationAdvisorCandidates();
}

function applicationOperationText(row: ClubApplication) {
  if (row.auditStatus === "pending") return "等待审核";
  if (row.auditStatus === "approved") return "已通过";
  return "已处理";
}

async function submitApplication() {
  if (!applicationFormRef.value || !currentUserId.value) return;
  if (!(await validateForm(applicationFormRef.value))) return;

  const isResubmit = applicationMode.value === "resubmit";
  const targetId = applicationTarget.value?.id;
  if (isResubmit && !targetId) return;

  saving.value = true;
  try {
    await requestJson<ClubApplication>(
      `/api/clubs/applications${isResubmit ? `/${targetId}` : ""}`,
      {
        method: isResubmit ? "PATCH" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ...applicationForm,
        }),
      },
    );
    ElMessage.success(isResubmit ? "社团注册申请已重新提交" : "社团注册申请已提交");
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
  memberTermForm.departmentId = undefined;
  memberTermForm.departmentName = "";
  memberTermForm.groupId = undefined;
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
  memberTermForm.departmentId = row.departmentId ?? undefined;
  memberTermForm.departmentName = row.departmentName ?? "";
  memberTermForm.groupId = row.groupId ?? undefined;
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
  memberTermForm.departmentId = row.departmentId ?? undefined;
  memberTermForm.departmentName = row.departmentName ?? "";
  memberTermForm.groupId = row.groupId ?? undefined;
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

function canBatchUpdateMemberGrouping(row: ClubMemberRecord) {
  if (!canFreelyUpdateMemberGrouping(row)) return false;
  return !(
    canManageSelectedClub.value &&
    !canAdministerMemberTerms.value &&
    row.userId === currentUserId.value
  );
}

function canBatchUpdateMemberTerm(row: ClubMemberRecord) {
  return row.isCurrent && canEditMemberTerm(row);
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
    memberGroupingForm.departmentId = row.departmentId ?? undefined;
    memberGroupingForm.departmentName = row.departmentName ?? "";
    memberGroupingForm.groupId = row.groupId ?? undefined;
    memberGroupingForm.groupName = row.groupName ?? "";
  } else {
    const scope =
      selectedCadreGroupingScopes.value.find(
        (item) => !item.groupName && item.departmentName === (row.departmentName ?? ""),
      ) ??
      selectedCadreGroupingScopes.value.find((item) => !item.groupName) ??
      selectedCadreGroupingScopes.value[0];
    memberGroupingMode.value = scope.groupName ? "own" : "department";
    applyGroupingSelectionByName(
      memberGroupingForm,
      scope.departmentName,
      scope.groupName || row.groupName || "",
    );
  }
  handleMemberGroupingDepartmentChange();
  memberGroupingDialogVisible.value = true;
}

function canSelectMemberForBatch(row: ClubMemberRecord) {
  return canBatchUpdateMemberGrouping(row) || canBatchUpdateMemberTerm(row);
}

function handleMemberSelectionChange(rows: ClubMemberRecord[]) {
  selectedMemberRows.value = rows;
}

function openMemberBatchGroupingDialog() {
  if (selectedMemberRows.value.length === 0) {
    ElMessage.warning("请先勾选需要批量分组的成员。");
    return;
  }

  const rows = selectedBatchGroupableRows.value;
  if (rows.length !== selectedMemberRows.value.length) {
    ElMessage.warning("已勾选成员中包含当前身份不可批量维护的成员。");
    return;
  }

  const first = rows[0];
  const sameDepartment = rows.every(
    (row) => (row.departmentId ?? null) === (first.departmentId ?? null),
  );
  const sameGroup = rows.every((row) => (row.groupId ?? null) === (first.groupId ?? null));
  memberBatchGroupingForm.departmentId = sameDepartment
    ? (first.departmentId ?? undefined)
    : undefined;
  memberBatchGroupingForm.departmentName = sameDepartment ? (first.departmentName ?? "") : "";
  memberBatchGroupingForm.groupId = sameGroup ? (first.groupId ?? undefined) : undefined;
  memberBatchGroupingForm.groupName = sameGroup ? (first.groupName ?? "") : "";
  memberBatchGroupingDialogVisible.value = true;
}

function openMemberBatchPositionDialog() {
  if (selectedMemberRows.value.length === 0) {
    ElMessage.warning("请先勾选需要批量设置职务的成员。");
    return;
  }

  const rows = selectedBatchPositionRows.value;
  if (rows.length !== selectedMemberRows.value.length) {
    ElMessage.warning("已勾选成员中包含当前身份不可批量维护职务的成员。");
    return;
  }

  const first = rows[0];
  const samePosition = rows.every((row) => (row.positionName ?? "") === (first.positionName ?? ""));
  memberBatchPositionForm.positionName = samePosition ? (first.positionName ?? "") : "";
  memberBatchPositionDialogVisible.value = true;
}

async function submitMemberGrouping() {
  if (!memberGroupingTarget.value || !selectedClubId.value || !currentUserId.value) return;

  if (memberGroupingMode.value !== "free" && !memberGroupingForm.groupId) {
    ElMessage.warning(
      memberGroupingMode.value === "own"
        ? "干部账号需要先登记自己的小组，才能把成员纳入本组。"
        : "请选择本部门下的小组。",
    );
    return;
  }

  groupingSaving.value = true;
  try {
    await requestJson<ClubMemberRecord>(
      `/api/clubs/${selectedClubId.value}/members/${memberGroupingTarget.value.memberId}/grouping`,
      {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          departmentId: memberGroupingForm.departmentId ?? null,
          departmentName: emptyToNull(memberGroupingForm.departmentName),
          groupId: memberGroupingForm.groupId ?? null,
          groupName: emptyToNull(memberGroupingForm.groupName),
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

async function submitMemberBatchGrouping() {
  if (!selectedClubId.value || selectedBatchGroupableRows.value.length === 0) return;

  if (!memberBatchGroupingForm.departmentId) {
    ElMessage.warning("请选择批量设置的部门。");
    return;
  }

  const rows = [...selectedBatchGroupableRows.value];
  groupingSaving.value = true;
  try {
    const results = await Promise.allSettled(
      rows.map((row) =>
        requestJson<ClubMemberRecord>(
          `/api/clubs/${selectedClubId.value}/members/${row.memberId}/grouping`,
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              departmentId: memberBatchGroupingForm.departmentId ?? null,
              departmentName: emptyToNull(memberBatchGroupingForm.departmentName),
              groupId: memberBatchGroupingForm.groupId ?? null,
              groupName: emptyToNull(memberBatchGroupingForm.groupName),
            }),
          },
        ),
      ),
    );
    const failed = results.filter((result) => result.status === "rejected").length;
    const succeeded = rows.length - failed;

    if (succeeded === 0) {
      throw new Error("批量分组保存失败");
    }

    if (failed > 0) {
      ElMessage.warning(`成功 ${succeeded} 名，失败 ${failed} 名`);
    } else {
      ElMessage.success(`已批量更新 ${rows.length} 名成员的分组`);
    }

    selectedMemberRows.value = [];
    memberBatchGroupingDialogVisible.value = false;
    await Promise.all([loadUsers(), loadMembers()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "批量分组保存失败");
  } finally {
    groupingSaving.value = false;
  }
}

async function submitMemberBatchPosition() {
  if (!selectedClubId.value || selectedBatchPositionRows.value.length === 0) return;

  const positionName = memberBatchPositionForm.positionName.trim();
  if (!positionName) {
    ElMessage.warning("请选择或填写批量设置的职务。");
    return;
  }

  if (isPrincipalPosition(positionName)) {
    ElMessage.warning("负责人职务需要逐人编辑确认，不能批量设置。");
    return;
  }

  const rows = [...selectedBatchPositionRows.value];
  termSaving.value = true;
  try {
    const results = await Promise.allSettled(
      rows.map((row) =>
        requestJson<ClubMemberRecord>(
          `/api/clubs/${selectedClubId.value}/members/${row.memberId}`,
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ positionName }),
          },
        ),
      ),
    );
    const failed = results.filter((result) => result.status === "rejected").length;
    const succeededRows = rows.filter((_, index) => results[index]?.status === "fulfilled");

    if (succeededRows.length === 0) {
      throw new Error("批量职务保存失败");
    }

    if (failed > 0) {
      ElMessage.warning(`成功 ${succeededRows.length} 名，失败 ${failed} 名`);
    } else {
      ElMessage.success(`已批量更新 ${rows.length} 名成员的职务`);
    }

    selectedMemberRows.value = [];
    memberBatchPositionDialogVisible.value = false;
    if (succeededRows.some((row) => row.userId === currentUserId.value)) {
      await refreshAuthSessionQuietly();
    }
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "批量职务保存失败");
  } finally {
    termSaving.value = false;
  }
}

async function submitMemberTerm() {
  if (!memberTermFormRef.value || !selectedClubId.value || !currentUserId.value) return;
  if (!(await validateForm(memberTermFormRef.value))) return;

  termSaving.value = true;
  try {
    const payload = {
      userId: memberTermForm.userId,
      departmentId: memberTermForm.departmentId ?? null,
      departmentName: emptyToNull(memberTermForm.departmentName),
      groupId: memberTermForm.groupId ?? null,
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

function resetFilters() {
  filters.auditStatus = "";
  filters.keyword = "";
  filters.submittedRange = [];
  void loadData();
}

function clearMemberFilters() {
  memberFilters.termName = "";
  memberFilters.departmentId = undefined;
  memberFilters.groupId = undefined;
  memberFilters.unassignedOnly = false;
  void loadMembers();
}

function toggleUnassignedMemberFilter(value: string | number | boolean) {
  if (!value) return;

  memberFilters.departmentId = undefined;
  memberFilters.groupId = undefined;
  void loadMembers();
}

function nextDisplayOrder(records: readonly { displayOrder: number }[]) {
  if (records.length === 0) return 10;
  return Math.max(...records.map((record) => record.displayOrder || 0)) + 10;
}

function nextDepartmentDisplayOrder() {
  return nextDisplayOrder(clubDepartments.value);
}

function nextGroupDisplayOrder(departmentId: number | undefined) {
  const department = departmentById(departmentId);
  return nextDisplayOrder(department?.groups ?? []);
}

function departmentPayloadFromForm(target: ClubDepartmentRecord | null) {
  return {
    departmentName: departmentForm.departmentName.trim(),
    departmentCode: target?.departmentCode ?? null,
    responsibilities: emptyToNull(departmentForm.responsibilities),
    contactPhone: emptyToNull(departmentForm.contactPhone),
    contactEmail: emptyToNull(departmentForm.contactEmail),
    officeLocation: emptyToNull(departmentForm.officeLocation),
    displayOrder: departmentForm.displayOrder,
    departmentStatus: departmentForm.departmentStatus,
  };
}

function groupPayloadFromForm(target: ClubGroupRecord | null) {
  return {
    groupName: groupForm.groupName.trim(),
    groupCode: target?.groupCode ?? null,
    responsibilities: emptyToNull(groupForm.responsibilities),
    contactPhone: emptyToNull(groupForm.contactPhone),
    contactEmail: emptyToNull(groupForm.contactEmail),
    activityLocation: emptyToNull(groupForm.activityLocation),
    displayOrder: groupForm.displayOrder,
    groupStatus: groupForm.groupStatus,
  };
}

function departmentPayloadFromRecord(department: ClubDepartmentRecord, displayOrder: number) {
  return {
    departmentName: department.departmentName,
    departmentCode: department.departmentCode,
    responsibilities: department.responsibilities,
    contactPhone: department.contactPhone,
    contactEmail: department.contactEmail,
    officeLocation: department.officeLocation,
    displayOrder,
    departmentStatus: department.departmentStatus,
  };
}

function groupPayloadFromRecord(group: ClubGroupRecord, displayOrder: number) {
  return {
    groupName: group.groupName,
    groupCode: group.groupCode,
    responsibilities: group.responsibilities,
    contactPhone: group.contactPhone,
    contactEmail: group.contactEmail,
    activityLocation: group.activityLocation,
    displayOrder,
    groupStatus: group.groupStatus,
  };
}

function moveItem<T>(items: readonly T[], sourceIndex: number, targetIndex: number) {
  const next = [...items];
  const [moved] = next.splice(sourceIndex, 1);
  next.splice(targetIndex, 0, moved);
  return next;
}

function isOrganizationDepartmentCollapsed(department: ClubDepartmentRecord) {
  return collapsedOrganizationDepartmentIds.value.has(department.departmentId);
}

function toggleOrganizationTreeRoot() {
  organizationTreeCollapsed.value = !organizationTreeCollapsed.value;
}

function toggleOrganizationDepartment(department: ClubDepartmentRecord) {
  if (department.groups.length === 0) return;

  const next = new Set(collapsedOrganizationDepartmentIds.value);
  if (next.has(department.departmentId)) {
    next.delete(department.departmentId);
  } else {
    next.add(department.departmentId);
  }
  collapsedOrganizationDepartmentIds.value = next;
}

function isOrganizationGroupExpanded(group: ClubGroupRecord) {
  return expandedOrganizationGroupIds.value.has(group.groupId);
}

function toggleOrganizationGroup(group: ClubGroupRecord) {
  const next = new Set(expandedOrganizationGroupIds.value);
  if (next.has(group.groupId)) {
    next.delete(group.groupId);
  } else {
    next.add(group.groupId);
  }
  expandedOrganizationGroupIds.value = next;
}

function startDepartmentDrag(row: ClubDepartmentRecord) {
  if (!canReorderDepartments.value || organizationSaving.value) return;
  draggingDepartmentId.value = row.departmentId;
  dragOverDepartmentId.value = null;
}

function markDepartmentDropTarget(row: ClubDepartmentRecord) {
  if (!draggingDepartmentId.value || draggingDepartmentId.value === row.departmentId) return;
  dragOverDepartmentId.value = row.departmentId;
}

async function dropDepartment(row: ClubDepartmentRecord) {
  const sourceId = draggingDepartmentId.value;
  draggingDepartmentId.value = null;
  dragOverDepartmentId.value = null;
  if (!sourceId || sourceId === row.departmentId || !selectedClubId.value) return;

  const sourceIndex = clubDepartments.value.findIndex(
    (department) => department.departmentId === sourceId,
  );
  const targetIndex = clubDepartments.value.findIndex(
    (department) => department.departmentId === row.departmentId,
  );
  if (sourceIndex < 0 || targetIndex < 0) return;

  const reordered = moveItem(clubDepartments.value, sourceIndex, targetIndex);
  await persistDepartmentOrder(reordered);
}

async function persistDepartmentOrder(reordered: ClubDepartmentRecord[]) {
  if (!selectedClubId.value || !canReorderDepartments.value) return;

  organizationSaving.value = true;
  try {
    await Promise.all(
      reordered.map((department, index) =>
        requestJson<ClubDepartmentRecord>(
          `/api/clubs/${selectedClubId.value}/departments/${department.departmentId}`,
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(departmentPayloadFromRecord(department, (index + 1) * 10)),
          },
        ),
      ),
    );
    ElMessage.success("排序已更新");
    await Promise.all([loadDepartments(), loadMembers()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "排序保存失败");
  } finally {
    organizationSaving.value = false;
  }
}

function startGroupDrag(group: ClubGroupRecord) {
  if (organizationSaving.value) return;
  const department = departmentById(group.departmentId);
  if (!department || !canReorderDepartmentGroups(department)) return;
  draggingGroup.value = {
    departmentId: group.departmentId,
    groupId: group.groupId,
  };
  dragOverGroupId.value = null;
}

function markGroupDropTarget(group: ClubGroupRecord) {
  const source = draggingGroup.value;
  if (!source || source.groupId === group.groupId || source.departmentId !== group.departmentId) {
    return;
  }
  dragOverGroupId.value = group.groupId;
}

async function dropGroup(target: ClubGroupRecord) {
  const source = draggingGroup.value;
  draggingGroup.value = null;
  dragOverGroupId.value = null;
  if (
    !source ||
    source.groupId === target.groupId ||
    source.departmentId !== target.departmentId ||
    !selectedClubId.value
  ) {
    return;
  }

  const department = departmentById(target.departmentId);
  if (!department) return;
  if (!canReorderDepartmentGroups(department)) return;

  const sourceIndex = department.groups.findIndex((group) => group.groupId === source.groupId);
  const targetIndex = department.groups.findIndex((group) => group.groupId === target.groupId);
  if (sourceIndex < 0 || targetIndex < 0) return;

  const reordered = moveItem(department.groups, sourceIndex, targetIndex);
  await persistGroupOrder(reordered);
}

async function persistGroupOrder(reordered: ClubGroupRecord[]) {
  if (!selectedClubId.value) return;

  organizationSaving.value = true;
  try {
    await Promise.all(
      reordered.map((group, index) =>
        requestJson<ClubGroupRecord>(`/api/clubs/${selectedClubId.value}/groups/${group.groupId}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(groupPayloadFromRecord(group, (index + 1) * 10)),
        }),
      ),
    );
    ElMessage.success("排序已更新");
    await Promise.all([loadDepartments(), loadMembers()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "排序保存失败");
  } finally {
    organizationSaving.value = false;
  }
}

function endOrganizationDrag() {
  draggingDepartmentId.value = null;
  draggingGroup.value = null;
  dragOverDepartmentId.value = null;
  dragOverGroupId.value = null;
}

function resetDepartmentForm() {
  departmentEditTarget.value = null;
  departmentForm.departmentName = "";
  departmentForm.responsibilities = "";
  departmentForm.contactPhone = "";
  departmentForm.contactEmail = "";
  departmentForm.officeLocation = "";
  departmentForm.displayOrder = nextDepartmentDisplayOrder();
  departmentForm.departmentStatus = "active";
}

function openCreateDepartmentDialog() {
  resetDepartmentForm();
  departmentDialogVisible.value = true;
}

function editDepartment(row: ClubDepartmentRecord) {
  departmentEditTarget.value = row;
  departmentForm.departmentName = row.departmentName;
  departmentForm.responsibilities = row.responsibilities ?? "";
  departmentForm.contactPhone = row.contactPhone ?? "";
  departmentForm.contactEmail = row.contactEmail ?? "";
  departmentForm.officeLocation = row.officeLocation ?? "";
  departmentForm.displayOrder = row.displayOrder;
  departmentForm.departmentStatus = row.departmentStatus;
  departmentDialogVisible.value = true;
}

async function submitDepartmentForm() {
  if (!selectedClubId.value || !canCreateMemberDepartment.value) return;
  const departmentName = departmentForm.departmentName.trim();
  if (!departmentName) {
    ElMessage.warning("请填写部门名称。");
    return;
  }

  organizationSaving.value = true;
  try {
    const target = departmentEditTarget.value;
    await requestJson<ClubDepartmentRecord>(
      target
        ? `/api/clubs/${selectedClubId.value}/departments/${target.departmentId}`
        : `/api/clubs/${selectedClubId.value}/departments`,
      {
        method: target ? "PATCH" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(departmentPayloadFromForm(target)),
      },
    );
    ElMessage.success(target ? "部门已更新" : "部门已新增");
    resetDepartmentForm();
    departmentDialogVisible.value = false;
    await Promise.all([loadDepartments(), loadMembers()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "部门保存失败");
  } finally {
    organizationSaving.value = false;
  }
}

function resetGroupForm(departmentId = groupCreateDepartmentOptions.value[0]?.departmentId) {
  groupEditTarget.value = null;
  groupForm.departmentId = departmentId;
  groupForm.groupName = "";
  groupForm.responsibilities = "";
  groupForm.contactPhone = "";
  groupForm.contactEmail = "";
  groupForm.activityLocation = "";
  groupForm.displayOrder = nextGroupDisplayOrder(departmentId);
  groupForm.groupStatus = "active";
}

function openCreateGroupDialog(departmentId?: number) {
  if (!groupCreateDepartmentOptions.value.length) {
    ElMessage.warning("请先新增可用部门。");
    return;
  }
  resetGroupForm(departmentId);
  groupDialogVisible.value = true;
}

function editGroup(row: ClubGroupRecord) {
  groupEditTarget.value = row;
  groupForm.departmentId = row.departmentId;
  groupForm.groupName = row.groupName;
  groupForm.responsibilities = row.responsibilities ?? "";
  groupForm.contactPhone = row.contactPhone ?? "";
  groupForm.contactEmail = row.contactEmail ?? "";
  groupForm.activityLocation = row.activityLocation ?? "";
  groupForm.displayOrder = row.displayOrder;
  groupForm.groupStatus = row.groupStatus;
  groupDialogVisible.value = true;
}

async function submitGroupForm() {
  if (!selectedClubId.value || !canCreateMemberGroup.value) return;
  const departmentId = groupForm.departmentId;
  const groupName = groupForm.groupName.trim();
  if (!departmentId) {
    ElMessage.warning("请选择小组所属部门。");
    return;
  }
  if (!groupName) {
    ElMessage.warning("请填写小组名称。");
    return;
  }

  organizationSaving.value = true;
  try {
    const target = groupEditTarget.value;
    await requestJson<ClubGroupRecord>(
      target
        ? `/api/clubs/${selectedClubId.value}/groups/${target.groupId}`
        : `/api/clubs/${selectedClubId.value}/departments/${departmentId}/groups`,
      {
        method: target ? "PATCH" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(groupPayloadFromForm(target)),
      },
    );
    ElMessage.success(target ? "小组已更新" : "小组已新增");
    resetGroupForm(departmentId);
    groupDialogVisible.value = false;
    await Promise.all([loadDepartments(), loadMembers()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "小组保存失败");
  } finally {
    organizationSaving.value = false;
  }
}

function departmentById(departmentId: number | null | undefined) {
  if (!departmentId) return undefined;
  return clubDepartments.value.find((department) => department.departmentId === departmentId);
}

function departmentByName(departmentName: string | null | undefined) {
  const normalized = departmentName?.trim();
  if (!normalized) return undefined;
  return activeClubDepartments.value.find((department) =>
    organizationNameMatches(department.departmentName, normalized),
  );
}

function groupById(groupId: number | null | undefined) {
  if (!groupId) return undefined;
  return clubDepartments.value
    .flatMap((department) => department.groups)
    .find((group) => group.groupId === groupId);
}

function groupByName(
  departmentId: number | null | undefined,
  groupName: string | null | undefined,
) {
  const normalized = groupName?.trim();
  if (!departmentId || !normalized) return undefined;
  return groupOptionsForDepartmentId(departmentId).find((group) =>
    organizationNameMatches(group.groupName, normalized),
  );
}

function groupOptionsForDepartmentId(departmentId: number | null | undefined) {
  return activeClubGroups.value.filter(
    (group) => !departmentId || group.departmentId === departmentId,
  );
}

function applyDepartmentSelection(form: OrganizationSelectionForm) {
  const department = departmentById(form.departmentId);
  form.departmentName = department?.departmentName ?? "";
  const group = groupById(form.groupId);
  if (!group || group.departmentId !== form.departmentId) {
    form.groupId = undefined;
    form.groupName = "";
  } else {
    form.groupName = group.groupName;
  }
}

function applyGroupSelection(form: OrganizationSelectionForm) {
  const group = groupById(form.groupId);
  form.groupName = group?.groupName ?? "";
  if (group && form.departmentId !== group.departmentId) {
    form.departmentId = group.departmentId;
    form.departmentName = departmentById(group.departmentId)?.departmentName ?? "";
  }
}

function applyGroupingSelectionByName(
  form: OrganizationSelectionForm,
  departmentName: string | null | undefined,
  groupName: string | null | undefined,
) {
  const department = departmentByName(departmentName);
  form.departmentId = department?.departmentId;
  form.departmentName = department?.departmentName ?? departmentName?.trim() ?? "";
  const group = groupByName(form.departmentId, groupName);
  form.groupId = group?.groupId;
  form.groupName = group?.groupName ?? groupName?.trim() ?? "";
}

function handleMemberFilterDepartmentChange() {
  memberFilters.groupId = undefined;
}

function handleMemberGroupingDepartmentChange() {
  applyDepartmentSelection(memberGroupingForm);
}

function handleMemberGroupingGroupChange() {
  applyGroupSelection(memberGroupingForm);
}

function handleMemberBatchDepartmentChange() {
  applyDepartmentSelection(memberBatchGroupingForm);
}

function handleMemberBatchGroupChange() {
  applyGroupSelection(memberBatchGroupingForm);
}

function handleMemberTermDepartmentChange() {
  applyDepartmentSelection(memberTermForm);
}

function handleMemberTermGroupChange() {
  applyGroupSelection(memberTermForm);
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

function compareOptionalText(left: string | null | undefined, right: string | null | undefined) {
  const normalizedLeft = left?.trim() ?? "";
  const normalizedRight = right?.trim() ?? "";
  if (!normalizedLeft && !normalizedRight) return 0;
  if (!normalizedLeft) return 1;
  if (!normalizedRight) return -1;
  return memberSortCollator.compare(normalizedLeft, normalizedRight);
}

function compareOptionalNumber(left: number | null | undefined, right: number | null | undefined) {
  const normalizedLeft = left ?? Number.MAX_SAFE_INTEGER;
  const normalizedRight = right ?? Number.MAX_SAFE_INTEGER;
  return normalizedLeft - normalizedRight;
}

function compareDateDesc(left: string | null | undefined, right: string | null | undefined) {
  const normalizedLeft = dateOnly(left);
  const normalizedRight = dateOnly(right);
  if (!normalizedLeft && !normalizedRight) return 0;
  if (!normalizedLeft) return 1;
  if (!normalizedRight) return -1;
  return normalizedRight.localeCompare(normalizedLeft);
}

function sortMemberRows(rows: readonly ClubMemberRecord[], mode: MemberSortMode) {
  return [...rows].sort((left, right) => {
    if (mode === "studentNo") return compareMemberByStudentNo(left, right);
    if (mode === "term") return compareMemberByTerm(left, right);
    return compareMemberByOrganization(left, right);
  });
}

function compareMemberByStudentNo(left: ClubMemberRecord, right: ClubMemberRecord) {
  return (
    compareOptionalText(left.studentNo, right.studentNo) ||
    compareOptionalText(left.userName, right.userName) ||
    compareMemberByOrganization(left, right)
  );
}

function compareMemberByTerm(left: ClubMemberRecord, right: ClubMemberRecord) {
  return (
    compareDateDesc(left.termStart, right.termStart) ||
    compareDateDesc(left.termEnd, right.termEnd) ||
    compareOptionalText(left.termName, right.termName) ||
    compareMemberByOrganization(left, right)
  );
}

function compareMemberByOrganization(left: ClubMemberRecord, right: ClubMemberRecord) {
  const leftDepartment = departmentById(left.departmentId);
  const rightDepartment = departmentById(right.departmentId);
  const leftGroup = groupById(left.groupId);
  const rightGroup = groupById(right.groupId);

  return (
    compareOptionalNumber(leftDepartment?.displayOrder, rightDepartment?.displayOrder) ||
    compareOptionalText(left.departmentName, right.departmentName) ||
    compareOptionalNumber(leftGroup?.displayOrder, rightGroup?.displayOrder) ||
    compareOptionalText(left.groupName, right.groupName) ||
    compareOptionalText(left.positionName, right.positionName) ||
    compareOptionalText(left.studentNo, right.studentNo) ||
    compareOptionalText(left.userName, right.userName)
  );
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

function isMemberUnassigned(member: ClubMemberRecord) {
  return (
    !member.departmentName?.trim() || !member.groupName?.trim() || !member.positionName?.trim()
  );
}

watch(currentUserId, () => {
  void loadData();
});

watch(selectedClubId, () => {
  memberFilters.departmentId = undefined;
  memberFilters.groupId = undefined;
  memberFilters.termName = "";
  memberFilters.unassignedOnly = false;
  resetDepartmentForm();
  resetGroupForm();
  organizationTreeCollapsed.value = false;
  collapsedOrganizationDepartmentIds.value = new Set();
  expandedOrganizationGroupIds.value = new Set();
  endOrganizationDrag();
  void loadDepartments();
});

watch(
  groupCreateDepartmentOptions,
  (options) => {
    if (!options.length) {
      groupForm.departmentId = undefined;
      return;
    }

    if (
      !groupForm.departmentId ||
      !options.some((item) => item.departmentId === groupForm.departmentId)
    ) {
      groupForm.departmentId = options[0].departmentId;
    }
  },
  { immediate: true },
);

watch(
  [
    selectedClubId,
    memberWorkspaceMode,
    () => memberFilters.termName,
    () => memberFilters.departmentId,
    () => memberFilters.groupId,
  ],
  () => {
    void loadMembers();
  },
);

watch(
  () => props.workspace,
  () => {
    activeTab.value = defaultActiveTab(props.workspace);
    memberWorkspaceMode.value = "current";
    applications.value = [];
    clubs.value = [];
    clubMembers.value = [];
    void loadData();
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
          <el-tag v-if="isOrganizationWorkspace && memberViewClubs.length > 0" effect="plain">
            可查看社团架构
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
          <el-input
            v-model="filters.keyword"
            clearable
            placeholder="社团名 / 申请人"
            class="filter-item"
            @keyup.enter="loadData"
          />
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
          <el-date-picker
            v-model="filters.submittedRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            start-placeholder="提交开始"
            end-placeholder="提交结束"
            class="filter-date-range"
          />
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
                  <el-step
                    title="平台审核"
                    :description="
                      row.auditStatus === 'pending' && row.reviewComment
                        ? '等待重新审核'
                        : row.reviewerName || '等待处理'
                    "
                  />
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
                  <el-descriptions-item label="联系电话">
                    {{ row.contactPhone || "-" }}
                  </el-descriptions-item>
                  <el-descriptions-item
                    v-if="row.reviewComment"
                    :label="row.auditStatus === 'pending' ? '上次退回意见' : '审核意见'"
                    :span="2"
                  >
                    {{ row.reviewComment }}
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
          <el-table-column
            v-if="isReviewer || canSubmitApplication"
            label="操作"
            width="180"
            fixed="right"
          >
            <template #default="{ row }">
              <el-button
                v-if="isReviewer && row.auditStatus === 'pending'"
                type="primary"
                plain
                :icon="Edit"
                @click="openReviewDialog(row)"
              >
                审核
              </el-button>
              <el-button
                v-else-if="canResubmitApplication(row)"
                type="primary"
                plain
                :icon="Edit"
                @click="openResubmitApplicationDialog(row)"
              >
                修改后重交
              </el-button>
              <span v-else class="muted">{{ applicationOperationText(row) }}</span>
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
            <el-descriptions-item label="创建时间">
              {{ formatDate(selectedClub.createdAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="更新时间">
              {{ formatDate(selectedClub.updatedAt || selectedClub.createdAt) }}
            </el-descriptions-item>
          </el-descriptions>

          <el-collapse class="approval-collapse">
            <el-collapse-item title="注册审批记录" name="approval">
              <el-descriptions :column="2" border>
                <el-descriptions-item label="审核状态">
                  <el-tag :type="auditTagType(selectedClub.auditStatus)" effect="plain">
                    {{ selectedClub.auditStatusText }}
                  </el-tag>
                </el-descriptions-item>
                <el-descriptions-item label="申请人">
                  {{ selectedClub.applicantName || "-" }}
                </el-descriptions-item>
                <el-descriptions-item label="申请材料">
                  {{ selectedClub.materialUrl || "-" }}
                </el-descriptions-item>
                <el-descriptions-item label="审核人">
                  {{ selectedClub.reviewerName || "-" }}
                </el-descriptions-item>
                <el-descriptions-item label="申请理由" :span="2">
                  {{ selectedClub.applyReason || "-" }}
                </el-descriptions-item>
                <el-descriptions-item label="审核意见" :span="2">
                  {{ selectedClub.reviewComment || "-" }}
                </el-descriptions-item>
              </el-descriptions>
            </el-collapse-item>
          </el-collapse>
        </div>
      </el-tab-pane>

      <el-tab-pane
        v-if="isOrganizationWorkspace && memberViewClubs.length > 0"
        label="社团架构"
        name="organization"
      >
        <el-empty v-if="memberViewClubs.length === 0" description="当前账号暂无可查看的社团架构" />

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
            </div>
            <div
              v-if="canCreateMemberDepartment || canCreateMemberGroup"
              class="organization-toolbar"
            >
              <el-button
                v-if="canCreateMemberDepartment"
                type="primary"
                :icon="Plus"
                @click="openCreateDepartmentDialog"
              >
                新增部门
              </el-button>
              <el-button
                v-if="canCreateMemberGroup"
                type="primary"
                plain
                :icon="Plus"
                @click="openCreateGroupDialog()"
              >
                新增小组
              </el-button>
            </div>
          </div>

          <div class="member-summary">
            <span>部门 {{ organizationSummary.departments }} 个</span>
            <span>启用部门 {{ organizationSummary.activeDepartments }} 个</span>
            <span>小组 {{ organizationSummary.groups }} 个</span>
            <span>启用小组 {{ organizationSummary.activeGroups }} 个</span>
            <span>当前成员 {{ organizationSummary.currentMembers }} 人</span>
          </div>

          <div class="organization-panel">
            <el-table
              v-loading="organizationLoading"
              :data="clubDepartments"
              border
              stripe
              row-key="departmentId"
              empty-text="暂无部门"
            >
              <el-table-column type="expand">
                <template #default="{ row }">
                  <el-table
                    class="nested-table"
                    :data="row.groups"
                    border
                    stripe
                    empty-text="暂无小组"
                    row-key="groupId"
                  >
                    <el-table-column type="expand" width="48">
                      <template #default="{ row: group }">
                        <div class="group-member-panel">
                          <div class="group-member-panel-head">
                            <strong>{{ group.groupName }} 成员</strong>
                            <span>当前 {{ groupCurrentMemberCount(group) }} 人</span>
                          </div>
                          <el-table
                            :data="groupCurrentMembers(group)"
                            border
                            stripe
                            size="small"
                            empty-text="暂无当前成员"
                            row-key="memberId"
                          >
                            <el-table-column prop="userName" label="成员" min-width="140" />
                            <el-table-column prop="studentNo" label="学号/工号" width="130" />
                            <el-table-column prop="positionName" label="职务" width="130">
                              <template #default="{ row: member }">
                                {{ member.positionName || "-" }}
                              </template>
                            </el-table-column>
                            <el-table-column prop="termName" label="任期" min-width="150">
                              <template #default="{ row: member }">
                                {{ member.termName || "-" }}
                              </template>
                            </el-table-column>
                          </el-table>
                        </div>
                      </template>
                    </el-table-column>
                    <el-table-column
                      v-if="canReorderDepartmentGroups(row)"
                      label=""
                      width="64"
                      align="center"
                    >
                      <template #default="{ row: group }">
                        <button
                          class="drag-handle"
                          :class="{
                            'is-dragging': draggingGroup?.groupId === group.groupId,
                            'is-drop-target': dragOverGroupId === group.groupId,
                          }"
                          type="button"
                          draggable="true"
                          :disabled="organizationSaving"
                          @dragstart="startGroupDrag(group)"
                          @dragover.prevent
                          @dragenter.prevent="markGroupDropTarget(group)"
                          @drop.prevent="dropGroup(group)"
                          @dragend="endOrganizationDrag"
                        >
                          <el-icon><Rank /></el-icon>
                        </button>
                      </template>
                    </el-table-column>
                    <el-table-column prop="groupName" label="小组" min-width="140" />
                    <el-table-column label="当前人数" width="100">
                      <template #default="{ row: group }">
                        {{ groupCurrentMemberCount(group) }}
                      </template>
                    </el-table-column>
                    <el-table-column prop="responsibilities" label="职责" min-width="180" />
                    <el-table-column prop="contactPhone" label="电话" width="130" />
                    <el-table-column prop="contactEmail" label="邮箱" min-width="170" />
                    <el-table-column prop="activityLocation" label="地点" width="140" />
                    <el-table-column label="状态" width="100">
                      <template #default="{ row: group }">
                        <el-tag
                          :type="group.groupStatus === 'active' ? 'success' : 'info'"
                          effect="plain"
                        >
                          {{ group.groupStatus === "active" ? "启用" : "停用" }}
                        </el-tag>
                      </template>
                    </el-table-column>
                    <el-table-column
                      v-if="canShowGroupOperationColumn(row)"
                      label="操作"
                      width="100"
                    >
                      <template #default="{ row: group }">
                        <el-button
                          v-if="canMaintainGroup(group)"
                          type="primary"
                          plain
                          :icon="Edit"
                          @click="editGroup(group)"
                        >
                          编辑
                        </el-button>
                      </template>
                    </el-table-column>
                  </el-table>
                </template>
              </el-table-column>
              <el-table-column v-if="canReorderDepartments" label="" width="64" align="center">
                <template #default="{ row }">
                  <button
                    class="drag-handle"
                    :class="{
                      'is-dragging': draggingDepartmentId === row.departmentId,
                      'is-drop-target': dragOverDepartmentId === row.departmentId,
                    }"
                    type="button"
                    draggable="true"
                    :disabled="organizationSaving"
                    @dragstart="startDepartmentDrag(row)"
                    @dragover.prevent
                    @dragenter.prevent="markDepartmentDropTarget(row)"
                    @drop.prevent="dropDepartment(row)"
                    @dragend="endOrganizationDrag"
                  >
                    <el-icon><Rank /></el-icon>
                  </button>
                </template>
              </el-table-column>
              <el-table-column prop="departmentName" label="部门" min-width="150" />
              <el-table-column prop="responsibilities" label="职责" min-width="180" />
              <el-table-column prop="contactPhone" label="电话" width="130" />
              <el-table-column prop="contactEmail" label="邮箱" min-width="170" />
              <el-table-column prop="officeLocation" label="地点" width="140" />
              <el-table-column label="小组数" width="90">
                <template #default="{ row }">{{ row.groups.length }}</template>
              </el-table-column>
              <el-table-column label="当前人数" width="100">
                <template #default="{ row }">{{ departmentCurrentMemberCount(row) }}</template>
              </el-table-column>
              <el-table-column label="状态" width="100">
                <template #default="{ row }">
                  <el-tag
                    :type="row.departmentStatus === 'active' ? 'success' : 'info'"
                    effect="plain"
                  >
                    {{ row.departmentStatus === "active" ? "启用" : "停用" }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column v-if="canShowOrganizationOperationColumn" label="操作" width="190">
                <template #default="{ row }">
                  <el-button
                    v-if="canMaintainDepartment(row)"
                    type="primary"
                    plain
                    :icon="Edit"
                    @click="editDepartment(row)"
                  >
                    编辑
                  </el-button>
                  <el-button
                    v-if="canMaintainDepartmentGroups(row)"
                    type="primary"
                    plain
                    :icon="Plus"
                    @click="openCreateGroupDialog(row.departmentId)"
                  >
                    小组
                  </el-button>
                </template>
              </el-table-column>
            </el-table>

            <section v-if="selectedClub" class="organization-map" aria-labelledby="org-map-title">
              <div class="organization-map-head">
                <h3 id="org-map-title">架构树</h3>
                <el-tag effect="plain">{{ selectedClub.name }}</el-tag>
              </div>

              <div class="organization-tree">
                <div class="tree-root-line">
                  <button
                    class="tree-toggle"
                    type="button"
                    :aria-label="organizationTreeCollapsed ? '展开社团架构' : '收起社团架构'"
                    :title="organizationTreeCollapsed ? '展开社团架构' : '收起社团架构'"
                    @click="toggleOrganizationTreeRoot"
                  >
                    <el-icon>
                      <Minus v-if="organizationTreeCollapsed" />
                      <Plus v-else />
                    </el-icon>
                  </button>
                  <div class="tree-node tree-node-root">
                    <div>
                      <strong>{{ selectedClub.name }}</strong>
                      <span>{{ selectedClub.category || "社团" }}</span>
                    </div>
                    <el-tag size="small" effect="plain">
                      当前 {{ organizationSummary.currentMembers }} 人
                    </el-tag>
                  </div>
                </div>

                <div v-if="!organizationTreeCollapsed" class="tree-department-list">
                  <div
                    v-for="department in clubDepartments"
                    :key="department.departmentId"
                    class="tree-department-branch"
                    :class="{ 'is-collapsed': isOrganizationDepartmentCollapsed(department) }"
                  >
                    <div class="tree-department-line">
                      <button
                        class="tree-toggle"
                        type="button"
                        :disabled="department.groups.length === 0"
                        :aria-label="
                          isOrganizationDepartmentCollapsed(department)
                            ? '展开部门小组'
                            : '收起部门小组'
                        "
                        :title="
                          isOrganizationDepartmentCollapsed(department)
                            ? '展开部门小组'
                            : '收起部门小组'
                        "
                        @click="toggleOrganizationDepartment(department)"
                      >
                        <el-icon>
                          <Minus v-if="isOrganizationDepartmentCollapsed(department)" />
                          <Plus v-else />
                        </el-icon>
                      </button>
                      <div class="tree-node tree-node-department">
                        <div>
                          <strong>{{ department.departmentName }}</strong>
                          <span>{{ department.responsibilities || "暂无职责" }}</span>
                        </div>
                        <div class="tree-node-meta">
                          <el-tag size="small" effect="plain">
                            当前 {{ departmentCurrentMemberCount(department) }} 人
                          </el-tag>
                          <el-tag
                            size="small"
                            :type="department.departmentStatus === 'active' ? 'success' : 'info'"
                            effect="plain"
                          >
                            {{ department.departmentStatus === "active" ? "启用" : "停用" }}
                          </el-tag>
                        </div>
                      </div>
                    </div>

                    <div
                      v-if="
                        !isOrganizationDepartmentCollapsed(department) &&
                        department.groups.length > 0
                      "
                      class="tree-group-list"
                    >
                      <div
                        v-for="group in department.groups"
                        :key="group.groupId"
                        class="tree-group-branch"
                      >
                        <div class="tree-group-line">
                          <button
                            class="tree-toggle"
                            type="button"
                            :aria-label="
                              isOrganizationGroupExpanded(group) ? '收起小组成员' : '展开小组成员'
                            "
                            :title="
                              isOrganizationGroupExpanded(group) ? '收起小组成员' : '展开小组成员'
                            "
                            @click="toggleOrganizationGroup(group)"
                          >
                            <el-icon>
                              <Plus v-if="isOrganizationGroupExpanded(group)" />
                              <Minus v-else />
                            </el-icon>
                          </button>
                          <div class="tree-node tree-node-group">
                            <div>
                              <strong>{{ group.groupName }}</strong>
                              <span>{{ group.responsibilities || "暂无职责" }}</span>
                            </div>
                            <div class="tree-node-meta">
                              <el-tag size="small" effect="plain">
                                当前 {{ groupCurrentMemberCount(group) }} 人
                              </el-tag>
                              <el-tag
                                size="small"
                                :type="group.groupStatus === 'active' ? 'success' : 'info'"
                                effect="plain"
                              >
                                {{ group.groupStatus === "active" ? "启用" : "停用" }}
                              </el-tag>
                            </div>
                          </div>
                        </div>

                        <div v-if="isOrganizationGroupExpanded(group)" class="tree-member-list">
                          <div
                            v-for="member in groupCurrentMembers(group)"
                            :key="member.memberId"
                            class="tree-member-line"
                          >
                            <div class="tree-node tree-node-member">
                              <strong>{{ member.userName }}</strong>
                              <span>
                                {{ member.positionName || "成员" }}
                                <template v-if="member.studentNo">
                                  / {{ member.studentNo }}</template
                                >
                              </span>
                            </div>
                          </div>

                          <div
                            v-if="groupCurrentMemberCount(group) === 0"
                            class="tree-empty-member"
                          >
                            暂无当前成员
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>

                  <el-empty
                    v-if="clubDepartments.length === 0"
                    description="暂无部门"
                    :image-size="80"
                  />
                </div>
              </div>
            </section>
          </div>
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
              v-model="memberFilters.departmentId"
              class="filter-item"
              clearable
              :disabled="memberFilters.unassignedOnly"
              placeholder="按部门筛选"
              @change="handleMemberFilterDepartmentChange"
            >
              <el-option
                v-for="department in activeClubDepartments"
                :key="department.departmentId"
                :label="department.departmentName"
                :value="department.departmentId"
              />
            </el-select>
            <el-select
              v-model="memberFilters.groupId"
              class="filter-item"
              clearable
              :disabled="memberFilters.unassignedOnly"
              placeholder="按小组筛选"
            >
              <el-option
                v-for="group in memberGroupOptions"
                :key="group.groupId"
                :label="group.groupName"
                :value="group.groupId"
              />
            </el-select>
            <el-select v-model="memberSortMode" class="filter-item" placeholder="排序方式">
              <el-option
                v-for="option in memberSortOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-checkbox
              v-model="memberFilters.unassignedOnly"
              @change="toggleUnassignedMemberFilter"
            >
              待补资料
            </el-checkbox>
            <el-button :icon="Refresh" @click="clearMemberFilters">清除筛选</el-button>
            <el-button
              v-if="canShowMemberBatchSelection"
              type="primary"
              plain
              :icon="Edit"
              :disabled="selectedMemberRows.length === 0"
              @click="openMemberBatchGroupingDialog"
            >
              批量分组
            </el-button>
            <el-button
              v-if="canShowMemberBatchSelection"
              type="primary"
              plain
              :icon="Edit"
              :disabled="selectedMemberRows.length === 0"
              @click="openMemberBatchPositionDialog"
            >
              批量职务
            </el-button>
          </div>

          <div class="member-summary">
            <span>当前列表 {{ memberGroupSummary.total }} 条</span>
            <span>有效任期 {{ memberGroupSummary.current }} 条</span>
            <span>部门 {{ memberGroupSummary.departments }} 个</span>
            <span>小组 {{ memberGroupSummary.groups }} 个</span>
            <span>待补资料 {{ memberGroupSummary.unassigned }} 条</span>
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
          @selection-change="handleMemberSelectionChange"
        >
          <el-table-column
            v-if="canShowMemberBatchSelection"
            type="selection"
            width="48"
            :selectable="canSelectMemberForBatch"
          />
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
              <span v-if="row.positionName">{{ row.positionName }}</span>
              <el-tag v-else type="warning" effect="plain">待补职务</el-tag>
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
      :title="applicationDialogTitle"
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
        <el-button type="primary" :loading="saving" @click="submitApplication">
          {{ applicationSubmitLabel }}
        </el-button>
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

    <el-dialog
      v-model="departmentDialogVisible"
      :title="departmentEditTarget ? '编辑部门' : '新增部门'"
      width="620px"
    >
      <el-form label-width="90px">
        <el-form-item label="部门名称" required>
          <el-input v-model="departmentForm.departmentName" maxlength="80" />
        </el-form-item>
        <el-form-item label="职责">
          <el-input
            v-model="departmentForm.responsibilities"
            type="textarea"
            :rows="3"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="联系方式">
          <div class="organization-inline">
            <el-input v-model="departmentForm.contactPhone" maxlength="40" placeholder="电话" />
            <el-input v-model="departmentForm.contactEmail" maxlength="80" placeholder="邮箱" />
          </div>
        </el-form-item>
        <el-form-item label="常用地点">
          <el-input v-model="departmentForm.officeLocation" maxlength="80" />
        </el-form-item>
        <el-form-item label="启用状态">
          <el-select v-model="departmentForm.departmentStatus">
            <el-option label="启用" value="active" />
            <el-option label="停用" value="inactive" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="departmentDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="organizationSaving" @click="submitDepartmentForm">
          保存部门
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="groupDialogVisible"
      :title="groupEditTarget ? '编辑小组' : '新增小组'"
      width="620px"
    >
      <el-form label-width="90px">
        <el-form-item label="所属部门" required>
          <el-select
            v-model="groupForm.departmentId"
            class="full-width"
            :disabled="Boolean(groupEditTarget)"
          >
            <el-option
              v-for="department in groupCreateDepartmentOptions"
              :key="department.departmentId"
              :label="department.departmentName"
              :value="department.departmentId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="小组名称" required>
          <el-input v-model="groupForm.groupName" maxlength="80" />
        </el-form-item>
        <el-form-item label="职责">
          <el-input
            v-model="groupForm.responsibilities"
            type="textarea"
            :rows="3"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="联系方式">
          <div class="organization-inline">
            <el-input v-model="groupForm.contactPhone" maxlength="40" placeholder="电话" />
            <el-input v-model="groupForm.contactEmail" maxlength="80" placeholder="邮箱" />
          </div>
        </el-form-item>
        <el-form-item label="活动地点">
          <el-input v-model="groupForm.activityLocation" maxlength="80" />
        </el-form-item>
        <el-form-item label="启用状态">
          <el-select v-model="groupForm.groupStatus">
            <el-option label="启用" value="active" />
            <el-option label="停用" value="inactive" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="groupDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="organizationSaving" @click="submitGroupForm">
          保存小组
        </el-button>
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
            v-model="memberGroupingForm.departmentId"
            class="full-width"
            :disabled="memberGroupingMode !== 'free'"
            placeholder="选择部门"
            @change="handleMemberGroupingDepartmentChange"
          >
            <el-option
              v-for="department in activeClubDepartments"
              :key="department.departmentId"
              :label="department.departmentName"
              :value="department.departmentId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="小组">
          <el-select
            v-model="memberGroupingForm.groupId"
            :disabled="memberGroupingMode === 'own'"
            class="full-width"
            clearable
            placeholder="选择小组"
            @change="handleMemberGroupingGroupChange"
          >
            <el-option
              v-for="group in memberGroupingGroupOptions"
              :key="group.groupId"
              :label="group.groupName"
              :value="group.groupId"
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

    <el-dialog v-model="memberBatchGroupingDialogVisible" title="批量设置部门/小组" width="520px">
      <el-alert
        class="dialog-alert"
        type="info"
        show-icon
        :closable="false"
        :title="`已选择 ${selectedBatchGroupableRows.length} 名成员，保存后会统一更新部门和小组。`"
      />
      <el-form label-width="90px">
        <el-form-item label="部门" required>
          <el-select
            v-model="memberBatchGroupingForm.departmentId"
            class="full-width"
            placeholder="选择部门"
            @change="handleMemberBatchDepartmentChange"
          >
            <el-option
              v-for="department in activeClubDepartments"
              :key="department.departmentId"
              :label="department.departmentName"
              :value="department.departmentId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="小组">
          <el-select
            v-model="memberBatchGroupingForm.groupId"
            class="full-width"
            clearable
            placeholder="选择小组（可选）"
            @change="handleMemberBatchGroupChange"
          >
            <el-option
              v-for="group in memberBatchGroupingGroupOptions"
              :key="group.groupId"
              :label="group.groupName"
              :value="group.groupId"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="memberBatchGroupingDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="groupingSaving" @click="submitMemberBatchGrouping">
          批量保存
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="memberBatchPositionDialogVisible" title="批量设置职务" width="520px">
      <el-alert
        class="dialog-alert"
        type="info"
        show-icon
        :closable="false"
        :title="`已选择 ${selectedBatchPositionRows.length} 名成员，保存后会统一更新职务，部门、小组和任期不变。`"
      />
      <el-form label-width="90px">
        <el-form-item label="职务" required>
          <el-select
            v-model="memberBatchPositionForm.positionName"
            class="full-width"
            filterable
            allow-create
            default-first-option
            placeholder="选择或输入职务"
          >
            <el-option
              v-for="position in memberPositionOptions"
              :key="position"
              :label="position"
              :value="position"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="memberBatchPositionDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="termSaving" @click="submitMemberBatchPosition">
          批量保存
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
            v-model="memberTermForm.departmentId"
            class="full-width"
            clearable
            placeholder="选择部门"
            @change="handleMemberTermDepartmentChange"
          >
            <el-option
              v-for="department in activeClubDepartments"
              :key="department.departmentId"
              :label="department.departmentName"
              :value="department.departmentId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="小组">
          <el-select
            v-model="memberTermForm.groupId"
            class="full-width"
            clearable
            placeholder="选择小组"
            @change="handleMemberTermGroupChange"
          >
            <el-option
              v-for="group in memberTermGroupOptions"
              :key="group.groupId"
              :label="group.groupName"
              :value="group.groupId"
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

.filter-date-range {
  width: 260px;
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

.approval-collapse {
  margin-top: 14px;
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

.organization-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.organization-toolbar {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

.organization-inline {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  width: 100%;
}

.nested-table {
  margin: 8px 0 8px 46px;
  width: calc(100% - 46px);
}

.group-member-panel {
  display: grid;
  gap: 10px;
  padding: 10px 12px 12px 54px;
}

.group-member-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  color: #455a6f;
}

.group-member-panel-head strong {
  color: #1f2d3d;
  font-weight: 650;
}

.group-member-panel-head span {
  font-size: 13px;
}

.drag-handle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  color: #66727f;
  background: #fff;
  cursor: grab;
  transition:
    color 0.18s ease,
    border-color 0.18s ease,
    background-color 0.18s ease,
    box-shadow 0.18s ease,
    transform 0.18s ease;
}

.drag-handle:hover:not(:disabled),
.drag-handle.is-dragging {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  transform: translateY(-1px);
}

.drag-handle.is-drop-target {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-8);
  box-shadow: 0 0 0 3px rgb(64 158 255 / 16%);
}

.drag-handle:active {
  cursor: grabbing;
  transform: scale(0.96);
}

.drag-handle:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

.organization-map {
  margin-top: 18px;
  border-top: 1px solid #e2e8f0;
  padding-top: 18px;
}

.organization-map-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}

.organization-map-head h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 650;
}

.organization-tree {
  overflow-x: auto;
  padding: 6px 2px 10px;
}

.tree-root-line,
.tree-department-line,
.tree-group-line {
  display: flex;
  align-items: stretch;
  gap: 10px;
  min-width: max-content;
}

.tree-department-list {
  position: relative;
  margin-left: 46px;
  padding: 16px 0 0 24px;
  border-left: 2px solid #d7e2ee;
}

.tree-department-branch {
  position: relative;
  padding-bottom: 16px;
}

.tree-department-branch::before,
.tree-group-line::before {
  content: "";
  position: absolute;
  width: 24px;
  border-top: 2px solid #d7e2ee;
}

.tree-department-branch::before {
  top: 18px;
  left: -24px;
}

.tree-group-list {
  position: relative;
  margin-left: 58px;
  padding: 12px 0 0 22px;
  border-left: 2px solid #e4edf6;
}

.tree-group-line {
  position: relative;
  padding-bottom: 10px;
}

.tree-group-line::before {
  top: 17px;
  left: -22px;
  width: 22px;
  border-color: #e4edf6;
}

.tree-member-list {
  position: relative;
  margin-left: 58px;
  padding: 2px 0 6px 22px;
  border-left: 2px solid #edf2d7;
}

.tree-member-line {
  position: relative;
  padding-bottom: 8px;
}

.tree-member-line::before {
  content: "";
  position: absolute;
  top: 16px;
  left: -22px;
  width: 22px;
  border-top: 2px solid #edf2d7;
}

.tree-empty-member {
  display: inline-flex;
  align-items: center;
  min-height: 34px;
  border: 1px dashed #d7dfca;
  border-radius: 8px;
  padding: 0 12px;
  color: #7a8794;
  background: #fcfdf8;
  font-size: 13px;
}

.tree-toggle {
  flex: 0 0 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: 1px solid #bfcedc;
  border-radius: 50%;
  color: #455a6f;
  background: #fff;
  cursor: pointer;
  transition:
    color 0.18s ease,
    border-color 0.18s ease,
    background-color 0.18s ease,
    transform 0.18s ease;
}

.tree-toggle:hover:not(:disabled) {
  color: var(--el-color-primary);
  border-color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  transform: translateY(-1px);
}

.tree-toggle:disabled {
  color: #a6b2bf;
  cursor: default;
  background: #f7fafc;
}

.tree-node {
  min-height: 46px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: 1px solid #d7e2ee;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 10px 24px rgb(31 41 55 / 6%);
}

.tree-node > div > strong,
.tree-node > div > span {
  display: block;
}

.tree-node > div > strong {
  color: #1f2d3d;
  font-size: 14px;
  font-weight: 650;
}

.tree-node > div > span {
  margin-top: 3px;
  color: #66727f;
  font-size: 12px;
}

.tree-node-member strong,
.tree-node-member span {
  display: block;
}

.tree-node-member strong {
  color: #1f2d3d;
  font-size: 13px;
  font-weight: 650;
}

.tree-node-member span {
  margin-top: 2px;
  color: #66727f;
  font-size: 12px;
}

.tree-node-meta {
  display: inline-flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 6px;
}

.tree-node-root {
  min-width: 230px;
  padding: 12px 16px;
  border-color: #9ac1ea;
  background: linear-gradient(180deg, #f7fbff 0%, #ffffff 100%);
}

.tree-node-department {
  min-width: 260px;
  padding: 10px 12px 10px 14px;
}

.tree-node-group {
  min-width: 230px;
  padding: 9px 12px;
  border-color: #dce7d5;
  background: #fbfdf9;
}

.tree-node-member {
  min-width: 200px;
  min-height: 38px;
  justify-content: flex-start;
  display: block;
  padding: 8px 12px;
  border-color: #e1e8cf;
  background: #fdfef9;
  box-shadow: none;
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

  .organization-inline {
    grid-template-columns: 1fr;
  }

  .nested-table {
    margin-left: 0;
    width: 100%;
  }

  .toolbar-actions,
  .identity-side,
  .member-controls,
  .member-filter-row,
  .filter-bar,
  .taxonomy-add,
  .organization-toolbar,
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
  .filter-date-range,
  .filter-item {
    width: 100%;
  }

  .club-detail-header {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>

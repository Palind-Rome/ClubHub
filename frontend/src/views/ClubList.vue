<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import { Check, Close, Edit, Plus, Refresh, Search, User } from "@element-plus/icons-vue";
import { type AuthResponse, type AuthRole, onSessionChange, readAuth } from "../authSession";

type AuditStatus = "pending" | "approved" | "rejected";
type ReviewDecision = "approved" | "rejected";
type MemberStatus = "active" | "ended" | "suspended";
type TermMode = "create" | "edit";

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

interface ApiError {
  message?: string;
  title?: string;
}

const principalRoleCodes = new Set([
  "club_president",
  "club_leader",
  "club_admin",
  "club_manager",
  "president",
]);
const clubApplyPermission = "club:apply";
const clubReviewPermission = "club:review";
const clubInfoManagePermission = "club:info:manage";
const clubMemberManagePermission = "club:member:manage";
const clubOperationViewPermission = "club:operation:view";
const clubStatusManagePermission = "club:status:manage";

const auth = ref<AuthResponse | null>(readAuth());
const users = ref<UserSummary[]>([]);
const currentUserId = computed(() => auth.value?.user.id);
const clubs = ref<Club[]>([]);
const applications = ref<ClubApplication[]>([]);
const clubMembers = ref<ClubMemberRecord[]>([]);
const loading = ref(true);
const usersLoading = ref(true);
const memberLoading = ref(false);
const saving = ref(false);
const reviewing = ref(false);
const profileSaving = ref(false);
const termSaving = ref(false);
const error = ref("");
const activeTab = ref("workspace");
const selectedClubId = ref<number>();
const includeHistory = ref(false);

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
  contactPhone: "",
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
  termName: "",
  termStart: todayInput(),
  termEnd: "",
  memberStatus: "active" as MemberStatus,
  contributionScore: 0,
  closeCurrentTerm: true,
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

let stopSessionListener: (() => void) | null = null;

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
    canSubmitClubApplication: hasPermission(clubApplyPermission),
    canReviewClubApplication: hasPermission(clubReviewPermission),
  };
});
const isReviewer = computed(() => currentUser.value?.canReviewClubApplication ?? false);
const canSubmitApplication = computed(() => currentUser.value?.canSubmitClubApplication ?? false);
const canManageClubProfiles = computed(
  () =>
    isReviewer.value ||
    hasPermission(clubInfoManagePermission) ||
    hasPermission(clubStatusManagePermission),
);
const canManageMemberTerms = computed(
  () => isReviewer.value || hasPermission(clubMemberManagePermission),
);
const canViewMemberDirectory = computed(
  () => canManageMemberTerms.value || hasPermission(clubOperationViewPermission),
);
const myApplications = computed(() =>
  applications.value.filter((item) => item.applicantUserId === currentUserId.value),
);
const reviewApplications = computed(() =>
  isReviewer.value ? applications.value : myApplications.value,
);
const applicationRows = computed(() => reviewApplications.value);
const pendingCount = computed(
  () => applicationRows.value.filter((item) => item.auditStatus === "pending").length,
);
const approvedCount = computed(
  () => applicationRows.value.filter((item) => item.auditStatus === "approved").length,
);
const rejectedCount = computed(
  () => applicationRows.value.filter((item) => item.auditStatus === "rejected").length,
);
const selectedClub = computed(
  () => clubs.value.find((club) => club.id === selectedClubId.value) ?? null,
);
const manageableClubs = computed(() => clubs.value.filter((club) => canManageClub(club)));
const profileRows = computed(() => (canManageClubProfiles.value ? manageableClubs.value : []));
const memberViewClubs = computed(() =>
  canViewMemberDirectory.value ? clubs.value.filter((club) => canViewClubDirectory(club)) : [],
);
const canManageSelectedClub = computed(
  () => selectedClub.value !== null && canManageClub(selectedClub.value),
);
const myMemberships = computed(() => currentUser.value?.memberships ?? []);
const identityRows = computed(() => {
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
const advisorOptions = computed(() => users.value.filter((user) => isAdvisorCandidate(user)));
const visibleTabs = computed(() => {
  const tabs: string[] = [];
  if (canSubmitApplication.value || isReviewer.value) tabs.push("workspace");
  if (profileRows.value.length > 0) tabs.push("profile");
  if (memberViewClubs.value.length > 0) tabs.push("members");
  if (identityRows.value.length > 0) tabs.push("identity");
  return tabs;
});
const hasClubWorkspace = computed(() => visibleTabs.value.length > 0);
const metricCards = computed(() => {
  const cards: Array<{ label: string; value: number }> = [];

  if (canSubmitApplication.value || isReviewer.value) {
    cards.push(
      { label: "待审核", value: pendingCount.value },
      { label: "已通过", value: approvedCount.value },
      { label: "已退回", value: rejectedCount.value },
    );
  }

  if (profileRows.value.length > 0) {
    cards.push({ label: "可维护社团", value: profileRows.value.length });
  }

  if (identityRows.value.length > 0) {
    cards.push({ label: "我的社团身份", value: identityRows.value.length });
  }

  return cards;
});

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

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

async function loadUsers(options: { clubId?: number } = {}) {
  if (!currentUserId.value) {
    users.value = [];
    usersLoading.value = false;
    return;
  }

  usersLoading.value = true;
  try {
    const query = new URLSearchParams({ viewerUserId: String(currentUserId.value) });
    if (options.clubId) query.set("clubId", String(options.clubId));
    users.value = await requestJson<UserSummary[]>(`/api/users?${query.toString()}`);
  } catch (e) {
    error.value = e instanceof Error ? e.message : "用户加载失败";
  } finally {
    usersLoading.value = false;
  }
}

async function loadData() {
  if (!currentUserId.value) {
    loading.value = false;
    applications.value = [];
    clubs.value = [];
    clubMembers.value = [];
    return;
  }

  loading.value = true;
  error.value = "";
  try {
    const query = new URLSearchParams({ viewerUserId: String(currentUserId.value) });
    if (filters.auditStatus) query.set("auditStatus", filters.auditStatus);
    const shouldLoadApplications = canSubmitApplication.value || isReviewer.value;
    const shouldLoadClubs = canManageClubProfiles.value || canViewMemberDirectory.value;

    const [applicationData, clubData] = await Promise.all([
      shouldLoadApplications
        ? requestJson<ClubApplication[]>(`/api/clubs/applications?${query.toString()}`)
        : Promise.resolve([]),
      shouldLoadClubs
        ? requestJson<Club[]>(`/api/clubs?viewerUserId=${currentUserId.value}`)
        : Promise.resolve([]),
    ]);
    applications.value = applicationData;
    clubs.value = clubData;
    syncSelectedClub();
    await loadMembers();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
}

async function loadMembers() {
  if (!currentUserId.value || !selectedClubId.value || !canViewSelectedClub()) {
    clubMembers.value = [];
    return;
  }

  memberLoading.value = true;
  try {
    const query = new URLSearchParams({
      viewerUserId: String(currentUserId.value),
      includeHistory: String(includeHistory.value),
    });
    clubMembers.value = await requestJson<ClubMemberRecord[]>(
      `/api/clubs/${selectedClubId.value}/members?${query.toString()}`,
    );
  } catch (e) {
    clubMembers.value = [];
    ElMessage.error(e instanceof Error ? e.message : "成员任期加载失败");
  } finally {
    memberLoading.value = false;
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
  if (user.canReviewClubApplication) return true;
  if (club.presidentUserId === user.id) return true;

  const hasRole = user.roles.some(
    (role) =>
      roleCoversClub(role, club.id) && principalRoleCodes.has((role.roleCode ?? "").toLowerCase()),
  );
  const hasPrincipalMembership = user.memberships.some(
    (membership) =>
      membership.clubId === club.id &&
      isActiveStatus(membership.memberStatus) &&
      isPrincipalPosition(membership.positionName),
  );

  return hasRole || hasPrincipalMembership;
}

function canViewClubDirectory(club: Club) {
  if (isReviewer.value) return true;
  if (canManageClub(club)) return true;

  const user = currentUser.value;
  if (!user) return false;

  return user.roles.some(
    (role) => roleCoversClub(role, club.id) && (role.roleCode ?? "").toLowerCase() === "advisor",
  );
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

async function openProfileDialog(row: Club) {
  if (!canManageClub(row)) {
    ElMessage.warning("当前身份不能维护该社团档案。");
    return;
  }

  selectedClubId.value = row.id;
  await Promise.all([loadMembers(), loadUsers({ clubId: row.id })]);
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
  await profileFormRef.value.validate();

  profileSaving.value = true;
  try {
    await requestJson<Club>(`/api/clubs/${profileTarget.value.id}/profile`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentUserId: currentUserId.value,
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

function resetMemberTermForm() {
  memberTermForm.userId = undefined;
  memberTermForm.departmentName = "";
  memberTermForm.groupName = "";
  memberTermForm.positionName = "";
  memberTermForm.termName = `${new Date().getFullYear()} 年任期`;
  memberTermForm.termStart = todayInput();
  memberTermForm.termEnd = "";
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
  await loadUsers({ clubId: selectedClub.value.id });
  memberTermDialogVisible.value = true;
}

function openEditMemberTermDialog(row: ClubMemberRecord) {
  if (!canManageSelectedClub.value) {
    ElMessage.warning("当前身份不能维护该社团成员任期。");
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

async function submitMemberTerm() {
  if (!memberTermFormRef.value || !selectedClubId.value || !currentUserId.value) return;
  await memberTermFormRef.value.validate();

  termSaving.value = true;
  try {
    const payload = {
      currentUserId: currentUserId.value,
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
    await Promise.all([loadUsers(), loadData()]);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "任期保存失败");
  } finally {
    termSaving.value = false;
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

function todayInput() {
  return new Date().toISOString().slice(0, 10);
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

function statusStep(row: ClubApplication) {
  return row.auditStatus === "pending" ? 1 : 3;
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

function roleCoversClub(role: UserRoleSummary, clubId: number) {
  return role.clubId === clubId || Boolean(role.clubIds?.includes(clubId));
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

function hasPermission(permission: string) {
  const permissions = auth.value?.permissions ?? [];
  return permissions.includes("*") || permissions.includes(permission);
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

function goProfile() {
  activeTab.value = "profile";
}

function goMembers() {
  activeTab.value = "members";
  syncSelectedClub();
}

function memberOptionLabel(member: ClubMemberRecord) {
  const position = member.positionName ? ` / ${member.positionName}` : "";
  return `${member.userName}${position}`;
}

function isPrincipalPosition(positionName: string | null | undefined) {
  if (!positionName) return false;
  return (
    positionName.includes("负责人") ||
    positionName.includes("会长") ||
    positionName.includes("社长") ||
    positionName.toLowerCase().includes("president") ||
    positionName.toLowerCase().includes("leader")
  );
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

watch(currentUserId, () => {
  void loadData();
});

watch([selectedClubId, includeHistory], () => {
  void loadMembers();
});

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
        <h2>社团组织管理</h2>
        <div class="subtitle">社团注册审核、档案维护、成员任期与干部换届</div>
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
        <div class="identity-tags">
          <el-tag v-if="currentUser.canSubmitClubApplication" type="success" effect="plain">
            可提交注册申请
          </el-tag>
          <el-tag v-if="currentUser.canReviewClubApplication" type="warning" effect="plain">
            可审核注册申请
          </el-tag>
          <el-tag v-if="profileRows.length > 0" type="primary" effect="plain">
            可维护社团档案
          </el-tag>
          <el-tag v-if="memberViewClubs.length > 0" effect="plain">可查看成员任期</el-tag>
          <el-tag v-if="identityRows.length > 0" effect="plain">我的社团身份</el-tag>
        </div>
        <div class="identity-actions">
          <el-button v-if="profileRows.length > 0" type="primary" plain @click="goProfile">
            维护档案
          </el-button>
          <el-button v-if="memberViewClubs.length > 0" plain @click="goMembers">
            查看任期
          </el-button>
        </div>
      </div>
    </section>

    <section v-if="metricCards.length > 0" class="metrics">
      <div v-for="card in metricCards" :key="card.label" class="metric">
        <span>{{ card.label }}</span>
        <strong>{{ card.value }}</strong>
      </div>
    </section>

    <el-empty
      v-if="!hasClubWorkspace"
      description="当前账号暂无社团相关任务"
      class="empty-workspace"
    />

    <el-tabs v-else v-model="activeTab" class="workspace-tabs">
      <el-tab-pane v-if="canSubmitApplication || isReviewer" label="当前工作台" name="workspace">
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

      <el-tab-pane v-if="profileRows.length > 0" label="社团档案维护" name="profile">
        <div class="workspace-head">
          <div>
            <h3>社团档案</h3>
            <p>维护社团名称、类别、简介、负责人、指导老师和联系方式。</p>
          </div>
          <el-button :icon="Refresh" @click="loadData">刷新</el-button>
        </div>

        <el-table
          v-loading="loading"
          :data="profileRows"
          border
          stripe
          empty-text="暂无可见社团"
          row-key="id"
        >
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
          <el-table-column label="操作" width="140" fixed="right">
            <template #default="{ row }">
              <el-button
                type="primary"
                plain
                :icon="Edit"
                :disabled="!canManageClub(row)"
                @click="openProfileDialog(row)"
              >
                维护
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane v-if="memberViewClubs.length > 0" label="成员与任期" name="members">
        <el-empty v-if="memberViewClubs.length === 0" description="当前账号暂无可查看的社团任期" />

        <div v-else class="member-head">
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
            <el-switch v-model="includeHistory" active-text="包含历史" inactive-text="当前有效" />
          </div>
          <el-button
            v-if="canManageSelectedClub"
            type="primary"
            :icon="Plus"
            @click="openCreateMemberTermDialog"
          >
            新增任期
          </el-button>
        </div>

        <el-table
          v-if="memberViewClubs.length > 0"
          v-loading="memberLoading"
          :data="clubMembers"
          border
          stripe
          :empty-text="canManageSelectedClub ? '暂无任期记录，可新增成员任期' : '暂无成员任期记录'"
          row-key="memberId"
        >
          <el-table-column prop="userName" label="成员" min-width="150" />
          <el-table-column prop="studentNo" label="学号/工号" width="130" />
          <el-table-column prop="departmentName" label="部门" width="130" />
          <el-table-column prop="groupName" label="小组" width="120" />
          <el-table-column prop="positionName" label="职位" width="130" />
          <el-table-column prop="termName" label="任期" min-width="150" />
          <el-table-column label="任期开始" width="130">
            <template #default="{ row }">{{ formatDateOnly(row.termStart) }}</template>
          </el-table-column>
          <el-table-column label="任期结束" width="130">
            <template #default="{ row }">{{ formatDateOnly(row.termEnd) }}</template>
          </el-table-column>
          <el-table-column label="状态" width="130">
            <template #default="{ row }">
              <el-tag :type="memberStatusTagType(row.memberStatus)" effect="plain">
                {{ memberStatusText(row.memberStatus) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="当前" width="100">
            <template #default="{ row }">
              <el-tag :type="row.isCurrent ? 'success' : 'info'" effect="plain">
                {{ row.isCurrent ? "当前" : "历史" }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="contributionScore" label="贡献分" width="100" />
          <el-table-column v-if="canManageSelectedClub" label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button type="primary" plain :icon="Edit" @click="openEditMemberTermDialog(row)">
                编辑
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane v-if="identityRows.length > 0" label="我的社团身份" name="identity">
        <el-table :data="identityRows" border stripe empty-text="暂无社团成员身份">
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
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="applicationDialogVisible" title="提交社团注册申请" width="620px">
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

    <el-dialog v-model="reviewDialogVisible" title="审核社团注册申请" width="560px">
      <el-form ref="reviewFormRef" :model="reviewForm" :rules="reviewRules" label-width="90px">
        <el-form-item label="社团">
          <el-input :model-value="reviewTarget?.name" disabled />
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
            :loading="usersLoading"
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
      v-model="memberTermDialogVisible"
      :title="memberTermMode === 'create' ? '新增成员任期' : '编辑成员任期'"
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
            :loading="usersLoading"
            filterable
            placeholder="选择用户"
          >
            <el-option
              v-for="user in users"
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
          <el-input v-model="memberTermForm.departmentName" maxlength="60" />
        </el-form-item>
        <el-form-item label="小组">
          <el-input v-model="memberTermForm.groupName" maxlength="60" />
        </el-form-item>
        <el-form-item label="职位" prop="positionName">
          <el-input v-model="memberTermForm.positionName" maxlength="60" />
        </el-form-item>
        <el-form-item label="任期名称" prop="termName">
          <el-input v-model="memberTermForm.termName" maxlength="80" />
        </el-form-item>
        <el-form-item label="任期开始" prop="termStart">
          <el-date-picker
            v-model="memberTermForm.termStart"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择开始日期"
          />
        </el-form-item>
        <el-form-item label="任期结束">
          <el-date-picker
            v-model="memberTermForm.termEnd"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="未结束可留空"
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
        <el-form-item v-if="memberTermMode === 'create'" label="换届处理">
          <el-switch
            v-model="memberTermForm.closeCurrentTerm"
            active-text="关闭原有效任期"
            inactive-text="保留并行任期"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="memberTermDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="termSaving" @click="submitMemberTerm">
          保存任期
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
.metrics,
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

.identity-tags,
.identity-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.metric {
  min-height: 82px;
  padding: 16px 18px;
  border-right: 1px solid #e6edf4;
}

.metric:last-child {
  border-right: 0;
}

.metric span {
  display: block;
  color: #6a7682;
  font-size: 13px;
}

.metric strong {
  display: block;
  margin-top: 8px;
  font-size: 28px;
  line-height: 1;
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

.member-head {
  justify-content: space-between;
  padding: 18px 0 14px;
}

.club-selector {
  width: 280px;
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
  .member-head {
    align-items: stretch;
    flex-direction: column;
  }

  .toolbar-actions,
  .identity-side,
  .member-controls,
  .filter-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .identity-tags,
  .identity-actions {
    justify-content: flex-start;
  }

  .club-selector,
  .filter-item {
    width: 100%;
  }

  .metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .metric {
    border-right: 0;
    border-bottom: 1px solid #e6edf4;
  }
}
</style>

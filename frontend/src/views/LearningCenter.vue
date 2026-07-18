<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules,
  type UploadUserFile,
} from "element-plus";
import {
  CreateLearningItemRequestDownloadPermissionEnum,
  CreateLearningItemRequestItemStatusEnum,
  LearningItemItemStatusEnum,
  UpdateLearningItemRequestDownloadPermissionEnum,
  UpdateLearningItemRequestItemStatusEnum,
  type Club,
  type LearningItem,
  type LearningItemStatistics,
  type LearningRecord,
  type LearningTeacherCandidate,
} from "../api";
import { apiClient } from "../apiClient";
import { onSessionChange, readAuth, saveAuth, type AuthRole } from "../authSession";

const api = apiClient;

const itemTypeOptions = [
  { label: "课程", value: "course" },
  { label: "讲座", value: "lecture" },
  { label: "培训", value: "training" },
  { label: "视频", value: "video" },
  { label: "文档", value: "document" },
  { label: "资料", value: "material" },
];
const courseTypeOptions = itemTypeOptions.filter((option) =>
  ["course", "lecture", "training"].includes(option.value),
);
const resourceTypeOptions = itemTypeOptions.filter((option) =>
  ["video", "document", "material"].includes(option.value),
);
const visibilityOptions = [
  { label: "面向全校", value: "public" },
  { label: "仅本社团成员", value: "club" },
  { label: "仅上传人所在部门", value: "department" },
];
const downloadPermissionOptions = [
  { label: "允许直接下载", value: "allow" },
  { label: "禁止下载", value: "deny" },
  { label: "需要审批", value: "approval" },
];
const courseTypes = new Set(["course", "lecture", "training"]);

const permissionCodes = {
  ownRecordsView: "own:records:view",
  courseEnroll: "course:enroll",
  clubResourceView: "club:resource:view",
  clubOperationView: "club:operation:view",
  resourceUpload: "resource:upload",
  clubStatsView: "club:stats:view",
  globalStatsView: "stats:view",
  resourceReview: "resource:review",
  resourceDelete: "resource:delete",
} as const;
const clubScopedPermissions = new Set<string>([
  permissionCodes.clubResourceView,
  permissionCodes.clubOperationView,
  permissionCodes.resourceUpload,
  permissionCodes.clubStatsView,
]);
const recordStatus = {
  enrolled: "enrolled",
  learning: "learning",
  completed: "completed",
  cancelled: "cancelled",
} as const;
type ItemVisibility = "public" | "club" | "department";
type DownloadPermission = "allow" | "deny" | "approval";

const auth = ref(readAuth());
const clubs = ref<Club[]>([]);
const learningItems = ref<LearningItem[]>([]);
const learningRecords = ref<LearningRecord[]>([]);
const instructorLookupResult = ref<LearningTeacherCandidate | null>(null);
const instructorLookupError = ref("");
const loading = ref(false);
const recordLoading = ref(false);
const instructorLookupLoading = ref(false);
const saving = ref(false);
const enrollingId = ref<number | null>(null);
const cancellingId = ref<number | null>(null);
const progressSavingId = ref<number | null>(null);
const learningId = ref<number | null>(null);
const downloadingId = ref<number | null>(null);
const deletingId = ref<number | null>(null);
const previewDialogVisible = ref(false);
const previewLoading = ref(false);
const previewError = ref("");
const previewKind = ref<"image" | "video" | "pdf">("pdf");
const previewConverted = ref(false);
const previewUrl = ref("");
const previewItem = ref<LearningItem | null>(null);
const statistics = ref<LearningItemStatistics | null>(null);
const statisticsLoading = ref(false);
const learningSection = ref<"course" | "resource">("course");
const courseScope = ref<"mine" | "club" | "all">("mine");
const courseScopeRefreshVersion = ref(0);
const resourceScopeRefreshVersion = ref(0);
const courseStatusFilter = ref("all");
const courseCategoryFilter = ref("all");
const courseKeyword = ref("");
const resourceClubFilter = ref<number | "all">("all");
const resourceScope = ref<"mine" | "club" | "all">("all");
const resourceVisibilityFilter = ref<ItemVisibility | "all">("all");
const resourceStatusFilter = ref("all");
const resourceCategoryFilter = ref("all");
const resourceKeyword = ref("");
const courseDialogVisible = ref(false);
const detailDrawerVisible = ref(false);
const detailItem = ref<LearningItem | null>(null);
const uploadDialogVisible = ref(false);
const uploading = ref(false);
const uploadFiles = ref<UploadUserFile[]>([]);
const recordDialogVisible = ref(false);
const statisticsDialogVisible = ref(false);
const courseDialogMode = ref<"create" | "edit">("create");
const editingItemId = ref<number | null>(null);
const selectedRecordItemId = ref<number | undefined>();
const courseFormRef = ref<FormInstance>();
const progressFormRef = ref<FormInstance>();
let stopSessionChange: (() => void) | undefined;
let instructorLookupRequestId = 0;

const courseForm = reactive({
  clubId: null as number | null,
  title: "",
  description: "",
  instructorUserId: null as number | null,
  instructorUserNumber: "",
  itemType: "course",
  categoryName: "",
  fileUrl: "",
  startAt: null as Date | null,
  endAt: null as Date | null,
  capacity: null as number | null,
  visibility: "club" as ItemVisibility,
  downloadPermission: "deny" as DownloadPermission,
  itemStatus: "published",
});

const progressForm = reactive({
  recordId: 0,
  progress: 0,
  durationMinutes: 0,
});

const uploadForm = reactive({
  clubId: null as number | null,
  title: "",
  categoryName: "",
  description: "",
  visibility: "club" as ItemVisibility,
  downloadPermission: "allow" as DownloadPermission,
});

const courseRules: FormRules<typeof courseForm> = {
  clubId: [{ required: true, message: "请选择发布社团", trigger: "change" }],
  title: [
    { required: true, message: "请输入课程名称", trigger: "blur" },
    { min: 1, max: 100, message: "课程名称不能超过 100 个字符", trigger: "blur" },
  ],
  itemType: [{ required: true, message: "请选择资源类型", trigger: "change" }],
  fileUrl: [{ validator: validateFileUrl, trigger: "blur" }],
  startAt: [{ validator: validateStartAt, trigger: "change" }],
  endAt: [
    {
      validator: (_rule, value: Date | null, callback) => {
        if (value && courseForm.startAt && value <= courseForm.startAt) {
          callback(new Error("结束时间必须晚于开始时间"));
          return;
        }
        callback();
      },
      trigger: "change",
    },
  ],
  capacity: [{ validator: validateCapacity, trigger: "change" }],
  visibility: [{ required: true, message: "请选择可见范围", trigger: "change" }],
  downloadPermission: [{ required: true, message: "请选择下载设置", trigger: "change" }],
  itemStatus: [{ required: true, message: "请选择发布状态", trigger: "change" }],
};

const progressRules: FormRules<typeof progressForm> = {
  progress: [
    { required: true, message: "请输入学习进度", trigger: "change" },
    {
      type: "number",
      min: 0,
      max: 100,
      message: "学习进度必须在 0 到 100 之间",
      trigger: "change",
    },
  ],
  durationMinutes: [{ type: "number", min: 0, message: "学习时长不能为负数", trigger: "change" }],
};

const currentUserId = computed(() => auth.value?.user.id ?? null);
const currentRoles = computed(() => auth.value?.roles ?? []);
const isCourseAdministrator = computed(() =>
  currentRoles.value.some((role) => {
    const roleCode = role.code?.trim().toUpperCase();
    return roleCode === "CLUB_ADMIN" || roleCode === "SYSTEM_ADMIN";
  }),
);
const courseScopeOptions = computed(() => [
  { label: "我的课程", value: "mine" },
  ...(isCourseAdministrator.value ? [] : [{ label: "社内课程", value: "club" }]),
  { label: "全部课程", value: "all" },
]);
const clubNameMap = computed(() => new Map(clubs.value.map((club) => [club.id, club.name])));
const itemMap = computed(() => new Map(learningItems.value.map((item) => [item.id, item])));
const manageableClubs = computed(() => clubs.value.filter((club) => canCreateForClub(club.id)));
const canViewOwnRecords = computed(() => hasPermission(permissionCodes.ownRecordsView));
const canEnrollCourses = computed(() => hasPermission(permissionCodes.courseEnroll));
const isCourseForm = computed(() => courseTypes.has(courseForm.itemType));
const courseCategoryOptions = computed(() =>
  [
    ...new Set(
      learningItems.value
        .filter(isCourseItem)
        .map((item) => item.categoryName?.trim())
        .filter((value): value is string => Boolean(value)),
    ),
  ].sort(),
);
const resourceCategoryOptions = computed(() =>
  [
    ...new Set(
      learningItems.value
        .filter((item) => !isCourseItem(item))
        .map((item) => item.categoryName?.trim())
        .filter((value): value is string => Boolean(value)),
    ),
  ].sort(),
);
const filteredItems = computed(() => {
  const courseSection = learningSection.value === "course";
  const search = (courseSection ? courseKeyword.value : resourceKeyword.value).trim().toLowerCase();
  const status = courseSection ? courseStatusFilter.value : resourceStatusFilter.value;
  const category = courseSection ? courseCategoryFilter.value : resourceCategoryFilter.value;
  return learningItems.value.filter((item) => {
    const course = isCourseItem(item);
    if (status !== "all" && item.itemStatus !== status) return false;
    if (courseSection !== course) return false;
    if (courseSection && courseScope.value === "mine") {
      const status = item.currentUserRecordStatus;
      const hasActiveRecord = Boolean(
        status && status !== "none" && status !== recordStatus.cancelled,
      );
      const isCurrentUserInstructor = item.instructorUserId === currentUserId.value;
      if (!hasActiveRecord && !isCurrentUserInstructor) return false;
    }
    if (courseSection && courseScope.value === "club" && item.visibility === "public") {
      return false;
    }
    if (
      !courseSection &&
      resourceScope.value === "mine" &&
      item.uploaderUserId !== currentUserId.value
    ) {
      return false;
    }
    if (!courseSection && resourceScope.value === "club" && item.visibility === "public") {
      return false;
    }
    if (
      !courseSection &&
      resourceClubFilter.value !== "all" &&
      item.clubId !== resourceClubFilter.value
    ) {
      return false;
    }
    if (
      !courseSection &&
      resourceVisibilityFilter.value !== "all" &&
      item.visibility !== resourceVisibilityFilter.value
    ) {
      return false;
    }
    if (category !== "all" && item.categoryName !== category) return false;
    if (!search) return true;
    return [item.title, item.categoryName, item.description]
      .filter(Boolean)
      .some((value) => value!.toLowerCase().includes(search));
  });
});
const courseDialogTitle = computed(() =>
  courseDialogMode.value === "create"
    ? "发布课程"
    : isCourseForm.value
      ? "编辑课程"
      : "编辑资源信息",
);
const recordDialogTitle = computed(() => {
  const item = selectedRecordItemId.value
    ? itemMap.value.get(selectedRecordItemId.value)
    : undefined;
  if (!item) return learningSection.value === "course" ? "我的课程记录" : "我的资源学习";
  return canViewRecords(item) ? `${item.title} - 学习用户` : `${item.title} - 学习记录`;
});
const visibleLearningRecords = computed(() => {
  if (selectedRecordItemId.value) return learningRecords.value;
  const courseSection = learningSection.value === "course";
  return learningRecords.value.filter((record) => {
    const item = itemMap.value.get(record.itemId);
    return item ? isCourseItem(item) === courseSection : false;
  });
});

/**
 * 表格 key 包含当前课程范围。切换“我的课程 / 社内课程 / 全部课程”时，
 * 让 Element Plus 表格立即重建，避免沿用上一个范围的行缓存和布局状态。
 */
const learningTableKey = computed(() =>
  learningSection.value === "course"
    ? `course-${courseScope.value}-${courseScopeRefreshVersion.value}`
    : `resource-${resourceScope.value}-${resourceScopeRefreshVersion.value}`,
);

/** 将课程状态转换为用户可读文本。 */
function statusLabel(status?: string | null, item?: LearningItem) {
  switch (status) {
    case "pending_review":
      return "待审核";
    case "rejected":
      return "审核驳回";
    case LearningItemItemStatusEnum.Published:
      return item && !isCourseItem(item) ? "已发布" : "开放加入";
    case LearningItemItemStatusEnum.Closed:
      return item && !isCourseItem(item) ? "已下架" : "已停止加入";
    case LearningItemItemStatusEnum.Finished:
      return "已结束";
    default:
      return "草稿";
  }
}

/** 判断条目是否为需要报名和排期的课程类资源。 */
function isCourseItem(item: Pick<LearningItem, "itemType">) {
  return courseTypes.has(item.itemType ?? "");
}

/** 非课程资源要求填写 HTTP/HTTPS 文件地址。 */
function validateFileUrl(_rule: unknown, value: string, callback: (error?: Error) => void) {
  const normalized = value.trim();
  if (!isCourseForm.value && !normalized) {
    callback(new Error("视频、文档和资料必须填写文件地址"));
    return;
  }
  const isInternalRef =
    normalized.startsWith("/api/learning/items/") ||
    normalized.startsWith("clubs/") ||
    normalized.startsWith("oss://");
  if (normalized && !/^https?:\/\/\S+$/i.test(normalized) && !isInternalRef) {
    callback(new Error("文件地址必须以 http:// 或 https:// 开头"));
    return;
  }
  callback();
}

/** 课程类型要求填写开始时间。 */
function validateStartAt(_rule: unknown, value: Date | null, callback: (error?: Error) => void) {
  if (isCourseForm.value && !value) {
    callback(new Error("课程类型必须选择开始时间"));
    return;
  }
  callback();
}

/** 课程类型要求填写正数容量。 */
function validateCapacity(_rule: unknown, value: number | null, callback: (error?: Error) => void) {
  if (isCourseForm.value && (!value || value <= 0)) {
    callback(new Error("课程容量必须大于 0"));
    return;
  }
  callback();
}

/** 将学习记录状态转换为用户可读文本。 */
function recordStatusLabel(status?: string | null) {
  switch (status) {
    case recordStatus.learning:
      return "学习中";
    case recordStatus.completed:
      return "已完成";
    case recordStatus.cancelled:
      return "已取消";
    case recordStatus.enrolled:
      return "已加入";
    default:
      return "未加入";
  }
}

/** 课程展示加入状态，资源展示学习进度，避免把资源描述成“加入”。 */
function itemRecordStatusLabel(item: LearningItem) {
  if (isCourseItem(item)) return recordStatusLabel(item.currentUserRecordStatus);
  switch (item.currentUserRecordStatus) {
    case recordStatus.learning:
      return "学习中";
    case recordStatus.completed:
      return "已完成";
    default:
      return "未开始";
  }
}

/** 返回资源类型的展示名称。 */
function itemTypeLabel(value?: string | null) {
  return itemTypeOptions.find((option) => option.value === value)?.label ?? value ?? "资源";
}

/** 返回资源可见范围的展示名称。 */
function visibilityLabel(value?: string | null) {
  return visibilityOptions.find((option) => option.value === value)?.label ?? "仅本社团成员";
}

/** 返回下载设置的展示名称。 */
function downloadPermissionLabel(value?: string | null) {
  return downloadPermissionOptions.find((option) => option.value === value)?.label ?? "禁止下载";
}

/** 判断当前用户是否能为指定社团发布课程。 */
function canCreateForClub(clubId: number) {
  return hasPermission(permissionCodes.resourceUpload, clubId);
}

/** 判断当前用户是否可以查看指定资源的匿名统计。 */
function canViewStatistics(item: LearningItem) {
  return (
    hasPermission(permissionCodes.resourceUpload, item.clubId) ||
    hasPermission(permissionCodes.clubStatsView, item.clubId) ||
    hasPermission(permissionCodes.clubOperationView, item.clubId) ||
    hasPermission(permissionCodes.globalStatsView)
  );
}

/** 判断当前用户是否可以查看指定资源的实名学习名单。 */
function canViewRecords(item: LearningItem) {
  return (
    hasPermission(permissionCodes.resourceUpload, item.clubId) ||
    hasPermission(permissionCodes.clubStatsView, item.clubId) ||
    hasPermission(permissionCodes.clubOperationView, item.clubId)
  );
}

function canReviewItem(item: LearningItem) {
  return (
    String(item.itemStatus) === "pending_review" &&
    hasPermission(permissionCodes.resourceReview, item.clubId)
  );
}

function canDeleteItem(item: LearningItem) {
  return item.canManage || hasPermission(permissionCodes.resourceDelete, item.clubId);
}

/** 打开资源上传窗口，并默认选中首个可管理社团。 */
function openUploadDialog() {
  if (!currentUserId.value) {
    ElMessage.warning("请先登录后再上传资源");
    return;
  }
  if (manageableClubs.value.length === 0) {
    ElMessage.warning("当前账号没有可上传资源的社团");
    return;
  }
  uploadForm.clubId = manageableClubs.value[0]?.id ?? null;
  uploadForm.title = "";
  uploadForm.categoryName = "";
  uploadForm.description = "";
  uploadForm.visibility = "club";
  uploadForm.downloadPermission = "allow";
  uploadFiles.value = [];
  uploadDialogVisible.value = true;
}

/** 将拖入队列的文件逐个提交，单个失败不会阻止其余文件继续上传。 */
async function uploadPendingResources() {
  if (!uploadForm.clubId) {
    ElMessage.warning("请选择文件所属社团");
    return;
  }
  const queuedFiles = uploadFiles.value.flatMap((entry) => (entry.raw ? [entry.raw] : []));
  if (queuedFiles.length === 0) {
    ElMessage.warning("请拖入或选择至少一个文件");
    return;
  }

  const oversized = queuedFiles.find((file) => file.size > 50 * 1024 * 1024);
  if (oversized) {
    ElMessage.warning(`“${oversized.name}”超过 50 MB，无法上传`);
    return;
  }

  uploading.value = true;
  let succeeded = 0;
  const failed: string[] = [];
  try {
    for (const file of queuedFiles) {
      const formData = new FormData();
      formData.append("clubId", String(uploadForm.clubId));
      formData.append("file", file, file.name);
      if (queuedFiles.length === 1 && uploadForm.title.trim()) {
        formData.append("title", uploadForm.title.trim());
      }
      formData.append("categoryName", uploadForm.categoryName.trim());
      formData.append("description", uploadForm.description.trim());
      formData.append("visibility", uploadForm.visibility);
      formData.append("downloadPermission", uploadForm.downloadPermission);

      try {
        const response = await fetch("/api/learning/resources/upload", {
          method: "POST",
          headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
          body: formData,
        });
        if (!response.ok) {
          const message = await response.text();
          throw new Error(message || `HTTP ${response.status}`);
        }
        succeeded += 1;
      } catch {
        failed.push(file.name);
      }
    }

    if (succeeded > 0) await loadLearningItems();
    if (failed.length === 0) {
      ElMessage.success(`已上传并提交审核 ${succeeded} 个文件`);
      uploadDialogVisible.value = false;
      uploadFiles.value = [];
    } else {
      ElMessage.warning(`成功 ${succeeded} 个，失败 ${failed.length} 个：${failed.join("、")}`);
    }
  } finally {
    uploading.value = false;
  }
}

/** 打开当前课程或资源的只读详情。 */
function openItemDetail(item: LearningItem) {
  detailItem.value = item;
  detailDrawerVisible.value = true;
}

/** 建立短时预览会话；内容请求只携带 HttpOnly Cookie，不暴露登录令牌或 OSS 地址。 */
async function openPreview(item: LearningItem) {
  if (isCourseItem(item)) {
    ElMessage.warning("培训课程没有可在线预览的资源文件");
    return;
  }

  previewItem.value = item;
  previewError.value = "";
  previewConverted.value = false;
  previewUrl.value = "";
  previewLoading.value = true;
  previewDialogVisible.value = true;
  try {
    const response = await fetch(`/api/learning/items/${item.id}/preview-session`, {
      method: "POST",
      credentials: "same-origin",
      headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
    });
    if (!response.ok) {
      const message = (await response.text()).replace(/^"|"$/g, "");
      throw new Error(message || `在线预览准备失败：HTTP ${response.status}`);
    }

    const kind = response.headers.get("X-ClubHub-Preview-Kind");
    previewKind.value = kind === "image" || kind === "video" ? kind : "pdf";
    previewConverted.value = response.headers.get("X-ClubHub-Preview-Converted") === "true";
    previewUrl.value = `/api/learning/items/${item.id}/preview?v=${Date.now()}`;
  } catch (error) {
    previewLoading.value = false;
    previewError.value = toErrorMessage(error, "在线预览准备失败，请稍后重试");
  }
}

function markPreviewReady() {
  previewLoading.value = false;
}

function markPreviewFailed() {
  previewLoading.value = false;
  previewError.value = previewConverted.value
    ? "Office 文档转换后的预览加载失败，请稍后重试或在获得权限后下载查看。"
    : "预览内容加载失败，请检查网络后重试。";
}

function clearPreview() {
  previewUrl.value = "";
  previewItem.value = null;
  previewError.value = "";
  previewLoading.value = false;
}

/** 确认后删除有权管理的课程或资源，并刷新当前列表。 */
async function deleteResource(item: LearningItem) {
  if (!canDeleteItem(item)) return;
  try {
    await ElMessageBox.confirm(
      `确认删除${isCourseItem(item) ? "课程" : "资源"}“${item.title}”吗？相关学习记录和已上传文件也会被删除，此操作不可撤销。`,
      `删除${isCourseItem(item) ? "课程" : "资源"}`,
      {
        confirmButtonText: "确认删除",
        cancelButtonText: "取消",
        type: "warning",
      },
    );
  } catch {
    return;
  }

  deletingId.value = item.id;
  try {
    const response = await fetch(`/api/learning/items/${item.id}`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
    });
    if (!response.ok) {
      const message = await response.text();
      throw new Error(message || `HTTP ${response.status}`);
    }
    ElMessage.success(`${isCourseItem(item) ? "课程" : "资源"}已删除`);
    if (detailItem.value?.id === item.id) detailDrawerVisible.value = false;
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "资源删除失败"));
  } finally {
    deletingId.value = null;
  }
}

/** 判断角色授权范围是否覆盖指定社团。 */
function roleCoversClub(role: AuthRole, clubId: number) {
  return role.clubId === clubId || role.clubIds.includes(clubId);
}

async function reviewItem(item: LearningItem, approved: boolean) {
  if (!canReviewItem(item)) return;
  try {
    await ElMessageBox.confirm(
      approved ? `确认通过并发布“${item.title}”？` : `确认驳回“${item.title}”？`,
      approved ? "审核通过" : "审核驳回",
      {
        confirmButtonText: "确认",
        cancelButtonText: "取消",
        type: approved ? "success" : "warning",
      },
    );
  } catch {
    return;
  }

  try {
    await api.reviewLearningItem({
      itemId: item.id,
      reviewLearningItemRequest: { result: approved ? "approved" : "rejected" },
    });
    ElMessage.success(approved ? "审核通过，内容已发布" : "已驳回该内容");
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "审核失败"));
  }
}

/** 按权限编码和社团作用域判断单个角色是否授权。 */
function roleAllowsPermission(role: AuthRole, permission: string, clubId?: number) {
  if (role.permissions.includes("*")) return true;
  if (!role.permissions.includes(permission)) return false;
  if (clubScopedPermissions.has(permission)) {
    return clubId !== undefined && roleCoversClub(role, clubId);
  }
  if (role.scope === "system") return true;
  return clubId !== undefined && roleCoversClub(role, clubId);
}

/** 按多角色权限并集判断当前会话是否授权。 */
function hasPermission(permission: string, clubId?: number) {
  return currentRoles.value.some((role) => roleAllowsPermission(role, permission, clubId));
}

/** 将 UTC 时间固定转换为北京时间展示。 */
function formatDate(value?: Date | string | null) {
  if (!value) return "未设置";
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return "未设置";
  return date.toLocaleString("zh-CN", {
    hour12: false,
    timeZone: "Asia/Shanghai",
  });
}

/** 将秒数转换为便于阅读的分钟数。 */
function formatDuration(seconds?: number | null) {
  if (!seconds) return "0 分钟";
  return `${Math.round(seconds / 60)} 分钟`;
}

/** 组合课程成员姓名与学工号。 */
function participantLabel(record: LearningRecord) {
  const name = record.userDisplayName || `用户 ${record.userId}`;
  return record.userNumber ? `${name}（${record.userNumber}）` : name;
}

/** 判断学习记录是否允许当前用户继续更新进度。 */
function canUpdateRecord(record: LearningRecord) {
  if (record.userId !== currentUserId.value) return false;
  if (
    record.enrollStatus === recordStatus.cancelled ||
    record.enrollStatus === recordStatus.completed
  ) {
    return false;
  }
  const item = itemMap.value.get(record.itemId);
  if (!item) return false;
  if (!isCourseItem(item)) return item.itemStatus === LearningItemItemStatusEnum.Published;
  if (!item.startAt) return false;
  return new Date(item.startAt).getTime() <= Date.now();
}

/** 执行表单校验并将失败统一转换为 false。 */
async function validateForm(form?: FormInstance) {
  if (!form) return false;
  return form.validate().catch(() => false);
}

/** 加载当前用户可见的社团列表。 */
async function loadClubs() {
  if (!currentUserId.value) {
    clubs.value = [];
    return;
  }

  try {
    clubs.value = await api.getClubs();
  } catch (error) {
    clubs.value = [];
    ElMessage.error(toErrorMessage(error, "社团列表加载失败"));
  }
}

/** 加载当前用户可见的课程及加入状态。 */
async function loadLearningItems() {
  if (!currentUserId.value) {
    learningItems.value = [];
    return;
  }

  loading.value = true;
  try {
    learningItems.value = await api.getLearningItems({});
  } catch (error) {
    learningItems.value = [];
    ElMessage.error(toErrorMessage(error, "课程列表加载失败"));
  } finally {
    loading.value = false;
  }
}

/** 课程范围变化时重新拉取数据，并刷新表格实例。 */
watch(courseScope, async () => {
  await loadLearningItems();
  await nextTick();
  courseScopeRefreshVersion.value += 1;
});

/** 管理员不提供“社内课程”范围；身份切换后回退到“全部课程”。 */
watch(isCourseAdministrator, (isAdministrator) => {
  if (isAdministrator && courseScope.value === "club") courseScope.value = "all";
});

/** 资源范围变化时重新拉取数据，并刷新表格实例。 */
watch(resourceScope, async () => {
  await loadLearningItems();
  await nextTick();
  resourceScopeRefreshVersion.value += 1;
});

/** 清除已查询到的授课人，仅在输入新学工号后重新确定授课人。 */
function resetInstructorLookup(clearSelectedInstructor = true) {
  instructorLookupRequestId += 1;
  instructorLookupResult.value = null;
  instructorLookupError.value = "";
  instructorLookupLoading.value = false;
  if (clearSelectedInstructor) courseForm.instructorUserId = null;
}

/** 输入新学工号后，使之前查询结果失效。 */
function handleInstructorNumberInput() {
  resetInstructorLookup();
}

/** 按学工号精确查询授课人；授课人可选，但填写后必须是正常状态的教师或学生。 */
async function lookupInstructor() {
  const userNumber = courseForm.instructorUserNumber.trim();
  resetInstructorLookup(false);
  if (!userNumber) return;
  if (!courseForm.clubId) {
    instructorLookupError.value = "请先选择发布社团，再查询授课人。";
    return;
  }

  const requestId = ++instructorLookupRequestId;
  instructorLookupLoading.value = true;
  try {
    const result = await api.getLearningInstructorByUserNumber({
      clubId: courseForm.clubId,
      userNumber,
    });
    if (requestId !== instructorLookupRequestId) return;

    courseForm.instructorUserId = result.id;
    instructorLookupResult.value = result;
  } catch (error) {
    if (requestId !== instructorLookupRequestId) return;

    courseForm.instructorUserId = null;
    instructorLookupError.value = toErrorMessage(
      error,
      "未找到正常状态的教师或学生账号，请核对学号或工号。",
    );
  } finally {
    if (requestId === instructorLookupRequestId) instructorLookupLoading.value = false;
  }
}

/** 社团切换后清空已查询的授课人，避免跨社团误用查询结果。 */
function handleCourseClubChange() {
  courseForm.instructorUserNumber = "";
  resetInstructorLookup();
}

/** 编辑条目类型切换时清理不适用字段。新建入口仅允许课程类型。 */
function handleItemTypeChange() {
  if (isCourseForm.value) {
    courseForm.fileUrl = "";
    courseForm.downloadPermission = "deny";
    return;
  }
  courseForm.instructorUserId = null;
  courseForm.instructorUserNumber = "";
  courseForm.startAt = null;
  courseForm.endAt = null;
  courseForm.capacity = null;
  courseForm.downloadPermission = "allow";
  resetInstructorLookup(false);
  courseFormRef.value?.clearValidate();
}

/** 重置课程表单为新建课程的默认值。 */
function resetCourseForm() {
  courseForm.clubId = manageableClubs.value[0]?.id ?? null;
  courseForm.title = "";
  courseForm.description = "";
  courseForm.instructorUserId = null;
  courseForm.instructorUserNumber = "";
  resetInstructorLookup(false);
  courseForm.itemType = "course";
  courseForm.categoryName = "";
  courseForm.fileUrl = "";
  courseForm.startAt = null;
  courseForm.endAt = null;
  courseForm.capacity = null;
  courseForm.visibility = "club";
  courseForm.downloadPermission = "deny";
  courseForm.itemStatus = "published";
  courseFormRef.value?.clearValidate();
}

/** 校验发布权限后打开课程创建对话框。 */
async function openCreateDialog() {
  if (!currentUserId.value) {
    ElMessage.warning("请先登录后再发布课程");
    return;
  }
  if (manageableClubs.value.length === 0) {
    ElMessage.warning("当前账号没有可发布课程的社团");
    return;
  }

  courseDialogMode.value = "create";
  editingItemId.value = null;
  resetCourseForm();
  courseDialogVisible.value = true;
}

/** 将课程数据填入表单并打开编辑对话框。 */
async function openEditDialog(item: LearningItem) {
  if (!item.canManage) {
    ElMessage.warning("当前账号没有修改该课程的权限");
    return;
  }

  courseDialogMode.value = "edit";
  editingItemId.value = item.id;
  courseForm.clubId = item.clubId;
  courseForm.title = item.title;
  courseForm.description = item.description ?? "";
  courseForm.instructorUserId = item.instructorUserId ?? null;
  courseForm.instructorUserNumber = "";
  resetInstructorLookup(false);
  courseForm.itemType = item.itemType ?? "course";
  courseForm.categoryName = item.categoryName ?? "";
  courseForm.fileUrl = item.fileUrl ?? "";
  courseForm.startAt = item.startAt ? new Date(item.startAt) : null;
  courseForm.endAt = item.endAt ? new Date(item.endAt) : null;
  courseForm.capacity = item.capacity ?? null;
  courseForm.visibility = item.visibility;
  courseForm.downloadPermission = item.downloadPermission;
  courseForm.itemStatus = ["published", "closed"].includes(String(item.itemStatus))
    ? String(item.itemStatus)
    : "draft";
  courseFormRef.value?.clearValidate();
  courseDialogVisible.value = true;
}

/** 校验并提交课程创建或更新请求。 */
async function submitCourse() {
  if (!currentUserId.value) return;
  if (!(await validateForm(courseFormRef.value))) return;
  if (!courseForm.clubId) return;
  if (isCourseForm.value && (!courseForm.startAt || !courseForm.capacity)) return;

  saving.value = true;
  try {
    const commonFields = {
      title: courseForm.title.trim(),
      description: courseForm.description.trim() || undefined,
      instructorUserId: isCourseForm.value ? courseForm.instructorUserId : undefined,
      itemType: courseForm.itemType,
      categoryName: courseForm.categoryName.trim() || undefined,
      fileUrl: isCourseForm.value ? undefined : courseForm.fileUrl.trim() || undefined,
      startAt: isCourseForm.value ? courseForm.startAt : undefined,
      endAt: isCourseForm.value ? (courseForm.endAt ?? undefined) : undefined,
      capacity: isCourseForm.value ? courseForm.capacity : undefined,
      visibility: courseForm.visibility,
      downloadPermission: isCourseForm.value ? "deny" : courseForm.downloadPermission,
    };

    if (courseDialogMode.value === "edit" && editingItemId.value) {
      await api.updateLearningItem({
        itemId: editingItemId.value,
        updateLearningItemRequest: {
          ...commonFields,
          downloadPermission:
            courseForm.downloadPermission as UpdateLearningItemRequestDownloadPermissionEnum,
          itemStatus: courseForm.itemStatus as UpdateLearningItemRequestItemStatusEnum,
        },
      });
      ElMessage.success(isCourseForm.value ? "课程信息已更新" : "资源信息已更新");
    } else {
      await api.createLearningItem({
        createLearningItemRequest: {
          ...commonFields,
          clubId: courseForm.clubId,
          downloadPermission:
            courseForm.downloadPermission as CreateLearningItemRequestDownloadPermissionEnum,
          itemStatus: courseForm.itemStatus as CreateLearningItemRequestItemStatusEnum,
        },
      });
      ElMessage.success("课程已保存");
    }

    courseDialogVisible.value = false;
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "课程保存失败"));
  } finally {
    saving.value = false;
  }
}

/** 为当前用户加入课程。 */
async function enroll(item: LearningItem) {
  if (!currentUserId.value) return;
  if (!item.canEnroll) {
    ElMessage.warning(item.enrollmentUnavailableReason || "当前不能加入该课程");
    return;
  }

  enrollingId.value = item.id;
  try {
    await api.enrollLearningItem({
      itemId: item.id,
    });
    ElMessage.success("已加入课程");
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "加入课程失败"));
  } finally {
    enrollingId.value = null;
  }
}

/** 确认后退出当前用户已加入的课程。 */
async function cancelEnrollment(item: LearningItem) {
  if (!currentUserId.value || !item.canCancelEnrollment) return;

  try {
    await ElMessageBox.confirm(
      `确认退出“${item.title}”吗？退出后课程名额会立即释放。`,
      "退出课程",
      {
        confirmButtonText: "确认退出",
        cancelButtonText: "继续学习",
        type: "warning",
      },
    );
  } catch {
    return;
  }

  cancellingId.value = item.id;
  try {
    await api.cancelLearningEnrollment({
      itemId: item.id,
    });
    ElMessage.success("已退出课程");
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "退出课程失败"));
  } finally {
    cancellingId.value = null;
  }
}

/** 为视频、文档或资料创建或恢复当前用户的学习记录。 */
async function startLearning(item: LearningItem) {
  if (!item.canStartLearning) {
    ElMessage.warning(item.learningUnavailableReason || "当前不能学习该资源");
    return;
  }

  learningId.value = item.id;
  try {
    await api.startLearningItem({ itemId: item.id });
    ElMessage.success("学习记录已创建，正在打开资源内容");
    await loadLearningItems();
    await openPreview(item);
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "开始学习失败"));
  } finally {
    learningId.value = null;
  }
}

/** 通过权限校验后记录下载行为并打开文件地址。 */
async function downloadItem(item: LearningItem) {
  if (!item.canDownload) {
    ElMessage.warning(item.downloadUnavailableReason || "当前不能下载该资源");
    return;
  }

  downloadingId.value = item.id;
  const opened = window.open("about:blank", "_blank");
  if (opened) opened.opener = null;
  try {
    const result = await api.downloadLearningItem({ itemId: item.id });
    if (result.fileUrl.startsWith("/api/learning/items/") && result.fileUrl.endsWith("/file")) {
      opened?.close();
      const response = await fetch(result.fileUrl, {
        headers: { Authorization: `Bearer ${auth.value?.token ?? ""}` },
      });
      if (!response.ok) throw new Error("文件内容获取失败");
      const objectUrl = URL.createObjectURL(await response.blob());
      const link = document.createElement("a");
      link.href = objectUrl;
      link.download = item.title;
      link.click();
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1_000);
    } else if (opened) {
      opened.location.replace(result.fileUrl);
    } else {
      const link = document.createElement("a");
      link.href = result.fileUrl;
      link.download = item.title;
      link.click();
    }
    ElMessage.success("下载权限校验通过，已记录本次下载");
    await loadLearningItems();
  } catch (error) {
    opened?.close();
    ElMessage.error(toErrorMessage(error, "资源下载失败"));
  } finally {
    downloadingId.value = null;
  }
}

/** 加载管理者可查看的资源学习统计。 */
async function openStatistics(item: LearningItem) {
  if (!canViewStatistics(item)) return;
  statistics.value = null;
  statisticsDialogVisible.value = true;
  statisticsLoading.value = true;
  try {
    statistics.value = await api.getLearningItemStatistics({ itemId: item.id });
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "学习统计加载失败"));
    statisticsDialogVisible.value = false;
  } finally {
    statisticsLoading.value = false;
  }
}

/** 打开个人学习记录或课程成员名单。 */
async function openRecords(item?: LearningItem) {
  if (!currentUserId.value) {
    ElMessage.warning("请先登录后查看学习记录");
    return;
  }
  if (!item && !canViewOwnRecords.value) {
    ElMessage.warning("当前账号没有查看个人学习记录的权限");
    return;
  }
  if (item && !canViewRecords(item) && item.currentUserRecordStatus === "none") {
    ElMessage.warning("当前账号没有查看该资源学习名单的权限");
    return;
  }

  selectedRecordItemId.value = item?.id;
  recordDialogVisible.value = true;
  progressForm.recordId = 0;
  await loadRecords();
}

/** 加载个人学习记录或指定课程成员名单。 */
async function loadRecords() {
  if (!currentUserId.value) return;

  recordLoading.value = true;
  try {
    learningRecords.value = await api.getLearningRecords({
      itemId: selectedRecordItemId.value,
    });
  } catch (error) {
    learningRecords.value = [];
    ElMessage.error(toErrorMessage(error, "学习记录加载失败"));
  } finally {
    recordLoading.value = false;
  }
}

/** 将可更新记录载入进度编辑表单。 */
function beginProgress(record: LearningRecord) {
  if (!canUpdateRecord(record)) return;
  progressForm.recordId = record.id;
  progressForm.progress = record.progress ?? 0;
  progressForm.durationMinutes = Math.round((record.durationSeconds ?? 0) / 60);
}

/** 提交当前用户的学习进度和累计时长。 */
async function submitProgress() {
  if (!currentUserId.value) return;
  if (!(await validateForm(progressFormRef.value))) return;

  progressSavingId.value = progressForm.recordId;
  try {
    await api.updateLearningProgress({
      recordId: progressForm.recordId,
      updateLearningProgressRequest: {
        progress: progressForm.progress,
        durationSeconds: Math.round(progressForm.durationMinutes * 60),
      },
    });
    ElMessage.success("学习进度已更新");
    progressForm.recordId = 0;
    await loadRecords();
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "学习进度更新失败"));
  } finally {
    progressSavingId.value = null;
  }
}

/** 刷新登录态以及课程页面的基础数据。 */
async function refreshAll() {
  auth.value = readAuth();
  await loadClubs();
  await loadLearningItems();
}

/** 从网络异常中提取适合界面展示的错误信息。 */
function toErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Response) return `${fallback}：HTTP ${error.status}`;
  if (error instanceof Error && /[\u3400-\u9fff]/.test(error.message)) {
    return error.message;
  }
  return fallback;
}

onMounted(async () => {
  if (auth.value) {
    try {
      const refreshedAuth = await api.refreshAuthSession();
      saveAuth(refreshedAuth);
      auth.value = refreshedAuth;
    } catch {
      /* 会话刷新失败时继续使用本地登录态，后端仍会执行最终权限校验。 */
    }
  }
  stopSessionChange = onSessionChange(() => {
    void refreshAll();
  });
  await refreshAll();
});

onUnmounted(() => {
  stopSessionChange?.();
});
</script>

<template>
  <section class="learning-page">
    <div class="page-header">
      <div>
        <h1>学习中心</h1>
        <p>课程用于报名与排期，资源用于在线学习与下载，两类内容独立管理。</p>
      </div>
    </div>

    <el-tabs v-model="learningSection" class="learning-tabs">
      <el-tab-pane label="课程" name="course" />
      <el-tab-pane label="资源" name="resource" />
    </el-tabs>

    <div v-if="learningSection === 'course'" class="section-toolbar">
      <el-input
        v-model="courseKeyword"
        clearable
        placeholder="搜索课程名称、分类或说明"
        class="search"
      />
      <el-select v-model="courseCategoryFilter" class="category-filter">
        <el-option label="全部分类" value="all" />
        <el-option
          v-for="category in courseCategoryOptions"
          :key="category"
          :label="category"
          :value="category"
        />
      </el-select>
      <el-segmented v-model="courseScope" :options="courseScopeOptions" />
      <el-select v-model="courseStatusFilter" class="status-filter">
        <el-option label="全部状态" value="all" />
        <el-option label="待审核" value="pending_review" />
        <el-option label="开放加入" value="published" />
        <el-option label="审核驳回" value="rejected" />
        <el-option label="停止加入" value="closed" />
        <el-option label="已结束" value="finished" />
        <el-option label="草稿" value="draft" />
      </el-select>
      <span class="toolbar-spacer" />
      <el-button v-if="canViewOwnRecords" @click="openRecords()">我的课程记录</el-button>
      <el-button type="primary" :disabled="manageableClubs.length === 0" @click="openCreateDialog">
        发布课程
      </el-button>
    </div>

    <div v-else class="section-toolbar">
      <el-segmented
        v-model="resourceScope"
        :options="[
          { label: '我的资源', value: 'mine' },
          { label: '社内资源', value: 'club' },
          { label: '所有资源', value: 'all' },
        ]"
      />
      <el-input
        v-model="resourceKeyword"
        clearable
        placeholder="搜索资源名称、分类或说明"
        class="search"
      />
      <el-select v-model="resourceCategoryFilter" class="category-filter">
        <el-option label="全部分类" value="all" />
        <el-option
          v-for="category in resourceCategoryOptions"
          :key="category"
          :label="category"
          :value="category"
        />
      </el-select>
      <el-select v-model="resourceClubFilter" class="resource-filter" placeholder="按社团筛选">
        <el-option label="所有社团" value="all" />
        <el-option v-for="club in clubs" :key="club.id" :label="club.name" :value="club.id" />
      </el-select>
      <el-select
        v-model="resourceVisibilityFilter"
        class="resource-filter"
        placeholder="按可见范围筛选"
      >
        <el-option label="所有可见范围" value="all" />
        <el-option
          v-for="option in visibilityOptions"
          :key="option.value"
          :label="option.label"
          :value="option.value"
        />
      </el-select>
      <el-select v-model="resourceStatusFilter" class="status-filter">
        <el-option label="全部状态" value="all" />
        <el-option label="待审核" value="pending_review" />
        <el-option label="已发布" value="published" />
        <el-option label="审核驳回" value="rejected" />
        <el-option label="已下架" value="closed" />
        <el-option label="草稿" value="draft" />
      </el-select>
      <span class="toolbar-spacer" />
      <el-button v-if="canViewOwnRecords" @click="openRecords()">我的资源学习</el-button>
      <el-button type="success" :disabled="manageableClubs.length === 0" @click="openUploadDialog">
        资源上传
      </el-button>
    </div>

    <el-table
      :key="learningTableKey"
      v-loading="loading"
      :data="filteredItems"
      :empty-text="learningSection === 'course' ? '暂无符合条件的课程' : '暂无符合条件的资源'"
    >
      <el-table-column label="标题" min-width="180">
        <template #default="{ row }">
          <el-link type="primary" :underline="false" @click="openItemDetail(row)">
            {{ row.title }}
          </el-link>
        </template>
      </el-table-column>
      <el-table-column label="发布社团" min-width="130">
        <template #default="{ row }">
          {{ clubNameMap.get(row.clubId) ?? `社团 ${row.clubId}` }}
        </template>
      </el-table-column>
      <el-table-column label="类型" width="90">
        <template #default="{ row }">
          {{ itemTypeLabel(row.itemType) }}
        </template>
      </el-table-column>
      <el-table-column label="分类" min-width="120">
        <template #default="{ row }">
          {{ row.categoryName || "未分类" }}
        </template>
      </el-table-column>
      <el-table-column prop="description" label="说明" min-width="180" show-overflow-tooltip>
        <template #default="{ row }">
          <span class="description-preview">{{ row.description || "暂无说明" }}</span>
        </template>
      </el-table-column>
      <el-table-column label="可见范围" min-width="145">
        <template #default="{ row }">
          {{ visibilityLabel(row.visibility) }}
        </template>
      </el-table-column>
      <el-table-column v-if="learningSection === 'resource'" label="下载设置" width="120">
        <template #default="{ row }">
          {{ downloadPermissionLabel(row.downloadPermission) }}
        </template>
      </el-table-column>
      <el-table-column v-if="learningSection === 'course'" label="课程安排" min-width="230">
        <template #default="{ row }">
          {{ formatDate(row.startAt) }} 至 {{ formatDate(row.endAt) }}
        </template>
      </el-table-column>
      <el-table-column label="学习人数" width="100" align="center">
        <template #default="{ row }">
          {{ row.currentEnrollments
          }}<template v-if="isCourseItem(row)"> / {{ row.capacity }}</template>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="110" align="center">
        <template #default="{ row }">
          <el-tag>{{ statusLabel(row.itemStatus, row) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column
        :label="learningSection === 'course' ? '报名状态' : '学习状态'"
        width="110"
        align="center"
      >
        <template #default="{ row }">
          {{ itemRecordStatusLabel(row) }}
        </template>
      </el-table-column>
      <el-table-column label="操作" width="560" fixed="right">
        <template #default="{ row }">
          <el-button
            v-if="isCourseItem(row) && canEnrollCourses && row.instructorUserId !== currentUserId"
            size="small"
            type="primary"
            :disabled="!row.canEnroll"
            :loading="enrollingId === row.id"
            :title="row.enrollmentUnavailableReason ?? ''"
            @click="enroll(row)"
          >
            加入课程
          </el-button>
          <el-button
            v-if="row.canCancelEnrollment"
            size="small"
            type="warning"
            :loading="cancellingId === row.id"
            @click="cancelEnrollment(row)"
          >
            退出课程
          </el-button>
          <el-button v-if="!isCourseItem(row)" size="small" @click="openPreview(row)">
            在线预览
          </el-button>
          <el-button
            v-if="!isCourseItem(row)"
            size="small"
            type="primary"
            :disabled="!row.canStartLearning"
            :loading="learningId === row.id"
            :title="row.learningUnavailableReason ?? ''"
            @click="startLearning(row)"
          >
            {{ row.currentUserRecordStatus === "none" ? "开始学习" : "继续学习" }}
          </el-button>
          <el-button
            v-if="!isCourseItem(row)"
            size="small"
            type="success"
            :disabled="!row.canDownload"
            :loading="downloadingId === row.id"
            :title="row.downloadUnavailableReason ?? ''"
            @click="downloadItem(row)"
          >
            下载
          </el-button>
          <el-button v-if="row.canManage" size="small" @click="openEditDialog(row)">
            编辑
          </el-button>
          <el-button v-if="canViewStatistics(row)" size="small" @click="openStatistics(row)">
            统计
          </el-button>
          <el-button
            v-if="canReviewItem(row)"
            size="small"
            type="success"
            @click="reviewItem(row, true)"
          >
            通过
          </el-button>
          <el-button
            v-if="canReviewItem(row)"
            size="small"
            type="warning"
            @click="reviewItem(row, false)"
          >
            驳回
          </el-button>
          <el-button
            v-if="canDeleteItem(row)"
            size="small"
            type="danger"
            :loading="deletingId === row.id"
            @click="deleteResource(row)"
          >
            删除
          </el-button>
          <el-button
            v-if="canViewRecords(row) || row.currentUserRecordStatus !== 'none'"
            size="small"
            @click="openRecords(row)"
          >
            {{ canViewRecords(row) ? "学习用户" : "学习记录" }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-drawer
      v-model="detailDrawerVisible"
      :title="detailItem ? `${isCourseItem(detailItem) ? '课程' : '资源'}详情` : '详情'"
      size="520px"
    >
      <template v-if="detailItem">
        <h2 class="detail-title">{{ detailItem.title }}</h2>
        <p class="detail-description">{{ detailItem.description || "暂无说明" }}</p>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="所属社团">
            {{ clubNameMap.get(detailItem.clubId) ?? `社团 ${detailItem.clubId}` }}
          </el-descriptions-item>
          <el-descriptions-item label="类型">
            {{ itemTypeLabel(detailItem.itemType) }}
          </el-descriptions-item>
          <el-descriptions-item label="分类">
            {{ detailItem.categoryName || "未分类" }}
          </el-descriptions-item>
          <el-descriptions-item label="可见范围">
            {{ visibilityLabel(detailItem.visibility) }}
          </el-descriptions-item>
          <template v-if="isCourseItem(detailItem)">
            <el-descriptions-item label="开始时间">
              {{ formatDate(detailItem.startAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="结束时间">
              {{ formatDate(detailItem.endAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="课程容量">
              {{ detailItem.capacity ?? "未设置" }}
            </el-descriptions-item>
            <el-descriptions-item label="报名状态">
              {{ itemRecordStatusLabel(detailItem) }}
            </el-descriptions-item>
          </template>
          <template v-else>
            <el-descriptions-item label="下载设置">
              {{ downloadPermissionLabel(detailItem.downloadPermission) }}
            </el-descriptions-item>
            <el-descriptions-item label="学习状态">
              {{ itemRecordStatusLabel(detailItem) }}
            </el-descriptions-item>
          </template>
          <el-descriptions-item label="发布状态">
            {{ statusLabel(detailItem.itemStatus, detailItem) }}
          </el-descriptions-item>
          <el-descriptions-item label="学习人数">
            {{ detailItem.currentEnrollments }}
          </el-descriptions-item>
        </el-descriptions>
        <div v-if="!isCourseItem(detailItem)" class="detail-actions">
          <el-button type="primary" plain @click="openPreview(detailItem)">在线预览</el-button>
          <el-button
            type="primary"
            :disabled="!detailItem.canStartLearning"
            :loading="learningId === detailItem.id"
            @click="startLearning(detailItem)"
          >
            {{ detailItem.currentUserRecordStatus === "none" ? "开始学习" : "继续学习" }}
          </el-button>
          <el-button
            type="success"
            :disabled="!detailItem.canDownload"
            :loading="downloadingId === detailItem.id"
            @click="downloadItem(detailItem)"
          >
            下载
          </el-button>
        </div>
      </template>
    </el-drawer>

    <el-dialog
      v-model="previewDialogVisible"
      :title="previewItem ? `在线预览 · ${previewItem.title}` : '在线预览'"
      width="min(1100px, 96vw)"
      destroy-on-close
      @closed="clearPreview"
    >
      <div
        class="preview-stage"
        v-loading="previewLoading"
        :element-loading-text="
          previewConverted ? '正在转换并加载 Office 文档…' : '正在安全加载预览内容…'
        "
      >
        <el-alert
          v-if="previewError"
          :title="previewError"
          type="error"
          :closable="false"
          show-icon
        />
        <img
          v-else-if="previewUrl && previewKind === 'image'"
          class="preview-image"
          :src="previewUrl"
          :alt="previewItem?.title ?? '学习资源图片'"
          referrerpolicy="no-referrer"
          @load="markPreviewReady"
          @error="markPreviewFailed"
        />
        <video
          v-else-if="previewUrl && previewKind === 'video'"
          class="preview-video"
          :src="previewUrl"
          controls
          preload="metadata"
          @loadedmetadata="markPreviewReady"
          @error="markPreviewFailed"
        >
          当前浏览器不支持该视频格式。
        </video>
        <iframe
          v-else-if="previewUrl"
          class="preview-document"
          :src="previewUrl"
          :title="previewItem?.title ?? '学习资源文档'"
          referrerpolicy="no-referrer"
          @load="markPreviewReady"
        />
      </div>
      <p class="preview-security-tip">
        在线预览与下载权限相互独立，不会记录下载时间或
        IP。浏览器必须接收内容才能展示，在线预览不能作为 DRM，也无法完全阻止截图、抓包或另存。
      </p>
      <template #footer>
        <el-button @click="previewDialogVisible = false">关闭</el-button>
        <el-button
          v-if="previewItem?.canDownload"
          type="success"
          :loading="downloadingId === previewItem.id"
          @click="previewItem && downloadItem(previewItem)"
        >
          下载原文件
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="uploadDialogVisible" title="资源上传" width="620px">
      <el-form label-width="100px">
        <el-form-item label="所属社团" required>
          <el-select v-model="uploadForm.clubId" class="upload-field">
            <el-option
              v-for="club in manageableClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="资源标题">
          <el-input
            v-model="uploadForm.title"
            maxlength="100"
            show-word-limit
            :disabled="uploadFiles.length > 1"
            :placeholder="
              uploadFiles.length > 1 ? '批量上传时使用各自文件名' : '不填则使用原文件名'
            "
          />
        </el-form-item>
        <el-form-item label="资源分类">
          <el-input
            v-model="uploadForm.categoryName"
            maxlength="100"
            show-word-limit
            placeholder="例如：培训资料、往届文档"
          />
        </el-form-item>
        <el-form-item label="资源说明">
          <el-input
            v-model="uploadForm.description"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
            placeholder="填写内容摘要、用途、版本或注意事项（可选）"
          />
        </el-form-item>
        <el-form-item label="可见范围" required>
          <el-select v-model="uploadForm.visibility" class="upload-field">
            <el-option
              v-for="option in visibilityOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="下载设置" required>
          <el-select v-model="uploadForm.downloadPermission" class="upload-field">
            <el-option
              v-for="option in downloadPermissionOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="选择文件" required>
          <el-upload
            v-model:file-list="uploadFiles"
            class="resource-uploader"
            drag
            multiple
            action="#"
            :auto-upload="false"
          >
            <div class="upload-copy">将文件拖到此处，或点击选择文件</div>
            <template #tip>
              <div class="el-upload__tip">
                支持批量上传，单个文件最大 50 MB，不允许可执行文件和脚本。
              </div>
            </template>
          </el-upload>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="uploading" @click="uploadDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="uploading" @click="uploadPendingResources">
          上传 {{ uploadFiles.length || "" }} 个文件
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="courseDialogVisible" :title="courseDialogTitle" width="680px">
      <el-form ref="courseFormRef" :model="courseForm" :rules="courseRules" label-width="120px">
        <el-form-item label="发布社团" prop="clubId" required>
          <el-select
            v-model="courseForm.clubId"
            placeholder="请选择社团"
            filterable
            :disabled="courseDialogMode === 'edit'"
            @change="handleCourseClubChange"
          >
            <el-option
              v-for="club in manageableClubs"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="标题" prop="title" required>
          <el-input v-model="courseForm.title" maxlength="100" show-word-limit />
        </el-form-item>
        <el-form-item :label="isCourseForm ? '课程说明' : '资源说明'">
          <el-input
            v-model="courseForm.description"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
          />
        </el-form-item>
        <el-form-item v-if="isCourseForm" label="授课人学工号（可选）">
          <el-input
            v-model="courseForm.instructorUserNumber"
            clearable
            maxlength="30"
            placeholder="输入学号或工号后点击查询"
            @input="handleInstructorNumberInput"
            @clear="resetInstructorLookup"
          >
            <template #append>
              <el-button :loading="instructorLookupLoading" @click="lookupInstructor">
                查询
              </el-button>
            </template>
          </el-input>
          <el-alert
            v-if="instructorLookupResult"
            class="instructor-result"
            type="success"
            :closable="false"
            show-icon
          >
            <template #title> 已识别：{{ instructorLookupResult.displayName }} </template>
          </el-alert>
          <el-text v-else-if="instructorLookupError" class="field-tip" type="danger">
            {{ instructorLookupError }}
          </el-text>
          <span v-else class="field-tip">
            可选；只接受正常状态的教师或学生账号。
            {{ courseForm.instructorUserId ? "不输入新学工号将保留当前授课人。" : "" }}
          </span>
        </el-form-item>
        <el-form-item :label="isCourseForm ? '课程类型' : '资源类型'" prop="itemType" required>
          <el-select v-model="courseForm.itemType" @change="handleItemTypeChange">
            <el-option
              v-for="option in isCourseForm ? courseTypeOptions : resourceTypeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="isCourseForm ? '课程分类' : '资源分类'">
          <el-input v-model="courseForm.categoryName" maxlength="100" />
        </el-form-item>
        <el-form-item v-if="!isCourseForm" label="文件地址" prop="fileUrl" required>
          <el-input
            v-model="courseForm.fileUrl"
            maxlength="255"
            placeholder="https://example.com/resource.pdf"
          />
          <span class="field-tip">资源上传生成的文件地址由系统维护，也可替换为外部链接。</span>
        </el-form-item>
        <el-form-item v-if="isCourseForm" label="开始时间" prop="startAt" required>
          <el-date-picker
            v-model="courseForm.startAt"
            type="datetime"
            placeholder="请选择开始时间"
          />
        </el-form-item>
        <el-form-item v-if="isCourseForm" label="结束时间（可选）" prop="endAt">
          <el-date-picker
            v-model="courseForm.endAt"
            type="datetime"
            placeholder="不填则课程长期开放"
          />
        </el-form-item>
        <el-form-item v-if="isCourseForm" label="课程容量" prop="capacity" required>
          <el-input-number v-model="courseForm.capacity" :min="1" controls-position="right" />
        </el-form-item>
        <el-form-item label="可见范围" prop="visibility" required>
          <el-radio-group v-model="courseForm.visibility">
            <el-radio-button
              v-for="option in visibilityOptions"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </el-radio-button>
          </el-radio-group>
          <span v-if="courseForm.visibility === 'department'" class="field-tip">
            部门范围以上传人在该社团的当前有效部门为准
          </span>
        </el-form-item>
        <el-form-item v-if="!isCourseForm" label="下载设置" prop="downloadPermission" required>
          <el-radio-group v-model="courseForm.downloadPermission">
            <el-radio-button
              v-for="option in downloadPermissionOptions"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="发布状态" prop="itemStatus" required>
          <el-radio-group v-model="courseForm.itemStatus">
            <el-radio-button value="draft">草稿</el-radio-button>
            <el-radio-button value="published">提交审核</el-radio-button>
            <el-radio-button v-if="courseDialogMode === 'edit'" value="closed">
              {{ isCourseForm ? "停止加入" : "下架" }}
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="courseDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitCourse">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="recordDialogVisible" :title="recordDialogTitle" width="1100px">
      <el-table v-loading="recordLoading" :data="visibleLearningRecords" empty-text="暂无学习记录">
        <el-table-column label="课程或资源" min-width="160">
          <template #default="{ row }">
            {{ itemMap.get(row.itemId)?.title ?? `资源 ${row.itemId}` }}
          </template>
        </el-table-column>
        <el-table-column label="学习用户" min-width="160">
          <template #default="{ row }">
            {{ participantLabel(row) }}
          </template>
        </el-table-column>
        <el-table-column label="开始学习" min-width="160">
          <template #default="{ row }">
            {{ formatDate(row.enrolledAt) }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            {{ recordStatusLabel(row.enrollStatus) }}
          </template>
        </el-table-column>
        <el-table-column label="进度" width="150">
          <template #default="{ row }">
            <el-progress :percentage="row.progress ?? 0" />
          </template>
        </el-table-column>
        <el-table-column label="学习时长" width="100">
          <template #default="{ row }">
            {{ formatDuration(row.durationSeconds) }}
          </template>
        </el-table-column>
        <el-table-column label="最近下载" min-width="160">
          <template #default="{ row }">
            {{ formatDate(row.downloadedAt) }}
          </template>
        </el-table-column>
        <el-table-column label="下载 IP" min-width="130">
          <template #default="{ row }">
            {{ row.downloadIp || "未记录" }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="110">
          <template #default="{ row }">
            <el-button v-if="canUpdateRecord(row)" size="small" @click="beginProgress(row)">
              更新进度
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-form
        v-if="progressForm.recordId"
        ref="progressFormRef"
        :model="progressForm"
        :rules="progressRules"
        class="progress-form"
        label-width="100px"
      >
        <el-form-item label="学习进度" prop="progress">
          <el-slider v-model="progressForm.progress" :min="0" :max="100" show-input />
        </el-form-item>
        <el-form-item label="学习时长" prop="durationMinutes">
          <el-input-number
            v-model="progressForm.durationMinutes"
            :min="0"
            controls-position="right"
          />
          <span class="unit">分钟</span>
        </el-form-item>
        <el-form-item>
          <el-button @click="progressForm.recordId = 0">取消</el-button>
          <el-button
            type="primary"
            :loading="progressSavingId === progressForm.recordId"
            @click="submitProgress"
          >
            保存进度
          </el-button>
        </el-form-item>
      </el-form>
    </el-dialog>

    <el-dialog v-model="statisticsDialogVisible" title="学习资源统计" width="620px">
      <div v-loading="statisticsLoading" class="statistics-panel">
        <template v-if="statistics">
          <h3>{{ statistics.title }}</h3>
          <el-descriptions :column="2" border>
            <el-descriptions-item label="学习人数">
              {{ statistics.learnerCount }} 人
            </el-descriptions-item>
            <el-descriptions-item label="完成人数">
              {{ statistics.completedCount }} 人
            </el-descriptions-item>
            <el-descriptions-item label="下载人数">
              {{ statistics.downloadCount }} 人
            </el-descriptions-item>
            <el-descriptions-item label="平均进度">
              {{ statistics.averageProgress.toFixed(1) }}%
            </el-descriptions-item>
            <el-descriptions-item label="平均学习时长">
              {{ formatDuration(statistics.averageDurationSeconds) }}
            </el-descriptions-item>
            <el-descriptions-item label="累计学习时长">
              {{ formatDuration(statistics.totalDurationSeconds) }}
            </el-descriptions-item>
          </el-descriptions>
        </template>
      </div>
    </el-dialog>
  </section>
</template>

<style scoped>
.learning-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
}

.page-header h1 {
  margin: 0 0 6px;
  font-size: 24px;
}

.page-header p {
  margin: 0;
  color: var(--el-text-color-secondary);
}

.section-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.toolbar-spacer {
  flex: 1 1 24px;
}

.search {
  width: 220px;
}

.category-filter {
  width: 140px;
}

.status-filter {
  width: 140px;
}

.learning-tabs :deep(.el-tabs__header) {
  margin-bottom: 0;
}

.resource-filter {
  width: 180px;
}

.upload-field,
.resource-uploader {
  width: 100%;
}

.upload-copy {
  color: var(--el-text-color-regular);
}

.description-preview {
  display: block;
  overflow: hidden;
  color: var(--el-text-color-secondary);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-title {
  margin: 0 0 12px;
  font-size: 20px;
}

.detail-description {
  margin: 0 0 20px;
  color: var(--el-text-color-regular);
  line-height: 1.7;
  white-space: pre-wrap;
}

.detail-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 20px;
}

.preview-stage {
  display: flex;
  min-height: 520px;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  background: var(--el-fill-color-light);
}

.preview-image {
  max-width: 100%;
  max-height: 72vh;
  object-fit: contain;
}

.preview-video {
  width: 100%;
  max-height: 72vh;
  background: #000;
}

.preview-document {
  width: 100%;
  height: 72vh;
  border: 0;
  background: #fff;
}

.preview-security-tip {
  margin: 12px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.6;
}

.field-tip {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.instructor-result {
  width: 100%;
  margin-top: 8px;
}

.progress-form {
  margin-top: 18px;
  padding-top: 16px;
  border-top: 1px solid var(--el-border-color-light);
}

.unit {
  margin-left: 8px;
  color: var(--el-text-color-secondary);
}

.statistics-panel {
  min-height: 150px;
}

.statistics-panel h3 {
  margin: 0 0 16px;
}

@media (max-width: 900px) {
  .page-header {
    align-items: stretch;
    flex-direction: column;
  }

  .section-toolbar {
    justify-content: flex-start;
  }

  .toolbar-spacer {
    display: none;
  }

  .preview-stage {
    min-height: 360px;
  }

  .preview-document {
    height: 60vh;
  }
}
</style>

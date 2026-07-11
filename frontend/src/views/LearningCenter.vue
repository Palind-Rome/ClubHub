<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
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
import { onSessionChange, readAuth, type AuthRole } from "../authSession";

const api = apiClient;

const itemTypeOptions = [
  { label: "课程", value: "course" },
  { label: "讲座", value: "lecture" },
  { label: "培训", value: "training" },
  { label: "视频", value: "video" },
  { label: "文档", value: "document" },
  { label: "资料", value: "material" },
];
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

const principalRoleCodes = new Set(["club_president", "club_leader", "club_manager", "president"]);
const officerRoleCodes = new Set(["club_officer", "officer", "club_manager"]);
const advisorRoleCodes = new Set(["advisor", "club_advisor", "teacher_advisor"]);
const manageableRoleCodes = new Set([
  ...principalRoleCodes,
  ...officerRoleCodes,
  ...advisorRoleCodes,
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
const statistics = ref<LearningItemStatistics | null>(null);
const statisticsLoading = ref(false);
const statusFilter = ref<string>("all");
const kindFilter = ref<"all" | "course" | "resource">("all");
const categoryFilter = ref("all");
const keyword = ref("");
const courseDialogVisible = ref(false);
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
const clubNameMap = computed(() => new Map(clubs.value.map((club) => [club.id, club.name])));
const itemMap = computed(() => new Map(learningItems.value.map((item) => [item.id, item])));
const manageableClubs = computed(() => clubs.value.filter((club) => canCreateForClub(club.id)));
const isCourseForm = computed(() => courseTypes.has(courseForm.itemType));
const categoryOptions = computed(() =>
  [
    ...new Set(
      learningItems.value
        .map((item) => item.categoryName?.trim())
        .filter((value): value is string => Boolean(value)),
    ),
  ].sort(),
);
const filteredItems = computed(() => {
  const search = keyword.value.trim().toLowerCase();
  return learningItems.value.filter((item) => {
    const course = isCourseItem(item);
    if (statusFilter.value !== "all" && item.itemStatus !== statusFilter.value) return false;
    if (kindFilter.value === "course" && !course) return false;
    if (kindFilter.value === "resource" && course) return false;
    if (categoryFilter.value !== "all" && item.categoryName !== categoryFilter.value) return false;
    if (!search) return true;
    return [item.title, item.categoryName, item.description]
      .filter(Boolean)
      .some((value) => value!.toLowerCase().includes(search));
  });
});
const courseDialogTitle = computed(() =>
  courseDialogMode.value === "edit" ? "编辑课程或资源" : "发布课程或上传资源",
);
const recordDialogTitle = computed(() => {
  const item = selectedRecordItemId.value
    ? itemMap.value.get(selectedRecordItemId.value)
    : undefined;
  if (!item) return "我的学习记录";
  return item.canManage ? `${item.title} - 学习用户` : `${item.title} - 学习记录`;
});

/** 将课程状态转换为用户可读文本。 */
function statusLabel(status?: string | null, item?: LearningItem) {
  switch (status) {
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
  if (normalized && !/^https?:\/\/\S+$/i.test(normalized)) {
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
  return currentRoles.value.some(
    (role) => manageableRoleCodes.has(normalize(role.code)) && roleCoversClub(role, clubId),
  );
}

/** 判断角色授权范围是否覆盖指定社团。 */
function roleCoversClub(role: AuthRole, clubId: number) {
  return role.clubId === clubId || role.clubIds.includes(clubId);
}

/** 统一清理用于比较的角色和状态代码。 */
function normalize(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
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

/** 资源类型切换时清理不适用字段。 */
function handleItemTypeChange() {
  if (isCourseForm.value) {
    courseForm.downloadPermission = courseForm.fileUrl ? "allow" : "deny";
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
  courseForm.itemStatus =
    item.itemStatus === LearningItemItemStatusEnum.Finished ? "closed" : item.itemStatus;
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
      fileUrl: courseForm.fileUrl.trim() || undefined,
      startAt: isCourseForm.value ? courseForm.startAt : undefined,
      endAt: isCourseForm.value ? (courseForm.endAt ?? undefined) : undefined,
      capacity: isCourseForm.value ? courseForm.capacity : undefined,
      visibility: courseForm.visibility,
      downloadPermission: courseForm.downloadPermission,
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
      ElMessage.success("课程信息已更新");
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
    ElMessage.success("学习记录已创建，可继续更新进度和时长");
    await loadLearningItems();
    await openRecords(item);
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
  try {
    const result = await api.downloadLearningItem({ itemId: item.id });
    const opened = window.open(result.fileUrl, "_blank", "noopener,noreferrer");
    if (opened) opened.opener = null;
    ElMessage.success("下载权限校验通过，已记录本次下载");
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "资源下载失败"));
  } finally {
    downloadingId.value = null;
  }
}

/** 加载管理者可查看的资源学习统计。 */
async function openStatistics(item: LearningItem) {
  if (!item.canManage) return;
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
        <p>统一管理培训课程、视频、文档和资料，控制可见与下载范围并查看学习统计。</p>
      </div>
      <div class="toolbar">
        <el-input v-model="keyword" clearable placeholder="搜索标题、分类或说明" class="search" />
        <el-select v-model="categoryFilter" class="category-filter">
          <el-option label="全部分类" value="all" />
          <el-option
            v-for="category in categoryOptions"
            :key="category"
            :label="category"
            :value="category"
          />
        </el-select>
        <el-segmented
          v-model="kindFilter"
          :options="[
            { label: '全部类型', value: 'all' },
            { label: '培训课程', value: 'course' },
            { label: '视频与资料', value: 'resource' },
          ]"
        />
        <el-segmented
          v-model="statusFilter"
          :options="[
            { label: '全部', value: 'all' },
            { label: '开放加入', value: 'published' },
            { label: '已停止加入', value: 'closed' },
            { label: '已结束', value: 'finished' },
            { label: '草稿', value: 'draft' },
          ]"
        />
        <el-button @click="openRecords()">我的学习记录</el-button>
        <el-button
          type="primary"
          :disabled="manageableClubs.length === 0"
          @click="openCreateDialog"
        >
          发布课程或资源
        </el-button>
      </div>
    </div>

    <el-table v-loading="loading" :data="filteredItems" empty-text="暂无可查看的课程或资源">
      <el-table-column prop="title" label="标题" min-width="180" />
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
      <el-table-column label="可见范围" min-width="145">
        <template #default="{ row }">
          {{ visibilityLabel(row.visibility) }}
        </template>
      </el-table-column>
      <el-table-column label="下载设置" width="120">
        <template #default="{ row }">
          {{ downloadPermissionLabel(row.downloadPermission) }}
        </template>
      </el-table-column>
      <el-table-column label="学习安排" min-width="230">
        <template #default="{ row }">
          <template v-if="isCourseItem(row)">
            {{ formatDate(row.startAt) }} 至 {{ formatDate(row.endAt) }}
          </template>
          <span v-else>在线学习资源</span>
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
      <el-table-column label="我的状态" width="110" align="center">
        <template #default="{ row }">
          {{ recordStatusLabel(row.currentUserRecordStatus) }}
        </template>
      </el-table-column>
      <el-table-column label="操作" width="430" fixed="right">
        <template #default="{ row }">
          <el-button
            v-if="isCourseItem(row)"
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
          <el-button v-if="row.canManage" size="small" @click="openStatistics(row)">
            统计
          </el-button>
          <el-button
            v-if="row.canManage || row.currentUserRecordStatus !== 'none'"
            size="small"
            @click="openRecords(row)"
          >
            {{ row.canManage ? "学习用户" : "学习记录" }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

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
        <el-form-item label="资源说明">
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
        <el-form-item label="资源类型" prop="itemType" required>
          <el-select v-model="courseForm.itemType" @change="handleItemTypeChange">
            <el-option
              v-for="option in itemTypeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="资源分类">
          <el-input v-model="courseForm.categoryName" maxlength="100" />
        </el-form-item>
        <el-form-item label="文件地址" prop="fileUrl" :required="!isCourseForm">
          <el-input
            v-model="courseForm.fileUrl"
            maxlength="255"
            placeholder="https://example.com/resource.pdf"
          />
          <span class="field-tip">
            {{ isCourseForm ? "课程可不填；填写后可按下载设置提供附件" : "视频、文档和资料必填" }}
          </span>
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
        <el-form-item label="下载设置" prop="downloadPermission" required>
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
            <el-radio-button value="published">{{
              isCourseForm ? "开放加入" : "发布"
            }}</el-radio-button>
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
      <el-table v-loading="recordLoading" :data="learningRecords" empty-text="暂无学习记录">
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

.toolbar {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}

.search {
  width: 220px;
}

.category-filter {
  width: 140px;
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

  .toolbar {
    justify-content: flex-start;
  }
}
</style>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from "element-plus";
import {
  CreateLearningItemRequestItemStatusEnum,
  DefaultApi,
  LearningItemItemStatusEnum,
  UpdateLearningItemRequestItemStatusEnum,
  type Club,
  type LearningItem,
  type LearningRecord,
  type LearningTeacherCandidate,
} from "../api";
import { onSessionChange, readAuth, type AuthRole } from "../authSession";

const api = new DefaultApi();

const courseTypeOptions = [
  { label: "课程", value: "course" },
  { label: "讲座", value: "lecture" },
  { label: "培训", value: "training" },
];
const visibilityOptions = [
  { label: "仅本社团成员", value: "club" },
  { label: "面向全校", value: "public" },
];

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
type CourseVisibility = "club" | "public";

const auth = ref(readAuth());
const clubs = ref<Club[]>([]);
const learningItems = ref<LearningItem[]>([]);
const learningRecords = ref<LearningRecord[]>([]);
const teacherCandidates = ref<LearningTeacherCandidate[]>([]);
const loading = ref(false);
const recordLoading = ref(false);
const teacherCandidateLoading = ref(false);
const saving = ref(false);
const enrollingId = ref<number | null>(null);
const cancellingId = ref<number | null>(null);
const progressSavingId = ref<number | null>(null);
const statusFilter = ref<string>("all");
const courseDialogVisible = ref(false);
const recordDialogVisible = ref(false);
const courseDialogMode = ref<"create" | "edit">("create");
const editingItemId = ref<number | null>(null);
const selectedRecordItemId = ref<number | undefined>();
const courseFormRef = ref<FormInstance>();
const progressFormRef = ref<FormInstance>();
let stopSessionChange: (() => void) | undefined;
let teacherCandidateClubId: number | null = null;

const courseForm = reactive({
  clubId: null as number | null,
  title: "",
  description: "",
  teacherUserId: null as number | null,
  itemType: "course",
  categoryName: "",
  startAt: null as Date | null,
  endAt: null as Date | null,
  capacity: null as number | null,
  visibility: "club" as CourseVisibility,
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
  teacherUserId: [
    { required: true, message: "请选择授课教师", trigger: "change" },
    { type: "number", min: 1, message: "请选择有效授课教师", trigger: "change" },
  ],
  itemType: [{ required: true, message: "请选择课程类型", trigger: "change" }],
  startAt: [{ required: true, message: "请选择课程开始时间", trigger: "change" }],
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
  capacity: [
    { required: true, message: "请输入课程容量", trigger: "change" },
    { type: "number", min: 1, message: "课程容量必须大于 0", trigger: "change" },
  ],
  visibility: [{ required: true, message: "请选择课程开放范围", trigger: "change" }],
  itemStatus: [{ required: true, message: "请选择课程状态", trigger: "change" }],
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
const filteredItems = computed(() => {
  if (statusFilter.value === "all") return learningItems.value;
  return learningItems.value.filter((item) => item.itemStatus === statusFilter.value);
});
const courseDialogTitle = computed(() =>
  courseDialogMode.value === "edit" ? "编辑培训课程" : "发布培训课程",
);
const recordDialogTitle = computed(() => {
  const item = selectedRecordItemId.value
    ? itemMap.value.get(selectedRecordItemId.value)
    : undefined;
  if (!item) return "我的学习记录";
  return item.canManage ? `${item.title} - 课程成员` : `${item.title} - 学习记录`;
});

/** 将课程状态转换为用户可读文本。 */
function statusLabel(status?: string | null) {
  switch (status) {
    case LearningItemItemStatusEnum.Published:
      return "开放加入";
    case LearningItemItemStatusEnum.Closed:
      return "已停止加入";
    case LearningItemItemStatusEnum.Finished:
      return "已结束";
    default:
      return "草稿";
  }
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

/** 返回课程类型的展示名称。 */
function courseTypeLabel(value?: string | null) {
  return courseTypeOptions.find((option) => option.value === value)?.label ?? value ?? "课程";
}

/** 返回课程开放范围的展示名称。 */
function visibilityLabel(value?: string | null) {
  return visibilityOptions.find((option) => option.value === value)?.label ?? "仅本社团成员";
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
  if (!item?.startAt) return false;
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
    learningItems.value = await api.getLearningItems({
      currentUserId: currentUserId.value,
    });
  } catch (error) {
    learningItems.value = [];
    ElMessage.error(toErrorMessage(error, "课程列表加载失败"));
  } finally {
    loading.value = false;
  }
}

/** 加载指定社团可选择的授课教师。 */
async function loadTeacherCandidates(clubId?: number | null) {
  if (!clubId || !currentUserId.value) {
    teacherCandidates.value = [];
    teacherCandidateClubId = null;
    return;
  }

  if (teacherCandidateClubId === clubId && teacherCandidates.value.length > 0) return;

  teacherCandidateLoading.value = true;
  try {
    teacherCandidates.value = await api.getLearningTeacherCandidates({
      currentUserId: currentUserId.value,
      clubId,
    });
    teacherCandidateClubId = clubId;
  } catch (error) {
    teacherCandidates.value = [];
    teacherCandidateClubId = null;
    ElMessage.error(toErrorMessage(error, "授课教师候选人加载失败"));
  } finally {
    teacherCandidateLoading.value = false;
  }
}

/** 返回授课教师候选人的姓名与学工号文本。 */
function teacherCandidateLabel(candidate: LearningTeacherCandidate) {
  return candidate.displayName.trim();
}

/** 社团切换后清空教师并刷新候选列表。 */
async function handleCourseClubChange(clubId?: number) {
  courseForm.teacherUserId = null;
  await loadTeacherCandidates(clubId);
}

/** 重置课程表单为新建课程的默认值。 */
function resetCourseForm() {
  courseForm.clubId = manageableClubs.value[0]?.id ?? null;
  courseForm.title = "";
  courseForm.description = "";
  courseForm.teacherUserId = null;
  courseForm.itemType = "course";
  courseForm.categoryName = "";
  courseForm.startAt = null;
  courseForm.endAt = null;
  courseForm.capacity = null;
  courseForm.visibility = "club";
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
  await loadTeacherCandidates(courseForm.clubId);
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
  courseForm.teacherUserId = item.teacherUserId ?? null;
  courseForm.itemType = item.itemType ?? "course";
  courseForm.categoryName = item.categoryName ?? "";
  courseForm.startAt = new Date(item.startAt);
  courseForm.endAt = item.endAt ? new Date(item.endAt) : null;
  courseForm.capacity = item.capacity;
  courseForm.visibility = item.visibility;
  courseForm.itemStatus =
    item.itemStatus === LearningItemItemStatusEnum.Finished ? "closed" : item.itemStatus;
  await loadTeacherCandidates(item.clubId);
  courseFormRef.value?.clearValidate();
  courseDialogVisible.value = true;
}

/** 校验并提交课程创建或更新请求。 */
async function submitCourse() {
  if (!currentUserId.value) return;
  if (!(await validateForm(courseFormRef.value))) return;
  if (
    !courseForm.clubId ||
    !courseForm.teacherUserId ||
    !courseForm.startAt ||
    !courseForm.capacity
  ) {
    return;
  }

  saving.value = true;
  try {
    const commonFields = {
      currentUserId: currentUserId.value,
      title: courseForm.title.trim(),
      description: courseForm.description.trim() || undefined,
      teacherUserId: courseForm.teacherUserId,
      itemType: courseForm.itemType,
      categoryName: courseForm.categoryName.trim() || undefined,
      startAt: courseForm.startAt,
      endAt: courseForm.endAt ?? undefined,
      capacity: courseForm.capacity,
      visibility: courseForm.visibility,
    };

    if (courseDialogMode.value === "edit" && editingItemId.value) {
      await api.updateLearningItem({
        itemId: editingItemId.value,
        updateLearningItemRequest: {
          ...commonFields,
          itemStatus: courseForm.itemStatus as UpdateLearningItemRequestItemStatusEnum,
        },
      });
      ElMessage.success("课程信息已更新");
    } else {
      await api.createLearningItem({
        createLearningItemRequest: {
          ...commonFields,
          clubId: courseForm.clubId,
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
      enrollLearningItemRequest: { currentUserId: currentUserId.value },
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
      enrollLearningItemRequest: { currentUserId: currentUserId.value },
    });
    ElMessage.success("已退出课程");
    await loadLearningItems();
  } catch (error) {
    ElMessage.error(toErrorMessage(error, "退出课程失败"));
  } finally {
    cancellingId.value = null;
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
      currentUserId: currentUserId.value,
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
        currentUserId: currentUserId.value,
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
  if (error instanceof Error) return error.message || fallback;
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
        <h1>培训课程</h1>
        <p>发布、加入和管理课程，并跟踪学习进度与完成情况。</p>
      </div>
      <div class="toolbar">
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
          发布课程
        </el-button>
      </div>
    </div>

    <el-table v-loading="loading" :data="filteredItems" empty-text="暂无可查看的课程">
      <el-table-column prop="title" label="课程名称" min-width="170" />
      <el-table-column label="发布社团" min-width="130">
        <template #default="{ row }">
          {{ clubNameMap.get(row.clubId) ?? `社团 ${row.clubId}` }}
        </template>
      </el-table-column>
      <el-table-column label="开放范围" width="110">
        <template #default="{ row }">
          {{ visibilityLabel(row.visibility) }}
        </template>
      </el-table-column>
      <el-table-column label="类型" width="90">
        <template #default="{ row }">
          {{ courseTypeLabel(row.itemType) }}
        </template>
      </el-table-column>
      <el-table-column label="课程时间" min-width="250">
        <template #default="{ row }">
          {{ formatDate(row.startAt) }} 至 {{ formatDate(row.endAt) }}
        </template>
      </el-table-column>
      <el-table-column label="加入人数" width="110" align="center">
        <template #default="{ row }"> {{ row.currentEnrollments }} / {{ row.capacity }} </template>
      </el-table-column>
      <el-table-column label="状态" width="110" align="center">
        <template #default="{ row }">
          <el-tag>{{ statusLabel(row.itemStatus) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="我的状态" width="110" align="center">
        <template #default="{ row }">
          {{ recordStatusLabel(row.currentUserRecordStatus) }}
        </template>
      </el-table-column>
      <el-table-column label="操作" width="310" fixed="right">
        <template #default="{ row }">
          <el-button
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
          <el-button v-if="row.canManage" size="small" @click="openEditDialog(row)">
            编辑
          </el-button>
          <el-button
            v-if="row.canManage || row.currentUserRecordStatus"
            size="small"
            @click="openRecords(row)"
          >
            {{ row.canManage ? "课程成员" : "学习记录" }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="courseDialogVisible" :title="courseDialogTitle" width="650px">
      <el-form ref="courseFormRef" :model="courseForm" :rules="courseRules" label-width="120px">
        <el-form-item label="发布社团" prop="clubId">
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
        <el-form-item label="课程名称" prop="title">
          <el-input v-model="courseForm.title" maxlength="100" show-word-limit />
        </el-form-item>
        <el-form-item label="课程简介">
          <el-input
            v-model="courseForm.description"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="授课教师" prop="teacherUserId">
          <el-select
            v-model="courseForm.teacherUserId"
            filterable
            :loading="teacherCandidateLoading"
            placeholder="按姓名或学工号搜索"
          >
            <el-option
              v-for="candidate in teacherCandidates"
              :key="candidate.id"
              :label="teacherCandidateLabel(candidate)"
              :value="candidate.id"
            />
          </el-select>
          <span class="field-tip">仅显示正常状态的教师或指导老师账号</span>
        </el-form-item>
        <el-form-item label="课程类型" prop="itemType">
          <el-select v-model="courseForm.itemType">
            <el-option
              v-for="option in courseTypeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="课程分类">
          <el-input v-model="courseForm.categoryName" maxlength="100" />
        </el-form-item>
        <el-form-item label="开始时间" prop="startAt">
          <el-date-picker
            v-model="courseForm.startAt"
            type="datetime"
            placeholder="请选择开始时间"
          />
        </el-form-item>
        <el-form-item label="结束时间（可选）" prop="endAt">
          <el-date-picker
            v-model="courseForm.endAt"
            type="datetime"
            placeholder="不填则课程长期开放"
          />
        </el-form-item>
        <el-form-item label="课程容量" prop="capacity">
          <el-input-number v-model="courseForm.capacity" :min="1" controls-position="right" />
        </el-form-item>
        <el-form-item label="开放范围" prop="visibility">
          <el-radio-group v-model="courseForm.visibility">
            <el-radio-button
              v-for="option in visibilityOptions"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="课程状态" prop="itemStatus">
          <el-radio-group v-model="courseForm.itemStatus">
            <el-radio-button value="draft">草稿</el-radio-button>
            <el-radio-button value="published">开放加入</el-radio-button>
            <el-radio-button v-if="courseDialogMode === 'edit'" value="closed">
              停止加入
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="courseDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitCourse">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="recordDialogVisible" :title="recordDialogTitle" width="860px">
      <el-table v-loading="recordLoading" :data="learningRecords" empty-text="暂无学习记录">
        <el-table-column label="课程" min-width="160">
          <template #default="{ row }">
            {{ itemMap.get(row.itemId)?.title ?? `课程 ${row.itemId}` }}
          </template>
        </el-table-column>
        <el-table-column label="课程成员" min-width="160">
          <template #default="{ row }">
            {{ participantLabel(row) }}
          </template>
        </el-table-column>
        <el-table-column label="加入时间" min-width="160">
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

.field-tip {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
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

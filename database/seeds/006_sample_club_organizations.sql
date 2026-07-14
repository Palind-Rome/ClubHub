-- 社团组织架构样例：依赖 001_sample_clubs.sql 与 005_sample_member_terms.sql。
-- 将成员任期中已经出现的部门和小组实体化，并回填 CLUB_MEMBERS.department_id / group_id。

MERGE INTO CLUB_DEPARTMENTS target
USING (
  SELECT 10101 AS department_id, 1 AS club_id, '主席团' AS department_name,
         'COMP-PRES' AS department_code, '统筹协会规划、换届、评优和跨部门协同' AS responsibilities,
         '021-6598-1101' AS contact_phone, 'president@computer.club' AS contact_email,
         '四平路校区学生活动中心 305' AS office_location, 10 AS display_order,
         'active' AS department_status
  FROM dual
  UNION ALL
  SELECT 10102, 1, '技术部', 'COMP-TECH', '维护协会技术项目、开发培训和开源协作',
         '021-6598-1102', 'tech@computer.club', '嘉定校区创新工坊 B204', 20, 'active'
  FROM dual
  UNION ALL
  SELECT 10103, 1, '活动部', 'COMP-ACT', '策划技术沙龙、赛事服务和社群活动',
         '021-6598-1103', 'activity@computer.club', '四平路校区学生活动中心 307', 30, 'active'
  FROM dual
  UNION ALL
  SELECT 10104, 1, '算法部', 'COMP-ALGO', '组织算法训练、竞赛组队和题解沉淀',
         '021-6598-1104', 'algo@computer.club', '嘉定校区机房 A312', 40, 'active'
  FROM dual
  UNION ALL
  SELECT 10105, 1, '宣传部', 'COMP-MEDIA', '负责协会新媒体、海报、推文和活动影像',
         '021-6598-1105', 'media@computer.club', '四平路校区学生活动中心 309', 50, 'active'
  FROM dual
  UNION ALL
  SELECT 10201, 2, '主席团', 'PHOTO-PRES', '统筹摄影社年度计划、影展和外联合作',
         '021-6598-2201', 'president@photo.club', '四平路校区艺术实践室 201', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 10202, 2, '活动部', 'PHOTO-ACT', '负责影展策划、活动报名和现场执行',
         '021-6598-2202', 'activity@photo.club', '四平路校区艺术实践室 202', 20, 'active'
  FROM dual
  UNION ALL
  SELECT 10203, 2, '外拍部', 'PHOTO-FIELD', '组织校园采风、城市外拍和安全备案',
         '021-6598-2203', 'field@photo.club', '四平路校区暗房 103', 30, 'active'
  FROM dual
  UNION ALL
  SELECT 10204, 2, '后期部', 'PHOTO-POST', '负责照片筛选、调色教学和作品归档',
         '021-6598-2204', 'post@photo.club', '四平路校区数字媒体实验室 406', 40, 'active'
  FROM dual
  UNION ALL
  SELECT 10205, 2, '影像记录部', 'PHOTO-REC', '承担校内活动跟拍、素材交付和影像库维护',
         '021-6598-2205', 'record@photo.club', '四平路校区艺术实践室 204', 50, 'active'
  FROM dual
  UNION ALL
  SELECT 10301, 3, '主席团', 'BADM-PRES', '统筹训练安排、队伍建设和赛事报名',
         '021-6598-3301', 'president@badminton.club', '嘉定校区体育馆 102', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 10302, 3, '竞训部', 'BADM-TRAIN', '负责日常训练、梯队培养和校队交流',
         '021-6598-3302', 'training@badminton.club', '嘉定校区体育馆 3 号场', 20, 'active'
  FROM dual
  UNION ALL
  SELECT 10303, 3, '赛事部', 'BADM-MATCH', '负责赛程编排、裁判协调和成绩归档',
         '021-6598-3303', 'match@badminton.club', '嘉定校区体育馆赛事办公室', 30, 'active'
  FROM dual
  UNION ALL
  SELECT 10304, 3, '宣传部', 'BADM-MEDIA', '负责赛讯发布、招新物料和活动记录',
         '021-6598-3304', 'media@badminton.club', '嘉定校区体育馆 104', 40, 'active'
  FROM dual
) source
ON (
  target.club_id = source.club_id
  AND target.department_name = source.department_name
)
WHEN MATCHED THEN UPDATE SET
  target.department_code = source.department_code,
  target.responsibilities = source.responsibilities,
  target.contact_phone = source.contact_phone,
  target.contact_email = source.contact_email,
  target.office_location = source.office_location,
  target.display_order = source.display_order,
  target.department_status = source.department_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (department_id, club_id, department_name, department_code, responsibilities, contact_phone, contact_email, office_location, display_order, department_status, created_at, updated_at)
  VALUES (source.department_id, source.club_id, source.department_name, source.department_code, source.responsibilities, source.contact_phone, source.contact_email, source.office_location, source.display_order, source.department_status, SYSDATE, SYSDATE);

MERGE INTO CLUB_GROUPS target
USING (
  SELECT group_seed.group_id,
         group_seed.club_id,
         department.department_id,
         group_seed.group_name,
         group_seed.group_code,
         group_seed.responsibilities,
         group_seed.contact_phone,
         group_seed.contact_email,
         group_seed.activity_location,
         group_seed.display_order,
         group_seed.group_status
  FROM (
  SELECT 20101 AS group_id, 1 AS club_id, '主席团' AS department_name, '负责人组' AS group_name,
         'COMP-PRES-LEAD' AS group_code, '处理协会日常决策、对外沟通和审批确认' AS responsibilities,
         '021-6598-1111' AS contact_phone, 'lead@computer.club' AS contact_email,
         '四平路校区学生活动中心 305' AS activity_location, 10 AS display_order,
         'active' AS group_status
  FROM dual
  UNION ALL
  SELECT 20102, 1, '技术部', '开发组', 'COMP-TECH-DEV', '承担协会网站、工具和活动系统开发',
         '021-6598-1121', 'dev@computer.club', '嘉定校区创新工坊 B204', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20103, 1, '技术部', '后端组', 'COMP-TECH-BE', '维护后端服务、数据库脚本和接口文档',
         '021-6598-1122', 'backend@computer.club', '嘉定校区创新工坊 B205', 20, 'active'
  FROM dual
  UNION ALL
  SELECT 20104, 1, '活动部', '赛事组', 'COMP-ACT-MATCH', '组织编程竞赛报名、志愿服务和现场执行',
         '021-6598-1131', 'contest@computer.club', '四平路校区学生活动中心 307', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20105, 1, '算法部', '训练组', 'COMP-ALGO-TRAIN', '负责周赛训练、题单维护和赛后复盘',
         '021-6598-1141', 'train@computer.club', '嘉定校区机房 A312', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20106, 1, '宣传部', '新媒体组', 'COMP-MEDIA-NEW', '负责推文排版、海报发布和活动摄影',
         '021-6598-1151', 'newmedia@computer.club', '四平路校区学生活动中心 309', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20201, 2, '主席团', '负责人组', 'PHOTO-PRES-LEAD', '协调摄影社排期、影展选题和校内沟通',
         '021-6598-2211', 'lead@photo.club', '四平路校区艺术实践室 201', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20202, 2, '活动部', '影展组', 'PHOTO-ACT-EXPO', '负责作品征集、布展、讲解和撤展',
         '021-6598-2221', 'expo@photo.club', '四平路校区艺术实践室 202', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20203, 2, '外拍部', '校园采风组', 'PHOTO-FIELD-CAMPUS', '组织校园采风路线、器材借用和安全提醒',
         '021-6598-2231', 'campus@photo.club', '四平路校区暗房 103', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20204, 2, '后期部', '修图组', 'PHOTO-POST-EDIT', '负责后期教学、作品调色和输出规范',
         '021-6598-2241', 'edit@photo.club', '四平路校区数字媒体实验室 406', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20205, 2, '影像记录部', '活动跟拍组', 'PHOTO-REC-EVENT', '承担校内活动跟拍、素材整理和交付',
         '021-6598-2251', 'event@photo.club', '四平路校区艺术实践室 204', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20301, 3, '主席团', '负责人组', 'BADM-PRES-LEAD', '负责协会训练计划、校队沟通和审批确认',
         '021-6598-3311', 'lead@badminton.club', '嘉定校区体育馆 102', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20302, 3, '竞训部', '校队训练组', 'BADM-TRAIN-TEAM', '负责校队训练、对抗赛和技术复盘',
         '021-6598-3321', 'team@badminton.club', '嘉定校区体育馆 3 号场', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20303, 3, '竞训部', '兴趣训练组', 'BADM-TRAIN-OPEN', '负责零基础训练、兴趣课和新人带练',
         '021-6598-3322', 'open@badminton.club', '嘉定校区体育馆 4 号场', 20, 'active'
  FROM dual
  UNION ALL
  SELECT 20304, 3, '赛事部', '裁判组', 'BADM-MATCH-REF', '负责比赛执裁、计分、赛程和申诉记录',
         '021-6598-3331', 'referee@badminton.club', '嘉定校区体育馆赛事办公室', 10, 'active'
  FROM dual
  UNION ALL
  SELECT 20305, 3, '宣传部', '赛讯组', 'BADM-MEDIA-NEWS', '负责赛讯推送、赛后战报和招新宣传',
         '021-6598-3341', 'news@badminton.club', '嘉定校区体育馆 104', 10, 'active'
  FROM dual
  ) group_seed
  JOIN CLUB_DEPARTMENTS department
    ON department.club_id = group_seed.club_id
   AND department.department_name = group_seed.department_name
) source
ON (
  target.club_id = source.club_id
  AND target.department_id = source.department_id
  AND target.group_name = source.group_name
)
WHEN MATCHED THEN UPDATE SET
  target.group_code = source.group_code,
  target.responsibilities = source.responsibilities,
  target.contact_phone = source.contact_phone,
  target.contact_email = source.contact_email,
  target.activity_location = source.activity_location,
  target.display_order = source.display_order,
  target.group_status = source.group_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (group_id, club_id, department_id, group_name, group_code, responsibilities, contact_phone, contact_email, activity_location, display_order, group_status, created_at, updated_at)
  VALUES (source.group_id, source.club_id, source.department_id, source.group_name, source.group_code, source.responsibilities, source.contact_phone, source.contact_email, source.activity_location, source.display_order, source.group_status, SYSDATE, SYSDATE);

UPDATE CLUB_MEMBERS member
SET department_id = (
  SELECT department.department_id
  FROM CLUB_DEPARTMENTS department
  WHERE department.club_id = member.club_id
    AND department.department_name = TRIM(member.department_name)
)
WHERE member.club_id IN (1, 2, 3)
  AND member.department_name IS NOT NULL
  AND TRIM(member.department_name) IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM CLUB_DEPARTMENTS department
    WHERE department.club_id = member.club_id
      AND department.department_name = TRIM(member.department_name)
  );

UPDATE CLUB_MEMBERS member
SET group_id = (
  SELECT club_group.group_id
  FROM CLUB_GROUPS club_group
  WHERE club_group.club_id = member.club_id
    AND club_group.department_id = member.department_id
    AND club_group.group_name = TRIM(member.group_name)
)
WHERE member.club_id IN (1, 2, 3)
  AND member.department_id IS NOT NULL
  AND member.group_name IS NOT NULL
  AND TRIM(member.group_name) IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM CLUB_GROUPS club_group
    WHERE club_group.club_id = member.club_id
      AND club_group.department_id = member.department_id
      AND club_group.group_name = TRIM(member.group_name)
  );

DECLARE
  v_missing_departments NUMBER;
  v_missing_groups NUMBER;
  v_invalid_group_links NUMBER;
BEGIN
  SELECT COUNT(*)
  INTO v_missing_departments
  FROM CLUB_MEMBERS
  WHERE club_id IN (1, 2, 3)
    AND department_name IS NOT NULL
    AND TRIM(department_name) IS NOT NULL
    AND department_id IS NULL;

  IF v_missing_departments > 0 THEN
    RAISE_APPLICATION_ERROR(-20136, 'Sample organization seed left member rows without department_id.');
  END IF;

  SELECT COUNT(*)
  INTO v_missing_groups
  FROM CLUB_MEMBERS
  WHERE club_id IN (1, 2, 3)
    AND group_name IS NOT NULL
    AND TRIM(group_name) IS NOT NULL
    AND group_id IS NULL;

  IF v_missing_groups > 0 THEN
    RAISE_APPLICATION_ERROR(-20137, 'Sample organization seed left member rows without group_id.');
  END IF;

  SELECT COUNT(*)
  INTO v_invalid_group_links
  FROM CLUB_MEMBERS member
  JOIN CLUB_GROUPS club_group
    ON club_group.group_id = member.group_id
  WHERE member.club_id IN (1, 2, 3)
    AND (
      club_group.club_id <> member.club_id
      OR club_group.department_id <> member.department_id
    );

  IF v_invalid_group_links > 0 THEN
    RAISE_APPLICATION_ERROR(-20138, 'Sample organization seed created inconsistent member group links.');
  END IF;
END;
/

COMMIT;

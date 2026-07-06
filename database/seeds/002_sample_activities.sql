-- 活动样例数据
INSERT INTO ACTIVITIES (activity_id, club_id, title, activity_type, description, location, start_at, end_at, capacity, activity_status, created_at)
VALUES (1, 1, '2025 秋季 Hackathon', 'competition', '48 小时编程马拉松，自由组队，主题现场公布。', '大学生活动中心 301', TIMESTAMP '2025-10-15 09:00:00', TIMESTAMP '2025-10-15 18:00:00', 60, 'published', SYSDATE);

INSERT INTO ACTIVITIES (activity_id, club_id, title, activity_type, description, location, start_at, end_at, capacity, activity_status, created_at)
VALUES (2, 1, 'Python 入门工作坊', 'workshop', '面向零基础同学，从环境配置到完成一个小项目。', '教学楼 B101', TIMESTAMP '2025-11-01 14:00:00', TIMESTAMP '2025-11-01 17:00:00', 30, 'published', SYSDATE);

INSERT INTO ACTIVITIES (activity_id, club_id, title, activity_type, description, location, start_at, end_at, capacity, activity_status, created_at)
VALUES (3, 2, '校园摄影大赛作品展', 'exhibition', '展出本届摄影大赛获奖及入围作品。', '图书馆一楼展厅', TIMESTAMP '2025-11-01 10:00:00', TIMESTAMP '2025-11-03 17:00:00', NULL, 'published', SYSDATE);

INSERT INTO ACTIVITIES (activity_id, club_id, title, activity_type, description, location, start_at, end_at, capacity, activity_status, created_at)
VALUES (4, 2, '人像摄影外拍活动', 'outing', '前往校园后山拍摄秋日人像，自带相机。', '校园后山', TIMESTAMP '2025-10-25 14:00:00', TIMESTAMP '2025-10-25 17:00:00', 15, 'draft', SYSDATE);

INSERT INTO ACTIVITIES (activity_id, club_id, title, activity_type, description, location, start_at, end_at, capacity, activity_status, created_at)
VALUES (5, 3, '羽毛球新生杯', 'competition', '面向全校新生的羽毛球比赛，分男女组。', '体育馆羽毛球场', TIMESTAMP '2025-10-20 14:00:00', TIMESTAMP '2025-10-20 17:00:00', 32, 'published', SYSDATE);

INSERT INTO ACTIVITIES (activity_id, club_id, title, activity_type, description, location, start_at, end_at, capacity, activity_status, created_at)
VALUES (6, 3, '每周常规训练', 'training', '每周二周四例训，新老队员均可参加。', '体育馆羽毛球场', TIMESTAMP '2025-09-01 19:00:00', TIMESTAMP '2025-09-01 21:00:00', NULL, 'ongoing', SYSDATE);

COMMIT;

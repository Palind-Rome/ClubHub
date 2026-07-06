-- 社团样例数据
INSERT INTO CLUBS (club_id, club_name, category, description, president_user_id, club_status, founded_at, created_at)
VALUES (1, '计算机协会', '学术科技', '编程竞赛、技术分享、黑客松组织', NULL, 'active', DATE '2024-09-01', SYSDATE);

INSERT INTO CLUBS (club_id, club_name, category, description, president_user_id, club_status, founded_at, created_at)
VALUES (2, '摄影社', '文化艺术', '校园摄影采风、人像摄影教学、作品展览', NULL, 'active', DATE '2024-09-15', SYSDATE);

INSERT INTO CLUBS (club_id, club_name, category, description, president_user_id, club_status, founded_at, created_at)
VALUES (3, '羽毛球协会', '体育竞技', '每周训练、校内联赛、校际交流赛', NULL, 'active', DATE '2024-03-10', SYSDATE);

INSERT INTO CLUBS (club_id, club_name, category, description, president_user_id, club_status, founded_at, created_at)
VALUES (4, '辩论队', '学术科技', '辩论技巧训练、校内辩论赛、校际交流', NULL, 'active', DATE '2023-11-01', SYSDATE);

INSERT INTO CLUBS (club_id, club_name, category, description, president_user_id, club_status, founded_at, created_at)
VALUES (5, '志愿者协会', '公益实践', '社区服务、支教活动、公益项目', NULL, 'active', DATE '2024-01-15', SYSDATE);

COMMIT;

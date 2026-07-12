# fix/90-activity-id-sequences

关联 Issue: #90

本分支将 ACTIVITIES / ACTIVITY_PARTICIPATIONS 主键改为 Oracle sequence 生成，移除应用层 MaxAsync()+1。

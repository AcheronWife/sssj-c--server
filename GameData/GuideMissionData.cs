namespace Gcg2OfflineServer.GameData;

/// <summary>
/// 引导任务配置。
/// 包含主线引导任务和进度奖励，影响剧情推进。
/// </summary>
public static class GuideMissionData
{
    public const int TaskGroup = 5;
    public const int ProgressTaskId = 41001;

    public class GuideMission
    {
        public int Id { get; set; }
        public int PrerequisiteId { get; set; }
        public int Target { get; set; }
        public List<int[]> Awards { get; set; } = new();
        public (int chapter, int index, int difficulty)? Level { get; set; }
    }

    public class ProgressAward
    {
        public int Id { get; set; }
        public int RequiredCompleted { get; set; }
        public List<int[]> Awards { get; set; } = new();
    }

    public static readonly List<GuideMission> Missions = new()
    {
        new() { Id = 40001, PrerequisiteId = 0, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } }, Level = (1, 1, 1) },
        new() { Id = 40002, PrerequisiteId = 40001, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } }, Level = (1, 2, 1) },
        new() { Id = 40003, PrerequisiteId = 40002, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } }, Level = (1, 3, 1) },
        new() { Id = 40004, PrerequisiteId = 40003, Target = 1, Awards = new() { new[] { 5, 2, 1, 1, 5 }, new[] { 7, 3, 1, 1, 1 } }, Level = (1, 6, 1) },
        new() { Id = 40005, PrerequisiteId = 40004, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } }, Level = (2, 6, 1) },
        new() { Id = 40006, PrerequisiteId = 40005, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } }, Level = (3, 6, 1) },
        new() { Id = 40014, PrerequisiteId = 40004, Target = 4, Awards = new() { new[] { 10, 1, 1, 1, 1 } } },
        new() { Id = 40008, PrerequisiteId = 40004, Target = 10, Awards = new() { new[] { 10, 1, 1, 1, 1 }, new[] { 7, 3, 1, 1, 1 } } },
        new() { Id = 40021, PrerequisiteId = 40005, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } } },
        new() { Id = 40025, PrerequisiteId = 40005, Target = 1, Awards = new() { new[] { 7, 7, 1, 4, 1 }, new[] { 7, 3, 1, 1, 1 } }, Level = (1, 1, 2) },
        new() { Id = 40022, PrerequisiteId = 40004, Target = 3900, Awards = new() { new[] { 15, 2, 1, 1, 20 } } },
        new() { Id = 40026, PrerequisiteId = 40004, Target = 1, Awards = new() { new[] { 15, 1, 1, 1, 2000 }, new[] { 7, 3, 1, 1, 1 } } },
        new() { Id = 40027, PrerequisiteId = 40004, Target = 1, Awards = new() { new[] { 10, 1, 1, 1, 1 } } },
        new() { Id = 40017, PrerequisiteId = 40003, Target = 80, Awards = new() { new[] { 7, 1, 4, 1, 1 }, new[] { 7, 3, 1, 1, 1 } } },
        new() { Id = 40018, PrerequisiteId = 40004, Target = 1, Awards = new() { new[] { 7, 1, 4, 1, 1 }, new[] { 7, 3, 1, 1, 1 } } },
    };

    public static readonly List<ProgressAward> ProgressAwards = new()
    {
        new() { Id = 1, RequiredCompleted = 3, Awards = new() { new[] { 7, 1, 4, 1, 5 } } },
        new() { Id = 2, RequiredCompleted = 6, Awards = new() { new[] { 7, 7, 1, 4, 1 } } },
        new() { Id = 3, RequiredCompleted = 9, Awards = new() { new[] { 7, 3, 1, 3, 1 } } },
        new() { Id = 4, RequiredCompleted = 12, Awards = new() { new[] { 8, 5, 2, 3, 1 } } },
        new() { Id = 5, RequiredCompleted = 15, Awards = new() { new[] { 7, 10, 1, 4, 1 } } },
    };

    public static long MakeTaskId(int missionId) => ((long)TaskGroup << 16) | (uint)missionId;
    public static int Progress(long taskValue) => (int)(taskValue >> 1);
    public static bool HasClaimed(long taskValue) => (taskValue & 1) == 1;
    public static long MakeTaskValue(int progress, bool claimed) => (long)progress * 2 + (claimed ? 1 : 0);

    public static GuideMission? GetMission(int missionId) => Missions.FirstOrDefault(m => m.Id == missionId);
    public static ProgressAward? GetProgressAward(int awardId) => ProgressAwards.FirstOrDefault(a => a.Id == awardId);

    /// <summary>计算已完成的引导任务数量。</summary>
    public static int CompletedCount(Dictionary<string, long> taskValues)
    {
        return Missions.Count(m =>
        {
            var key = MakeTaskId(m.Id).ToString();
            var value = taskValues.TryGetValue(key, out var v) ? v : 0;
            return Progress(value) >= m.Target;
        });
    }

    /// <summary>根据关卡通关情况同步引导任务进度（新增方法，不影响原有逻辑）。</summary>
    public static void SyncByLevel(Dictionary<string, long> taskValues, int chapter, int index, int difficulty)
    {
        foreach (var mission in Missions)
        {
            if (mission.Level.HasValue &&
                mission.Level.Value.chapter == chapter &&
                mission.Level.Value.index == index &&
                mission.Level.Value.difficulty == difficulty)
            {
                var key = MakeTaskId(mission.Id).ToString();
                var current = taskValues.TryGetValue(key, out var v) ? v : 0;
                taskValues[key] = MakeTaskValue(mission.Target, HasClaimed(current));
            }
        }
    }
}

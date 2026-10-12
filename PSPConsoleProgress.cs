using System;

namespace Game {
    public sealed class PSPConsoleProgress {
        public int TrophyMask { get; set; }
        public int PongWins { get; set; }
        public int PongBestRally { get; set; }
        public int BreakoutBestScore { get; set; }
        public string ServerAddress { get; set; } = "";

        public static readonly string[] TrophyNames = ["首个得分", "战胜 AI", "完美对局", "首次击破", "高分挑战", "清空球场"];
        public static readonly string[] TrophyDescriptions = ["乒乓对战中获得 1 分", "率先得到 5 分，赢下比赛", "以 5 : 0 战胜 AI", "击破任意一块砖块", "单局打砖块获得 500 分", "击破全部 40 块砖块"];

        public bool HasTrophy(int index) => (TrophyMask & (1 << index)) != 0;
        public bool Unlock(int index) {
            if (HasTrophy(index)) return false;
            TrophyMask |= 1 << index;
            return true;
        }
        public int TrophyCount(int gameIndex) {
            int count = 0;
            for (int i = gameIndex * 3; i < gameIndex * 3 + 3; i++) if (HasTrophy(i)) count++;
            return count;
        }
    }
}

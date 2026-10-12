using System;

namespace Game {
    public sealed class PSPConsoleProgress {
        public int TrophyMask { get; set; }
        public int PongWins { get; set; }
        public int PongBestRally { get; set; }
        public int BreakoutBestScore { get; set; }
        public int TetrisBestScore { get; set; }
        public string ServerAddress { get; set; } = DefaultServerAddress;

        // 默认联机入口；换入口只改这一行。
        public const string DefaultServerAddress = "frp-art.com:39504";

        // 旧版默认/已下线地址：读档时自动换成当前默认值，玩家手填的其他地址保留。
        static readonly string[] LegacyServerAddresses = ["", "1.95.85.178:8001", "1.95.85.178", "bot.suancaixianyu.cn:8001", "bot.suancaixianyu.cn:28886"];

        public static string MigrateServerAddress(string saved) {
            string text = (saved ?? "").Trim();
            foreach (string legacy in LegacyServerAddresses) {
                if (string.Equals(text, legacy, System.StringComparison.OrdinalIgnoreCase)) return DefaultServerAddress;
            }
            return text;
        }

        public static readonly string[] TrophyNames = ["首个得分", "战胜 AI", "完美对局", "首次击破", "高分挑战", "清空球场", "首次消行", "万分方块", "一次四行"];
        public static readonly string[] TrophyDescriptions = ["乒乓对战中获得 1 分", "率先得到 5 分，赢下比赛", "以 5 : 0 战胜 AI", "击破任意一块砖块", "单局打砖块获得 500 分", "击破全部 40 块砖块", "俄罗斯方块中消除任意一行", "单局俄罗斯方块达到 10000 分", "一次消除 4 行"];

        public bool HasTrophy(int index) => (TrophyMask & (1 << index)) != 0;
        public bool Unlock(int index) {
            if (HasTrophy(index)) return false;
            TrophyMask |= 1 << index;
            return true;
        }
        public int TotalTrophyCount() {
            int count = 0;
            for (int g = 0; g < PSPConsoleState.GameCount; g++) count += TrophyCount(g);
            return count;
        }
        public int TrophyCount(int gameIndex) {
            int count = 0;
            for (int i = gameIndex * 3; i < gameIndex * 3 + 3; i++) if (HasTrophy(i)) count++;
            return count;
        }
    }
}

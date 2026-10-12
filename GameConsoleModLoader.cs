using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game {
    public class GameConsoleModLoader : ModLoader {
        public const string PackageName = "codex.GameConsole";

        public override void __ModInitialize() {
            new Harmony(PackageName).PatchAll(typeof(GameConsoleModLoader).Assembly);
            ModsManager.RegisterHook("OnLoadingFinished", this);
        }

        public override void OnLoadingFinished(List<System.Action> actions) {
            int contents = BlocksManager.GetBlockIndex<PSPBlock>();
            Log.Information($"游戏机模组已加载：PSP 内容索引 {contents}，默认数据值 1451。");
        }
    }
}

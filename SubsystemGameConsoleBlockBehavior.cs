using System;
using System.Collections.Generic;
using System.Globalization;
using Engine;
using TemplatesDatabase;

namespace Game {
    public class SubsystemGameConsoleBlockBehavior : SubsystemBlockBehavior {
        readonly Dictionary<int, PSPConsoleProgress> m_profiles = new();
        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<PSPBlock>(true)];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            ValuesDictionary profiles = valuesDictionary.GetValue("ConsoleProfiles", new ValuesDictionary());
            foreach (KeyValuePair<string, object> entry in profiles) {
                if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerIndex)
                    || entry.Value is not ValuesDictionary saved) continue;
                m_profiles[playerIndex] = new PSPConsoleProgress {
                    TrophyMask = saved.GetValue("TrophyMask", 0) & 511,
                    PongWins = Math.Max(0, saved.GetValue("PongWins", 0)),
                    PongBestRally = Math.Max(0, saved.GetValue("PongBestRally", 0)),
                    BreakoutBestScore = Math.Max(0, saved.GetValue("BreakoutBestScore", 0)),
                    TetrisBestScore = Math.Max(0, saved.GetValue("TetrisBestScore", 0)),
                    ServerAddress = PSPConsoleProgress.MigrateServerAddress(saved.GetValue("ServerAddress", ""))
                };
            }
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            base.Save(valuesDictionary);
            ValuesDictionary profiles = new();
            foreach (KeyValuePair<int, PSPConsoleProgress> entry in m_profiles) {
                PSPConsoleProgress progress = entry.Value;
                profiles.SetValue(entry.Key.ToString(CultureInfo.InvariantCulture), new ValuesDictionary {
                    { "TrophyMask", progress.TrophyMask }, { "PongWins", progress.PongWins },
                    { "PongBestRally", progress.PongBestRally }, { "BreakoutBestScore", progress.BreakoutBestScore }, { "TetrisBestScore", progress.TetrisBestScore },
                    { "ServerAddress", progress.ServerAddress }
                });
            }
            valuesDictionary.SetValue("ConsoleProfiles", profiles);
        }

        PSPConsoleProgress GetProgress(ComponentPlayer player) {
            int index = player.PlayerData.PlayerIndex;
            if (!m_profiles.TryGetValue(index, out PSPConsoleProgress progress)) {
                progress = new PSPConsoleProgress();
                m_profiles.Add(index, progress);
            }
            return progress;
        }

        public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
            if (componentPlayer == null || inventory == null || slotIndex < 0) {
                return false;
            }
            int value = inventory.GetSlotValue(slotIndex);
            if (inventory.GetSlotCount(slotIndex) <= 0
                || Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<PSPBlock>(true)) {
                return false;
            }
            foreach (Dialog dialog in DialogsManager.Dialogs) {
                if (dialog is PSPConsoleDialog && dialog.ParentWidget == componentPlayer.GuiWidget) return true;
            }
            try {
                // Use the same right-hand EditItem button as the musket.
                // PSPConsoleInputGuard blocks world controls while the Dialog is attached.
                DialogsManager.ShowDialog(componentPlayer.GuiWidget, new PSPConsoleDialog(inventory, slotIndex, componentPlayer, GetProgress(componentPlayer)));
            }
            catch (Exception exception) {
                Log.Error($"游戏机界面打开失败：{exception}");
                componentPlayer.ComponentGui.DisplaySmallMessage("游戏机界面打开失败", Color.White, false, false);
            }
            return true;
        }
    }
}

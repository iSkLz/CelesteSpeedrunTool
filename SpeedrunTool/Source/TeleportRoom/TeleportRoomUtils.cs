using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Force.DeepCloner;
using Microsoft.Xna.Framework;
using Monocle;
using On.Celeste.Editor;
using static Celeste.Mod.SpeedrunTool.Other.ButtonConfigUi;
using LevelTemplate = Celeste.Editor.LevelTemplate;

namespace Celeste.Mod.SpeedrunTool.TeleportRoom {
    public static class TeleportRoomUtils {
        private const string FlagPrefix = "summit_checkpoint_";
        private static readonly List<Session> RoomHistory = new List<Session>();
        private static int HistoryIndex = -1;
        private static bool AllowRecord;
        private static Vector2? RespawnPoint;

        public static void Load() {
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
            On.Celeste.Level.TransitionRoutine += LevelOnTransitionRoutine;
            On.Celeste.LevelExit.ctor += LevelExitOnCtor;
            On.Celeste.SummitCheckpoint.Update += SummitCheckpointOnUpdate;
            MapEditor.LoadLevel += MapEditorOnLoadLevel;
            On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        }

        public static void Unload() {
            On.Celeste.Level.Update -= LevelOnUpdate;
            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
            On.Celeste.Level.TransitionRoutine -= LevelOnTransitionRoutine;
            On.Celeste.LevelExit.ctor -= LevelExitOnCtor;
            On.Celeste.SummitCheckpoint.Update -= SummitCheckpointOnUpdate;
            MapEditor.LoadLevel -= MapEditorOnLoadLevel;
            On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        }

        private static void SummitCheckpointOnUpdate(On.Celeste.SummitCheckpoint.orig_Update orig, SummitCheckpoint self) {
            bool lastActivated = self.Activated;
            orig(self);
            if (!lastActivated && self.Activated) {
                if (Engine.Scene is Level level && level.GetPlayer() is Player player) {
                    player.Add(new Coroutine(WaitSessionReady(level.Session)));
                }
            }
        }

        private static void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition) {
            orig(self, session, startPosition);
            if (RespawnPoint.HasValue) {
                session.RespawnPoint = RespawnPoint;
                RespawnPoint = null;
            }
            if (AllowRecord) {
                RecordTransitionRoom(session);
            }
        }

        private static void MapEditorOnLoadLevel(MapEditor.orig_LoadLevel orig, Editor.MapEditor self,
            LevelTemplate level, Vector2 at) {
            AllowRecord = true;
            orig(self, level, at);
            AllowRecord = false;
        }

        private static IEnumerator WaitSessionReady(Session self) {
            yield return null;
            RecordTransitionRoom(self);
        }

        private static void TeleportTo(Session session, bool fromHistory = false) {
            if (SpeedrunToolModule.Settings.FastTeleport && Engine.Scene is Level level && level.GetPlayer() is Player player) {
                // 修复问题：死亡瞬间传送 PlayerDeadBody 没被清除，导致传送完毕后 madeline 自动爆炸
                level.Entities.UpdateLists();

                // External
                RoomTimerManager.Instance.ResetTime();
                DeathStatisticsManager.Instance.Died = false;

                level.SetFieldValue("transition", null); // 允许切换房间时传送
                Glitch.Value = 0f;
                Engine.TimeRate = 1f;
                Engine.FreezeTimer = 0f;
                Distort.Anxiety = 0f;
                Distort.GameRate = 1f;
                Audio.SetMusicParam("fade", 1f);
                level.Displacement.Clear(); // 避免冲刺后残留爆破效果
                level.Particles.Clear();
                level.ParticlesBG.Clear();
                level.ParticlesFG.Clear();
                TrailManager.Clear(); // 清除冲刺的残影

                if (!fromHistory) {
                    BetterMapEditor.Instance.FixTeleportProblems(session, session.RespawnPoint);
                }

                session.DeepCloneTo(level.Session);

                // 修改自 level.TeleportTo(player, session.Level, Player.IntroTypes.Respawn);
                level.Remove(player);
                if (level.Entities.FindFirst<SpeedrunTimerDisplay>() is Entity timer) {
                    level.Remove(timer);
                }

                level.UnloadLevel();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 修复：章节计时器在章节完成隐藏后传送无法重新显示
                level.Add(new SpeedrunTimerDisplay());
                level.LoadLevel(Player.IntroTypes.Respawn);
                level.Entities.UpdateLists();

                level.Completed = false;
                level.InCutscene = false;
                level.SkippingCutscene = false;

                // 节奏块房间传送出来时恢复音乐
                if (level.Tracker.GetEntities<CassetteBlock>().Count == 0) {
                    level.Tracker.GetEntity<CassetteBlockManager>()?.RemoveSelf();
                }

                // new player instance
                player = level.GetPlayer();
                level.Camera.Position = player.CameraTarget;
                level.Update();
            } else {
                if (!fromHistory) {
                    BetterMapEditor.ShouldFixTeleportProblems = true;
                }

                RespawnPoint = session.RespawnPoint;
                Engine.Scene = new LevelLoader(session.DeepClone());
            }
        }

        private static void LevelExitOnCtor(On.Celeste.LevelExit.orig_ctor orig, LevelExit self, LevelExit.Mode mode,
            Session session, HiresSnow snow) {
            orig(self, mode, session, snow);

            if (mode != LevelExit.Mode.GoldenBerryRestart) {
                Reset();
            }
        }

        private static void Reset() {
            RoomHistory.Clear();
            HistoryIndex = -1;
        }

        private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            if (!SpeedrunToolModule.Enabled) {
                return;
            }

            if (self.Paused || StateManager.Instance.State != StateManager.States.None) return;

            if (Mappings.LastRoom.Pressed()) {
                Mappings.LastRoom.ConsumePress();
                TeleportToLastRoom(self);
            } else if (Mappings.NextRoom.Pressed()) {
                Mappings.NextRoom.ConsumePress();
                TeleportToNextRoom(self);
            }
        }

        private static void TeleportToLastRoom(Level level) {
            if (HistoryIndex > 0 && HistoryIndex < RoomHistory.Count) {
                // Glyph 这种传送到其他房间是不做记录的，所以只回到当前记录的房间
                if (level.Session.Level == RoomHistory[HistoryIndex].Level) {
                    HistoryIndex--;
                }

                TeleportTo(RoomHistory[HistoryIndex]);
                return;
            }

            var levelDatas = LevelDataReorderUtils.GetReorderLevelDatas(level);
            if (levelDatas == null) {
                return;
            }

            LevelData currentLevelData = level.Session?.LevelData;
            if (currentLevelData == null) {
                return;
            }

            if (SearchSummitCheckpoint(false, level)) {
                TeleportTo(level.Session);
                return;
            }

            int index = levelDatas.IndexOf(currentLevelData);
            if (index <= 0) {
                return;
            }

            index--;
            LevelData lastLevelData = levelDatas[index];
            while (lastLevelData.Dummy && index > 0) {
                index--;
                lastLevelData = levelDatas[index];
            }

            if (lastLevelData.Dummy) {
                return;
            }

            level.Session.Level = lastLevelData.Name;
            level.Session.RespawnPoint = null;

            SearchSummitCheckpoint(false, lastLevelData, level);
            TeleportTo(level.Session);
        }

        private static void TeleportToNextRoom(Level level) {
            if (HistoryIndex >= 0 && HistoryIndex < RoomHistory.Count - 1) {
                HistoryIndex++;
                TeleportTo(RoomHistory[HistoryIndex], true);
                return;
            }

            var levelDatas = LevelDataReorderUtils.GetReorderLevelDatas(level);
            if (levelDatas == null) {
                return;
            }

            LevelData currentLevelData = level.Session?.LevelData;
            if (currentLevelData == null) {
                return;
            }

            if (SearchSummitCheckpoint(true, level)) {
                // 根据数据跳到下一个房间也需要记录
                RecordAndTeleport(level.Session);
                return;
            }

            int index = levelDatas.IndexOf(currentLevelData);
            if (index < 0 || index == levelDatas.Count - 1) {
                return;
            }

            index++;
            LevelData nextLevelData = levelDatas[index];
            while (nextLevelData.Dummy && index < levelDatas.Count - 1) {
                index++;
                nextLevelData = levelDatas[index];
            }

            if (nextLevelData.Dummy) {
                return;
            }

            level.Session.Level = nextLevelData.Name;
            level.Session.RespawnPoint = null;

            SearchSummitCheckpoint(true, nextLevelData, level);
            // 根据数据跳到下一个房间也需要记录
            RecordAndTeleport(level.Session);
        }

        private static bool SearchSummitCheckpoint(bool next, Level level) {
            // 查找当前房间是否有未触发的旗子，且旗子数现有的小，如果有则跳到旗子处
            int currentFlagNumber = int.MaxValue;

            foreach (string flag in level.Session.Flags) {
                if (!flag.StartsWith(FlagPrefix)) {
                    continue;
                }

                if (int.TryParse(flag.Replace(FlagPrefix, ""), out int flagNumber)) {
                    currentFlagNumber = Math.Min(currentFlagNumber, flagNumber);
                }
            }

            if (currentFlagNumber == int.MaxValue && !next) {
                currentFlagNumber = -1;
            }

            List<SummitCheckpoint> flagList = level.Entities.FindAll<SummitCheckpoint>()
                .Where(checkpoint => !checkpoint.Activated).ToList();

            // from samll to big
            flagList.Sort((first, second) => first.Number - second.Number);

            if (flagList.Count > 0) {
                SummitCheckpoint summitCheckpoint = null;
                if (next && flagList.LastOrDefault(checkpoint => checkpoint.Number < currentFlagNumber) is SummitCheckpoint biggest) {
                    summitCheckpoint = biggest;
                } else if (!next && flagList.FirstOrDefault(checkpoint => checkpoint.Number > currentFlagNumber) is SummitCheckpoint smallest) {
                    summitCheckpoint = smallest;
                }

                if (summitCheckpoint == null) return false;

                level.Session.RespawnPoint = level.GetSpawnPoint(summitCheckpoint.Position);
                level.Session.SetFlag(FlagPrefix + summitCheckpoint.Number);
                return true;
            }

            return false;
        }

        private static void SearchSummitCheckpoint(bool next, LevelData levelData, Level level) {
            // 查找上/下个房间是否有旗子，如果有则跳到旗子处
            List<EntityData> flagList = levelData.Entities.Where(data => data.Name == "summitcheckpoint").ToList();

            flagList.Sort((first, second) => first.Int("number") - second.Int("number"));
            if (flagList.Count > 0) {
                EntityData summitCheckpoint;
                if (next) {
                    summitCheckpoint = flagList.Last();
                } else {
                    summitCheckpoint = flagList.First();
                }
                level.Session.RespawnPoint = level.GetSpawnPoint(levelData.Position+summitCheckpoint.Position);
                level.Session.SetFlag(FlagPrefix + summitCheckpoint.Int("number"));
            }
        }

        private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self,
            Player.IntroTypes playerIntro, bool isFromLoader) {
            orig(self, playerIntro, isFromLoader);

            // 切换章节清理历史记录
            if (RoomHistory.Count > 0 && RoomHistory[0].Area != self.Session.Area) {
                Reset();
            }

            // 非初始房间
            if (HistoryIndex != -1) {
                return;
            }

            // 进入章节的第一个房间
            RoomHistory.Add(self.Session.DeepClone());
            HistoryIndex = 0;
        }

        private static IEnumerator LevelOnTransitionRoutine(On.Celeste.Level.orig_TransitionRoutine orig, Level self,
            LevelData next, Vector2 direction) {
            IEnumerator enumerator = orig(self, next, direction);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }

            // 切图结束后
            RecordTransitionRoom(self.Session);
        }

        // 记录自行进入的房间或者触碰的旗子
        private static void RecordTransitionRoom(Session currentSession) {
            // 如果不是指向末尾证明曾经后退过，所以要记录新数据前必须清除后面的记录
            if (HistoryIndex < RoomHistory.Count - 1) {
                RoomHistory.RemoveRange(HistoryIndex + 1, RoomHistory.Count - HistoryIndex - 1);
            }

            // 增加记录          
            RecordRoom(currentSession);
        }

        // 记录通过查找地图数据传送的房间
        private static void RecordAndTeleport(Session session) {
            RecordRoom(session);
            TeleportTo(session);
        }

        private static void RecordRoom(Session session) {
            // 如果存在相同的房间且存档点相同则先清除
            for (var i = RoomHistory.Count - 1; i >= 0; i--) {
                if (RoomHistory[i].Level == session.Level && RoomHistory[i].RespawnPoint == session.RespawnPoint) {
                    RoomHistory.RemoveAt(i);
                }
            }

            RoomHistory.Add(session.DeepClone());
            HistoryIndex = RoomHistory.Count - 1;
        }
    }
}
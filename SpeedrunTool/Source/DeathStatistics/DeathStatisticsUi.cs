using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Other;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics {
    public class DeathStatisticsUi : TextMenu {
        private static SpeedrunToolSaveData SaveData => SpeedrunToolModule.SaveData;

        private static readonly Dictionary<string, int> ColumnHeaders = new Dictionary<string, int> {
            {Dialog.Clean(DialogIds.Chapter), 400},
            {Dialog.Clean(DialogIds.Room), 400},
            {Dialog.Clean(DialogIds.LostTime), 400},
            {Dialog.Clean(DialogIds.CauseOfDeath), 400},
        };

        private bool closing;
        private float inputDelay;

        public DeathStatisticsUi() {
            Reload(SaveData.Selection);
            OnESC = OnCancel = () => {
                Focused = false;
                closing = true;
                SaveData.SetSelection(Selection);
            };
            MinWidth = 600f;
            Position.Y = ScrollTargetY;
            Alpha = 0.0f;
        }

        // TODO 排序
        // TODO 过滤功能
        // TODO 重新组织菜单
        // TODO 研究能否记录死亡现场的录像回放
        private void Reload(int index = -1) {
            Clear();

            Add(new Header(Dialog.Clean(DialogIds.CheckDeathStatistics)));

            if (SaveData.DeathInfos.Count == 0) {
                AddEmptyInfo();
            } else {
                AddTotalInfo();
                AddListHeader();
                AddListItems();
                AddClearButton();
            }

            if (index < 0) {
                return;
            }

            Selection = index;
        }

        private void AddEmptyInfo() {
            Add(new Button(DialogIds.NoData.DialogClean()) {
                Disabled = true,
                Selectable = false
            });
        }

        private void AddTotalInfo() {
            Add(new TotalItem(new Dictionary<string, string> {
                {$"{Dialog.Clean(DialogIds.TotalDeathCount)}: ", SaveData.GetTotalDeathCount().ToString()},
                {$"{Dialog.Clean(DialogIds.TotalLostTime)}: ", SaveData.GetTotalLostTime()},
            }));
            Add(new SubHeader(""));
        }

        private void AddListHeader() {
            Add(new ListItem(ColumnHeaders, false));
        }

        private void AddListItems() {
            SaveData.DeathInfos.ForEach(deathInfo => {
                Dictionary<string, int> labels = new Dictionary<string, int> {
                    {deathInfo.Chapter, 400},
                    {deathInfo.Room, 400},
                    {deathInfo.GetLostTime(), 400},
                    {deathInfo.CauseOfDeath, 400},
                };
                ListItem item = new ListItem(labels);
                item.Pressed(() => {
                    SaveData.Selection = Selection;
                    deathInfo.TeleportToDeathPosition();
                });
                Add(item);
            });
        }

        private void AddClearButton() {
            Add(new SubHeader(""));
            Button clearButton = new Button(Dialog.Clean(DialogIds.ClearDeathStatistics)) {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = () => {
                    SaveData.Clear();
                    Reload(0);
                }
            };
            Add(clearButton);
        }

        public override void Update() {
            base.Update();

            if (!closing && ButtonConfigUi.Mappings.CheckDeathStatistics.Pressed()) {
                ButtonConfigUi.Mappings.CheckDeathStatistics.ConsumePress();
                OnESC?.Invoke();
            }

            if (inputDelay > 0.0) {
                inputDelay -= Engine.DeltaTime;
                if (inputDelay <= 0.0) {
                    Focused = true;
                }
            }

            Alpha = Calc.Approach(Alpha, closing ? 0.0f : 1f, Engine.DeltaTime * 8f);
            if (!closing || Alpha > 0.0) {
                return;
            }

            Close();
        }

        public override void Render() {
            Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * Ease.CubeOut(Alpha));
            base.Render();
        }
    }

    public class ListItem : TextMenu.Item {
        private const string ConfirmSfx = "event:/ui/main/button_select";
        private const int Divider = 20;
        private const float FixedWidth = 1600f;
        private readonly Dictionary<string, int> labels;

        public ListItem(Dictionary<string, int> labels, bool selectable = true) {
            this.labels = labels;
            Selectable = selectable;
            Disabled = !selectable;
        }

        public override void ConfirmPressed() {
            Audio.Play(ConfirmSfx);
            base.ConfirmPressed();
        }

        public override float LeftWidth() {
            return FixedWidth;
        }

        public override float Height() {
            return ActiveFont.LineHeight;
        }

        public override void Render(Vector2 position, bool highlighted) {
            float alpha = Container.Alpha;
            Color color = Disabled
                ? Color.Gray * alpha
                : (highlighted ? Container.HighlightColor : Color.White) * alpha;
            Color strokeColor = Color.Black * (alpha * alpha * alpha);

            Vector2 offset = Vector2.Zero;
            foreach (var label in labels) {
                float scale = 1f;
                float measureWidth = ActiveFont.Measure(label.Key).X;
                if (measureWidth > label.Value - Divider) {
                    scale = (label.Value - Divider) / measureWidth;
                }

                ActiveFont.DrawOutline(label.Key, position + offset, new Vector2(0.0f, 0.5f), Vector2.One * scale,
                    color, 2f,
                    strokeColor);
                offset += new Vector2(label.Value, 0);
            }
        }
    }

    public class TotalItem : TextMenu.Item {
        private const float FixedWidth = 1600f;
        private readonly Dictionary<string, string> labels;

        public TotalItem(Dictionary<string, string> labels) {
            this.labels = labels;
            Selectable = false;
        }

        public override float LeftWidth() {
            return FixedWidth;
        }

        public override float Height() {
            return ActiveFont.LineHeight;
        }

        public override void Render(Vector2 position, bool highlighted) {
            float alpha = Container.Alpha;
            Color color = Color.Gray * alpha;
            Color strokeColor = Color.Black * (alpha * alpha * alpha);

            Vector2 offset = Vector2.Zero;
            foreach (var label in labels) {
                ActiveFont.DrawOutline(label.Key, position + offset, new Vector2(0.0f, 0.5f), Vector2.One, color, 2f,
                    strokeColor);
                ActiveFont.DrawOutline(label.Value, position + offset + ActiveFont.Measure(label.Key).XComp(),
                    new Vector2(0.0f, 0.5f), Vector2.One, Color.White, 2f, strokeColor);
                offset += new Vector2(FixedWidth / labels.Count, 0);
            }
        }
    }
}
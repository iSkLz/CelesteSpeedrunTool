using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FlingBirdAction : AbstractEntityAction {
        private Dictionary<EntityID, FlingBird> savedFlingBirds = new Dictionary<EntityID, FlingBird>();
        private const string RestoreBird = "RestoreBird";

        public override void OnQuickSave(Level level) {
            savedFlingBirds = level.Entities.GetDictionary<FlingBird>();
        }

        private void RestoreFlingBirdPosition(On.Celeste.FlingBird.orig_ctor_EntityData_Vector2 orig,
            FlingBird self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedFlingBirds.ContainsKey(entityId)) {
                    self.SetExtendedDataValue(RestoreBird, true);
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedFlingBirds.Clear();
        }

        private void FlingBirdOnUpdate(On.Celeste.FlingBird.orig_Update orig, FlingBird self) {
            if (self.GetExtendedDataValue<bool>(RestoreBird)) {
                self.SetExtendedDataValue(RestoreBird, false);
                EntityID entityId = self.GetEntityId();
                if (IsLoadStart && savedFlingBirds.ContainsKey(entityId)) {
                    FlingBird savedFlingBird = savedFlingBirds[entityId];
                    int segmentIndex = (int) savedFlingBird.GetPrivateField("segmentIndex");

                    if (segmentIndex > 0) {
                        bool atEnding = segmentIndex >= savedFlingBird.NodeSegments.Count;
                        Sprite sprite = (Sprite) self.GetPrivateField("sprite");
                        sprite.Scale = Vector2.One;
                        if (atEnding) {
                            self.Position = savedFlingBird.NodeSegments[segmentIndex - 1].Last();
                            sprite.Play("hoverStressed");
                            sprite.Scale.X = 1f;
                            // WaitForLightningClear
                            self.SetPrivateField("state", 3);
                        }
                        else {
                            self.Position = savedFlingBird.NodeSegments[segmentIndex].First();
                            sprite.Scale.X = -1f;
                            if (savedFlingBird.SegmentsWaiting[segmentIndex]) {
                                sprite.Play("hoverStressed");
                            }
                            else {
                                sprite.Play("hover");
                            }
                        }

                        self.CopyPrivateField("segmentIndex", savedFlingBird);
                    }
                }
            }

            orig(self);
        }

        private static void FlingBirdOnOnPlayer(On.Celeste.FlingBird.orig_OnPlayer orig, FlingBird self, Player player) {
            if (player.SceneAs<Level>().Frozen) {
                return;
            }
            orig(self, player);
        }

        public override void OnLoad() {
            On.Celeste.FlingBird.ctor_EntityData_Vector2 += RestoreFlingBirdPosition;
            On.Celeste.FlingBird.Update += FlingBirdOnUpdate;
            On.Celeste.FlingBird.OnPlayer += FlingBirdOnOnPlayer;
        }

        public override void OnUnload() {
            On.Celeste.FlingBird.ctor_EntityData_Vector2 -= RestoreFlingBirdPosition;
            On.Celeste.FlingBird.Update -= FlingBirdOnUpdate;
            On.Celeste.FlingBird.OnPlayer -= FlingBirdOnOnPlayer;
        }
    }
}
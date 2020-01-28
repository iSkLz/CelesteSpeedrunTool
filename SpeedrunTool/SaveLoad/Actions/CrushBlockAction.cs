using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrushBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, CrushBlock> savedCrushBlocks = new Dictionary<EntityID, CrushBlock>();

        public override void OnClear() {
            savedCrushBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrushBlock.ctor_EntityData_Vector2 += RestoreCrushBlockState;
            On.Celeste.CrushBlock.Attack += CrushBlockOnAttack;
            On.Celeste.CrushBlock.MoveHCheck += CrushBlockOnMoveHCheck;
            On.Celeste.CrushBlock.MoveVCheck += CrushBlockOnMoveVCheck;
            On.Celeste.CrushBlock.Update += CrushBlockOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.CrushBlock.ctor_EntityData_Vector2 -= RestoreCrushBlockState;
            On.Celeste.CrushBlock.Attack -= CrushBlockOnAttack;
            On.Celeste.CrushBlock.MoveHCheck -= CrushBlockOnMoveHCheck;
            On.Celeste.CrushBlock.MoveVCheck -= CrushBlockOnMoveVCheck;
            On.Celeste.CrushBlock.Update -= CrushBlockOnUpdate;
        }

        public override void OnQuickSave(Level level) {
            savedCrushBlocks = level.Entities.GetDictionary<CrushBlock>();
        }

        private void RestoreCrushBlockState(On.Celeste.CrushBlock.orig_ctor_EntityData_Vector2 orig, CrushBlock self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedCrushBlocks.ContainsKey(entityId)) {
                    CrushBlock savedCrushBlock = savedCrushBlocks[entityId];
                    if (self.Position != savedCrushBlock.Position) {
                        self.Position = savedCrushBlock.Position;
                        self.Add(new FastForwardComponent<CrushBlock>(savedCrushBlock, OnFastForward));

                        object returnStack = savedCrushBlock.GetPrivateField("returnStack").Copy();
                        self.SetPrivateField("returnStack", returnStack);
                        self.Add(new RestoreCrushBlockStateComponent(savedCrushBlock));
                    }
                } else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void OnFastForward(CrushBlock entity, CrushBlock savedEntity) {
            for (int i = 0; i < 24; i++) {
                entity.Update();
            }
        }

        private static void CrushBlockOnAttack(On.Celeste.CrushBlock.orig_Attack orig, CrushBlock self,
            Vector2 direction) {
            orig(self, direction);
            self.SetExtendedDataValue("lastCrushDir", direction);
        }

        private static bool CrushBlockOnMoveHCheck(On.Celeste.CrushBlock.orig_MoveHCheck orig, CrushBlock self,
            float amount) {
            if (self.GetExtendedDataValue<bool>("IsReturning")) {
                self.SetExtendedDataValue("IsReturning", false);
                return true;
            }

            return orig(self, amount);
        }

        private static bool CrushBlockOnMoveVCheck(On.Celeste.CrushBlock.orig_MoveVCheck orig, CrushBlock self,
            float amount) {
            if (self.GetExtendedDataValue<bool>("IsReturning")) {
                self.SetExtendedDataValue("IsReturning", false);
                return true;
            }

            return orig(self, amount);
        }

        private static void CrushBlockOnUpdate(On.Celeste.CrushBlock.orig_Update orig, CrushBlock self) {
            try {
                orig(self);
            } catch (NullReferenceException) {
                ((Sprite) self.GetPrivateField("face")).Play("idle");
            }
        }

        private class RestoreCrushBlockStateComponent : Monocle.Component {
            private readonly MethodInfo attackMethodInfo =
                typeof(CrushBlock).GetMethod("Attack", BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly CrushBlock savedCrushBlock;

            public RestoreCrushBlockStateComponent(CrushBlock savedCrushBlock) : base(true, false) {
                this.savedCrushBlock = savedCrushBlock;
            }

            public override void Update() {
                object crushDir = savedCrushBlock.GetPrivateField("crushDir");

                if (crushDir != null) {
                    AudioAction.MuteAudioPathVector2("event:/game/06_reflection/crushblock_activate");

                    if ((Vector2) crushDir != Vector2.Zero) {
                        attackMethodInfo?.Invoke(Entity, new[] {crushDir});
                    } else {
                        AudioAction.MuteAudioPathVector2("event:/game/06_reflection/crushblock_impact");
                        AudioAction.MuteSoundSource("event:/game/06_reflection/crushblock_move_loop");
                        object lastCrushDir = savedCrushBlock.GetExtendedDataValue<Vector2>("lastCrushDir");
                        attackMethodInfo?.Invoke(Entity, new[] {lastCrushDir});
                        Entity.SetExtendedDataValue("IsReturning", true);
                    }

                    Entity.SetPrivateField("canActivate", !(bool) savedCrushBlock.GetPrivateField("chillOut"));
                }

                RemoveSelf();
            }
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ClutterSwitchAction : AbstractEntityAction {
        private Dictionary<EntityID, ClutterSwitch> savedClutterSwitches = new Dictionary<EntityID, ClutterSwitch>();

        public override void OnQuickSave(Level level) {
            savedClutterSwitches = level.Entities.GetDictionary<ClutterSwitch>();
        }

        private void RestoreClutterSwitchPosition(On.Celeste.ClutterSwitch.orig_ctor_EntityData_Vector2 orig,
            ClutterSwitch self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedClutterSwitches.ContainsKey(entityId)) {
                ClutterSwitch savedClutterSwitch = savedClutterSwitches[entityId];
                self.Add(new Coroutine(Restore(self, savedClutterSwitch)));
            }
        }

        private IEnumerator Restore(ClutterSwitch self, ClutterSwitch savedClutterSwitch) {
            self.Position = savedClutterSwitch.Position;
            yield break;
        }

        public override void OnClear() {
            savedClutterSwitches.Clear();
        }

        public override void OnLoad() {
            On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 += RestoreClutterSwitchPosition;
        }

        public override void OnUnload() {
            On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 -= RestoreClutterSwitchPosition;
        }
    }
}
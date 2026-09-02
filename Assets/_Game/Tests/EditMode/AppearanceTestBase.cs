using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Shared setup for appearance validation tests.</summary>
    internal abstract class AppearanceTestBase
    {
        protected DefinitionRegistry<AppearanceOptionDefinition> Options;
        private List<AppearanceOptionDefinition> _created;

        [SetUp]
        public void SetUp()
        {
            Options = new DefinitionRegistry<AppearanceOptionDefinition>();
            _created = new List<AppearanceOptionDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (AppearanceOptionDefinition option in _created)
            {
                Object.DestroyImmediate(option);
            }
        }

        protected AppearanceOptionDefinition Add(string id, AppearanceSlot slot,
            GenderAvailability availability)
        {
            var option = ScriptableObject.CreateInstance<AppearanceOptionDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_slot\":" + (int)slot
                + ",\"_genderAvailability\":" + (int)availability + "}", option);
            _created.Add(option);
            Options.Register(option);
            return option;
        }

        protected void AddNeutralSet()
        {
            Add("face_001", AppearanceSlot.Face, GenderAvailability.Any);
            Add("eye_001", AppearanceSlot.Eyes, GenderAvailability.Any);
            Add("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            Add("hair_color_001", AppearanceSlot.HairColor, GenderAvailability.Any);
            Add("skin_001", AppearanceSlot.SkinTone, GenderAvailability.Any);
        }

        protected static CharacterAppearanceState FullyDressed()
        {
            var appearance = new CharacterAppearanceState(CharacterId.New());
            appearance.Select(AppearanceSlot.Face, new DefinitionId("face_001"));
            appearance.Select(AppearanceSlot.Eyes, new DefinitionId("eye_001"));
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_001"));
            appearance.Select(AppearanceSlot.HairColor, new DefinitionId("hair_color_001"));
            appearance.Select(AppearanceSlot.SkinTone, new DefinitionId("skin_001"));
            return appearance;
        }
    }
}

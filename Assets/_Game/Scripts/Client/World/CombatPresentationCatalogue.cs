using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Which combat presentation an attack resolves to — keyed by the equipped weapon, with
    /// an unarmed fallback.
    /// </summary>
    /// <remarks>
    /// <b>This is the data that replaces a class switch.</b> The presenter asks this "what
    /// does an attack look like with <c>item.weapon.x</c> equipped, or with nothing?" and
    /// plays whatever it answers. Adding the Swordsman, Archer, Mage or Cleric attack is an
    /// entry here plus an animation — never an <c>if class ==</c> in presentation code.
    ///
    /// <b>Presentation only</b>, like every other visual catalogue.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Presentation/Combat Presentation Catalogue",
        fileName = "CombatPresentationCatalogue")]
    public sealed class CombatPresentationCatalogue : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("The equipped weapon item id this presentation is for. Leave invalid "
                + "for the unarmed entry.")]
            public DefinitionId Weapon;

            public CombatPresentationDefinition Presentation;
        }

        [Tooltip("The attack shown when no weapon is equipped — the approved cross punch.")]
        [SerializeField] private CombatPresentationDefinition _unarmed;

        [SerializeField] private Entry[] _weapons = new Entry[0];

        public CombatPresentationDefinition Unarmed => _unarmed;

        public IReadOnlyList<Entry> Weapons => _weapons;

        /// <summary>
        /// The presentation for a given equipped weapon, or the unarmed fallback.
        /// </summary>
        /// <remarks>Returns null only when nothing is authored at all, which the presenter
        /// reads as "use the shipped defaults" — so an unconfigured scene still shows the
        /// approved punch.</remarks>
        public CombatPresentationDefinition Resolve(DefinitionId equippedWeapon)
        {
            if (equippedWeapon.IsValid && _weapons != null)
            {
                for (var i = 0; i < _weapons.Length; i++)
                {
                    if (_weapons[i].Weapon == equippedWeapon && _weapons[i].Presentation != null)
                    {
                        return _weapons[i].Presentation;
                    }
                }
            }

            return _unarmed;
        }
    }
}
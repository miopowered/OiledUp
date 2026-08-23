using System.Collections.Generic;
using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The crate that arrives each morning. Spawns a physical vial per sample and hands them out
    /// one at a time — you have one pair of hands, which is where queue pressure starts.
    /// </summary>
    public sealed class IntakeCrate : Interactable
    {
        [SerializeField] private Transform slotRoot;
        [SerializeField] private int columns = 4;
        [SerializeField] private float slotSpacing = 0.11f;

        private readonly List<VialProp> waiting = new();
        private readonly List<Transform> slots = new();

        private void Start()
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab == null) return;

            lab.Lab.DayStarted += OnDayStarted;
            if (lab.Lab.DayInProgress) OnDayStarted(lab.Lab.Day);
        }

        private void OnDestroy()
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab != null) lab.Lab.DayStarted -= OnDayStarted;
        }

        private void OnDayStarted(int day)
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab == null) return;

            foreach (var sample in lab.Lab.Samples.All)
            {
                if (sample.Location.Kind != SampleLocationKind.InCrate) continue;
                if (lab.PropFor(sample.Id) != null) continue;

                var slot = SlotFor(waiting.Count);
                var vial = lab.SpawnVial(sample, slot);
                if (vial != null) waiting.Add(vial);
            }
        }

        private Transform SlotFor(int index)
        {
            while (slots.Count <= index)
            {
                int i = slots.Count;
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(slotRoot != null ? slotRoot : transform, false);
                go.transform.localPosition = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * slotSpacing,
                    0f,
                    (i / columns) * slotSpacing);
                slots.Add(go.transform);
            }
            return slots[index];
        }

        public int Remaining
        {
            get
            {
                int n = 0;
                foreach (var v in waiting)
                {
                    if (v != null) n++;
                }
                return n;
            }
        }

        public override string Prompt(PlayerInteractor player)
        {
            if (player.Carried != null) return "Hands full";
            return Remaining > 0
                ? $"Take next sample ({Remaining} in crate)"
                : "Crate empty";
        }

        public override bool CanInteract(PlayerInteractor player) =>
            player.Carried == null && Remaining > 0;

        public override void Interact(PlayerInteractor player)
        {
            for (int i = 0; i < waiting.Count; i++)
            {
                var vial = waiting[i];
                if (vial == null) continue;

                waiting.RemoveAt(i);
                player.TryCarry(vial);

                var sample = LabRuntime.Instance?.SampleFor(vial.SampleId);
                if (sample != null)
                {
                    // Reads the paper label out loud, because the tag has to be transcribed at the
                    // terminal from memory or from a second look at the vial. That transcription is
                    // where §5.1's mis-logging comes from, so the tag is stated once, here.
                    player.Say($"{sample.Id} — {sample.EquipmentTag} — {sample.Profile.DisplayName}, " +
                               $"{sample.HoursSinceOilChange:F0} h on the oil." +
                               (string.IsNullOrEmpty(sample.FieldTechNote) ? "" : $" \"{sample.FieldTechNote}\"") +
                               " Book it in at the terminal.",
                        5f);
                }
                return;
            }
        }
    }
}

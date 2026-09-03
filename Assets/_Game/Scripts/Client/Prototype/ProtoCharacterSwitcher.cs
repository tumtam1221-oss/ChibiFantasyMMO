using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE character swapper for PHASE 07.1.
    ///
    /// Both characters run the exact same controller, camera and input objects.
    /// Swapping only changes which character instance is active and which transform
    /// the shared camera follows, so there is no per-gender controller.
    /// </summary>
    public sealed class ProtoCharacterSwitcher : MonoBehaviour
    {
        [SerializeField] private ProtoThirdPersonCamera cameraRig;
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private GameObject[] characters;

        private int _activeIndex = -1;

        public int ActiveIndex { get { return _activeIndex; } }
        public int Count { get { return characters == null ? 0 : characters.Length; } }

        public GameObject Active
        {
            get
            {
                if (characters == null || _activeIndex < 0 || _activeIndex >= characters.Length)
                    return null;
                return characters[_activeIndex];
            }
        }

        public void Configure(ProtoThirdPersonCamera rig, ProtoPlayerInput src, GameObject[] chars)
        {
            cameraRig = rig;
            input = src;
            characters = chars;
        }

        private void Start()
        {
            if (_activeIndex < 0) Activate(0);
        }

        public void Activate(int index)
        {
            if (characters == null || characters.Length == 0) return;
            index = Mathf.Clamp(index, 0, characters.Length - 1);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] == null) continue;
                bool on = (i == index);

                if (!on && characters[i].activeSelf)
                {
                    // Clear motion and animator state before parking the character so
                    // nothing leaks into the next activation.
                    ProtoThirdPersonController c = characters[i].GetComponent<ProtoThirdPersonController>();
                    if (c != null) c.ResetMotion();
                }

                characters[i].SetActive(on);
            }

            _activeIndex = index;

            GameObject active = characters[index];
            if (active == null) return;

            ProtoThirdPersonController ctrl = active.GetComponent<ProtoThirdPersonController>();
            if (ctrl != null)
            {
                ctrl.SetInput(input);
                if (cameraRig != null) ctrl.SetCamera(cameraRig.transform);
                ctrl.ResetMotion();
            }

            if (cameraRig != null) cameraRig.SetTarget(active.transform, true);
        }

        public void Next()
        {
            if (characters == null || characters.Length == 0) return;
            Activate((_activeIndex + 1) % characters.Length);
        }
    }
}

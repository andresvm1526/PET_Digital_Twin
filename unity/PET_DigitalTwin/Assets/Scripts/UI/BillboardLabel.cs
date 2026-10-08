// BillboardLabel.cs
// Rotates a GameObject (typically a TextMeshPro label) to always face the main camera.
// Attach to the floating label child of each machine.

using UnityEngine;

namespace PETDigitalTwin
{
    public class BillboardLabel : MonoBehaviour
    {
        private UnityEngine.Camera _cam;

        private void Start()
        {
            _cam = UnityEngine.Camera.main;
        }

        private void LateUpdate()
        {
            if (_cam == null)
            {
                _cam = UnityEngine.Camera.main;
                return;
            }

            // Face the camera direction (not look-at, to avoid flipping)
            transform.rotation = _cam.transform.rotation;
        }
    }
}

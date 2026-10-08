// CameraController.cs
// Manages three camera modes:
//   1. OVERVIEW  – fixed elevated orthographic-ish view of the full plant
//   2. FREE      – WASD + mouse drag orbit for exploration
//   3. FOLLOW    – tracks the selected machine and frames it

using UnityEngine;

namespace PETDigitalTwin.Camera
{
    public class CameraController : MonoBehaviour
    {
        // ── Camera mode ──────────────────────────────────────────────
        public enum Mode { Overview, Free, Follow }

        [Header("Initial mode")]
        public Mode currentMode = Mode.Overview;

        // ── Overview settings ────────────────────────────────────────
        [Header("Overview")]
        public Vector3 overviewPosition = new(3f, 22f, -18f);
        public Vector3 overviewRotation = new(52f, 0f, 0f);

        // ── Free camera settings ─────────────────────────────────────
        [Header("Free Camera")]
        public float moveSpeed   = 10f;
        public float lookSpeed   = 3f;
        public float scrollSpeed = 5f;

        // ── Follow settings ──────────────────────────────────────────
        [Header("Follow")]
        public float followDistance = 8f;
        public float followHeight   = 5f;
        public float followSmooth   = 5f;
        private Transform _followTarget;

        // ── Internal ─────────────────────────────────────────────────
        private Vector3 _freeDragStart;
        private bool    _isDragging;

        // ── Unity lifecycle ──────────────────────────────────────────
        private void Start()
        {
            SetMode(currentMode);

            // Subscribe to machine selection
            if (PlantManager.Instance != null)
                PlantManager.Instance.OnMachineSelected += ctrl => FollowMachine(ctrl.transform);
        }

        private void Update()
        {
            HandleHotkeys();

            switch (currentMode)
            {
                case Mode.Free:     UpdateFreeCamera();    break;
                case Mode.Follow:   UpdateFollowCamera();  break;
                // Overview: static, nothing to update per-frame
            }
        }

        // ── Mode switching ───────────────────────────────────────────
        public void SetMode(Mode mode)
        {
            currentMode = mode;

            if (mode == Mode.Overview)
            {
                transform.position = overviewPosition;
                transform.eulerAngles = overviewRotation;
            }
        }

        public void FollowMachine(Transform target)
        {
            _followTarget = target;
            SetMode(Mode.Follow);
        }

        // ── Hotkeys ──────────────────────────────────────────────────
        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F1)) SetMode(Mode.Overview);
            if (Input.GetKeyDown(KeyCode.F2)) SetMode(Mode.Free);
            if (Input.GetKeyDown(KeyCode.Escape) && currentMode == Mode.Follow) SetMode(Mode.Overview);
        }

        // ── Free camera ──────────────────────────────────────────────
        private void UpdateFreeCamera()
        {
            // WASD movement
            float h   = Input.GetAxis("Horizontal");
            float v   = Input.GetAxis("Vertical");
            float up  = Input.GetKey(KeyCode.Q) ? -1f : Input.GetKey(KeyCode.E) ? 1f : 0f;
            float spd = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);

            transform.Translate(new Vector3(h, up, v) * spd * Time.deltaTime, Space.Self);

            // Mouse scroll zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            transform.Translate(Vector3.forward * scroll * scrollSpeed, Space.Self);

            // Right-mouse drag to orbit
            if (Input.GetMouseButtonDown(1))
            {
                _isDragging   = true;
                _freeDragStart = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(1))
                _isDragging = false;

            if (_isDragging)
            {
                float deltaX = Input.GetAxis("Mouse X") * lookSpeed;
                float deltaY = Input.GetAxis("Mouse Y") * lookSpeed;
                transform.eulerAngles += new Vector3(-deltaY, deltaX, 0f);
            }
        }

        // ── Follow camera ────────────────────────────────────────────
        private void UpdateFollowCamera()
        {
            if (_followTarget == null) { SetMode(Mode.Overview); return; }

            Vector3 desired = _followTarget.position
                              + Vector3.back  * followDistance
                              + Vector3.up    * followHeight;

            transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
            transform.LookAt(_followTarget.position + Vector3.up * 1f);
        }
    }
}
